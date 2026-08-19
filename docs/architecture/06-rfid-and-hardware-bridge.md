# 06 — RFID & Hardware Bridge

Browsers cannot open LLRP sockets, COM ports or cash drawers. Every peripheral is owned by
**`Retail25.TerminalAgent`**, one .NET 8 Worker Service per physical POS machine, which talks to the
server over SignalR and exposes a tiny localhost API for browser-initiated actions.

```
┌──────────── POS machine ─────────────┐        ┌──────── Server ────────┐
│  Browser (Next.js)                   │  HTTPS │  Retail25.Api          │
│    │  fetch → localhost:8477         │◄──────►│   PosHub               │
│    ▼                                 │        │   TerminalHub          │
│  Retail25.TerminalAgent (service)    │◄──────►│   Redis (debounce)     │
│   ├ RfidReaderService  ── LLRP/TCP ──┼─► RFID reader (antennas 1..4)   │
│   ├ ScaleService ───── RS-232 ───────┼─► Mettler-Toledo (W / Z)        │
│   ├ ReceiptPrinter ─── ESC/POS ──────┼─► slip printer                  │
│   ├ CashDrawerService ── pulse ──────┼─► 27,112,0,50,250               │
│   └ PoleDisplayService ── serial ────┼─► Logic Controls PD3000         │
└──────────────────────────────────────┘        └────────────────────────┘
```

---

## 1. EPC ↔ product mapping

- `SerializedUnit.Epc` — 24–96 hex characters (SGTIN-96 is 24 hex; the column is `varchar(96)`,
  uppercase-normalised, unique partial index).
- One EPC = **one physical unit**. Quantity is never encoded in a tag; ten shirts are ten EPCs.
- Mapping is created at: goods receipt (commissioning), label printing (encode + associate), or
  bulk import (`Retail25.Migration` / CSV).
- Unmapped EPC seen at checkout → `epc.unknown`, surfaced in the feed as an actionable row
  ("associate to item…" for a supervisor), never silently dropped.

### EPC state machine

```
Provisioned ──commission──► InStock ──read at checkout──► InCart ──sale──► Sold
     ▲                         │  ▲                          │              │
     │                         │  └────release/timeout───────┘              │
     │                    transfer│                                         │
     │                         ▼  │                                    return│
   (label)                 Transferred                                      ▼
                                                                        Returned ──► InStock
Any ──shrinkage/write-off──► Lost      Sold ──void sale──► InStock
```

Transitions are guarded in the domain and enforced by a DB check constraint + optimistic
concurrency. `InCart → Sold` is a compare-and-swap: a second station cannot sell the same unit.

---

## 2. Tag ingest pipeline

```
LLRP RO_ACCESS_REPORT
   └─ ReaderSession                  one per reader; parse EPC, antenna, RSSI, seen times, readCount
      └─ local pre-filter            drop RSSI < threshold (only where the reader measured it)
         └─ TagBuffer                coalesce duplicates within 250 ms, keyed on (reader, EPC)
            └─ PublishReaderTags     SignalR → server, addressed by reader, every 200 ms or 50 tags
               └─ IngestReaderTagsCommand
                  └─ TagObservationRouter
                     ├─ (readerId, antenna) → ReaderAntennaAssignment → stationId
                     ├─ unassigned antennas reported, never dropped
                     └─ one batch fans out to as many stations as it has antennas
                        └─ IngestTagReadsCommand, per station
                           ├─ debounce      SET tag:{epc} {stationId} NX PX {debounceMs}
                           ├─ resolve       SerializedUnit by EPC
                           ├─ validate      InStock ✓ | Sold/Void/Lost ✗ → CartLineRejected
                           ├─ validate loc. unit.LocationId == station.LocationId
                           └─ AddRfidBatchCommand → cart lines → CartLinesAdded / TotalsChanged
```

### Reader + antenna → station

The load-bearing change, and the one that dates everything written before it.

A reader used to *be* a station: `ReaderProfile.StationId` decided where a read landed, so a
four-antenna reader could only ever watch one till. It is now the **antenna** that stands for a
station, and the pair `(readerId, antennaNumber)` is the routing key:

```
Device (a PC, identified by a key that survives DHCP)
  └─ RfidReader (identified by serial where the protocol reports one; host and port are mutable)
       ├─ Antenna 1 → Station ST-001
       ├─ Antenna 2 → Station ST-002
       ├─ Antenna 3 → Station ST-003
       └─ Antenna 4 → Station ST-004
```

Routing on the antenna alone would be wrong in a way that only appears at scale: antenna 1 exists on
every reader in the building, and 63 of them would resolve to one till.

`UNIQUE(ReaderId, AntennaNumber)` is enforced by the database rather than checked in a handler,
because a check-then-save does not survive two administrators saving at once and the failure it
admits is silent — one antenna feeding two tills, both ringing the same garment, neither looking
wrong.

