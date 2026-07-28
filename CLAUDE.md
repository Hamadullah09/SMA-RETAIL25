# CLAUDE.md

Guidance for Claude Code when working in this repository. Read this before making changes.

---

## What this project is

Retail25 rebuilds **Retail Plus for Windows 2.5** (True North Computer Services, 2008) as a
self-hosted web POS, inventory and accounts-receivable system. The legacy DBF file store, mapped
network drives and WinForms screens are replaced by PostgreSQL, an ASP.NET Core API and a Next.js
front end. Bulk RFID reading is added; every legacy identification path (stock code, Code 39,
Type 2 random-weight, serial number) is kept.

**The parity contract is `User Guide Retail25.pdf`, not the brief.** The brief's feature matrix is a
subset. When the two disagree, the guide wins, and the guide's page number belongs in the code
comment.

---

## The rule that overrides everything else

> **Nothing is hardcoded.** Every rule the guide describes is data.

The user stated this explicitly and it is the project's defining constraint. Before writing a
literal, ask whether a shopkeeper might ever want it different.

| Never write | Write instead |
|---|---|
| `if (taxRate == 0.05m)` | a `tax_configuration` row, effective-dated per location |
| a `switch` on product type | behaviour rows resolved from a registry |
| a fixed pricing precedence | `pricing_rule_setting` rows |
| `27,112,0,50,250` in a drawer service | `printer_profile.drawer_trigger` |
| hardcoded tender buttons | `tender_type` rows |
| hardcoded roles or permissions | `permission` + `role_permission` rows |
| `decimal.Round(x, 2)` | `RoundingPolicy` built from the `Currency` row |

Seed data supplies working defaults; an administrator changes them in settings.

**Corollary:** when configuration is missing, fail loudly. `CartPricingService` returns
`tax.not_configured` rather than assuming zero tax. A till that silently charges nothing is worse
than one that stops.

---

## Architecture

Clean Architecture, CQRS with MediatR. Dependencies point inward and architecture tests enforce it.

```
Retail25.Domain          entities, value objects, pricing engine. No project references.
Retail25.Application     commands, queries, orchestration. Depends on Domain only.
Retail25.Infrastructure  EF Core, identity, Redis, seeding. Implements Application ports.
Retail25.Api             controllers, SignalR hubs, host.
Retail25.TerminalAgent   per-till service: RFID, scale, drawer, pole display.
Retail25.Migration       legacy DBF and CSV importer.
```

- Application defines ports (`ICartStore`, `IPosNotifier`, `IPaymentGateway`); Infrastructure
  implements them. Application never names a vendor or a transport.
- `SignalRPosNotifier` lives in Api because the hubs do. Application only sees `IPosNotifier`.

### Load-bearing decisions

1. **Ledgers, not mutable rows.** Stock, AR, loyalty and drawer movements are append-only and
   replayable. `Product.OnHand` is a derived snapshot. The legacy system mutated DBF records and
   needed `Rebuild`/`Reindex` to recover; that failure mode is designed out.
2. **Price at quote time, freeze at sale time.** `CartLine` stores what the cashier *asked for*
   (quantity, typed price, chosen level, tax keys) and caches what the engine *decided*. Prices are
   frozen once, onto `SaleLine`, at commit. A reprint a year later reads those columns, so a later
   rate change cannot alter an issued document (guide p.56).
3. **One path to a total.** `ICartPricingService` is the only way to add up a cart. This is what
   makes the test suite meaningful — the totals the tests pin are the totals the receipt uses.
4. **The cart lives on the server.** RFID reads arrive from a daemon, not a browser.

---

## Money

- `decimal` everywhere. Never `double` or `float`.
- Ledger precision is 4 dp (`Money.StorageScale`); presentation scale comes from the currency row.
- `Percentage` holds the number as typed: five percent is `5`, not `0.05`.
- Round **once per line per tax**, never on a running total. That is the penny-drift bug, and it is
  pinned by a test.
- Cash tenders and change round to `MinimumTender`; electronic tenders settle exactly.

---

## Testing

Run before claiming anything works:

```bash
dotnet build backend/Retail25.sln     # warnings are errors
dotnet test  backend/Retail25.sln
```

