# 11 — Delivery Roadmap

Phases are ordered so that **a store could actually run on the system at the end of Phase 3**, and
every later phase adds capability without destabilising the till.

Each phase lists concrete exit criteria. No phase is "done" until its criteria are demonstrable.

---

## Phase 0 — Foundation

**Build**
- Repo skeleton per [02](02-solution-structure.md): solution, 5 backend projects, Next.js app,
  central package management, `.editorconfig`, analyzers, warnings-as-errors.
- `deploy/docker-compose.yml`: postgres 16, redis 7, api, web, otel-collector.
- `ApplicationDbContext` + first migration (identity, OpenIddict, `Location`, `BusinessProfile`).
- MediatR pipeline behaviours (logging, validation, performance, transaction, idempotency).
- Serilog + OpenTelemetry, `/health/live`, `/health/ready`.
- CI: build, test, lint, container publish. Architecture tests enforcing dependency rules.

**Exit** — `docker compose up` yields a running API + web shell; `dotnet test` green; a migration
applies to a clean database; CI passes on a PR.

## Phase 1 — Identity & shell

**Build**
- OpenIddict (authorization code + PKCE, refresh rotation), ASP.NET Core Identity.
- Next.js BFF: login, callback, httpOnly encrypted session, silent refresh, logout.
- Permission catalogue + preset roles mapped from legacy levels 0–4; `[RequiresPermission]`.
- Staff PIN fast-switch; step-up approval mechanism.
- App shell: sidebar, command palette skeleton, global hotkey registry, theme tokens, `DataGrid`
  component with virtualization + saved views.
- Audit log + `AuditLogEntry` writing interceptor.

**Exit** — a user logs in, no token is reachable from JS (verified in an E2E test), a
permission-denied command returns 403, audit rows appear, `Ctrl+K` navigates.

## Phase 2 — Catalog, configuration & masters

**Build**
- `Product` (all 10 types), `Department`, `Category`, price levels, tax flags, notes, messages,
  substitute/tag-along/parent links, `Location`, sequences seeded from legacy "next number" values.
- `Customer`, `Supplier` with full legacy field sets.
- `TaxConfiguration` (effective-dated), `PosPolicy`, `StationSettings`, `TenderType`, `Currency`.
- Settings UI mirroring the legacy Setup tabs: Business ID, Taxes, POS, Printers, Hardware, Users,
  Options.
- Browse + Form View screens for inventory/customers/suppliers with live grid updates.
- Soft delete + restore ("Undelete Items").

**Exit** — a store's catalog, taxes and stations can be configured end-to-end through the UI; grids
update live across two browser sessions.

## Phase 3 — POS core ⭐ *first usable release*

**Build**
- Pricing & tax engine per [04](04-pricing-and-tax-engine.md), **golden-file suite written first**.
- Server cart in Redis; `PosHub`; the five-region POS UI; line detail drawer; F-key map.
- Identifier resolution: stock code, UPC, Code 39 scan, Type 2 random-weight, variant/serial prompt.
- Credits menu (discount, coupon, return, bottle, trade-in), unknown item, suspend/recall, void with
  supervisor step-up, per-sale tax override with legacy non-retroactive semantics.
- Split tender (cash/card-manual/on-account placeholder), change and `MinimumTender` rounding.
- `SalesTransaction` + ledgers; drawer sessions (float, pay in/out, close with variance); POS history.
- Receipt/invoice rendering; reprint; itemized sales log with filters and export.
- Fast Scan Mode, Auto Save, Confirm Before Saving, station defaults.

**Exit** — cashier completes a cash sale, a split-tender sale, a return and a void, entirely by
keyboard; totals match the golden files; the drawer closes with a correct variance report; a second
station sees stock change live. **A store could trade on this.**

## Phase 4 — RFID, matrix, serialized & hardware

**Build**
- `Retail25.TerminalAgent`: LLRP reader service, Redis debouncing, SignalR client, offline spool.
- `SerializedUnit` + EPC state machine; commissioning at receipt; bulk-read cart flow; Live RFID
  Feed UI with rejection reasons; antenna zoning, RSSI/read-count thresholds.
- RFID simulator in `tools/` so the flow is developable and testable without hardware.
- Matrix items (dimensions, variants, per-variant stock); serialized picking at sale.
- Cash drawer pulse, ESC/POS receipt printing (20/40-col + invoice), pole display, weigh scale.

**Exit** — 300 tags read into a cart in under 2 s with zero duplicates; a sold tag re-read is
rejected with a clear reason; drawer/printer/scale/pole all driven from the UI; reader outage shows
red and manual entry still works.

## Phase 5 — Money & commerce depth

**Build**
- `IPaymentGateway` + simulator, then the chosen processor (**Q1**); gift cards (issue on sale,
  balance inquiry, redemption) and gift certificates.
- Accounts receivable: on-account tender, invoices, partial payments, distribute-payment
  (oldest-first), late charges with the legacy penalty-first rules, void/refund, statements,
  receivables report, credit limits.
- Loyalty/bonus points end to end.
- Customer orders/back orders (+ "Fill This Order"), layaways, price quotes.
- Kits (explode on sale), case break (parent/child), stock receiving, manual adjustments.
- Purchasing: suppliers per item with ranking, PO generation with all six quantity strategies,
  PO editing grid, post order, post shipment with landed-cost allocation, partial receipts,
  PO printing/export, matrix orders.

**Exit** — a full purchase-to-sale-to-payment cycle runs: raise a PO, receive it with freight, sell
on account, take a partial payment, accrue a late charge, print a statement.

## Phase 6 — Back office, reporting & sync

