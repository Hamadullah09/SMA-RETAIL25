# RFID multi-reader discovery and antenna-to-station management

What the system does when a shop plugs several RFID readers into an Ethernet switch, how it was
changed to do it, and how to prove it on real hardware.

Commit `89aaeb4`, 11 September 2026.

---

## 1. The RFID architecture that already existed

Most of what the brief asks for was already built. That is worth stating first, because the useful
change was small and specific, and a second implementation beside the working one would have been
the worst possible outcome.

**Identity is the hardware, not the address.** `backend/src/Retail25.Domain/Terminals/RfidTopology.cs`
defines three aggregates:

| Entity | Stable identity | Mutable description |
|---|---|---|
| `Device` — the PC an agent runs on | `DeviceKey` (`PC-001`), issued at enrolment | `Hostname`, `LocalIpAddresses`, `AgentVersion`, `LastHeartbeat` |
| `RfidReader` — the box on the wall | `ReaderKey` (`RFID-001`) and `SerialNumber` | `Host`, `Port`, `Protocol`, `AntennaCount`, `LastSeen` |
| `ReaderAntennaAssignment` — one socket on one box | `(ReaderId, AntennaNumber)`, unique in the database | `StationId`, `IsEnabled` |

`RfidReader.MoveTo(host, port)` exists precisely so a DHCP change updates two columns instead of
orphaning the assignments hanging off the reader.

**The antenna is what decides where a read lands.** `ReaderAntennaAssignment` replaced
`Reader → Station` with `Reader + Antenna → Station`. Four rows against one reader make four
independent tills out of one box; 252 rows make 252 tills out of 63 boxes with no different code
running. The uniqueness constraint is in the database, not in a handler, so two administrators
saving at once cannot point one physical antenna at two tills.

**The agent was already multi-reader.** `Retail25.TerminalAgent/Rfid/RfidReaderService.cs` holds a
`Dictionary<long, RunningSession>`, takes its desired set from `DeviceConfigurationStore` (which the
server fills from the topology), and reconciles on change — start what is new, stop what was
removed, restart what was edited. Each session reconnects on its own clock so one unplugged reader
does not take the other lanes down.

**The assignment screen was already built.** `frontend/src/components/settings/rfid-topology-tab.tsx`
laid out machines, then readers, then a per-antenna station select, with a warning counting antennas
that are assigned to nothing.

**Two hosting topologies, deliberately alternatives.**

- *Agent-held*: a `Retail25.TerminalAgent` Windows service on each till opens the reader sockets.
  Used where the API is hosted off-site (`pos.sma-techno.net`).
- *Server-held*: `Rfid:ServerReaders:Enabled` makes the API open them itself. Only works where the
  API is on the shop's own network. This is what the development machine runs today.

**The gaps.** Four, all in the step before any of the above:

1. `ReaderDiscovery.FindAsync` stopped at the first address that answered. It was written to re-find
   *one* configured reader that had moved, and it did that well. A switch with seven readers
   reported one.
2. An open TCP port was the whole test. Anything listening on the same port was indistinguishable
   from a reader.
3. `UhfSerialCommand.GetReaderIdentifier` (`0x68`) was defined and never called, so
   `RfidReader.SerialNumber` was only ever populated by hand — the column the whole identity model
   rests on.
4. There was no path from "a sweep found something" to "a row exists an administrator can assign".

---

## 2. What changed

**A reader now has to prove what it is.** `ReaderIdentityProbe` opens a candidate address and speaks
the R2000-family serial protocol to it:

| Question | Opcode | What it gives | If unanswered |
|---|---|---|---|
| Firmware version | `0x72` | `FirmwareVersion`, and the proof | **Not a reader.** The candidate is discarded. |
| Reader identifier | `0x68` | `SerialNumber` (hex, trailing zeros trimmed) | Identity falls back to the address, recorded as a known weakness |
| Output power | `0x77` | One byte per antenna port ⇒ `AntennaCount` | Falls back to four, flagged `AntennaCountReported: false` |

Timeouts are 600 ms to connect and 1200 ms per reply. Firmware is the only required answer: a device
that opens a socket and says nothing is refused, which is the case the estate is actually in today
(see §11).