- `Retail25.Domain.UnitTests` — pricing and tax. Includes property tests over 500 randomly
  generated carts each: tax reconciles, proration loses no penny, inclusive pricing charges exactly
  the sticker price.
- `Retail25.ArchitectureTests` — dependency rules **and the parity benchmark**.

### The benchmark

`docs/BENCHMARK.md` is **generated**, never hand-edited. Each of 155 legacy behaviours names the
code that must exist; reflection resolves it against the compiled assemblies.

**A feature cannot be marked delivered by editing a document — only by writing the code.**

When you implement a capability, add its evidence to
`backend/tests/Retail25.ArchitectureTests/Benchmark/capabilities.json` and raise
`MinimumImplementedPercent` so the gain cannot regress. Prefer naming a **command or service** over
an entity: a table nobody can write to is not a feature.

---

## Conventions

- File-scoped namespaces, nullable enabled, `TreatWarningsAsErrors`.
- Domain constructors are private; construct through `Create` factories returning `Result<T>`.
- Expected failures return `Result`/`Error` with a stable machine-readable code
  (`stock.insufficient`). Exceptions are for genuinely exceptional conditions.
- Errors carry a code the UI translates. Never a hardcoded English sentence in a business rule.
- EF migrations live in `Infrastructure/Persistence/Migrations` and are exempt from analyzers via a
  scoped `.editorconfig` — generated code, not a licence to weaken rules elsewhere.

### Comments

Explain **why**, never what. The guide's page number is the highest-value thing a comment can carry:

```csharp
// Guide p.84, verbatim: a reward requires that there "cannot already be a discount
// applied to the subtotal of the sale".
```

Do not narrate control flow, restate a signature, or leave "TODO: use actual tax config" in code
that ships — that exact comment sat above a hardcoded 5% tax rate for weeks.

---

## Working on this repo

- **Verify before reporting.** Run the build and the tests. If something fails, say so with the
  output.
- **Read the guide page** cited by the parity matrix before implementing a legacy behaviour. The
  subtleties are load-bearing: the per-sale tax override is deliberately not retroactive; a missing
  price level falls through rather than erroring; a subtotal discount suppresses loyalty rewards.
- **Do not lower a test's expectations to make it pass.**
- **Check `docs/BENCHMARK.md`** to see what genuinely exists before assuming a feature is there.

### Where to look

| Question | File |
|---|---|
| How is a price decided? | `docs/architecture/04-pricing-and-tax-engine.md` |
| What does the legacy system do? | `docs/architecture/01-scope-and-parity.md` |
| What actually works today? | `docs/BENCHMARK.md` |
| What is the CSV telling us? | `docs/architecture/12-csv-data-model.md` |
| How do I run it? | `docs/RUNNING-IN-VISUAL-STUDIO.md` |
| What was asked for? | `PROMPT.md` |

---

## Running it

Verified working on 2026-07-28. `docs/RUNNING-IN-VISUAL-STUDIO.md` has the full setup; the short
version once PostgreSQL is installed and the `retail25` database exists:

```bash
cd backend/src/Retail25.Api && dotnet run
```

Migrations apply and seed on start when `Database:AutoMigrate` is set. A 30.00 item with the
seeded 5% and 7% taxes must quote **33.60** — if it does, configuration → engine → cart is intact.

**Redis is optional.** Leave `ConnectionStrings:Redis` empty and carts are held in the API process.
Do not default it to `localhost:6379`: a machine without Redis then logs connection failures
forever while appearing configured.

## Current state

See `docs/BENCHMARK.md` for the authoritative number. As of the last run: **71 of 155 delivered**.

Pricing and tax are effectively complete and heavily tested. Reporting, purchasing depth, RFID
hardware and the accounting connector are largely untouched. The front end is mid-build; the API
and Swagger are the reliable way to exercise the system today.

### Known gaps

- Authentication is cookie-based. OpenIddict with authorization code + PKCE is still needed for the
  terminal agent and third-party clients (`STF-001`).
- The CSV importer and the schema changes it implies (`docs/architecture/12`) are not built.
- Many entities still rely on EF convention rather than explicit configuration.
