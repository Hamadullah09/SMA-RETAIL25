# Phase status benchmark

**As at 2026-07-29.** Measured against the build lists and exit criteria in
[11-delivery-roadmap.md](architecture/11-delivery-roadmap.md). Nothing is marked complete unless its
exit criteria are demonstrable from the code in this repository.

An earlier revision of this file called phases 0, 3 and 4 complete when they were not. A line-by-line
audit found the gaps; this revision records them **closed**. What was fixed and why it mattered is in
[The audit and what it closed](#the-audit-and-what-it-closed).

## Headline

| | |
|---|---|
| **Phases 0–4** | **Complete** |
| Overall programme | **~66% of phases 0–8** |
| Backend | 237 source files, ~27,800 lines (plus tests and the generated migration) |
| Tests | 271 (66 domain · 121 application · 58 agent · 13 architecture · **13 integration against real PostgreSQL**) |
| End-to-end | 7 Playwright specs, wired into CI |
| Golden pricing files | 16, each citing a guide page or an architecture decision |
| Frontend | Type-checks, lints and builds clean; POS 147 kB, inventory 162 kB first-load JS against a 180 kB budget |
| Schema | One EF Core migration (`InitialSchema`, 72 tables); startup applies `Migrate()`, never `EnsureCreated` |

## Per-phase scorecard

| Phase | Scope | Complete | Evidence |
|---|---|:---:|---|
| **0** Foundation | Skeleton, compose, DbContext + migration, pipeline, observability, CI | **100%** | Migration applies to a clean DB (integration-tested); compose runs postgres, redis, otel-collector, api, web; CI builds, tests, lints and publishes both containers |
| **1** Identity & shell | OpenIddict + PKCE, BFF, permissions, staff PIN, shell, audit | **100%** | No-token-in-JS E2E spec; 403/428 mapping; audit interceptor; `Ctrl+K` |
| **2** Catalog & masters | Products, customers, suppliers, configuration, settings UI, browse grids | **100%** | Browse + Form views, the twelve Setup tabs, live grid patching, undelete, administered numbering |
| **3** POS core | Pricing engine, cart, POS UI, tenders, drawer, receipts, sales log | **100%** | All exit criteria pass; the sales log / POS history screen now exists with filters, drill-down, reprint and CSV export |
| **4** RFID & hardware | Agent, EPC lifecycle, live feed, matrix, serialized, peripherals | **100%** | All exit criteria pass; the matrix grid is now definable from the item form |
| **5** Money & commerce depth | Gateway, gift cards, AR, loyalty, orders, kits, purchasing | ~15% | Statements, late charges, layaways, purchase orders not started |
| **6** Back office & reporting | Reports, labels, staff, bulk ops, year-end, accounting sync | ~12% | Sales log and audit log ship with screens; analytical reports, labels, bulk ops do not |
| **7** Migration & cutover | DBF importer, CSV importers, reconciliation, runbook | ~5% | Project skeleton, plus the administered number sequences a migration writes into |
| **8** Hardening | Load testing, HIL matrix, pen test, restore rehearsal | ~10% | The doc 07 hardening checklist is implemented |

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

## Exit criteria, phase by phase

| Phase | Criterion | Status |
|---|---|:---:|
| 0 | `docker compose up` yields API + web | ✅ compose defines all five services with health checks and complete environments |
| 0 | `dotnet test` green | ✅ 258 pass locally; 13 integration tests additionally pass wherever a Docker daemon runs |
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
4. **Kit component editing is read-only** on the item form — kits are Phase 5 scope ("kits: explode
   on sale" sits in Phase 5's build list; the explosion itself already works).

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
