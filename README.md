# SMA RETAIL

SMA RETAIL, by SMA Technology: a clean-room rewrite of the Retail Plus 5.2 point-of-sale system on .NET 10 and SQL Server Express. Each store's Retail Plus data is imported once at cutover.

- Plan: [docs/REWRITE_PLAN.md](docs/REWRITE_PLAN.md)
- Data layer: [docs/DATA_LAYER_PLAN.md](docs/DATA_LAYER_PLAN.md)
- Source file formats the importer reads: [docs/DATA_FORMATS.md](docs/DATA_FORMATS.md)
- Phase 0 data-engine spike: [docs/spikes/phase0-rdd-spike.md](docs/spikes/phase0-rdd-spike.md)

## Layout

| Folder | Contents |
|---|---|
| `src/RetailPlus.Core` | Domain model (`Model/`) and business rules (`Rules/`: pricing, taxes, cash rounding, bonus points, late charges, reordering, random-weight barcodes). No I/O |
| `src/RetailPlus.Data` | EF Core 10 on SQL Server: `RetailPlusDbContext`, configuration, migrations, sequences, and the application services (`Services/`: setup, inventory, clients/suppliers, sales, drawer, A/R, purchasing, backup) |
| `src/RetailPlus.Migration` | The one-time import: DBF/FPT reader (`Dbf/`), memo codecs (`Codecs/`), importer with verification and report (`Import/`) |
| `src/RetailPlus.Import` | Command-line importer |
| `src/RetailPlus.Reports` | Report queries, CSV/HTML output, receipt / statement / drawer report text |
| `src/RetailPlus.Rfid` | UHF RFID: D2184 protocol driver (`D2184/`), simulated reader, the RFID hub (antenna-to-station routing) and its HTTP client |
| `src/RetailPlus.RfidHub` | `SmaRetail.RfidHub`: the RFID hub as a service for many readers and stations |
| `src/RetailPlus.Devices` | Receipt printer (raw ESC/POS through Windows), cash drawer, pole display, scale |
| `src/RetailPlus.Settings` | Per-station settings (`station.json`) |
| `src/RetailPlus.Integrations` | CSV import and export of inventory and clients |
| `src/RetailPlus.App` | The WPF desktop app |
| `tests/RetailPlus.Core.Tests` | Business rule tests |
| `tests/RetailPlus.Data.Tests` | Schema and service integration tests on a throwaway LocalDB database |
| `tests/RetailPlus.Rfid.Tests` | Protocol, driver (against a fake reader on a socket), hub routing with 63 simulated readers, hub client |
| `tests/RetailPlus.Migration.Tests` | Reader, codec and importer tests: synthetic fixtures, plus live runs on real data when present |
| `tests/RetailPlus.Parity` | Placeholder for old-vs-new comparison runs |
| `tools/dbf/` | Read-only Python tools, including the reader's test oracle |
| `spikes/` | Phase 0 experiments (X# runtime). Not in `RetailPlus.sln`; build them directly if needed |
| `spike-data/` | Local copies of Retail Plus data (git-ignored; contains the vendor's program and sample data) |

## Run the desktop app

```
dotnet run --project src/RetailPlus.App
```

On first start the app creates (or upgrades) its database, by default `RetailPlus_App` on `(localdb)\MSSQLLocalDB`, and seeds a store, the `POS` location, tender types and station 1. Station settings live in `%LOCALAPPDATA%\RetailPlus\station.json` (Setup › This station); `--localdb <name>`, `--station <n>` and `--config <file>` override them. When the database has users, the app asks for a login; users imported from Retail Plus choose a new password on their first login, because the old passwords are not carried over.

Screens: Point of Sale, Sales Journal, Inventory, Clients, Suppliers, Invoices & A/R, Purchase Orders, Orders & RMAs, Reports, Tools (import, backup, restore, rebuild, recurring billing, year end) and Setup.

Automated checks: `--smoke` opens every screen, runs every report and rings up a sale, then exits 0 on success; `--screenshot-tour <folder>` saves a PNG of each screen. Both write to `%LOCALAPPDATA%\RetailPlus\retailplus-check.log`.

## Barcodes, labels and keyboard

Items carry a product barcode (UPC-A, EAN-13 or EAN-8, check digit verified). An item without the maker's barcode gets
an in-store UPC-A: number system 4, the shop's own number and the check digit (*Inventory › item › Make one*, or
automatically when its labels are printed). A USB barcode scanner works as a keyboard: at the POS whatever is scanned
or typed goes into the code box, wherever the focus is, so scanning and RFID work at the same time; the same item
scanned twice is one line with quantity 2, and an RFID tag read for a piece already scanned joins that piece.
*Inventory › Print labels* prints barcode labels (item, price, UPC-A) for all items or the chosen ones, on A4 sheets
of 30 (3 × 10) or 24, or on a label printer (50 × 30 or 38 × 25 mm), and saves them as a PDF.

Everything can be done from the keyboard, and every button shows its key (F12 pay, F5 quantity, Ctrl+P print,
Ctrl+1 … Ctrl+0 for the screens; Enter and Esc on OK and Cancel). F1 lists them.

## RFID

Tagged items ring up by themselves at the POS, alongside barcodes. Readers (D2184, TCP/IP or serial), their four
antennas and the station each antenna serves are set up in *RFID readers*; a site with many readers on an Ethernet
switch runs the RFID hub service on one PC:

```
dotnet run --project src/RetailPlus.RfidHub -- --localdb RetailPlus_App
```

and every station points at it in *Setup › This station › RFID*. Design, installation and capacity:
[docs/RFID.md](docs/RFID.md).

## Import a store's Retail Plus data

From the app: Tools › Import from Retail Plus 5. From the command line:

```
dotnet run --project src/RetailPlus.Import -- --source <data folder> --localdb RetailPlus_App [--ini <Plus50.INI>] [--force]
```

The import runs in one transaction, checks control totals (record counts, stock, balances) against the source, and writes `report.md` and `report.json`.

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
