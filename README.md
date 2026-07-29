# Retail25 — Architecture Dossier

Modernization of **Retail Plus for Windows 2.5** (True North Computer Services, 2008) into a
self-hosted, web-based enterprise POS + Inventory + AR/AP platform.

| | |
|---|---|
| **Backend** | C# / .NET 8 LTS, Clean Architecture, CQRS via MediatR |
| **Database** | PostgreSQL 16 + EF Core 8 (explicit migrations) |
| **Identity** | OpenIddict 5.x + ASP.NET Core Identity, Authorization Code + PKCE |
| **Realtime** | ASP.NET Core SignalR |
| **Cache/Debounce** | Redis (StackExchange.Redis) |
| **Frontend** | Next.js 14 App Router, React 18, TypeScript, Tailwind + Radix/shadcn, TanStack Query, Zustand |
| **Edge** | `Retail25.TerminalAgent` — .NET 8 `IHostedService` on each POS machine (RFID/LLRP, scale, drawer, pole display, ESC/POS) |
| **Source of truth for scope** | `User Guide Retail25.pdf` (v2.5, 13 pp.) + the project brief |

## Read in this order

| # | Document | What it settles |
|---|---|---|
| 01 | [Scope & Legacy Parity Matrix](01-scope-and-parity.md) | Every legacy feature, its replacement, and its phase. What is deliberately dropped. |
| 02 | [Solution Structure](02-solution-structure.md) | Full directory tree, backend + frontend, project references, dependency rules. |
| 03 | [Domain Model & ERD](03-domain-model.md) | Aggregates, entities, invariants, ERDs, ledger design. |
| 04 | [Pricing & Tax Engine](04-pricing-and-tax-engine.md) | The deterministic pipeline. Highest-risk logic in the system — specified separately. |
| 05 | [Application Layer, API & Realtime](05-application-api-realtime.md) | CQRS slices, endpoint map, SignalR hub contracts, domain events, outbox. |
| 06 | [RFID & Hardware Bridge](06-rfid-and-hardware-bridge.md) | EPC lifecycle, LLRP ingest, Redis debouncing, peripherals, offline queue. |
| 07 | [Security & Identity](07-security-and-identity.md) | OpenIddict/PKCE flow, BFF cookies, legacy access levels 0–4 → permissions, audit. |
| 08 | [Frontend & UX](08-frontend-ux.md) | Miller's-Law POS layout, keyboard map, screen inventory, design system rules. |
| 09 | [Integration & Data Migration](09-integration-migration.md) | Accounting sync (replaces QB-XML), DBF importer, multi-store replication (replaces FTP). |
| 10 | [NFRs, Deployment, Testing](10-nfr-deployment-testing.md) | Topology, performance budgets, observability, backup/DR, test strategy. |
| 11 | [Delivery Roadmap](11-delivery-roadmap.md) | Phases 0–8, exit criteria per phase, risk register. |

## Architectural stance in one page

1. **The transaction is a ledger, not a row.** Sales, stock movements, AR balances and drawer
   totals are append-only ledgers with derived snapshots. The legacy system mutated DBF records in
   place and needed `Rebuild`/`Reindex` commands to recover; we make corruption structurally
   impossible and make "void", "refund" and "year-end close" ordinary ledger operations.
2. **Money and tax are computed once and frozen.** The guide is explicit: *"When re-printing an
   invoice, the same taxes and charges are applied that were in effect at the time of the original
   sale."* Every sale line stores its resolved price, discount, tax basis and tax amounts as
   immutable snapshot columns. See [04](04-pricing-and-tax-engine.md).
3. **The cart lives on the server.** RFID reads arrive from a daemon, not a browser. A
   server-authoritative cart in Redis (with a Postgres write-behind for suspend/recall) is the only
   design where bulk reads, multi-station visibility and browser refresh all behave.
4. **Hardware is isolated behind one process per station.** Browsers cannot open LLRP sockets, COM
   ports or cash drawers. `Retail25.TerminalAgent` owns all of it and speaks only SignalR + a
   localhost loopback API.
5. **Every external system is an adapter.** Payments (X-Charge is dead), accounting (QB-XML is
   dead), label printing and legacy import are ports with swappable implementations. No vendor name
   appears in Domain or Application.
6. **Backward-compatible identification.** RFID EPC is the new fast path, but stock-code entry,
   Code 39 scans, Type 2 random-weight barcodes and serial-number picking all remain first-class.
   A store can run this system with zero RFID hardware.

## Approved decisions (2026-07-27)

Architecture approved. **All defaults accepted** — Q1–Q7 and P1–P5 are settled as below.

