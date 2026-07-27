# 10 — Non-Functional Requirements, Deployment & Testing

## 1. Non-functional targets

| Attribute | Target | How it is met |
|---|---|---|
| **Cart mutation latency** | p95 ≤ 120 ms, p99 ≤ 250 ms | Redis-backed cart; pricing engine is pure and in-process; no N+1 (projections only) |
| **RFID tag → visible line** | p95 ≤ 300 ms end-to-end | 200 ms agent batching + single Redis round trip + SignalR push |
| **Bulk read throughput** | 300 tags in ≤ 2 s | Batched ingest, pipelined Redis, single bulk insert |
| **Sale completion** | p95 ≤ 400 ms including receipt dispatch | One DB transaction; printing is async via outbox |
| **Browse grid** | 50k rows, first page ≤ 300 ms | Cursor pagination + covering indexes + virtualization |
| **Concurrent stations** | 50 per store server, 500 org-wide | Load-tested at 2× target before go-live |
| **Availability** | 99.9% during trading hours | Health checks, auto-restart, hot standby DB optional |
| **RPO / RTO** | RPO ≤ 5 min, RTO ≤ 60 min | WAL archiving + PITR; documented, rehearsed restore |
| **Data retention** | Financial 7 y, audit 7 y, carts 30 d, tag reads 90 d | Partition drop policies |
| **Time** | All timestamps `timestamptz` in UTC; business date per location timezone | Explicit `BusinessDate` on transactions — a 2am sale belongs to the previous business day if the store says so |

## 2. Deployment topology

### Default: single on-prem host (Docker Compose)

```
                        ┌── Caddy / nginx (TLS, one origin) ──┐
                        │   /            → web   (Next.js)     │
                        │   /api, /hubs  → api   (.NET 8)      │
                        └──────────────────────────────────────┘
   containers: web · api · postgres:16 · redis:7 · otel-collector · (optional) seq
   volumes:    pgdata · redisdata · files (photos, receipt archives) · backups
   host:       Windows Server or Linux; stations reach it by hostname over TLS
```

Stations run only the browser + `Retail25.TerminalAgent` (Windows service).

### Scale-out path (no rewrite required)

- API replicas behind the reverse proxy; SignalR Redis backplane is already configured.
- Postgres primary + streaming replica; read-only reports routed to the replica.
- HQ/cloud deployment: same compose file, different network topology; store agents connect over
  VPN/TLS.

### Configuration

Twelve-factor: environment variables + mounted secrets. Per-environment appsettings hold only
non-secret defaults. `Database:AutoMigrate` is **false** in production; migrations run as an
explicit deployment step with a pre-migration backup.

## 3. Observability

| Signal | Tooling |
|---|---|
| Logs | Serilog → console (JSON) → OTLP collector; correlation id on every request, hub message and outbox dispatch |
| Traces | OpenTelemetry: ASP.NET Core, HttpClient, EF Core, StackExchange.Redis, MediatR (custom), SignalR (custom). A trace spans browser → API → DB → outbox → agent print |
| Metrics | `sales_completed_total`, `cart_mutation_duration`, `rfid_tags_ingested_total`, `rfid_tags_rejected_total{reason}`, `outbox_lag_seconds`, `sync_failures_total`, `drawer_variance`, `agent_online{station}` |
| Health | `/health/live` (process), `/health/ready` (DB, Redis, migrations applied) |
| Alerting | Outbox lag > 5 min · agent offline during trading hours · sync failures · drawer variance beyond threshold · auth failure spike |

Business-visible dashboards (sales by hour, tender mix, top sellers, shrinkage) are ordinary reports
over the ledger, not a separate analytics stack.

## 4. Backup & disaster recovery (replaces the floppy-disk chapter)

- Nightly `pg_dump` (custom format) + continuous WAL archiving → off-host storage, encrypted.
- Redis is a cache and a transient cart store; it is **not** the system of record. Cart loss on a
  Redis failure costs at most the in-progress carts, which are also written behind to Postgres on
  suspend and on every 30 s tick.
- Object storage (photos, receipt archives) synced nightly.
- **Restore rehearsal is a scheduled task**, quarterly, with a written runbook and a recorded RTO.
  The legacy guide begged users to make backups; we make the restore the tested artefact, because an
  untested backup is a rumour.
- `Retail25.Migration` snapshots the DB automatically before any legacy import.

## 5. Testing strategy

| Level | Scope | Tooling | Gate |
|---|---|---|---|
| **Unit — domain** | Pricing/tax golden files, costing, order-quantity formulas, EPC state machine, random-weight parser, late-charge accrual | xUnit + FluentAssertions + Verify | 100% of pricing/tax branches; CI blocking |
| **Property** | Tax sums, discount proration, tender balance, inclusive-tax round trip | CsCheck/FsCheck | CI blocking |
| **Unit — application** | Handlers with fakes; permission enforcement per command | xUnit | CI blocking |
| **Architecture** | Dependency rules, no EF types above Infrastructure, every command has a validator | NetArchTest | CI blocking |
| **Integration** | Real Postgres + Redis via Testcontainers; migrations apply cleanly; concurrency (two stations, one unit); idempotency replay; outbox delivery | xUnit + Testcontainers | CI blocking |
| **Contract** | OpenAPI snapshot + generated TS client compiles; SignalR payload shapes | Verify + tsc | CI blocking |
| **E2E** | Cash sale, split tender, return, void with supervisor approval, suspend/recall, bulk RFID with a simulated reader, AR invoice + partial payment, PO receive | Playwright | CI blocking on main |
| **Load** | 50 stations, sustained sales + bulk reads; soak for drawer close accuracy | k6 / NBomber | Pre-release |
| **Hardware-in-the-loop** | Real reader, printer, drawer, scale, pole display | Manual checklist per release | Pre-release |
| **Migration** | Import a legacy dataset, reconcile totals against legacy reports | Integration + report diff | Before go-live |
| **Differential (if legacy data is available)** | Replay historical sales through the new pricing engine, diff to the cent | Custom harness | Strongest parity evidence — see doc 04 §8 |

### CI pipeline

```
lint (dotnet format, eslint, prettier)
 → build (backend + frontend, warnings as errors)
 → unit + property + architecture tests
 → integration tests (Testcontainers)
 → contract snapshot check
 → E2E (Playwright, on PR to main)
 → security scan (dotnet list package --vulnerable, npm audit, container scan)
 → bundle-size + performance budget check
 → publish artefacts (api image, web image, agent MSI)
```

## 6. Definition of done (per feature)

A feature is done when: the command/query exists with a validator and permission; unit tests cover
its rules; an integration test exercises the endpoint; the UI is keyboard-operable and matches the
design rules; realtime updates propagate; audit entries are written; the parity matrix row in
[01](01-scope-and-parity.md) is checked off with the guide page reference; and docs/OpenAPI are
regenerated.
