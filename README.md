# Retail25 — Architecture Dossier

Modernization of **Retail Plus for Windows 2.5** (True North Computer Services, 2008) into a
self-hosted, web-based enterprise POS + Inventory + AR/AP platform.

Live at **[pos.sma-techno.net](https://pos.sma-techno.net)**.

| | |
|---|---|
| **Backend** | C# / .NET 10, Clean Architecture, CQRS via MediatR |
| **Database** | SQL Server + EF Core (explicit migrations, applied on start when `Database:AutoMigrate`) |
| **Identity** | OpenIddict + ASP.NET Core Identity, Authorization Code + PKCE, BFF cookies |
| **Realtime** | ASP.NET Core SignalR (`/hubs/pos`, `/hubs/rfid`, `/hubs/inventory`, `/hubs/terminal`) |
| **Cart / debounce / idempotency** | SQL Server-backed by default; Redis is an explicit opt-in via `Cache:Provider` |
| **Frontend** | Next.js 14 App Router, React 18, TypeScript, Tailwind + Radix/shadcn, TanStack Query, Zustand |
| **Edge** | `Retail25.TerminalAgent` — a Windows service on each till (RFID, scale, drawer, pole display, ESC/POS) |
| **Source of truth for scope** | `User Guide Retail25.pdf` (v2.5) + the project brief. Where they disagree, the guide wins. |

> **On the stack line above.** This project began on .NET 8, PostgreSQL and Redis, and this file
> described that arrangement long after it stopped being true. It is now SQL Server on .NET 10, with
> Redis optional. If something here disagrees with the code, the code is right and this file is a bug.

## Read in this order

| # | Document | What it settles |
|---|---|---|
| 01 | [Scope & Legacy Parity Matrix](docs/architecture/01-scope-and-parity.md) | Every legacy feature, its replacement, and its phase. What is deliberately dropped. |
| 02 | [Solution Structure](docs/architecture/02-solution-structure.md) | Directory tree, project references, dependency rules. |
| 03 | [Domain Model & ERD](docs/architecture/03-domain-model.md) | Aggregates, entities, invariants, ledger design. |
| 04 | [Pricing & Tax Engine](docs/architecture/04-pricing-and-tax-engine.md) | The deterministic pipeline. Highest-risk logic in the system. |
| 05 | [Application Layer, API & Realtime](docs/architecture/05-application-api-realtime.md) | CQRS slices, endpoint map, SignalR hub contracts. |
| 06 | [RFID & Hardware Bridge](docs/architecture/06-rfid-and-hardware-bridge.md) | EPC lifecycle, reader protocols, debouncing, peripherals, offline queue. |
| 07 | [Security & Identity](docs/architecture/07-security-and-identity.md) | OpenIddict/PKCE, BFF cookies, legacy access levels 0–4 → permissions, audit. |
| 08 | [Frontend & UX](docs/architecture/08-frontend-ux.md) | POS layout, keyboard map, screen inventory, design system rules. |
| 09 | [Integration & Data Migration](docs/architecture/09-integration-migration.md) | Accounting sync, DBF/CSV importer, multi-store replication. |
| 10 | [NFRs, Deployment, Testing](docs/architecture/10-nfr-deployment-testing.md) | Topology, performance budgets, observability, test strategy. |
| 11 | [Delivery Roadmap](docs/architecture/11-delivery-roadmap.md) | Phases, exit criteria, risk register. |
| 12 | [Schema Reference](docs/architecture/12-schema-reference.md) | Tables and columns, and the PostgreSQL → SQL Server provider swap. |

Operational runbooks live in [docs/runbooks](docs/runbooks): [backup and
restore](docs/runbooks/restore.md), [running the server in the
shop](docs/runbooks/on-premise.md), and [read-only diagnostic
queries](docs/runbooks/diagnostic-queries.sql). Phase-by-phase evidence is in
[docs/PHASE-STATUS.md](docs/PHASE-STATUS.md).

## Architectural stance in one page

1. **The transaction is a ledger, not a row.** Sales, stock movements, AR balances and drawer
   totals are append-only ledgers with derived snapshots. `Product.OnHand` is a snapshot of
   `stock_ledger_entries`, never an independently mutated number. The legacy system mutated DBF
   records in place and needed `Rebuild`/`Reindex` to recover; that failure mode is designed out.
2. **Money and tax are computed once and frozen.** The guide is explicit: *"When re-printing an
   invoice, the same taxes and charges are applied that were in effect at the time of the original
   sale."* Every sale line stores its resolved price, discount, tax basis and tax amounts as
   immutable columns, so a later rate change cannot alter an issued document.
3. **One path to a total.** `ICartPricingService` is the only way to add up a cart, which is what
   makes the test suite meaningful: the totals the tests pin are the totals the receipt uses.
4. **The cart lives on the server.** RFID reads arrive from a daemon, not a browser.
5. **Hardware is isolated behind one process per station.** Browsers cannot open reader sockets, COM
   ports or cash drawers. The terminal agent owns all of it and speaks SignalR plus a loopback API.
6. **Every external system is an adapter.** Payments, accounting, label printing and legacy import
   are ports with swappable implementations. No vendor name appears in Domain or Application.
7. **Backward-compatible identification.** RFID EPC is the fast path, but stock-code entry, Code 39
   scans, Type 2 random-weight barcodes and serial-number picking all remain first-class. A store can
   run this system with no RFID hardware at all.

### Standing build constraint — nothing hardcoded

Every rule the guide describes is expressed as **data**, not as a literal in a method body:

| Instead of | We store |
|---|---|
| `if (taxRate == 0.05m)` | `tax_configuration` rows, effective-dated per location |
| a `switch` on product type | behaviour rows resolved from a registry |
| a fixed pricing precedence | `pricing_rule_setting` rows |
| `27,112,0,50,250` in the drawer service | `printer_profile.drawer_trigger` |
| hardcoded tender buttons | `tender_type` rows |
| hardcoded roles/permissions | `permission` + `role_permission` rows |
| hardcoded F-keys | `keybinding` rows per scope |
| `decimal.Round(x, 2)` | `RoundingPolicy` built from the `Currency` row |

Seed data supplies working defaults; an administrator changes them in settings.

**Corollary: when configuration is missing, fail loudly.** `CartPricingService` returns
`tax.not_configured` rather than assuming zero tax. A till that silently charges nothing is worse
than one that stops.

## Deployment

`main` deploys itself. A push runs
[`.github/workflows/deploy-myasp.yml`](.github/workflows/deploy-myasp.yml), which gates every
publish step on `dotnet test backend/Retail25.sln` and then ships the API and front end to myASP.NET
shared IIS hosting over Web Deploy. There are no zip files to build by hand.

The API is mounted at `/backend` behind the Next.js app at the site root, so browser and API share
one origin and the BFF keeps tokens out of JavaScript.

| Job | Does |
|---|---|
| `test` | The full suite. Everything else `needs:` this. |
| `backend`, `frontend`, `database` | Publish artefacts, run migrations |
| `deploy` | Web Deploy to the host |

Two limits of this host are worth knowing, because neither is a bug in the application: **WebSockets
are not upgraded**, so SignalR falls back to long-polling; and a reader on the shop LAN
(`192.168.x.x`) is **not routable from a datacentre**, so LAN reader mode cannot work hosted. Both
are answered by [running the server in the shop](docs/runbooks/on-premise.md).

## Status

Pricing and tax are effectively complete and heavily tested. The POS rings sales by keyboard,
barcode and tag; splits payments; takes returns and refunds; voids with supervisor approval; and
closes a drawer against a counted variance. The back office browses and reprints sales, manages the
catalogue and masters, and shows an audit trail. Reporting depth, purchasing, and the accounting
connector are the thin areas.

See [docs/PHASE-STATUS.md](docs/PHASE-STATUS.md) for the evidence behind each claim, rather than
trusting a summary line here — a status paragraph is the first thing in any repository to go stale,
as the header of this file attests.

### Known gaps

- The CSV importer and the schema changes it implies are not built.
- Restore of the portable database export is deliberately not implemented; a half-tested restore is
  worse than none. Native SQL Server backup/restore works where the database is local.
- `appsettings.json` still defaults `ConnectionStrings:Redis` to `localhost:6379`. On a machine with
  no Redis that logs connection failures while appearing configured; set `Cache:Provider` explicitly.

## Running it locally

You need **SQL Server** (Express is fine) and Node. There is no Docker requirement.

```bash
dotnet run --project backend/src/Retail25.Api
```

Set `Auth:AdminEmail` and `Auth:AdminPassword` in user secrets — there is no default administrator,
because a seeded credential is a published credential. Set `ConnectionStrings:DefaultConnection` to
your instance, and `Database:AutoMigrate` to apply migrations and seed on start.

```bash
cd frontend && cp .env.example .env.local && npm ci && npm run dev
```

**A 30.00 item with the seeded 5% and 7% taxes must quote 33.60.** If it does, configuration →
pricing engine → cart is intact.

## Testing

```bash
dotnet build backend/Retail25.sln
dotnet test backend/Retail25.sln
```

Warnings are errors. As of 2026-08-17:

| Project | Tests |
|---|---|
| `Retail25.Domain.UnitTests` | 156 — pricing and tax, including property tests over 500 random carts each |
| `Retail25.Application.UnitTests` | 598 |
| `Retail25.TerminalAgent.UnitTests` | 115 |
| `Retail25.ArchitectureTests` | 13 — dependency rules |
| `Retail25.IntegrationTests` | 128 — needs a real SQL Server |

The integration tests skip rather than fail when no database is configured. To run them against a
local instance instead of Testcontainers:

```bash
set RETAIL25_TEST_SQL_CONNECTION=Server=.\SQLEXPRESS;Database=retail25_tests;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True
```

Run the integration suite on its own. Sharing one database with a concurrent run produces lock
contention that looks exactly like flaky tests — a 3-minute suite took 31 minutes and reported three
false failures that way.

## The terminal agent

The agent is a Windows service installed **on each till**. It is deliberately *not* part of the
automatic deploy: pushing to `main` updates the API and the web front end, and leaves the agent at
whatever version was last installed on that machine. Updating it is a separate, explicit step.

It owns the hardware and answers a loopback API on `127.0.0.1:8477` (`/status`,
`/reader/diagnostics`, scale and printer self-tests). Anything that moves money or stock goes through
the server instead, so it is permission-checked and auditable.

**The station id comes from the agent, not the bundle.** `NEXT_PUBLIC_STATION_ID` is compiled into
the JavaScript at build time, so it is the same for every browser in the shop; the till asks the
agent which machine it is and falls back to the build-time value only for a browser with no agent.
The agent's CORS allow-list (`Agent:WebOrigin`) and its Private Network Access header are what let a
page served from the public internet read that answer.

### Readers

A reader is reached over the network *or* over a serial lead — the same protocol either way, and
which one is decided by what the profile's host looks like. If the configured address does not
answer, the agent sweeps the till's own /24 and then tries serial ports.

**A device must prove it is a reader before one is reported.** Opening a TCP socket proves something
is listening; opening a COM port proves almost nothing, because Windows opens a serial port whether
or not anything is behind it. A firmware query has to come back before the connection counts. This is
not hypothetical: on one till the highest COM port was `Intel(R) Active Management Technology - SOL
(COM3)`, a motherboard virtual port. It opened, the agent reported a healthy reader, and the real one
on the shop LAN was ignored — a cashier held a tag against a reader the screen called green and
nothing happened.

When a reader will not read, the panel's raw read rate is the number to look at, and
`GET http://127.0.0.1:8477/status` tells you the device, the mode and whether the agent holds it.
`Connected` and `reading` are different facts: a reader in mode `Off` holds a perfectly good session.