**A sweep answers every address.** `ReaderDiscovery.DiscoverAsync(port, inUse, ct)` runs in two
phases: a cheap TCP knock across every address on every IPv4 network the machine is attached to (48
in parallel, 400 ms each), then the protocol conversation with only the handful that answered (8 in
parallel, because each holds a socket on hardware that allows one client). On a shop network that is
seven conversations instead of seven hundred and sixty-two.

Results are deduplicated on serial number, falling back to address where a unit reports none — two
rows an administrator can merge beat one reader silently swallowing another's antennas.

**Readers already in use are excluded before the sweep starts, not probed and discarded.** This
reader family accepts exactly one client; probing an address a running session holds would take the
reader away from a till mid-sale.

**Ports come from the estate, then from a guess.** `ReaderDiscoveryService.PortsToSweep` tries, in
order: the ports the shop's own readers already answer on, then the station's profile port, then
`Agent:DiscoveryPorts` (default `4001, 5084`). Taking the port from the profile alone — as the first
cut did — meant a fresh agent swept the placeholder `5084` it ships with, on exactly the installation
discovery exists to serve.

**Findings are recorded by the server, never by the till.**
`RecordReaderDiscoveryHandler` matches a sighting to a known reader **by serial first, address
second**, and address only for units that report no serial. A serial that matches nothing is a new
reader even if it sits at a familiar address — inheriting the old reader's antenna assignments would
point them at different hardware.

Readers that were *not* found are deliberately left alone. A sweep is evidence that something is
present, never that something is absent.

Antenna count is only ever widened unless the reader itself reported the number. A silent reader
falling back to four must not shrink a count somebody corrected to eight, because that would strand
every assignment on the ports it removed.

**The screen says how many readers are up, and why the rest are not.** `GetRfidTopologyQuery` now
returns a `ReaderState` per reader and a `ReaderStateSummary` for the shop:

| State | Evidence | What to do |
|---|---|---|
| `Connected` | The server holds the socket, or the driving agent's last heartbeat carried a sighting | Nothing |
| `Discovered` | Found by a sweep; no machine has claimed it | Assign its antennas |
| `Error` | The machine is checking in and is *not* holding the reader | Walk to the reader: power, cable, switch port, another client |
| `Offline` | The machine driving it has stopped checking in | Walk to the PC. Nothing can be said about the reader |
| `Disabled` | Switched off by an administrator | Nothing. Not counted as a fault |

`Connected` is measured against the machine's own heartbeat rather than the wall clock. The agent
stamps the sighting and the heartbeat in the same check-in, so while it holds the reader the two are
equal and the gap only opens once it checks in without it. A wall-clock rule would keep saying
"connected" for the length of the staleness window — precisely the minute somebody is stood at a
till that has stopped reading.

**Changes are pushed, not polled.** `IRfidNotifier.TopologyChangedAsync(locationId, reason)`
broadcasts `TopologyChanged` on `/hubs/rfid` to the store group, carrying nothing but a reason
(`discovery`, `reader`, `assignment`). Watchers re-read through the permission-checked endpoint. It
is raised when discovery adds or moves a reader, when a reader's connected-ness flips at a check-in
(one message per check-in however many readers flipped), and when an antenna is assigned or cleared.
A sweep that finds the same readers in the same places — the normal outcome every quarter of an hour
on every till — pushes nothing.

---

## 3. Files changed

22 files, +2285 / −32.

### New

| File | What it is |
|---|---|
| `backend/src/Retail25.Devices/Rfid/ReaderIdentityProbe.cs` | `ReaderIdentity`, `IReaderIdentityProbe`, `ReaderIdentityProbe`. The one place that decides "this is a reader". |
| `backend/src/Retail25.Application/Terminals/RecordReaderDiscoveryCommand.cs` | `RecordReaderDiscoveryCommand` / `Handler` / `ReaderDiscoveryOutcome`. Serial-first matching, key allocation, antenna-count widening. |
| `backend/src/Retail25.TerminalAgent/Rfid/ReaderDiscoveryService.cs` | The background sweeper and its `SweepAsync`. 15-minute interval, 45-second first sweep. |
| `backend/tests/Retail25.Application.UnitTests/Terminals/ReaderDiscoveryRegistrationTests.cs` | 8 tests. What a sweep may change about the reader list. |
| `backend/tests/Retail25.Application.UnitTests/Terminals/ReaderStateTests.cs` | 10 tests. What the screen is allowed to say about a reader. |
| `backend/tests/Retail25.TerminalAgent.UnitTests/Rfid/DiscoveryPortsTests.cs` | 5 tests. Which ports a sweep knocks on and in what order. |
| `backend/tests/Retail25.TerminalAgent.UnitTests/Rfid/FakeReaderIdentityProbe.cs` | A fluent fake, so a sweep can be tested without hardware. |

