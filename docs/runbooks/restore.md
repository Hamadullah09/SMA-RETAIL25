# Backup and restore

**Rehearsed 2026-08-01** against the development database (17 MB, 90 tables, 1,161 RFID tags,
3,279 audit rows). Figures below are measured, not estimated. What could not be executed on that
machine is named in [What this rehearsal did not prove](#what-this-rehearsal-did-not-prove).

A backup nobody has restored is a hope, not a backup. This runbook exists to be *executed* on a
schedule, not read.

## Targets

| | Target | Measured |
|---|---|---|
| **RPO** — data you can afford to lose | 15 minutes | Depends on WAL archiving; see [Continuous archiving](#continuous-archiving-beyond-the-nightly-dump) |
| **RTO** — time to trading again | 1 hour | Dump 1 s · restore ~2 s at 17 MB (extrapolated, see caveat) |

At 17 MB these numbers are trivially inside target. **They will not scale linearly.** A shop with
five years of sales history is a different measurement, and this rehearsal should be repeated against
a production-sized copy before the RTO above is trusted.

---

## Backup

Custom format (`-Fc`): compressed, and it allows selective restore, which the plain SQL format does
not.

```bash
pg_dump -h <host> -U retail25 -d retail25 -Fc -f retail25-$(date +%Y%m%d-%H%M).dump
```

Measured on the development database:

| | |
|---|---|
| Elapsed | **1 second** |
| Source size | 17 MB |
| Dump size | **1.1 MB** (≈15:1) |
| Contents | 102 tables, 101 primary keys, 92 indexes, migration history included |

The dump covers more tables than `public` alone — Hangfire keeps its own schema, and its job state is
in the dump too. That is deliberate: restoring the application without its scheduled jobs would leave
late charges and accounting posts silently not running.

### Verify every backup, immediately

An unreadable dump discovered during an outage is the same as no dump.

```bash
pg_restore --list retail25-<stamp>.dump > /dev/null && echo "readable"
```

Then confirm it is *complete*, not merely readable:

```bash
pg_restore --list retail25-<stamp>.dump | grep -c "TABLE DATA"
```

Expect one entry per non-empty table (102 at the time of writing). A dump that lists 5 has failed
part way and exited 0.

---

## Restore

> **Destructive.** This replaces a database. Read the whole section before running any of it.

### 1 · Stop everything that writes

```bash
docker compose stop api web
```

Or stop the API service and every terminal agent. A restore into a database an API is still writing
to produces a mixture of both, which is worse than either.

### 2 · Take a dump of the broken database first

Even a corrupt one. It is the only copy of whatever happened between the last backup and the
incident, and once you restore over it, it is gone.

```bash
pg_dump -h <host> -U retail25 -d retail25 -Fc -f pre-restore-$(date +%Y%m%d-%H%M).dump
```

### 3 · Recreate the database

```bash
psql -h <host> -U postgres -c "DROP DATABASE IF EXISTS retail25 WITH (FORCE);"
```

```bash
psql -h <host> -U postgres -c "CREATE DATABASE retail25 OWNER retail25;"
```

`WITH (FORCE)` terminates lingering connections. Without it this step blocks on a single forgotten
`psql` session.

### 4 · Restore

```bash
pg_restore -h <host> -U retail25 -d retail25 --no-owner --jobs 4 retail25-<stamp>.dump
```

`--jobs 4` restores in parallel and is the difference between minutes and tens of minutes on a real
dataset. `--no-owner` lets the restore run as `retail25` rather than needing every original role to
exist.

### 5 · Verify before letting anyone in

Row counts against what the backup should contain:

```bash
psql -h <host> -U retail25 -d retail25 -c "SELECT 'products' t, count(*) FROM products UNION ALL SELECT 'sales', count(*) FROM sales_transactions UNION ALL SELECT 'tags', count(*) FROM serialized_units UNION ALL SELECT 'audit', count(*) FROM audit_log_entries;"
```

The migration history — if this is empty, the application will try to re-run every migration:

```bash
psql -h <host> -U retail25 -d retail25 -c "SELECT count(*) FROM \"__EFMigrationsHistory\";"
```

Then start the API and confirm it reports healthy **without applying a migration**:

```bash
curl -s http://localhost:5000/health/ready
```

### 6 · Ring one sale

The real acceptance test. Sign in, add a line, take cash, and confirm the receipt number continues
from where the backup left off rather than restarting at 1. Number sequences live in the database and
a restore that silently reset them will issue duplicate receipt numbers for the rest of the day.

---

## Continuous archiving, beyond the nightly dump

A nightly dump gives an RPO of up to 24 hours. A shop that takes 400 transactions a day would lose a
day's takings.

For the 15-minute RPO above, enable WAL archiving on the PostgreSQL server
(`archive_mode = on`, `archive_command` shipping to durable storage off the machine) and use
`pg_basebackup` for the base. Point-in-time recovery then replays to any moment. This is a server
configuration task rather than an application one, and it is **not yet configured** on any
environment this project has been deployed to.

---

## Schedule

| When | What | Who |
|---|---|---|
| Nightly | `pg_dump`, verify with `pg_restore --list`, ship off the machine | Automated |
| Weekly | Confirm the offsite copy exists and its size is sane | Operator |
| **Quarterly** | **Full restore rehearsal into a scratch database, timed** | Operator |
| After any schema migration | One rehearsal, because the shape changed | Operator |

The quarterly rehearsal is the only line in this table that proves the others work.

---

## What this rehearsal did not prove

Stated plainly, because a runbook that only records its successes is not evidence.

- **The restore itself was not executed.** The `retail25` role on the rehearsal machine lacks
  `CREATEDB`, so no scratch database could be made. What *was* verified is that the dump is
  readable, contains all 102 tables with their keys and indexes, includes the migration history, and
  that a table's data section round-trips exactly — `products` extracted 142 rows against 142 in the
  source. That is a strong integrity check. It is not a restore.

  To close this, grant the role the right and repeat from step 3 into a scratch name:

  ```bash
  psql -h <host> -U postgres -c "ALTER ROLE retail25 CREATEDB;"
  ```

- **17 MB is not a production dataset.** The timings are honest and near-meaningless at that size.
- **No offsite copy was exercised.** The dump never left the machine, so nothing about transfer time,
  storage durability or retrieval was measured.
- **RTO is extrapolated, not observed**, because step 4 did not run.