Scale is rows, not code. 63 readers × 4 antennas = 252 stations run the same routing as one reader
with four. Measured against SQL Server at that size: routing a batch 2 ms, the health dashboard
71 ms, a 63-reader machine's configuration 169 ms.

**Not yet proven against hardware.** No test here has driven two physical readers; the numbers above
are the database and the routing, not the radios.

### Why debouncing lives in two places

| Layer | Window | Purpose |
|---|---|---|
| Agent ring buffer | 250 ms | a reader reports the same tag 20×/second; this is pure noise reduction and must not cost a round trip |
| Redis (`SET NX PX`) | 3 s (configurable per reader profile) | **cross-station** arbitration and idempotency across agent reconnects — the reason the brief specifies Redis |

Redis keys: `tag:{epc}` (claim), `station:{id}:cart` (active cart), `cart:{id}` (serialized cart
state, TTL 12 h, write-behind to SQL Server on suspend/complete), `epcmap:{epc}` (resolution cache).

### Anti-false-positive controls

Bulk RFID at a checkout desk reads things you did not intend to sell. Controls, all
per-`ReaderProfile`:

1. **Antenna zoning** — only antennas tagged `Checkout` feed carts. `Exit` antennas feed loss
   prevention; `Receiving` feeds goods-in; `Shelf` feeds cycle counting.
2. **RSSI floor** — tags weaker than the threshold are ignored (a tag on the next shelf is quieter
   than one in the basket).
3. **Read-count floor** — require *N* reads within the window before a tag is accepted.
4. **Session gating** — the reader only runs in `Inventory` mode when a cart is active and the
   cashier has pressed *Read* (or continuous mode, if the store prefers, configurable).
5. **Explicit confirmation** — the Live RFID Feed shows arriving tags with a short "settling"
   animation; the cashier confirms the batch before it is priced. Configurable to auto-accept.
6. **State rejection** — `Sold`, `Void`, `Lost` units are always rejected with a visible reason.

## 3. `RfidReaderService` sketch

```csharp
public sealed class RfidReaderService(
    IRfidReader reader,               // LLRP impl | Http | Mqtt | Simulator  (Q3)
    ITagBuffer buffer,
    IServerConnection server,
    IOptionsMonitor<ReaderProfile> profile,
    ILogger<RfidReaderService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await reader.ConnectAsync(profile.CurrentValue, ct);      // LLRP: SET_READER_CONFIG,
                await reader.StartRoSpecAsync(ct);                        // ADD_ROSPEC, ENABLE, START
                await foreach (var read in reader.ReadsAsync(ct))         // channel from the LLRP callback
                {
                    if (read.Rssi < profile.CurrentValue.RssiThresholdDbm) continue;
                    if (!profile.CurrentValue.IsCheckoutAntenna(read.Antenna)) continue;
                    buffer.Offer(read);                                   // 250 ms coalescing window
                }
            }
            catch (Exception ex) { log.ReaderFaulted(ex); await Backoff(ct); }
            finally { await reader.SafeStopAsync(); }
        }
    }
}
```
A separate `TagFlushService` drains `ITagBuffer` on a 200 ms timer and calls
`server.PublishTagsAsync(batch)`. Keepalive/heartbeat every 5 s; three missed keepalives ⇒
reconnect and emit `TagStreamStatus{ readerOnline: false }` so the UI shows a red strip rather than
silently reading nothing.

### `IRfidReader` implementations

| Protocol | Class | Transport | Notes |
|---|---|---|---|
| `Llrp` | `LlrpRfidReader` | TCP | EPCglobal LLRP 1.0.1; a continuous ROSpec, reported as tags arrive. |
| `UhfSerial` | `UhfSerialRfidReader` | TCP | The R2000-family "UHF RFID Reader Serial Interface Protocol" (v3.1) spoken by devices such as the D2184B — either the reader's own network interface, or a serial-to-Ethernet bridge (an IPort module or equivalent) in front of a unit wired via RS-232. Unlike LLRP this protocol has no push-forever mode: `cmd_real_time_inventory` (`0x89`) runs one round and stops, so the agent re-issues it back-to-back for as long as the reader is started, cycling `cmd_set_work_antenna` (`0x74`) across the profile's `Checkout` antennas between rounds. |
| `Simulator` | `SimulatedRfidReader` | — | No hardware; backs the RFID demo/simulator and exercises the debounce and rejection paths in dev. |

Both hardware readers reuse the same `ReaderProfileContract.Host`/`Port` fields — a store swapping
from an LLRP-speaking reader to a D2184B is a protocol dropdown change, not a new deployment shape.

## 4. Other peripherals