### Changed

| File | Change |
|---|---|
| `backend/src/Retail25.TerminalAgent/Rfid/ReaderDiscovery.cs` | Added `DiscoverAsync`: two-phase sweep, identification, dedupe. `FindAsync` untouched. |
| `backend/src/Retail25.Application/Terminals/RfidTopologyAdmin.cs` | `ReaderState`, `ReaderStateSummary`, `StateOf`, `ServerSessionFor`; notifier on write. |
| `backend/src/Retail25.Application/Terminals/DeviceRegistryCommands.cs` | `Held(lastSeen, heartbeat)` — the one definition of "connected"; push on flip. |
| `backend/src/Retail25.Application/Abstractions/IRfidNotifier.cs` | `TopologyChangedAsync`. |
| `backend/src/Retail25.Infrastructure/Realtime/RfidHub.cs` | `RfidNotifier.TopologyChangedAsync` → store group. |
| `backend/src/Retail25.Api/Controllers/RfidTopologyController.cs` | `POST api/v1/rfid-topology/discovered`. |
| `backend/src/Retail25.Contracts/Terminals/DeviceConfiguration.cs` | `DiscoveredReaderContract`, `ReaderDiscoveryReport`. |
| `backend/src/Retail25.TerminalAgent/AgentOptions.cs` | `DiscoverReaders` (default on), `DiscoveryPorts` (default `4001, 5084`). |
| `backend/src/Retail25.TerminalAgent/LocalApi/LocalApiEndpoints.cs` | `POST /discovery/scan` on loopback. |
| `backend/src/Retail25.TerminalAgent/Program.cs` | Registers the probe and the sweeper. |
| `frontend/src/components/settings/rfid-topology-tab.tsx` | Summary strip, per-reader state badge and hint, "Scan for readers", live updates. |
| `frontend/src/lib/rfid-api.ts` | `rfidApi.scan()`. |
| `frontend/src/lib/rfid-hub.ts` | `connect(stationId: number \| null, …)`, `onTopologyChanged`. |

---

## 4. Database migrations

**None.** No schema change was needed, and that is a consequence of the model that was already
there: `RfidReader.SerialNumber`, `Host`, `Port`, `AntennaCount` and `LastSeen` all existed, as did
`ReaderAntennaAssignment`. Discovery fills columns that were already declared and previously only
writable by hand.

`ReaderState` is derived at query time from `Device.LastHeartbeat`, `RfidReader.LastSeen`,
`RfidReader.IsEnabled` and `RfidReader.DeviceId`. It is deliberately not a stored column: a stored
state would be a second source of truth that could disagree with the timestamps it was computed from.

---

## 5. API changes

| Method | Route | Permission | Purpose |
|---|---|---|---|
| `POST` | `/api/v1/rfid-topology/discovered` | `Terminals.Register` | **New.** An agent reports what its sweep found. Refused with `device.not_found` unless the device key is registered at that location. |
| `GET` | `/api/v1/rfid-topology?locationId=` | `Settings.Read` | **Changed.** Each reader now carries `state` and `deviceOnline`; the payload carries a `summary`. |

Unchanged and still the only way to assign an antenna:
`PUT /api/v1/rfid-topology/readers/{readerId}/antennas/{antennaNumber}` (`Settings.Hardware`),
`PUT /api/v1/rfid-topology/readers`, `GET /api/v1/rfid-topology/dashboard`,
`POST /api/v1/rfid-topology/backfill`, and the enrolment pair.

Request body for the new endpoint:

```json
{
  "locationId": 1,
  "deviceKey": "PC-001",
  "readers": [
    {
      "host": "192.168.0.178", "port": 4001, "protocol": "UhfSerial",
      "serialNumber": "A1B2C3D4", "firmwareVersion": "8.2",
      "antennaCount": 4, "antennaCountReported": true
    }
  ]
}
```

