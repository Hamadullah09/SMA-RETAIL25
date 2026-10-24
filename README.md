# Retail Plus rewrite

A clean-room rewrite of the Retail Plus 5.2 point-of-sale system on .NET 10 and SQL Server Express. Each store's Retail Plus data is imported once at cutover.

- Plan: [docs/REWRITE_PLAN.md](docs/REWRITE_PLAN.md)
- Data layer: [docs/DATA_LAYER_PLAN.md](docs/DATA_LAYER_PLAN.md)
- Source file formats the importer reads: [docs/DATA_FORMATS.md](docs/DATA_FORMATS.md)
- Phase 0 data-engine spike: [docs/spikes/phase0-rdd-spike.md](docs/spikes/phase0-rdd-spike.md)

## Layout

| Folder | Contents |
|---|---|
| `src/RetailPlus.Core` | Domain model (`Model/`) and, later, the business rules. No I/O |
| `src/RetailPlus.Data` | EF Core 10 on SQL Server: `RetailPlusDbContext`, entity configuration, migrations, sequences |
| `src/RetailPlus.Migration` | The one-time import. So far: the read-only DBF/FPT reader (`Dbf/`) |
| `src/` (others) | `Settings`, `Devices`, `Reports`, `Integrations`, `App` (WPF): empty skeletons |
| `tests/RetailPlus.Data.Tests` | Integration tests on a throwaway LocalDB database |
| `tests/RetailPlus.Migration.Tests` | Reader tests: synthetic fixtures with golden JSON, plus a live comparison with the Python oracle on real data |
| `tests/` (others) | `Core.Tests`, `Parity` (placeholders) |
| `tools/dbf/` | Read-only Python tools, including the reader's test oracle |
| `spikes/` | Phase 0 experiments (X# runtime). Not in `RetailPlus.sln`; build them directly if needed |
| `spike-data/` | Local copies of Retail Plus data (git-ignored; contains the vendor's program and sample data) |

## Build and test

Requires the .NET 10 SDK (see `global.json`), SQL Server LocalDB for the database tests, and Python 3 for the live oracle test.

```
dotnet build RetailPlus.sln
dotnet test RetailPlus.sln
```

- **Database tests** create and drop their own database on `(localdb)\MSSQLLocalDB`. Set `RETAILPLUS_TEST_SQL` to a connection string (without a database name) to use another server. Without a server they're skipped.
- **The live oracle test** reads every table in `spike-data/baseline` and `spike-data/run2`, or in the folders listed in `RETAILPLUS_DBF_SAMPLES` (separated by `;`), and compares the reader with `tools/dbf/dbf_to_json.py`. Without data or Python it's skipped; the fixture tests always run.

## Database migrations

```
dotnet tool restore
dotnet ef migrations add <Name> --project src/RetailPlus.Data --startup-project src/RetailPlus.Data --output-dir Migrations
dotnet ef database update --project src/RetailPlus.Data --startup-project src/RetailPlus.Data
```

The tools target the LocalDB database `RetailPlus_Dev` unless `RETAILPLUS_DESIGN_DB` holds a connection string.

The vendor installer (`Retail52.exe`) and manual (`Retail Plus 50.pdf`) in this folder are reference material only and are excluded from git.