| # | Question | **Decision taken** |
|---|---|---|
| Q1 | Payment processor | `IPaymentGateway` port + `SimulatorPaymentGateway`. Real processor is a config-selected adapter added later; **no vendor name appears in Domain or Application.** |
| Q2 | Accounting system | `IAccountingConnector` port. `CsvExportConnector` + `GenericRestConnector` ship first; QuickBooks Online adapter follows. |
| Q3 | RFID reader | LLRP via `IRfidReader`, plus `SimulatedRfidReader` so the whole flow is developable and testable without hardware. Reader endpoint/protocol/antenna zones/RSSI are **database rows**, not code. |
| Q4 | Stores / offline | Single-tenant, multi-location schema. Agent-side store-and-forward for peripherals. Full offline store mode stays a Phase 8 option (schema already supports it). |
| Q5 | Legacy data | Importer built against the documented v2.5 DBF layout + the `TSTINV11.xls` sample; column maps are editable YAML, not code. |
| Q6 | Tax jurisdiction | Legacy Tax1/Tax2 + compound + add-on charge + inclusive/exclusive model implemented **exactly**, behind `ITaxPolicy`. Names, rates, compounding and taxability are all configuration rows — a US, Canadian or VAT store is a data change. |
| Q7 | Deployment | Docker Compose on one on-prem host; compose split so HQ/store separation is config only. |
| P1 | Sale price vs break points/levels | Break points and explicit level selection outrank the date-ranged sale price — **and the whole ladder is reorderable from the `pricing_rule_setting` table without a code change.** |
| P2 | Subtotal-discount tax proration | Prorated by line net; rounding residue to the largest line. |
| P3 | Rounding | Away-from-zero, 2 dp, once per line per tax. Scale and mode are per-currency configuration. |
| P4 | Cash rounding | Cash tenders/change round to `MinimumTender`; non-cash exact. |
| P5 | Loyalty clawback on return | At the original earn rate, stored on the loyalty ledger entry. |

### Standing build constraint — nothing hardcoded

Every rule the guide describes is expressed as **data**, not as a literal in a method body:

| Instead of | We store |
|---|---|
| `if (taxRate == 0.05m)` | `tax_configuration` rows, effective-dated per location |
| a `switch` on product type | `product_type_behaviour` rows → strategy resolved from a registry |
| a fixed pricing precedence | `pricing_rule_setting` rows (`ruleKey`, `order`, `enabled`, `parameters` JSONB) |
| `27,112,0,50,250` in the drawer service | `printer_profile.drawer_trigger` |
| hardcoded tender buttons | `tender_type` rows with ordering, icon key and capability flags |
| hardcoded roles/permissions | `permission` + `role_permission` rows, seeded and editable |
| hardcoded F-keys | `keybinding` rows per scope, overridable per station/user |
| report SQL sprinkled in handlers | declarative `report_definition` rows |
| label/receipt layouts in C# | `document_template` rows |
| enum-only lookups (departments, categories, item types, reasons) | reference tables with seed data |

Seed data supplies working defaults on day one; an administrator can change any of it in the UI.

## Status

**Phases 0–4 complete.** A user signs in without a token ever reaching JavaScript; a store's
catalogue, taxes, stations and staff are configured end to end through the UI, with browse grids that
patch themselves as another workstation edits; a cashier rings a sale entirely by keyboard, splits
the payment, takes a return, voids with supervisor approval and closes the drawer against a counted
variance; a basket of tagged items reads into the cart in one action, with every refused tag
explaining itself; and the back office can browse every sale, reprint it, and see who changed what
in the audit log. The schema is a real EF Core migration applied with `Migrate()` — integration
tests prove it lands on a clean database and that the model never drifts from it. See
[PHASE-STATUS.md](docs/PHASE-STATUS.md) for the audit trail behind each claim.

| Phase | State |
|---|---|
| **0 — Foundation** | **Complete** — migration + design-time factory, Testcontainers suite, otel-collector, container publish |
| **1 — Identity & shell** | **Complete** |
| **2 — Catalog & masters** | **Complete** — Browse + Form views, the Setup tabs, live grids, undelete |
| **3 — POS core** | **Complete** — including the sales log / POS history screen with reprint and CSV export |
| **4 — RFID & hardware** | **Complete** — including the matrix grid editor; hardware-in-the-loop trial remains an operational step |
| 5–8 | Barely started |

The terminal agent has its own [README](backend/src/Retail25.TerminalAgent/README.md);
`tools/Retail25.RfidSimulator` drives the whole RFID flow with no reader attached.

### Running it locally

```bash
docker compose -f deploy/docker-compose.yml up postgres redis
```

Set `Auth:AdminEmail` and `Auth:AdminPassword` in
[appsettings.Development.json](backend/src/Retail25.Api/appsettings.Development.json) — there is no
default administrator, because a seeded credential is a published credential.

```bash
dotnet run --project backend/src/Retail25.Api
```

```bash
cd frontend && cp .env.example .env.local && npm ci && npm run dev
```

### Local toolchain (2026-07-29)

| Tool | State | Effect |
|---|---|---|
| .NET SDK | ✅ 9.0.316 / 10.0.302; .NET 8 targeting packs present | Backend targets `net8.0`, builds and tests locally |
| Node.js / npm | ✅ v24.18.0 | Frontend installs, type-checks, lints and builds |
| Git | ✅ present | |
| **Docker** | ✅ Docker Desktop installed | Start Docker Desktop before `docker compose up` or the integration suite; without the daemon the 13 integration tests skip with a message rather than fail. |
| **psql** | ❌ missing | Optional; only for manual database inspection. |