Response: `{ "found": 1, "added": 1, "moved": 0, "unchanged": 0, "addedKeys": ["RFID-001"] }`.

---

## 6. Terminal agent changes

- `ReaderDiscoveryService` — a `BackgroundService`, registered as a singleton *and* handed to the
  host so the loopback API can reach the same instance. First sweep 45 seconds after start (long
  enough for known readers to be held and therefore excluded); every 15 minutes thereafter. A failed
  sweep logs and retries; it never ends the service.
- `POST http://127.0.0.1:8477/discovery/scan` — sweeps now and returns `{ found, readers }`, so the
  settings page can say "three readers" immediately instead of waiting for the timer. Loopback-bound
  and guarded a second time by `LoopbackOnlyFilter`.
- `Agent:DiscoverReaders` — on by default. Worth turning off where connection attempts are treated as
  an intrusion, or where readers are pinned and nothing should be looking for others. Readers already
  registered keep working either way.
- `Agent:DiscoveryPorts` — the starting guess for a machine nobody has configured.

---

## 7. Front-end changes

All in the existing RFID tab under **Admin → Settings → RFID**; no new screen, no new visual
language. It uses the same `pos-table`, `pos-input`, `pos-button` and `pos-badge` classes as the rest
of the back office, and the on-scale type tokens the linter enforces.

- A header line: **"4 of 7 readers connected"**, followed by pills for whatever is missing — *1 not
  answering*, *1 machine offline*, *1 discovered*. Counted on the server so two screens cannot
  disagree.
- Each reader carries its state as a badge, and — when it is not `Connected` — one line saying what
  to do about it.
- **Scan for readers** calls the agent's loopback endpoint. When no agent answers, the screen says so
  and says why: only a machine on the shop's LAN can see the readers.
- The tab holds a `/hubs/rfid` connection scoped to the store (no station) and re-reads on
  `TopologyChanged`. A failure to connect is silent: the page still works, it just stops updating on
  its own.

---

## 8. SignalR changes

One new server-to-client message on the existing `/hubs/rfid`:

```
TopologyChanged  →  { locationId, reason }        reason ∈ { discovery, reader, assignment }
```

Sent to `rfid:location:{locationId}`. It deliberately carries no rows: the settings view is
permission-checked and clients re-read it. Broadcasting the reader list on a hub would mean two
shapes of the same data and a hub that has to decide what each watcher may see.

`RfidHub.SubscribeToLocation` and the hub ticket already accepted a null station, so no hub or
authentication change was needed — only the client, which previously demanded a station id.

---

## 9. Test coverage

**27 new tests.** Totals below are the full suite as run on 11 September 2026.

| Project | Tests | Result |
|---|---|---|
| `Retail25.Domain.UnitTests` | 170 | pass |
| `Retail25.Application.UnitTests` | 677 | pass |
| `Retail25.TerminalAgent.UnitTests` | 141 | pass |
| `Retail25.ArchitectureTests` | 16 | pass |
| `Retail25.IntegrationTests` | 23 pass, **10 fail**, 111 skipped | see below |
| `frontend` (vitest) | 81 | pass |

What the new tests pin:

*Registration* (`ReaderDiscoveryRegistrationTests`) — a new reader is registered; seven readers are
all named apart; **a reader that changed address keeps its antenna assignments**; a *different*
reader at a familiar address is a different reader; a reader missed by one scan is left alone; a
reported antenna count beats the default; an assumed count never narrows a known one; findings from
an unregistered machine are refused.

*State* (`ReaderStateTests`) — each of the five states against the evidence that should produce it;
a reader dropped moments ago does not linger as connected; a switched-off reader is not a fault; a
reader the server holds itself is connected with no agent anywhere; the summary counts seven readers
across four states.

*Sweep* (`ReaderDiscoveryTests`) — a device that answers TCP and fails the protocol is rejected; an
identity is reported including an eight-antenna unit; one serial on two addresses is one reader; an
address a session already holds is never probed.

*Ports* (`DiscoveryPortsTests`) — the estate's own ports first; a till told nothing still has
somewhere to look; no port swept twice; an unset port is not swept.

*Estate scale* (`RfidScaleTests`, pre-existing) — 252 antennas across 63 readers, no two antennas
resolving to the same station. **Docker-gated, so not run here.**

