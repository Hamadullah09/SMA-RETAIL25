# Hardware-in-the-loop test matrix

Every peripheral this system drives, and what has to be true before a till goes on a shop floor.

Run it as a checklist against real equipment. A driver written to a specification and tested against
specification-derived bytes is not the same as one that has met the device — the RFID rows below
were all passing their unit tests for weeks while the reader was, in fact, never being opened.

## Status

| Device class | Status | Verified on |
|---|:---:|---|
| **UHF RFID reader (R2000 family / D2184)** | ✅ **Passed** | 2026-08-01 |
| LLRP reader | ⬜ Not attempted | — |
| Receipt printer (ESC/POS) | ⬜ Not attempted | — |
| Cash drawer (printer-kicked) | ⬜ Not attempted | — |
| Scale (serial, NCI/Toledo) | ⬜ Not attempted | — |
| Pole display (serial) | ⬜ Not attempted | — |
| Barcode scanner (keyboard wedge) | ⬜ Not attempted | — |

Everything except the RFID reader is unattempted because no such device has been available. That is
an operational gap, not missing code.

---

## R1 · UHF RFID reader — **passed 2026-08-01**

**Device:** UHF RFID D2184, TCP/IP at `192.168.0.178:4001`, ISO 18000-6C tags, four antennas.
**Protocol:** R2000-family *UHF RFID Reader Serial Interface Protocol* v3.1 (`ReaderProtocol.UhfSerial`).

| # | Check | Result |
|---|---|---|
| R1.1 | Agent opens a TCP session to the reader | ✅ `Connected to UHF Serial 192.168.0.178:4001, inventorying antennas all` |
| R1.2 | Frames reassemble across packet boundaries | ✅ no checksum failures across 189 batches |
| R1.3 | EPCs decode correctly | ✅ e.g. `E28069150000600B40A75995` — matches the vendor demo byte for byte |
| R1.4 | Antenna number is reported | ✅ `ant 1` |
| R1.5 | RSSI is reported and plausible | ✅ −47 to −69 dBm, varying with distance |
| R1.6 | Reads reach the server | ✅ 189 `IngestTagReadsCommand` batches, zero errors |
| R1.7 | Debounce collapses repeat reads | ✅ ×124–×140 folded into one observation per window |
| R1.8 | Tags appear on the till | ✅ live panel, EPC + antenna + RSSI + read count |
| R1.9 | Unknown tags are shown, not hidden | ✅ "Not recognised" until commissioned |
| R1.10 | Commissioning maps a tag to an item | ✅ 9 tags → `DEMO-0105`, state `InStock` |
| R1.11 | Commissioned tags resolve on the feed | ✅ immediately, after the cache-invalidation fix |
| R1.12 | Session gating with no sale open | ✅ "No sale is open at this till" |

### Still open on this device

| # | Check | Why it is still open |
|---|---|---|
| R1.13 | All four antennas simultaneously | Only antenna 1 was connected on the bench |
| R1.14 | Sustained 5,000 reads/sec from real hardware | The [benchmark](../../RFID_Throughput_Benchmark.md) proves the *pipeline* at that rate against synthesised frames. What a D2184 actually emits at full tilt is unmeasured |
| R1.15 | Reader power-cycle mid-sale reconnects | Not attempted |
| R1.16 | Network drop and recovery | Not attempted |
| R1.17 | Metal and liquid detuning | Needs the real merchandise mix |
| R1.18 | Read range against the checkout zone | Needs the physical counter |
| R1.19 | Two tills, adjacent antennas, no cross-reads | Needs a second till **and Redis** — cross-till arbitration is off under `Cache:Provider=InMemory` |

### Faults this row found

Recorded because they are the argument for doing hardware testing at all. Every one passed its unit
tests and would have shipped:

1. Agent presented its secret as a bearer token; every call refused, silently.
2. Machine principal resolved to zero permissions; profile fetch 403'd.
3. Device profile only applied on reconnect, so the agent kept the simulator for the life of the process.
4. Commissioned tags kept reading "Not recognised" — the EPC cache had no invalidation.

---

## R2 · LLRP reader

| # | Check |
|---|---|
| R2.1 | Agent connects on 5084 and completes the LLRP handshake |
| R2.2 | `ROSpec` added, enabled and started |
| R2.3 | Tag reports decode: EPC, antenna, peak RSSI, first/last seen |
| R2.4 | Keepalive maintained; reader-initiated disconnect handled |
| R2.5 | Reconnect with backoff after a power cycle |

---

## P1 · Receipt printer (ESC/POS)

| # | Check |
|---|---|
| P1.1 | Sale receipt prints: header, lines, tax breakdown, tender, change |
| P1.2 | Non-ASCII in item names does not corrupt the rest of the receipt |
| P1.3 | Paper-out mid-receipt reports a failure — **and the sale stays saved and reprintable** |
| P1.4 | Reprint produces an identical document |
| P1.5 | Training-mode receipt carries the "TRAINING — NOT A REAL SALE" watermark |
| P1.6 | Barcode on the receipt scans back |
| P1.7 | Cut command fires at the right point |

## P2 · Cash drawer

| # | Check |
|---|---|
| P2.1 | Opens on cash tender |
| P2.2 | Does **not** open on card tender unless the policy says so |
| P2.3 | `Drawer.Pop` from the UI opens it and writes an audit row |
| P2.4 | Drawer disconnected: sale completes, failure is reported |

## P3 · Scale

| # | Check |
|---|---|
| P3.1 | Weight read on demand, correct unit |
| P3.2 | Unstable reading is refused rather than guessed |
| P3.3 | Zero/tare from the UI |
| P3.4 | Weight × unit price matches the golden pricing files |
| P3.5 | Disconnected scale: manual entry still available |

## P4 · Pole display

| # | Check |
|---|---|
| P4.1 | Line item and running total appear as rung |
| P4.2 | Change due shown after tender |
| P4.3 | Idle message returns after the sale |
| P4.4 | Text longer than the display truncates rather than wrapping into nonsense |

## P5 · Barcode scanner

| # | Check |
|---|---|
| P5.1 | EAN-13 scan adds the right line |
| P5.2 | Code 39 from this system's own printed labels reads back |
| P5.3 | A scan mid-typing does not interleave with keyboard input |
| P5.4 | Unknown barcode prompts rather than failing silently |

---

## Running this matrix

1. One station, one device class at a time. Two unknowns at once means neither result is trustworthy.
2. Record the **model and firmware** of every device — "a receipt printer" is not a test record.
3. Log failures with the agent log excerpt attached.
4. Re-run the affected class after any change to `Retail25.TerminalAgent`.
5. A row is only ✅ when it passed **on hardware**. A passing unit test is not a passing row, which
   is the entire lesson of R1.
