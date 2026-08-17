# Backup and restore

> ⚠️ **Not rehearsed on this engine.** The 2026-08-01 rehearsal was against PostgreSQL, and the
> system has since moved to SQL Server. Every command below has been rewritten for the new engine;
> **none of them has been executed**, and the timings that used to be in this document have been
> removed rather than carried across, because a figure measured on `pg_dump` says nothing about
> `BACKUP DATABASE`. Treat the whole runbook as untested until the next rehearsal, and schedule that
> rehearsal before the RTO below is relied on. This is the "after any schema migration" line in the
> schedule at the bottom, and an engine change is the largest version of it there is.

A backup nobody has restored is a hope, not a backup. This runbook exists to be *executed* on a
schedule, not read.

## Targets

| | Target | Measured |
|---|---|---|
| **RPO** — data you can afford to lose | 15 minutes | Depends on log backups; see [Continuous archiving](#continuous-archiving-beyond-the-nightly-dump) |
| **RTO** — time to trading again | 1 hour | **Not measured on SQL Server.** |

**These will not scale linearly** whatever the engine. A shop with five years of sales history is a
different measurement, and the rehearsal must be repeated against a production-sized copy before the
RTO above is trusted.

---

## Backup

> **`COMPRESSION` is Standard and Enterprise only.** On SQL Server Express — which is what a single
> shop runs, and what `on-premise.md` recommends — the whole statement fails with *"BACKUP DATABASE
> WITH COMPRESSION is not supported on Express Edition"* and **writes no file**. Drop the word on
> Express; the backup is then uncompressed and everything else about it is identical. Verified on
> Express 2022: without it, 3,786 pages in 0.33s and `RESTORE VERIFYONLY` reports a valid set.

`COMPRESSION` because the backup ships off the machine, `CHECKSUM` because it is what makes the
verification in the next step mean anything — without it, `RESTORE VERIFYONLY` confirms the file is
structurally a backup and not that its pages are intact.

```bash
sqlcmd -S <host> -E -Q "BACKUP DATABASE [retail25] TO DISK = N'/backups/retail25-<stamp>.bak' WITH INIT, COMPRESSION, CHECKSUM"
```

One database covers everything, Hangfire included: its tables live in the same database, so a
restore brings the scheduled jobs back with the data. That is deliberate — restoring the application
without its jobs would leave late charges and accounting posts silently not running.

### Verify every backup, immediately

An unreadable backup discovered during an outage is the same as no backup.

```bash
sqlcmd -S <host> -E -Q "RESTORE VERIFYONLY FROM DISK = N'/backups/retail25-<stamp>.bak' WITH CHECKSUM"
```

Then confirm it is *complete*, not merely readable — one row per file in the backup set, and a
`BackupSize` in the right order of magnitude:

```bash
sqlcmd -S <host> -E -Q "RESTORE FILELISTONLY FROM DISK = N'/backups/retail25-<stamp>.bak'"
```

---

## Restore

> **Destructive.** This replaces a database. Read the whole section before running any of it.

### 1 · Stop everything that writes

```bash
docker compose stop api web
```

Or stop the API service and every terminal agent. A restore into a database an API is still writing
to produces a mixture of both, which is worse than either.

### 2 · Back up the broken database first

Even a corrupt one. It is the only copy of whatever happened between the last backup and the
incident, and once you restore over it, it is gone.

```bash
sqlcmd -S <host> -E -Q "BACKUP DATABASE [retail25] TO DISK = N'/backups/pre-restore.bak' WITH INIT, COMPRESSION"
```

### 3 · Take the database single-user

A restore fails outright while any session is connected, and the message names the database rather
than the connection holding it open — so this is the step, not an optimisation. `ROLLBACK
IMMEDIATE` does not wait for those sessions to finish what they are doing; that is the point.

```bash
sqlcmd -S <host> -E -Q "ALTER DATABASE [retail25] SET SINGLE_USER WITH ROLLBACK IMMEDIATE"
```

### 4 · Restore

`REPLACE` overwrites the existing database. `RECOVERY` brings it online; use `NORECOVERY` instead
only if you are about to apply log backups on top (see point-in-time, below).

```bash
sqlcmd -S <host> -E -Q "RESTORE DATABASE [retail25] FROM DISK = N'/backups/retail25-<stamp>.bak' WITH REPLACE, RECOVERY"
```

```bash
sqlcmd -S <host> -E -Q "ALTER DATABASE [retail25] SET MULTI_USER"
```

### 5 · Verify before letting anyone in

Row counts against what the backup should contain:

```bash
sqlcmd -S <host> -E -d retail25 -Q "SELECT 'products' t, count(*) FROM products UNION ALL SELECT 'sales', count(*) FROM sales_transactions UNION ALL SELECT 'tags', count(*) FROM serialized_units UNION ALL SELECT 'audit', count(*) FROM audit_log_entries;"
```

The migration history — if this is empty, the application will try to re-run every migration:

```bash
sqlcmd -S <host> -E -d retail25 -Q "SELECT count(*) FROM [__EFMigrationsHistory]"
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

For the 15-minute RPO above, put the database in FULL recovery model and schedule log backups
(`BACKUP LOG [retail25] TO DISK = ...`) shipping to durable storage off the machine. Point-in-time
recovery then restores the last full backup `WITH NORECOVERY`, applies each log backup in order, and
finishes with `WITH STOPAT = <moment>, RECOVERY`. This is a server configuration task rather than an
application one, and it is **not yet configured** on any environment this project has been deployed
to.

---

## Schedule

| When | What | Who |
|---|---|---|
| Nightly | `BACKUP DATABASE ... WITH CHECKSUM`, verify with `RESTORE VERIFYONLY`, ship off the machine | Automated |
| Weekly | Confirm the offsite copy exists and its size is sane | Operator |
| **Quarterly** | **Full restore rehearsal into a scratch database, timed** | Operator |
| After any schema migration | One rehearsal, because the shape changed | Operator |

The quarterly rehearsal is the only line in this table that proves the others work.

---

## What this rehearsal did not prove

Stated plainly, because a runbook that only records its successes is not evidence.

- **Nothing in this document has been run on SQL Server.** The commands are correct for the engine;
  that is not the same as verified. The previous rehearsal, on PostgreSQL, got as far as proving a
  dump was readable and complete and never executed a restore either — the login could not create a
  scratch database.

  To close this, grant the login the right and rehearse the whole thing into a scratch name:

  ```bash
  sqlcmd -S <host> -E -Q "ALTER SERVER ROLE dbcreator ADD MEMBER [retail25]"
  ```

- **No offsite copy has been exercised.** Nothing about transfer time, storage durability or
  retrieval has been measured.
- **RTO is a target, not an observation.** No restore has been timed on this engine at any size.