**The 10 integration failures are environmental and pre-existing.** Every one reports
`Docker is either not running or misconfigured` from Testcontainers; hardware virtualisation is
disabled in this machine's BIOS. They were failing identically before this work and the count did not
change. The 111 skipped tests are gated on the same container.

---

## 10. Build and test commands

```bash
dotnet build backend/Retail25.sln
```

```bash
dotnet test backend/Retail25.sln
```

```bash
cd frontend && npm run build && npm test
```

`npm test` needs `npm install` to have completed — `vitest` is declared in `devDependencies` but was
missing from `node_modules` on this machine, which also broke `npm run build` until it was installed.

To sweep from a bench without touching a live till, run the agent on a spare loopback port and point
it at an address nothing serves, so it can neither enrol nor report:

```bash
./Retail25.TerminalAgent.exe --Agent:StationId=1 --Agent:ApiUrl=http://127.0.0.1:9 --Agent:LocalApiUrl=http://127.0.0.1:8478 --Agent:DisablePeripherals=true
```

```bash
curl -s -X POST http://127.0.0.1:8478/discovery/scan
```

---

## 11. Hardware and protocol assumptions

- **The protocol is the R2000-family serial interface v3.1 over TCP**, verified against D2184B
  firmware 8.2. Opcodes used by the probe: `0x72` firmware, `0x68` identifier, `0x77` output power.
  Address byte `0xFF` (public address), so a reader that has been given a non-default device address
  still answers.
- **One TCP client at a time.** This is load-bearing throughout: it is why sweeps exclude readers in
  use, why identification runs 8-wide rather than 48-wide, and why two hosts must never both be
  configured to hold the same reader.
- **Output power is only sometimes an antenna count.** Some firmware answers one byte per port —
  which is the count — and some answers a single byte shared across ports, which says nothing. The
  contract carries `AntennaCountReported` so the server can tell a measurement from a fallback of
  four, and never lets a fallback narrow a known count.
- **A reader may report no serial number.** Those are identified by address, which is weaker, and is
  recorded in the model rather than hidden.
- **Discovery sweeps every IPv4 network the machine is attached to**, excluding link-local
  `169.254.x`. On a machine with both a wired shop LAN and Wi-Fi that is two /24s.

**Verified on real hardware, 11 September 2026:** a sweep from this machine covered 508 addresses
across `192.168.0.0/24` and `192.168.18.0/24` in about 5 seconds per port. Port 5084: nothing
answered. Port 4001: **one host answered TCP and was refused**, because the serial-to-Ethernet bridge
at `192.168.0.178:4001` accepted the connection and the reader behind it did not answer a firmware
query. The API's own reader session reported the same thing independently
(`opened but did not answer a firmware query, so it is not a reader`). That is the intended
behaviour and it is exactly the case the brief asks for — an open port is not a reader.

**Not verified on real hardware:** a *positive* identification, because the reader was silent
throughout. The serial number, firmware string and antenna count paths are covered by unit tests and
use the same `UhfSerialCodec` framing that the working reader session uses, but they have not been
seen against a powered, idle D2184. §13 is the procedure for confirming that.

---

## 12. Known limitations

1. **The positive identification path is untested against live hardware.** See above.
2. **Server-held readers are matched to topology rows by `host:port`.** The server-hosted path drives
   the older `ReaderProfile` table, which has no serial number column; host and port are the only
   fact both tables hold. Two readers behind one NAT address would be indistinguishable. Agent-held
   readers use the serial and are unaffected.
3. **The server-hosted path still routes reads by `ReaderProfile.StationId` and `AntennaZones`, not
   by `ReaderAntennaAssignment`.** A LAN deployment therefore gets antenna→station assignment as
   *configuration and reporting* but not yet as *routing*. Run **Bring existing readers across** to
   populate the topology, or run the terminal agent, which honours the assignments fully. Unifying
   the two is the obvious next piece of work and was deliberately left out of this change, because
   changing how a working shop routes its reads is not a side effect.
4. **A reader going offline because its PC died is detected by a 15-second timeout, not a push.**
   Absence produces no event. Everything else — discovery, assignment, a reader dropping while its
   agent is alive — is pushed immediately.