| Device | Transport | Contract |
|---|---|---|
| **Cash drawer** | ESC/POS pulse over the printer port, or serial | Configurable trigger string, decimal-ASCII, default Epson `27,112,0,50,250`, Star `07`; repeat count; `OpenDrawerOnPrint` (guide p.80) |
| **Slip printer** | Raw bytes to port/USB/network | `SetupCommand`, `CutterCommand` (Epson `27,105`, Star `27,100,48`), `RedCommand` `27,114,49` / `BlackCommand` `27,114,48`, page eject, extra copy on card sales, 20-col / 40-col / full invoice modes (p.78–80) |
| **Pole display** | `System.IO.Ports`, PD3000 | Line 1 ≤ 45 chars scrolling idle, line 2 ≤ 19 chars fixed idle; live item/price during a sale (p.80–81) |
| **Weigh scale** | RS-232 | `GetWeight` char (default `W`), `ZeroScale` char (default `Z`), configurable baud/parity/stop bits (p.81) |
| **Card terminal** | Vendor SDK / cloud | Behind `IPaymentGateway` — **Q1**. Semi-integrated (terminal talks to the processor directly) strongly preferred: it keeps card data out of our process and shrinks PCI scope to SAQ-C/P2PE |
| **Label printer** | Zebra ZPL / ESC-POS / Windows driver | Code 39 rendered server-side; RFID label printers additionally receive an EPC encode command and return the written EPC for association |

## 5. Local API (browser → agent)

`http://127.0.0.1:8477`, bound to loopback only, paired to the browser session with a token minted
by the server when the station registers.

```
GET  /status               → { agentVersion, reader, printer, scale, drawer, poleDisplay }
POST /scale/weight         → { value, unit, stable }          (F-key "Get Weight")
POST /scale/zero
POST /drawer/open          → server-side permission check first; agent verifies token
POST /reader/mode          → { mode: "off" | "onDemand" | "continuous" }
POST /print/test
```
Printing and drawer-pop for *sales* go server → agent over `TerminalHub` (authoritative, auditable).
The local API exists only for actions with no server-side meaning (scale reads, self-tests) and for
sub-100 ms latency where it matters.

## 6. Offline behaviour

The agent is resilient; the *business* is not fully offline-capable in v1 (see Q4).

| Failure | Behaviour |
|---|---|
| Server unreachable | Agent queues tag reads and status to a local SQLite spool (bounded, 24 h), retries with backoff; **carts do not advance**. UI shows an unmistakable offline banner; cashier can fall back to cash-and-paper per store policy. |
| Reader unreachable | That reader's session backs off and retries; **its siblings keep reading**, because each reader has its own session, connection and backoff. The stations it feeds show `ReaderOffline` on the health screen while the rest of the estate is unaffected. Manual entry and barcode scanning continue normally. |
| Machine unreachable | Every station fed by that PC shows `AgentOffline`, stated once against the machine rather than repeated per station — liveness is a property of the PC, and the health screen derives station availability downward from it. |
| Antenna unassigned | Reads resolve to no station and are **reported, not dropped**. This is the most common commissioning mistake and is otherwise invisible: the reads happen, nothing reaches a till, and a dead antenna, a bad cable and an unconfigured one all look identical. |
| Printer unreachable | Sale still completes; receipt is queued and reprintable — the legacy "printer jammed, reprint the last sale" story (p.12) is preserved. |
| Agent crashed | Windows service auto-restart; browser detects `PeripheralStatus` loss within 15 s. |

If Q4 comes back as "the store must keep selling through an outage", the design change is a
store-local API + a SQL Server replica with conflict-free number ranges per station — a Phase 8 item,
scoped but not built in v1.

## 7. Deployment & updates

- Packaged as a Windows Service (`winsw` or WiX MSI), auto-start, runs as a dedicated low-privilege
  account with COM-port access.
- Config: `appsettings.json` for `apiUrl`, the machine's `deviceKey`, and a one-time `enrolmentCode`
  generated from Administration → Settings → RFID. **Everything else is pulled from the server** —
  which readers this machine drives, where they are, and what each antenna stands for — so adding a
  reader or re-pointing an antenna is a settings edit rather than a site visit.
- Enrolment: the code is single-use and expires in 24 hours. The agent presents it once, is told
  which machine it is, and is handed the durable credential over TLS; that credential is written
  under ProgramData and used from then on. The file an installer carries is therefore worth nothing
  after first start and nothing to anyone else before it.

  *Known gap:* what enrolment currently hands back is the secret **every** agent shares. On one till
  that is untidy; across 252 it cannot be rotated for a single machine, and one compromised PC is
  every PC. `IAgentCredentialProvider` is the seam where a per-device secret replaces it — the seam
  exists, the improvement does not yet.
- A machine may drive several readers. Each gets its own session, so one reader failing costs that
  reader alone. `DesiredReaders()` is the single place that decides how many exist.
- Auto-update: agent polls `/api/v1/terminals/agent-version`, downloads a signed package, and
  restarts outside trading hours (configurable window).
- Structured logs to file + OTLP; `GET /status` doubles as the health probe.
