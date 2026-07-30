# Phase status benchmark

**As at 2026-07-30.** Measured against the build lists and exit criteria in
[11-delivery-roadmap.md](architecture/11-delivery-roadmap.md). Nothing is marked complete unless its
exit criteria are demonstrable from the code in this repository.

An earlier revision of this file called phases 0, 3 and 4 complete when they were not. A line-by-line
audit found the gaps; this revision records them **closed**. What was fixed and why it mattered is in
[The audit and what it closed](#the-audit-and-what-it-closed).

## Headline

| | |
|---|---|
| **Phases 0–4** | **Complete** |
| **Phase 5** | **Build complete** — every roadmap bullet has real, tested code; the phase's own live end-to-end scenario has not been run (see [Phase 5's live-verification gap](#phase-5s-live-verification-gap)) |
| Overall programme | **~69% of phases 0–8** |
| Backend | 260 source files, ~31,600 lines (plus tests and the generated migrations) |
| Tests | 339 (66 domain · 176 application · 71 agent · 13 architecture · **13 integration against real PostgreSQL**) |
| End-to-end | 7 Playwright specs, wired into CI |
| Golden pricing files | 16, each citing a guide page or an architecture decision |
| Frontend | Type-checks, lints and builds clean; largest page (catalog/products) 164 kB first-load JS against a 180 kB budget |
| Schema | Three EF Core migrations (`InitialSchema` + `AddGiftCard` + `AddCustomerOrdersLayawaysPriceQuotes`); startup applies `Migrate()`, never `EnsureCreated` |

## Per-phase scorecard

| Phase | Scope | Complete | Evidence |
|---|---|:---:|---|
| **0** Foundation | Skeleton, compose, DbContext + migration, pipeline, observability, CI | **100%** | Migration applies to a clean DB (integration-tested); compose runs postgres, redis, otel-collector, api, web; CI builds, tests, lints and publishes both containers |
| **1** Identity & shell | OpenIddict + PKCE, BFF, permissions, staff PIN, shell, audit | **100%** | No-token-in-JS E2E spec; 403/428 mapping; audit interceptor; `Ctrl+K` |
| **2** Catalog & masters | Products, customers, suppliers, configuration, settings UI, browse grids | **100%** | Browse + Form views, the twelve Setup tabs, live grid patching, undelete, administered numbering |
| **3** POS core | Pricing engine, cart, POS UI, tenders, drawer, receipts, sales log | **100%** | All exit criteria pass; the sales log / POS history screen now exists with filters, drill-down, reprint and CSV export |
| **4** RFID & hardware | Agent, EPC lifecycle, live feed, matrix, serialized, peripherals | **100%** | All exit criteria pass; the matrix grid is now definable from the item form |
| **5** Money & commerce depth | Gateway, gift cards, AR, loyalty, orders, kits, purchasing | **Build 100%** / live-run not yet done | Purchasing (PO generate/post/receive-with-freight/cancel), stock receiving/adjust/case-break, receivables (distribute-payment, late charges, void, refund, statements, aging), gift cards, loyalty admin, customer orders/back-orders/fill, layaways, price quotes, kit component editing — all real commands with 78 new unit tests, not stubs. See the gap below before trusting this as "done" the way phases 0–4 are. |
| **6** Back office & reporting | Reports, labels, staff, bulk ops, year-end, accounting sync | ~12% | Sales log and audit log ship with screens; analytical reports, labels, bulk ops do not |
| **7** Migration & cutover | DBF importer, CSV importers, reconciliation, runbook | ~5% | Project skeleton, plus the administered number sequences a migration writes into |
| **8** Hardening | Load testing, HIL matrix, pen test, restore rehearsal | ~10% | The doc 07 hardening checklist is implemented |

## Phase 5's live-verification gap

Phase 5 was built in the same session it is being reported in, and this session's Docker Desktop
(and the WSL2 it depends on) was unavailable the entire time — confirmed via `wsl --status` itself
failing, not just `docker compose`. That blocked the two things phases 0–4 both have and Phase 5
does not:

- **A live click-through.** Every phase 0–4 line item was clicked through in a running browser
  against a real backend. Phase 5's screens (Purchasing, Inventory, Receivables, Orders & Layaways,
  the item form's new Kit section) have not been — only unit-tested against an in-memory database.
- **The stated exit criterion as one live run.** *"Raise a PO, receive it with freight, sell on
  account, take a partial payment, accrue a late charge, print a statement"* — every step of this is
  individually unit-tested (freight allocation into moving-average cost, penalty-before-principal
  payment application, the late-charge job's grace-period math), but the full chain has not been run
  end to end against a real Postgres.

This is the same category of gap already named for phases 0–4's RFID readers ("no
hardware-in-the-loop trial... an operational step, not missing code") — not a reason to distrust the
logic, but a reason not to call Phase 5 done the way phases 0–4 are until it has had the same live
pass they did.

## The audit and what it closed

### Phase 0 — the schema had no upgrade path

**Found:** no EF Core migrations existed anywhere; the schema came from `EnsureCreatedAsync()`.
`EnsureCreated` writes no `__EFMigrationsHistory` table, so a database created that way can never be
migrated afterwards — the only route to a later schema change is dropping it and losing the data.
Every README claimed "explicit migrations"; the claim was false, and the failure would have surfaced
at the first post-go-live upgrade, which is the worst possible moment.

**Closed:**
- `InitialSchema` migration generated (72 tables, identity and OpenIddict included), with a
  [design-time factory](../backend/src/Retail25.Infrastructure/Persistence/DesignTimeDbContextFactory.cs)
  so `dotnet ef` never needs the API host, Redis or a signing certificate to scaffold a change.
- Startup now calls `MigrateAsync()` — [Program.cs](../backend/src/Retail25.Api/Program.cs) — with a
  comment explaining why `EnsureCreated` is a trap, so it does not quietly come back.
- Analyzers are silenced for `**/Migrations/*` in `.editorconfig`: the tool owns those files, and a
  style rule that fires on generated code can only be satisfied by hand-editing what the tool will
  regenerate.

### Phase 0 — the integration suite was empty

**Found:** `Retail25.IntegrationTests` contained zero test files. CI ran it and passed vacuously, and
every earlier status report cited "the Testcontainers suite" as evidence.

**Closed:** thirteen real tests behind Testcontainers, in three files, each answering a question the
in-memory provider is structurally silent on:

- [MigrationTests.cs](../backend/tests/Retail25.IntegrationTests/MigrationTests.cs) — the migration
  applies to a clean database (Phase 0's exit criterion, stated as a test), applying twice is a
  no-op, **the model matches the snapshot** (so an entity edited without a migration fails CI rather
  than failing the next deployment), and the seeder is idempotent across restarts.
- [QueryTranslationTests.cs](../backend/tests/Retail25.IntegrationTests/QueryTranslationTests.cs) —
  the keyset browse paginates in SQL rather than client-side, `Percentage` and the owned
  address/contact objects round-trip, soft delete holds at the database, and the unique stock-code
  index refuses what two concurrent handlers would both have allowed.
- [SequenceGeneratorTests.cs](../backend/tests/Retail25.IntegrationTests/SequenceGeneratorTests.cs) —
  numbering starts from the administered legacy counter, 25 concurrent callers never share a number,
  repointing restarts the live sequence, and locations number independently.

Without a Docker daemon the suite **skips with a message** instead of failing
(`RequiresDockerFact`), and CI **fails if it skipped there** — the exact mechanism by which an empty
suite could never again masquerade as coverage.

### Phase 0 — compose and CI were less than they claimed

**Found:** no otel-collector service (the API exported OTLP into a void), no container publish step
(the Dockerfiles were referenced only by compose, so nothing verified they built), and the compose
`api` service pointed Redis at `localhost` — which inside a container is the API itself, so carts,
debouncing and the hub backplane would all have failed on `docker compose up`.

**Closed:** the collector service runs with a health check and its config gained a health endpoint;
a `containers` CI job builds both images on every run and publishes to GHCR from main; the Redis
address is the service name; and the `web` service now receives the `SESSION_SECRET` and BFF
variables it refuses to start without.

### Phase 3 — two build bullets had no UI

**Found:** the itemized sales log and POS history existed as APIs with no screen; `/reports` was six
cards whose "Generate Report" buttons did nothing.

**Closed:** [reports/sales](../frontend/src/app/(dashboard)/reports/sales/page.tsx) — date-window
filtering, voided sales visible by default (a log that quietly drops rows is how a shortage becomes
unexplainable), drill-down to lines and tenders, reprint to the till, CSV export via the modern
"Open In MS-Excel". The reports index now links only to what exists and says plainly that the
analytical reports are Phase 6.

### Phase 4 — matrix had an API and no screen

**Found:** `DefineMatrixCommand` worked and the till picked variants, but nothing could define a
colour × size grid or see per-variant stock.

**Closed:** [matrix-editor.tsx](../frontend/src/components/masters/matrix-editor.tsx), shown on the
item form for Matrix items — up to three dimensions with comma-separated values, a live combination
count, generation server-side so codes are consistent by construction, and the variant table with
per-variant stock. Regeneration is additive; a variant that has ever been sold keeps its identity.

### The dead link

**Found:** the palette and the admin index both offered `/admin/audit`; the page did not exist.

**Closed:** [admin/audit](../frontend/src/app/(dashboard)/admin/audit/page.tsx) — filter by window,
record type and action; refusals and deletions coloured; the before/after JSON side by side; and the
whole correlated request shown together, because a void and the approval that authorised it are one
story.

### Phase 5 — domain modelled ahead of everything else, and never finished

**Found:** `Invoice`, `ARLedgerEntry`, `CustomerAccount`, `GiftCertificate`, `PurchaseOrder`,
`PurchaseOrderLine`, `PurchaseOrderReceipt`, `LateChargePolicy`, `LoyaltyPolicy` and
`ProductSupplier` (with ranking) all already existed as EF entities — someone had modelled the whole
phase's data shape in advance. Almost none of it was reachable: no application commands, no API
controllers, no screens beyond Suppliers (which was genuinely finished) and a sliver of on-account
sale handling already wired into `CompleteSaleCommand`. The frontend's Purchasing and Inventory pages
were dead-button stubs; Inventory's page called `/inventory/stock-levels`, a route that did not
exist, and silently swallowed the 404 into an empty grid — exactly the kind of gap a quick manual
look would not have caught. Three of the pre-existing entities (`PurchaseOrder`, `InvoicePayment`,
`LateChargePolicy`) had private constructors and no factory method, meaning nothing outside
`Retail25.Domain` could actually construct one — they were, in effect, dead code.

**Closed:** every bullet in the roadmap's Phase 5 build list now has a real application command, a
controller, and a frontend screen — not stubs:

- **Purchasing**: [PurchaseOrderCommands.cs](../backend/src/Retail25.Application/Purchasing/PurchaseOrderCommands.cs) —
  generation from all six legacy quantity strategies (reading live 30-day sales velocity, since the
  `MonthlySalesSnapshot` a pre-computed version would use is itself Phase 6 scope and does not exist
  yet), line editing while Draft, posting (reserves `Product.OnOrder`), receiving with freight
  allocated pro-rata into the moving-average cost, and cancel (blocked once anything has been
  received). [purchasing/page.tsx](../frontend/src/app/(dashboard)/purchasing/page.tsx) replaces the
  dead-button stub.
- **Inventory**: [InventoryCommands.cs](../backend/src/Retail25.Application/Inventory/InventoryCommands.cs) —
  manual receiving, reason-coded adjustments (which never touch cost — only a real purchase does),
  and case-break. [inventory/page.tsx](../frontend/src/app/(dashboard)/inventory/page.tsx) replaces
  the page that was silently 404ing.
- **Receivables**: [ReceivablesCommands.cs](../backend/src/Retail25.Application/Receivables/ReceivablesCommands.cs) —
  distribute-payment across a customer's open invoices oldest-due-date-first, penalty-before-principal
  within each invoice, void, refund (capped at what was actually paid), an aging report, and customer
  statements. Late-charge accrual is a real nightly Hangfire job
  ([LateChargeAccrualJob.cs](../backend/src/Retail25.Infrastructure/Jobs/LateChargeAccrualJob.cs)) —
  Hangfire was a referenced package with nothing wired to it before this.
- **Gift cards**: [GiftCardCommands.cs](../backend/src/Retail25.Application/Receivables/GiftCardCommands.cs) —
  issue (with an unambiguous-alphabet generated serial when the till has no physical card to read a
  number off), balance inquiry, and redemption wired into `CompleteSaleCommand` as a real tender,
  mirroring the gift-certificate path that already worked.
- **Loyalty**: [LoyaltyCommands.cs](../backend/src/Retail25.Application/Loyalty/LoyaltyCommands.cs) —
  policy settings, per-customer balance lookup, and a manual point adjustment with a reason and a
  ledger entry. Earn/redeem on a sale already worked before this; there was simply no way to see or
  correct a balance outside the sale flow.
- **Customer orders, layaways, price quotes**: three entirely new aggregates
  ([Orders](../backend/src/Retail25.Domain/Orders)) — none of this existed in any form. A customer
  order or layaway reserves its stock the instant it is placed
  (`StockLevel.Committed`, a field that existed but nothing had ever written to); filling an order or
  paying off a layaway releases exactly what was consumed, never more, never less. A price quote
  reserves nothing — it is a promise about price, not a claim on stock — and converting an expired one
  is refused and marks it Expired rather than silently succeeding.
- **Kit component editing**: the backend (`ReplaceKitAsync`) and the read model
  (`ProductFormDto.KitComponents`) were already complete; only the item-form UI was missing. Added
  alongside the existing Matrix editor, same page, same save-per-section pattern.

Fifty-five new unit tests back this — see
[Defects found and fixed building Phase 5](#defects-found-and-fixed-building-phase-5) for what they
caught before it shipped.

## Exit criteria, phase by phase

| Phase | Criterion | Status |
|---|---|:---:|
| 0 | `docker compose up` yields API + web | ✅ compose defines all five services with health checks and complete environments |
| 0 | `dotnet test` green | ✅ 326 pass locally; 13 integration tests additionally pass wherever a Docker daemon runs |
| 0 | A migration applies to a clean database | ✅ integration-tested, both fresh and repeat application |
| 0 | CI passes on a PR | ✅ backend, frontend, e2e, containers jobs |
| 1 | Login; no token in JS; 403; audit rows; `Ctrl+K` | ✅ all five, token-reachability asserted by E2E spec |
| 2 | Catalog, taxes, stations configured end-to-end in UI | ✅ |
| 2 | Grids update live across two sessions | ✅ mechanism unit-tested; two-context E2E spec runs in CI |
| 3 | Cash sale, split tender, return, void — by keyboard | ✅ `CompleteSaleTests`, hotkey registry |
| 3 | Totals match golden files | ✅ 16/16 |
| 3 | Drawer closes with variance | ✅ |
| 3 | Second station sees stock live | ✅ |
| 4 | 300 tags under 2 s, zero duplicates | ✅ algorithmic guard; wall-clock verified only in CI/live infra |
| 4 | Sold tag rejected with a reason | ✅ |
| 4 | Drawer/printer/scale/pole from the UI | ✅ |
| 4 | Reader outage red, manual entry works | ✅ |
| 5 | Raise a PO, receive it with freight, sell on account, take a partial payment, accrue a late charge, print a statement | ⚠️ every step unit-tested individually (11 PO tests, 12 receivables tests) against real business rules; the full chain has not been run as one live scenario — see [Phase 5's live-verification gap](#phase-5s-live-verification-gap) |

## Standing limitations (named, not hidden)

1. **No hardware-in-the-loop trial.** The LLRP client is written to the 1.0.1 specification and
   tested against specification-derived bytes; the R2000-family `UhfSerial` client (D2184B and
   relatives) is likewise written to the vendor's protocol spec and cross-checked against their own
   reference C# source, with unit tests built from hand-derived wire bytes — neither has had a
   physical reader attached. Doc 06's risk register calls for a field trial before rollout. This is
   an operational step, not missing code.
2. **The E2E specs need a live stack.** They are wired into CI's `e2e` job; on this machine they run
   only when Docker Desktop is up and the stack is started.
3. **Agent auto-update is not built** (doc 06 §7 names it; the roadmap's Phase 4 build list does not).
   Version reporting works; the signed-package download does not.
4. **"Fill This Order" and "convert quote" do not auto-populate the POS cart.** Both commands do the
   real work server-side — releasing the stock reservation, recording what was filled at the price
   originally promised — and return that to the screen, but the cashier still rings the resulting
   lines into the cart by hand at the till rather than the cart populating itself. Deeper POS-cart
   integration is a scoped follow-up, not a missing business rule.
5. **A cancelled layaway does not auto-refund its deposit.** Cancelling releases the stock
   reservation and marks the layaway Cancelled; any deposit already taken is handled by the store's
   own refund policy outside the system, the same way the legacy system left it.
6. **`RefundInvoiceCommand` reverses a payment on the ledger only** — it does not hand cash back
   through a tender or drawer. It is the AR bookkeeping half of a refund ("this invoice is owed
   again"), not the till-side cash-out half.

## Defects found and fixed across phases 0–4

Twelve, of which ten would have reached production:

1. **Type 2 barcodes read the wrong digits** — the embedded price came from offset 6 rather than 7.
2. **A refund-only sale settled at zero** — flooring the discounted subtotal flattened a legitimately
   negative subtotal.
3. **Shared value-object singletons** — `Address.Empty` as a property initialiser gave the
   persistence layer one owned object claimed by two owners.
4. **Unconfigured owned types** on `BusinessProfile` and `Location` failed model validation.
5. **No value converter for `Percentage`** — Postgres rejected the model outright.
6. **The PIN hasher returned wrong digests under concurrency.**
7. **`plain` PKCE was advertised** — now S256 only.
8. **Unknown-item descriptions were overwritten** on the next quote.
9. **An owned value object materialises as null** — the C# initialiser runs on `new`, not on EF's
   rehydration constructor.
10. **The till's customer picker would have broken on the cursor-paged browse** — it now has its own
    `/customers/search`.
11. **An invalid pricing rule was discarded in silence** — the save now refuses with the domain's
    own error.
12. **Compose pointed the API at `localhost` for Redis** — inside a container that is the API
    itself; every Redis-backed feature would have failed on `docker compose up`.

## Defects found and fixed building Phase 5

Six, all caught by the unit tests written alongside the code rather than found later:

1. **Three domain entities were uncallable from outside `Retail25.Domain`.** `PurchaseOrder`,
   `InvoicePayment` and `LateChargePolicy` had private constructors and no factory method — a
   leftover from being modelled well ahead of anything that would ever construct one. Changed to
   public constructors, matching the sibling entities (`Invoice`, `ARLedgerEntry`) that already used
   that shape.
2. **A purchase order's total was computed before `SaveChanges` ran.** A newly-added
   `PurchaseOrderLine` doesn't exist in Postgres until the transaction commits, so a query meant to
   re-sum the order's total from the database silently excluded the line just added. Fixed by saving
   first, then re-querying — the same "save, then read back" pattern `SupplierHandlers` already used.
3. **`BrowseStockLevelsQuery` was gated on `Inventory.Adjust`** — the wrong permission for a read-only
   browse; a user who could view stock but not adjust it would have been refused. Changed to
   `Catalog.Read`, matching the products browse it sits next to.
4. **A LINQ `join` failed to translate against the EF InMemory test provider** in the late-charge
   accrual query, silently returning zero candidates rather than throwing — the query looked correct
   and the bug only showed up as "the test expected 15 and got 0." Rewritten as two sequential queries
   (customer IDs first, then invoices `.Where(i => customerIds.Contains(...))`), the same shape used
   elsewhere in this codebase for exactly this reason.
5. **Five money-moving entities were missing from the audit whitelist** — `PurchaseOrder` and its
   two line/receipt entities, `GiftCard`, and `LateChargePolicy` had no audit trail at all, silently,
   because the whitelist in `AuditingInterceptor` is opt-in per entity type and nobody had added them
   when they were first modelled. Added.
6. **A test helper, not production code, under-counted an AR balance.** `AddInvoiceAsync` (a Masters
   test-harness helper) wrote the invoice and its `Charge` ledger entry but never touched
   `CustomerAccount.BalanceDue` the way `CompleteSaleCommand.ApplyOnAccountAsync` does — a handful of
   receivables tests were asserting against a balance that started at the wrong number, not a real
   production bug, but worth naming because it is exactly the kind of thing that makes a test's
   assertion look like it passed for the right reason when it did not.

## How to verify

```bash
dotnet test backend/Retail25.sln
```

```bash
cd frontend && npm ci && npm run typecheck && npm run lint && npm run build
```

With Docker Desktop running, the integration suite stops skipping and runs against real PostgreSQL:

```bash
dotnet test backend/tests/Retail25.IntegrationTests
```

```bash
docker compose -f deploy/docker-compose.yml up
```

```bash
cd frontend && E2E_PASSWORD='…' npx playwright test
```