5. **Discovery cannot find a reader that is powered off, mid-reboot, or already held by another
   client.** By design: the sweep proves presence, never absence, and never takes a socket from a
   till.
6. **`Agent:DiscoveryPorts` defaults to two ports.** A shop using a third port must either set it or
   register the first such reader by hand; after that the estate's own port is swept automatically.
7. **`SSH.NET 2023.0.0` in `Retail25.IntegrationTests` carries a known high-severity advisory**
   (`NU1903`, GHSA-q939-rpr3-3284). Pre-existing, test-only, transitive through Testcontainers.
8. **The settings screen has not been exercised in a signed-in browser in this session.** It is
   covered by `tsc --noEmit`, the lint rules, and a clean `next build`, but signing in would mean
   entering the administrator's password, which is not something to automate. A five-minute manual
   pass is in §13.

---

## 13. Testing it for real: one to seven readers on a switch

**Before you start.** One machine with the terminal agent installed, on the same LAN as the switch.
Note the shop's `locationId` and the machine's `DeviceKey` (its computer name unless set). Confirm
nothing else holds the readers — the vendor demo, another till, a second API with
`Rfid:ServerReaders:Enabled`. This family allows one client and a second one takes the reader away.

**1 — One reader, cold.**
Power the reader, plug it into the switch, wait for its link light.
On the till, open **Admin → Settings → RFID** and press **Scan for readers**.

Expect: `Found 1 reader(s)` within about 10 seconds, then a row `RFID-001` with a **Discovered**
badge, the reader's serial, and four antennas all showing *Not assigned — reads nothing*.

If it says no agent is answering, you are on a machine without one — do this from a till.
If it finds nothing, check §12 point 6: the reader may be on a port not in the sweep list.

**2 — Assign one antenna.**
Set antenna 1 to a till. The unassigned-antenna warning should drop by one.
Within a few seconds the badge should become **Connected** — the agent picks the reader up from the
configuration without a restart.
Present a tag at antenna 1: it must appear on that till and on no other.

**3 — Prove the antenna is what routes, not the reader.**
Assign antenna 2 to a *different* till. Present a tag at antenna 2.
It must land on the second till. This is the whole point of the model; if it lands on the first, stop
and report it.

**4 — Add readers two through seven, one at a time.**
Plug each in, press **Scan for readers**, wait for the new row.

Expect: each appears as `RFID-002` … `RFID-007` with its own serial and antenna count. Seven readers
should give you 28 antennas if they are all four-port units — check the count matches the hardware,
because a unit that would not report it is filed as four.
Leave the settings page open on a second screen while you plug in the last one: the row must appear
**without pressing anything**. That is the SignalR push.
The header should read **"7 of 7 readers connected"** once every reader has at least one antenna
assigned and each has been picked up.

**5 — Pull a network cable.**
Unplug one reader from the switch.

Expect: within about 15 seconds that reader becomes **Not answering** — its machine is alive and
cannot reach it — and the header drops to **6 of 7**. The other six keep selling.
Plug it back in. It must return to **Connected** on its own, with no restart and no re-assignment.

**6 — Change its address.**
Reboot the router, or release the reader's DHCP lease so it comes back on a different IP.

Expect: the reader keeps the same `RFID-nnn` key, the `host` on the row updates to the new address,
**and every antenna assignment is exactly as you left it**. This is the test that matters most; it is
also `A_reader_that_changed_address_keeps_its_antenna_assignments` in the suite.

**7 — Switch one off.**
Disable a reader. It must show **Switched off**, drop out of the connected count, and not be counted
as a fault.

**8 — Restart the agent, then the server.**
Both must come back to the same seven readers and the same assignments with nothing re-entered.

**9 — Check the sweep is not noisy.**
Leave it for an hour and read `C:\ProgramData\Retail25\TerminalAgent\logs\agent-*.log`. You should see four sweeps,
each reporting how many addresses were swept and how many answered, and no report to the server on
the sweeps where nothing changed.

**If a reader will not identify.** The log line to look for is
`Swept N addresses on port P: M answered`. If `M` is zero the reader is not reachable — switch port,
power, subnet. If `M` is one or more but the count found is zero, something is listening and not
speaking the protocol: either it is not a reader, or the reader is powered but not answering, or
another client is holding it. That last one is the most common and the least obvious.
