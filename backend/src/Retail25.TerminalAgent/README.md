# Retail25.TerminalAgent

One process per POS machine, owning every peripheral (doc 06).

Browsers cannot open LLRP sockets, COM ports or cash drawers. The agent owns all of it and speaks
only two things: SignalR to the server, and a loopback HTTP API to the browser on the same machine.

## What it does

| Service | Responsibility |
|---|---|
| `RfidReaderService` | Keeps a reader connected, reconnecting with backoff forever |
| `LlrpRfidReader` | LLRP 1.0.1 over TCP: SET_READER_CONFIG, ADD/ENABLE/START_ROSPEC, RO_ACCESS_REPORT parsing, keepalive |
| `SimulatedRfidReader` | The same interface with no hardware, so the flow is developable and testable |
| `TagBuffer` | 250 ms coalescing window — a reader reports one tag twenty times a second |
| `TagFlushService` | Publishes batches every 200 ms; spools when the server is unreachable |
| `SqliteTagSpool` | Durable, bounded queue that survives a power cut |
| `PeripheralCoordinator` | Printer, drawer, scale and pole display, behind one lock |
| `EscPosRenderer` | Receipt → bytes, using the profile's own escape codes |
| `HeartbeatService` | Tells the server the hardware is alive, every 5 s |
| `ProfileRefreshService` | Pulls device settings from the server; the safety net behind the hub push |
| `LocalApiEndpoints` | `127.0.0.1:8477` — scale reads, self-tests, status |

## Configuration

Only four things are configured locally. Everything else — reader endpoint, antenna zoning,
thresholds, escape codes, port settings — is pulled from the server, because those are exactly the
settings that change when a store swaps hardware, and editing a file on each till is the site visit
the design exists to avoid.

```json
{
  "Agent": {
    "StationId": "…",
    "ApiUrl": "http://server:5000",
    "BootstrapSecret": "…",
    "LocalApiUrl": "http://127.0.0.1:8477"
  }
}
```

Two switches help on a bench:

- `ForceReaderProtocol: "Simulator"` — run the whole flow with no reader attached.
- `DisablePeripherals: true` — on a developer machine, opening COM1 either fails or talks to
  something that is not a till printer.

## Running it

```bash
dotnet run --project backend/src/Retail25.TerminalAgent
```

It starts even when the server is down. That is deliberate: a till whose server is unreachable still
needs its local API for the scale, its reader running, and its spool collecting.

```bash
curl http://127.0.0.1:8477/status
```

## Driving it without hardware

```bash
dotnet run --project tools/Retail25.RfidSimulator -- --station <guid> --mode burst --count 300
```

`--mode stray` publishes reads that the reader profile should refuse — too weak, wrong antenna. If
those reach the cart, the anti-false-positive controls are not doing their job.

## Installing as a service

`UseWindowsService` is already wired and no-ops when not running as one, so the same binary works on
a bench and in a shop.

```powershell
sc.exe create "Retail25 Terminal Agent" binPath= "C:\Retail25\Agent\Retail25.TerminalAgent.exe" start= auto
sc.exe description "Retail25 Terminal Agent" "RFID, printer, drawer, scale and pole display for this till"
sc.exe start "Retail25 Terminal Agent"
```

Run it as a dedicated low-privilege account with COM-port access. Logs and the spool live under
`%LOCALAPPDATA%\Retail25\TerminalAgent`.

## What is not built

Auto-update. `GET /api/v1/terminals/agent-version`, the signed package download and the
restart-outside-trading-hours window are specified in doc 06 §7 and are not implemented; the agent
reports its version on every heartbeat, which is the half that the server side needs.
