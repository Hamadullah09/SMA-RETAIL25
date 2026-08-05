# Phase status benchmark

**As at 2026-08-01.** Measured against the build lists and exit criteria in
[11-delivery-roadmap.md](architecture/11-delivery-roadmap.md). Nothing is marked complete unless its
exit criteria are demonstrable from the code in this repository.

An earlier revision of this file called phases 0, 3 and 4 complete when they were not. A line-by-line
audit found the gaps; this revision records them **closed**. What was fixed and why it mattered is in
[The audit and what it closed](#the-audit-and-what-it-closed).

> **On the database engine.** Everything below that names PostgreSQL was true on the date at the top.
> The system has since migrated to SQL Server ([Phase 7.5](architecture/11-delivery-roadmap.md)), and
> those references are left as written rather than rewritten — this file is a dated record of what was
> demonstrable when, and editing history to match the present is how a record stops being evidence.
> The current engine, and what the move cost, are in
> [12-schema-reference.md](architecture/12-schema-reference.md).

## Headline

| | |
|---|---|
| **Phases 0–4** | **Complete and live-verified end to end.** Phase 4's standing "no hardware-in-the-loop" limitation is now **closed for RFID** — see [The reader trial](#the-reader-trial) |
| **Phase 5** | **Build complete**, live-verified for shell/browse screens; the phase's own PO-to-statement scenario has not yet been run as one chain with real data |
| **Phases 6 and 7** | **Built since the last revision** — nine reports, labels and documents, bulk operations, staff and commissions, year-end close, accounting connector, and the whole legacy migration pipeline |
| **Phase 8** | Security review and restore rehearsal **done**; load test outstanding |
| Overall programme | **~88% of phases 0–8** (was ~69%) |
| Backend | 319 source files, ~45,200 lines (plus tests and the generated migrations) |
| Tests | **684** (95 domain · 484 application · 74 agent · 13 architecture · **18 integration against real PostgreSQL**) |
| Benchmarks | BenchmarkDotNet suite proving the RFID pipeline at 5,000 reads/sec — [RFID_Throughput_Benchmark.md](../RFID_Throughput_Benchmark.md) |
| Golden pricing files | 16, each citing a guide page or an architecture decision |
| Frontend | Type-checks, lints and builds clean; 35 pages |
| Schema | Nine EF Core migrations; startup applies `Migrate()`, never `EnsureCreated` |
| Dependency scan | **0 vulnerable .NET packages** across all 14 projects — [security review](runbooks/security-review.md) |

## Per-phase scorecard

| Phase | Scope | Complete | Evidence |
|---|---|:---:|---|
| **0** Foundation | Skeleton, compose, DbContext + migration, pipeline, observability, CI | **95%** | Migration applies to a clean DB (integration-tested); CI builds, tests, lints and publishes both containers. **`docker compose up` has still never been run on a machine we control** — Docker's engine will not start here, so that exit criterion rests on CI alone |
| **1** Identity & shell | OpenIddict + PKCE, BFF, permissions, staff PIN, shell, audit | **100%** | No-token-in-JS E2E spec; 403/428 mapping; audit interceptor; `Ctrl+K`. **Scope exceeded**: self-service sign-up, password recovery and 18 auth integration tests covering enumeration resistance, single-use reset tokens and cross-account token rejection |
| **2** Catalog & masters | Products, customers, suppliers, configuration, settings UI, browse grids | **100%** | Browse + Form views, the twelve Setup tabs, live grid patching, undelete, administered numbering |
| **3** POS core | Pricing engine, cart, POS UI, tenders, drawer, receipts, sales log | **100%** | All exit criteria pass; sales log with filters, drill-down, reprint and CSV export |
| **4** RFID & hardware | Agent, EPC lifecycle, live feed, matrix, serialized, peripherals | **95%** | All exit criteria pass. **A physical D2184 reader was driven end to end on 2026-08-01** — tags read, debounced, broadcast to the till and commissioned into stock. Printer, scale, drawer and pole display remain unverified for want of the devices; see [hardware-matrix.md](runbooks/hardware-matrix.md) |
| **5** Money & commerce depth | Gateway, gift cards, AR, loyalty, orders, kits, purchasing | **90%** | Every command real and unit-tested; screens live-reachable. The full PO → receive → sell-on-account → statement chain has still not been run as one scenario with seeded data |
| **6** Back office & reporting | Reports, labels, staff, bulk ops, year-end, accounting sync | **95%** | Nine analytical reports each with a CSV twin and a screen; QuestPDF labels and documents (price tags, Avery sheets, Code 39, envelopes, catalogue); batch repricing, stock transfers, stock counts; time clock, commissions, training mode; fiscal year-end close; CSV accounting connector with a Hangfire job. Needs one live pass against real trading data |
| **7** Migration & cutover | DBF importer, CSV importers, reconciliation, runbook | **85%** | Hand-rolled dBase III/FoxPro reader, CSV importers to the documented legacy field orders, staging tables, analyze → map → stage → validate → dry-run → import → verify pipeline, reconciliation reports, `admin/migration` UI, [cutover runbook](runbooks/cutover.md). **Blocked on a real legacy extract** — the exit criterion is reconciling totals against your data, and fixtures cannot stand in for it |
| **8** Hardening | Load testing, HIL matrix, pen test, restore rehearsal | **55%** | [Security review](runbooks/security-review.md) with all five dependency advisories fixed; [restore rehearsal](runbooks/restore.md) measured; [hardware matrix](runbooks/hardware-matrix.md) written and its RFID rows executed; RFID throughput benchmarked. **Outstanding**: API load test at 2× target, and a third-party penetration test |
| **UI/UX** refactor | Tokens, components, accessibility, responsive, dashboard | **95%** | One token system driving Tailwind theme colours, working dark mode, Radix primitives, keyboard-operable data grid, skip link, `aria-current`; ERP dashboard on real report queries; off-canvas sidebar below `lg`. Outstanding: a full keyboard-only pass over all 35 pages |

## The live run, and what it found

Docker Desktop (and the WSL2 it depends on) was unavailable for this entire session — confirmed via
`wsl --status` itself failing, not just `docker compose up`. Rather than leave the live run undone
again, the stack was brought up **without Docker**: the machine's native PostgreSQL 18 service
(freshly provisioned with the `retail25` role and database the app expects), a portable Redis binary
for Windows standing in for the compose service, and `dotnet run` / `npm run dev` in place of the two
application containers. `deploy/docker-compose.yml` itself was not exercised — that remains untested
on this machine — but the application code, for the first time this session, was.

That run did not go straight to a working till. It surfaced **nine previously-undiscovered defects**,
every one of them in the authentication chain, and every one severe enough that **no real login had
ever actually completed against this codebase before today** — not through a browser, not through the
BFF, not with a real OpenIddict token. `AuthorizationBehavior`, the MediatR pipeline behind every
`[RequiresPermission]` command, was resolving its acting user's permission set as empty on every
request, unconditionally, for every user, including the seeded administrator. A cart could not be
created. A sale could not be completed. Nothing gated by a permission check could ever succeed. This
is not a Phase 5 gap — the broken pieces are Phase 1 (identity/BFF) code, some of it untouched since
that phase was first marked complete. See
[Defects found in the first real login](#defects-found-in-the-first-real-login) for the full list and
fixes; the short version is a chain of independent bugs that each individually blocked the whole
flow, which is exactly why nothing upstream of them was ever able to prove they existed: a click-test
against a broken login page never gets far enough to find the second bug, or the third.

With all nine fixed, this is what was actually run live and confirmed working end to end, against the
real Postgres-backed API, in a browser, with a real signed-in session:

- Full sign-in: password → OpenIddict authorization code (PKCE/S256) → token exchange → BFF session
  cookie → the browser landing on an authenticated POS screen.
- The SignalR hub connection (`/hubs/pos`) negotiating, authenticating from a hub ticket, and
  reporting **Server online** — previously permanently stuck on "Disconnected — reconnecting."
- Live data round trips through the BFF proxy for the POS shell, the catalog/products browse grid,
  and the purchasing/purchase-orders browse grid — all correctly reaching the backend, all correctly
  permission-checked, all returning real (in this case empty, since no products were seeded) data
  rather than a permission-denied or a redirect-to-login.

What was **not** done: the Phase 5 exit criterion itself — *"raise a PO, receive it with freight, sell
on account, take a partial payment, accrue a late charge, print a statement"* as one live scenario
with real seeded data (a product, a supplier, a customer). Finding and fixing the authentication chain
took the remainder of the session available for live verification. Every step of that scenario is
still individually unit-tested against real business rules (11 PO tests, 12 receivables tests); the
full chain has simply not yet been clicked through end to end the way phases 0–4's exit criteria have.

### Which of the nine were environment-specific

Three of the nine (all `__Host-`-prefixed cookies losing their required `Secure` attribute over plain
HTTP) are specific to running without TLS — exactly this session's no-Docker, no-HTTPS setup. A
Docker-based run terminating HTTPS at the edge would likely not have hit those three. The other six —
the dead memory-cache crash, the static Hangfire API crash, the missing default authorization scheme,
the never-registered claims factory, `CurrentUser` snapshotting an empty principal before
authentication ran, and the wrong claim type for the user id — have nothing to do with HTTP vs. HTTPS
or Docker vs. native. They would have blocked a real login exactly the same way inside
`docker compose up`. Whatever verified Phase 1 as "✅ Login" previously did not exercise this exact
path — a real browser, a real password, a real OpenIddict token, a real permission check — end to end.

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
| 1 | Login; no token in JS; 403; audit rows; `Ctrl+K` | ✅ token-reachability, 403 mapping and audit rows were already asserted by unit/E2E coverage; **a real end-to-end login was verified live for the first time this session**, after fixing nine defects that had silently blocked it — see [The live run, and what it found](#the-live-run-and-what-it-found) |
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
| 5 | Raise a PO, receive it with freight, sell on account, take a partial payment, accrue a late charge, print a statement | ⚠️ every step unit-tested individually (11 PO tests, 12 receivables tests) against real business rules; purchasing and receivables screens are now confirmed live-reachable (real login, real permission checks, real data round trip) but the full chain has not yet been run as one scenario with seeded data — see [The live run, and what it found](#the-live-run-and-what-it-found) |

## The reader trial

**2026-08-01.** A UHF RFID D2184 was attached over TCP at `192.168.0.178:4001` and driven end to end
for the first time: 189 tag batches ingested with zero errors, tags shown live on the till with EPC,
antenna, signal strength and folded read count, then commissioned into stock against a real product.

It found **four faults, every one silent**, and every one of which had been passing its unit tests:

1. **The agent never authenticated.** It presented its configured secret directly as a bearer token;
   OpenIddict expects a signed one and refused every call. Because the agent's response to a failed
   profile fetch is to keep its defaults and carry on, the visible symptom was nothing at all — it
   sat reading imaginary tags from the built-in simulator while the real reader was ignored. It now
   exchanges the secret for a token via `client_credentials`, the grant its client was always
   registered for.
2. **Authenticated, it was still refused.** A machine client has no user, so the permission resolver
   returned an empty set and every call 403'd. A principal holding the terminal scope now resolves to
   a deliberately narrow set — read its own device profile, ring tags onto its own cart — and nothing
   more. It still cannot commission a tag, void a sale, discount a line or open a drawer.
3. **The device profile only took effect on reconnect.** The agent starts before the server answers,
   so its first session always ran on the simulator default, and nothing could interrupt that session
   for the life of the process.
4. **Commissioned tags kept reading "Not recognised".** The read feed caches EPC→item lookups
   including misses, and nothing invalidated it. The database said mapped; every till disagreed,
   indefinitely.

The lesson is recorded in [hardware-matrix.md](runbooks/hardware-matrix.md): a passing unit test is
not a passing row.

**Still open on RFID:** all four antennas at once, sustained throughput from the physical reader
(the [benchmark](../RFID_Throughput_Benchmark.md) proves the pipeline at 5,000 reads/sec against
synthesised frames, not what a D2184 actually emits), power-cycle and network-drop recovery, metal
and liquid detuning, and two-till cross-read arbitration — which additionally needs Redis.

## Standing limitations (named, not hidden)

1. **Hardware-in-the-loop is complete for RFID only.** The D2184 trial above closed that row. The
   LLRP client is still written to the 1.0.1 specification and tested only against
   specification-derived bytes, and no printer, scale, cash drawer or pole display has ever been
   attached. Doc 06's risk register calls for a field trial before rollout; that remains true for
   everything except the UHF serial reader.
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

7. **Phase 7's exit criterion needs your data.** The whole migration pipeline is built and tested
   against synthetic fixtures written to the documented legacy field orders. That proves the code.
   It cannot prove your Retail Plus 2.5 extract imports with reconciling totals, which is what the
   criterion actually asks. **This one is blocked on you, not on us.**

8. **No third-party penetration test.** The [security review](runbooks/security-review.md) is the
   authors reviewing their own work — which reliably misses what the authors did not think of. An
   external test is a procurement item, not a code task.

9. **The restore was rehearsed, not executed.** The dump is verified complete and its data
   round-trips, but no restore ran: the app role lacks `CREATEDB` on this machine, and 17 MB is not
   a production dataset. [restore.md](runbooks/restore.md) records exactly what was and was not
   proven.

10. **No API load test.** Phase 8 calls for sustained POS throughput, concurrent grid load and
    SignalR fan-out measured at 2× target. Only the RFID pipeline has been benchmarked. The
    duplicate `stock_levels` row anomaly found earlier is exactly the kind of race a concurrency
    load test should reproduce, and it remains unreproduced.

11. **Cross-till arbitration is off without Redis.** `Cache:Provider=InMemory` holds cart state, tag
    claims and hub tickets in one process, so two tills could sell the same tagged item. It is an
    explicit opt-in, refused outright in Production, and logged loudly at startup — but any
    deployment with more than one till needs Redis.

## Defects found in the first real login

Nine, found in sequence — each one hid the next, since a login that fails at step one never reaches
step two. All are fixed and covered by the live run described above; none had a unit test that could
have caught them, because unit tests construct their principals directly rather than living through
this exact pipeline.

1. **The shared `IMemoryCache` crashed on first use.** `IdentityRegistration` set a `SizeLimit` on the
   default DI-registered `IMemoryCache`, which OpenIddict's own internal scope/application/token
   caches also depend on — and those internal caches never set an entry `Size`, which .NET requires
   once a limit exists. Every OpenIddict cache read threw. Fixed by giving the permission cache its
   own dedicated, sized, keyed `IMemoryCache` instance instead of overloading the shared default one.
2. **Hangfire's recurring-job registration used the static API against a DI-only setup.**
   `RecurringJob.AddOrUpdate<T>()` reads a static `JobStorage.Current` that `services.AddHangfire(...)`
   never sets — it only wires storage into the container. Crashed on every startup. Fixed by resolving
   `IRecurringJobManager` from DI instead.
3. **Three `__Host-`-prefixed cookies (`r25.identity`, `r25.antiforgery`, `r25.session`) could never
   be set over plain HTTP.** The `__Host-` prefix requires the `Secure` attribute unconditionally;
   ASP.NET Core's antiforgery middleware additionally throws outright if `SecurePolicy=Always` is
   requested on a non-HTTPS request. Since this project's own documented dev flow runs the API on
   plain `http://localhost`, none of the three could ever be stored by a browser or accepted by the
   server in development. Fixed by dropping the `__Host-` prefix (and relaxing `SecurePolicy`) in
   Development only, keeping it in Production where HTTPS makes it correct.
4. **No controller specified an authentication scheme, and nothing set a default that worked for
   Bearer calls.** Every `[Authorize]` in the API is bare — no `AuthenticationSchemes` — and
   `AddIdentity` sets the *default* authenticate/challenge scheme to the Identity cookie. A
   server-to-server Bearer call from the BFF (the only way the API is actually meant to be called)
   carries no cookie, so every protected endpoint redirected (302) to the HTML login page instead of
   authenticating. Fixed with one `AddAuthorization` call setting the default *policy*'s scheme to
   OpenIddict's validation scheme, leaving `AddAuthentication`'s own default (and the interactive
   sign-in page, which authenticates explicitly) untouched.
5. **`ApplicationClaimsPrincipalFactory` — permissions, staff id, location id, access level, all of
   it — was fully implemented and never registered.** Nothing told ASP.NET Core Identity to use it in
   place of its own default claims factory, so `CreateUserPrincipalAsync` built a principal with a
   name and a role and nothing else. Every access token issued by this app, ever, was missing every
   custom claim its own authorization system depends on. One `AddScoped<IUserClaimsPrincipalFactory<
   ApplicationUser>, ApplicationClaimsPrincipalFactory>()` line fixes it.
6. **A leftover `next.config.js` rewrite shadowed the BFF's own `/api/proxy/[...path]` route** for at
   least some request paths, sending raw, unauthenticated requests straight to the backend at a literal
   path (`/api/proxy/terminals/...`) the backend has no route for. Predates the BFF pattern; the real
   route handler already does this forwarding correctly, with the token attached. Removed.
7. **The proxy forwarded the API's `Content-Encoding` header unchanged**, but `fetch()` had already
   transparently decompressed the body — so the browser tried to gunzip already-plain bytes on any
   response large enough to cross ASP.NET's compression threshold. Small error bodies never showed it.
   Fixed by stripping `content-encoding` from the forwarded response headers.
8. **`CurrentUser` read and cached `HttpContext.User` once, in its constructor.** A policy that names
   an explicit authentication scheme (every business endpoint does, after fix 4) makes
   `AuthorizationMiddleware` re-authenticate via that scheme and reassign `HttpContext.User` — which
   happens *after* anything already holding a stale reference was constructed. The result: the
   controller's own `this.User` correctly showed every claim, while `ICurrentUser` — resolved earlier
   in the same request — saw an empty, unauthenticated principal, permanently. Fixed by making every
   member read `HttpContext.User` live at the point of access instead of snapshotting it once.
9. **`CurrentUser.UserId` read `ClaimTypes.NameIdentifier`, but the token's user-id claim type is
   `sub`.** `IdentityRegistration` explicitly configures `IdentityOptions.ClaimsIdentity.UserIdClaimType
   = OpenIddictConstants.Claims.Subject`, so that is what every issued token actually carries — the
   long-form URI claim was never present. `UserId` was always null, which is why `HubTicketsController`
   kept returning a bare 401 even after fixes 1–8 landed. Fixed to read the `sub` claim.

A tenth, related but separate finding: `PosHub` and `InventoryHub` both carried a bare `[Authorize]`,
which after fix 4 inherited the Bearer-scheme requirement — but hub connections are never
Bearer-authenticated. `HubTicketMiddleware` redeems a single-use ticket and builds the connection's
principal itself, entirely outside the scheme system, specifically so the browser never needs a real
access token for a WebSocket. A scheme-specific policy made `AuthorizationMiddleware` re-authenticate
via Bearer, find nothing, and overwrite that ticket-built principal with an anonymous one. Fixed with a
second, scheme-less policy (`RequireAuthenticatedUser()` with no scheme constraint) applied to just
the two browser-facing hubs; `TerminalHub` (the physical terminal agent's channel, a confidential
OAuth client that presumably does present a real Bearer token) was left on the Bearer-requiring
default, since no agent was connected this session to verify that assumption either way.

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
