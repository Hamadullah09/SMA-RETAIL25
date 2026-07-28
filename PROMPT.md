# PROMPT.md

The standing brief for Retail25, and the record of every decision taken against it.

Maintained alongside the work: when a requirement is added, answered or changed, it is recorded
here. `CLAUDE.md` says *how* to build; this says *what* and *why*.

---

## 1. The role

Principal Enterprise Solutions Architect and Senior Full-Stack .NET Engineer. Architect, design and
write production-ready code to rebuild the legacy Retail25 POS from scratch as a modern, web-based,
enterprise POS and ERP application. Clean Architecture, CQRS via MediatR, SOLID.

## 2. What is being built

Retail25 modernises a self-hosted retail system onto .NET 8 and Next.js. The file-based DBF layer,
manual network drive mappings and WinForms interfaces are replaced by an async, event-driven web
architecture. Standard barcode scanning is joined by real-time bulk RFID reading, **while keeping
full backward-compatible item lookup**.

## 3. Technology

| Layer | Choice |
|---|---|
| Backend | C# / .NET 8 LTS Web API, Clean Architecture, CQRS via MediatR |
| Identity | OpenIddict 5.x + ASP.NET Core Identity, authorization code + PKCE |
| Data | PostgreSQL via EF Core 8, explicit migrations |
| Realtime | ASP.NET Core SignalR — RFID streaming and multi-station sync |
| Cache | StackExchange.Redis — tag debouncing, session and cart storage |
| Frontend | Next.js 14 App Router, React, TypeScript |
| UI | Tailwind + Radix / shadcn primitives |
| State | TanStack Query + Zustand |
| Client security | httpOnly secure cookies with PKCE. **No JWTs in localStorage.** |
| Hardware bridge | .NET 8 `IHostedService` per till: LLRP/TCP for RFID, `System.IO.Ports` for scales, ESC/POS pulses (`27,112,0,50,250`) for drawers, serial pole displays |

## 4. Functional scope

Required modules, each to be fully implemented:

- **POS engine** — bulk RFID, manual SKU, barcode; multi-tax with overrides; percentage add-on
  charges; subtotal discounts; coupons, bottle returns, trade-ins, gift certificates, returns;
  split tender across cash, credit, debit, gift card and accounts receivable; fast scan, auto-save,
  hold/park, receipt printing, signature line.
- **Inventory and serialized EPC** — 24 to 96 character EPC mapped to SKUs; Type 2 random-weight
  items evaluated against `Price 1`; department, category, supplier, reorder points, cost, price,
  gross margin; Code 39 label printing; gift card inventory with zero-tax flags and issue-on-sale.
- **Customers and AR** — profiles, addresses, credit limits, balances; open invoices from POS,
  partial payments, payment history.
- **Purchasing** — supplier directory; purchase order creation, editing, receiving, and conversion
  into A/P bills with a default 30-day due date.
- **ERP and accounting sync** — two-way REST/ETL replacing QB-XML, syncing customers, inventory,
  suppliers and invoices; daily POS revenue batched to GL accounts.
- **Data migration** — administrative import of legacy inventory, client, invoice and supplier
  records from `.DBF` and flat backups.
- **Multi-station concurrency** — SignalR broadcasts updating stock, carts and grids across all
  workstations without manual refresh.

> **Scope correction made during design:** the brief's matrix is a subset of what the user guide
> documents. Matrix and kit items, layaways, quotes, customer back-orders, bonus points, price
> levels and break points, staff hours and commissions, multi-location transfers, drawer float and
> pay in/out, late charges and year-end close are all in the guide and therefore in scope. The full
> enumeration is `docs/architecture/01-scope-and-parity.md`.

## 5. UI and interaction

**Anti-AI design guarantee.** No generic bento-box cards, no glowing neon gradients, no cliché
dashboard templates. Professional enterprise minimalism: high-contrast slate/zinc, subtle borders,
sharp typography, precise spatial hierarchy, functional status indicators.

**Reference.** Odoo POS layout efficiency and split-pane simplicity — left order lines with totals,
numpad and payment; right a searchable product grid with categories — adapted for instantaneous
bulk RFID scanning rather than manual clicking. **Different visual design and styling from Odoo.**

**Cognitive ergonomics.**
- Miller's Law: at most five distinct functional groups on the POS screen — live RFID feed and
  cart, summary totals, payment matrix, customer context, top navigation and status.
- Visual chunking: primary actions (Pay, Hold, Cancel) weighted prominently; secondary actions
  (Notes, Overrides) nested cleanly.
- Keyboard-first: full coverage via a command palette (`Ctrl+K`) and explicit function keys
  (`F4` total, `F3` credits, `F12` close) for high-speed operation without a mouse.

**Usability requirement:** the interface must be understandable by a layperson with no training.

## 6. Standing constraints

1. **Nothing hardcoded.** Every functionality in the document must be data-driven and configurable.
   This is the defining constraint — see `CLAUDE.md`.
2. **Every functionality in the user guide must be present and fully functional**, verified by the
   benchmark rather than asserted.
3. The user guide PDF is the parity contract.

## 7. Decisions taken

Approved 2026-07-27. All defaults accepted.

