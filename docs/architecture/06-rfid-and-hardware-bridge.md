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
   └─ RfidReaderService              parse EPC, antenna, RSSI, firstSeen/lastSeen, readCount
      └─ local pre-filter            drop RSSI < threshold; drop antenna zones ≠ Checkout
         └─ local ring buffer        coalesce duplicates within 250 ms (per reader)
            └─ PublishTags(batch)    SignalR → server, batched every 200 ms or 50 tags
               └─ TagIngestHandler
                  ├─ Redis debounce  SET tag:{epc} {stationId} NX PX {debounceMs}  (default 3000)
                  ├─ resolve         SerializedUnit by EPC (cached; Redis hash, 5-min TTL)
                  ├─ validate state  InStock ✓  | Sold/Void/Lost ✗ → CartLineRejected
                  ├─ validate loc.   unit.LocationId == station.LocationId
                  └─ AddRfidBatchCommand → cart lines → CartLinesAdded / TotalsChanged
```

### Why debouncing lives in two places

| Layer | Window | Purpose |
|---|---|---|
| Agent ring buffer | 250 ms | a reader reports the same tag 20×/second; this is pure noise reduction and must not cost a round trip |
| Redis (`SET NX PX`) | 3 s (configurable per reader profile) | **cross-station** arbitration and idempotency across agent reconnects — the reason the brief specifies Redis |

Redis keys: `tag:{epc}` (claim), `station:{id}:cart` (active cart), `cart:{id}` (serialized cart
state, TTL 12 h, write-behind to Postgres on suspend/complete), `epcmap:{epc}` (resolution cache).

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
| Reader unreachable | Reader status red; manual entry and barcode scanning continue to work normally. |
| Printer unreachable | Sale still completes; receipt is queued and reprintable — the legacy "printer jammed, reprint the last sale" story (p.12) is preserved. |
| Agent crashed | Windows service auto-restart; browser detects `PeripheralStatus` loss within 15 s. |

If Q4 comes back as "the store must keep selling through an outage", the design change is a
store-local API + Postgres replica with conflict-free number ranges per station — a Phase 8 item,
scoped but not built in v1.

## 7. Deployment & updates

- Packaged as a Windows Service (`winsw` or WiX MSI), auto-start, runs as a dedicated low-privilege
  account with COM-port access.
- Config: `appsettings.json` for `stationId` + `apiUrl` + a bootstrap secret; **device profiles are
  pulled from the server** so a peripheral change is a settings edit, not a site visit.
- Auto-update: agent polls `/api/v1/terminals/agent-version`, downloads a signed package, and
  restarts outside trading hours (configurable window).
- Structured logs to file + OTLP; `GET /status` doubles as the health probe.