**Build**
- Reports: sales (by product/department/client/period), inventory, top sellers, analysis, stock
  value, understock/overstock (legacy heuristics), on order, stock received, tax report, COGS,
  commissions, hours, reward points, open layaways, receivables.
- Labels & documents: price tags, bin/shelf labels, barcode labels with server-rendered Code 39,
  RFID tag encoding, Avery 5160/8160/8163/S-644N, COM10 envelopes, catalogue/price list.
- Staff: time clock, commission rules and calculation, date-range reporting.
- Bulk operations: batch price adjustment, batch tax flags, stock count import + variance/shrinkage,
  stock transfers between locations.
- Fiscal year-end close (archive, roll monthly to last year).
- Accounting connector (**Q2**): mapping UI, pre-flight validation, push/pull, daily POS revenue
  posting, PO→A/P bill, sync log.
- Training mode (legacy level 0).

**Exit** — every legacy report has a modern equivalent producing reconciling numbers; a full day's
revenue posts to the accounting system and the sync log shows request/response for troubleshooting.

## Phase 7 — Migration & cutover

**Build**
- `Retail25.Migration`: DBF/FPT reader, analyze → map → stage → validate → dry-run → import → verify.
- CSV importers with the documented legacy field orders (inventory, clients, suppliers, `.ASC`
  register sales, stock-count files).
- Reconciliation reports (item count, inventory value, AR balance, YTD sales) diffed against legacy
  reports.
- Cutover runbook: freeze, final legacy backup, import, verify, parallel-run window, rollback plan.

**Exit** — a real legacy dataset imports with a clean validation report and reconciling totals; a
rehearsed rollback completes inside the RTO.

## Phase 7.5 — SQL Server migration ✅ *done*

Requested after Phase 7, when SQL Server turned out to be a requirement rather than the default
assumption the original PostgreSQL choice had been made against. Scheduled here rather than folded
into Phase 8 because it touches the persistence layer and everything after it should be tested on
the engine that will run in production.

| Step | What |
|---|---|
| 1 | Provider swap: `UseSqlServer`, `Hangfire.SqlServer`, `Testcontainers.MsSql`. |
| 2 | Dialect: `jsonb` → `nvarchar(max)`, filtered-index predicates, sequence syntax. |
| 3 | **Decimal precision convention.** The one that would have shipped a bug: an unspecified `decimal` was arbitrary-precision `numeric` on PostgreSQL and becomes a truncating `decimal(18,2)` on SQL Server. Sixty-six properties, including tax amounts and every cost. |
| 4 | Migrations regenerated from the model — the old ones carry Npgsql annotations and will not compile. |
| 5 | Test fixtures onto SQL Server, including the drop/recreate that SQL Server will not do while sessions are connected. |
| 6 | Whole suite green on the new engine, and one operation rewritten because of what it exposed. |

**Exit criteria — met.** 800 tests pass against SQL Server 2019. The API boots, migrates, seeds
(136 products, 1,152 tags) and installs its Hangfire objects. The tag importer runs end to end.

**What it cost beyond the mechanical work:** one real defect. Validating a migration batch wrote a
verdict to every staging row through the change tracker — twenty thousand UPDATE statements for a
twenty-thousand-item export. Npgsql's thousand-statement batches hid it; SQL Server's tens did not,
and it went from seconds to sixteen minutes. Rewritten as a set operation. Detail in
[12](12-schema-reference.md#the-move-from-postgresql).

**Still open:** `row_version` is a column that no longer claims to be a concurrency token, because
it never was one — nothing maps or checks it. Tracked separately; it is a pre-existing gap the
migration surfaced rather than caused.

---

## Phase 8 — Hardening & optional extensions

Load testing to 2× target · hardware-in-the-loop matrix · security review + pen test ·
restore rehearsal · documentation and staff training material.
Optional, subject to your priorities: offline store mode (Q4), supplier web-submit/EDI,
mobile stocktaking PWA for handheld RFID, advanced loss prevention using exit-antenna zones.

---

## Risk register

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| R1 | Pricing/tax parity defects reach production | **Critical** | Golden files before code; property tests; differential replay against legacy data; parallel-run window in Phase 7 |
| R2 | RFID false reads put wrong items in carts | **High** | Antenna zoning, RSSI/read-count thresholds, EPC state rejection, confirm-batch UX, Phase 4 field trial before rollout |
| R3 | Payment processor choice arrives late (Q1) | High | Gateway port + simulator from Phase 5 start; the entire payment UX is testable without the vendor |
| R4 | Legacy data quality blocks migration | High | Analyze/validate phases produce actionable reports early; run an analysis pass in Phase 2 even though import is Phase 7 |
| R5 | Feature volume (~180 behaviours) causes drift | Medium | Parity matrix is the checklist; each row cites a guide page; DoD requires ticking a row |
| R6 | Staff adoption — muscle memory is 15+ years deep | Medium | F-key map preserved exactly; same tab order in the item detail drawer; training mode; cheat sheet overlay |
| R7 | Hardware variance across stores (printers, drawers, scales) | Medium | Everything is a configurable escape-code profile, as in the legacy system; HIL test matrix |
| R8 | Single on-prem host is a SPOF | Medium | Documented restore inside RTO; replica + scale-out path designed in, enabled if you want it |

## What I need from you to start

1. **Approval of this architecture** (or the changes you want).
2. Answers to **Q1–Q7** in the [README](README.md) — none block Phase 0–2, but Q6 (tax
   jurisdiction) and Q7 (deployment target) shape early work, and P1–P5 in
   [04](04-pricing-and-tax-engine.md) should be confirmed before Phase 3.
3. Confirmation of **priority order** if you want it different from the phases above — e.g. if RFID
   is the whole point of the project for you, Phase 4 can be pulled forward and interleaved with
   Phase 3 at some cost in rework.