| # | Question | Decision |
|---|---|---|
| Q1 | Payment processor | `IPaymentGateway` port + simulator. Real processor is a config-selected adapter; no vendor name in Domain or Application. |
| Q2 | Accounting system | `IAccountingConnector` port. CSV and generic REST first, QuickBooks Online after. |
| Q3 | RFID reader | LLRP behind `IRfidReader`, plus a simulator. Endpoint, antennas and RSSI are database rows. |
| Q4 | Stores / offline | Single-tenant, multi-location. Agent-side store-and-forward. Full offline mode deferred. |
| Q5 | Legacy data | Importer built against the documented v2.5 DBF layout and the supplied CSV; column maps are editable YAML. |
| Q6 | Tax jurisdiction | Legacy Tax1/Tax2 + compound + add-on + inclusive/exclusive implemented exactly, behind `ITaxPolicy`. All rates are rows. |
| Q7 | Deployment | Docker Compose on one on-prem host; HQ/store separation is config only. |
| P1 | Sale price vs break points | Break points and explicit level selection outrank the date-ranged sale price. Reorderable from data. |
| P2 | Subtotal-discount tax proration | Prorated by line net; rounding residue to the largest line. |
| P3 | Rounding | Away-from-zero, once per line per tax. Scale and mode per currency. |
| P4 | Cash rounding | Cash tenders and change round to `MinimumTender`; non-cash exact. |
| P5 | Loyalty clawback on return | At the original earn rate, stored on the ledger entry. |

## 8. Supplied data

`RETAIL PLUS 2.5/TSTINV11.csv` — 28 rows, 89 columns, plus a legend block.

Stated by the user:
- EPC numbers are RFID reads.
- There are four unique prices: **daily customer, retailer, wholesaler**, and a fourth.
- `ONHAND` is the quantity of the product.
- 72 units of `COLUMBIA POLO` share one PLU but **each unit has its own EPC**.

Analysis and the schema it implies: `docs/architecture/12-csv-data-model.md`. Headlines:

- Full expansion is **1,293 units across 25 products**; only 28 EPCs are supplied, so the importer
  creates one tagged unit and the rest `AwaitingCommission` rather than fabricating tag IDs.
- Price levels are named in a `price_level_definition` table. Level 4 is seeded **Distributor** —
  an assumption, since only three were named. Renaming is a settings change.
- Eight data-quality findings need decisions, notably **non-unique PLUs**
  (`9988776654321140` is two different products), one product spanning four PLUs, and negative
  on-hand on three rows.

## 9. Open questions

| # | Question | Blocks |
|---|---|---|
| A | What should price level 4 be called? Seeded as *Distributor*. | Cosmetic only |
| B | Duplicate PLUs — prompt the cashier to choose, or merge into one product? | CSV importer |
| C | `AERO WOMENS SANDALS` spans four PLUs — separate products, or variants of a matrix item? | CSV importer |
| D | Negative on-hand — import honestly and flag, or clamp to zero? Recommendation: import honestly; clamping hides real shrinkage. | CSV importer |

## 10. Delivery order

Phases are in `docs/architecture/11-delivery-roadmap.md`. A store can trade at the end of Phase 3.

Progress is measured by `docs/BENCHMARK.md`, which verifies each capability by reflection rather
than by assertion.

---

## Change log

| Date | Change |
|---|---|
| 2026-07-27 | Architecture approved; Q1–Q7 and P1–P5 answered with defaults. |
| 2026-07-27 | Scope corrected: user guide, not the brief's matrix, is the parity contract. |
| 2026-07-27 | Odoo POS adopted as layout reference with distinct styling; layperson usability added as a requirement. |
| 2026-07-27 | `TSTINV11.csv` supplied; per-unit EPC model and named price levels adopted. |
| 2026-07-27 | Parity benchmark introduced as the measure of completeness. |
| 2026-07-28 | `CLAUDE.md` and `PROMPT.md` created and adopted as maintained records. |
| 2026-07-28 | Dependencies installed and the system run end to end for the first time — see below. |

---

## First live run — 2026-07-28

Installed on the development machine: **PostgreSQL 16.14** (winget), **Node.js 24.18**,
**npm 11.16**, **.NET 8 runtime with SDK 9/10**, **dotnet-ef 8.0.29**.

Verified working:

| Step | Result |
|---|---|
| Migrations applied to an empty database | 60 tables created |
| Seeding | Currency, location, 4 named price levels, tax configuration, POS policy, 7 tender types, station, 5 roles, administrator |
| `GET /health/live` and `/health/ready` | `Healthy` |
| `POST /api/v1/auth/login` | Session cookie issued; `GET /auth/me` returns the full permission set |
| Sale: 1 × COLUMBIA POLO at 30.00 | subtotal 30.00, GST 1.50, PST 2.10, **total 33.60** |
| Volume breaks on the same line | qty 2 → 27.99 (Retailer), qty 4 → 24.79 (Wholesaler), qty 6 → 19.99 (Distributor), each with `PriceOrigin.Break` |
| Frontend | Builds to 11 routes; `/`, `/pos`, `/inventory` all serve 200 |

Two defects were found only by running it, and fixed:

1. `IIdempotencyStore` was registered as a MediatR behaviour with no implementation, so **every
   command failed at request time** while start-up looked clean.
2. `appsettings.json` defaulted the Redis connection to `localhost:6379`, so a machine without
   Redis logged connection failures indefinitely while appearing configured. It now defaults to
   empty, which selects the in-memory cart store.

Not yet verified: SignalR broadcasting between two stations, and the terminal agent.
