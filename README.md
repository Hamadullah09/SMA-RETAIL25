# SMA RETAIL

SMA RETAIL, by SMA Technology: a clean-room rewrite of the Retail Plus 5.2 point-of-sale system on .NET 10. Each till keeps its own SQLite database, so it sells without a network; the central server uses the same data model on SQL Server ([docs/WEB_INTEGRATION_PLAN.md](docs/WEB_INTEGRATION_PLAN.md)). Each store's Retail Plus data is imported once at cutover.

- Plan: [docs/REWRITE_PLAN.md](docs/REWRITE_PLAN.md)
- Data layer: [docs/DATA_LAYER_PLAN.md](docs/DATA_LAYER_PLAN.md)
- Source file formats the importer reads: [docs/DATA_FORMATS.md](docs/DATA_FORMATS.md)
- Phase 0 data-engine spike: [docs/spikes/phase0-rdd-spike.md](docs/spikes/phase0-rdd-spike.md)

## Layout

| Folder | Contents |
|---|---|
| `src/RetailPlus.Core` | Domain model (`Model/`) and business rules (`Rules/`: pricing, taxes, cash rounding, bonus points, late charges, reordering, random-weight barcodes). No I/O |
| `src/RetailPlus.Data` | The data model on EF Core 10, the same on SQLite and SQL Server: `RetailPlusDbContext` (version 7 GUID keys, store and station on every till-made row, UTC change times, per-station document numbers), configuration, and the application services (`Services/`: setup, inventory with its stock movement ledger, clients/suppliers, sales, drawer, A/R, purchasing, backup) |
| `src/RetailPlus.Data.Sqlite` | The SQLite migrations (the tills) |
| `src/RetailPlus.Data.SqlServer` | The SQL Server migrations (the server) |
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
| `tests/RetailPlus.Data.Tests` | Schema and service integration tests, each run on a throwaway SQLite file and on a throwaway LocalDB database |
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

On first start the app creates (or upgrades) its database, by default the SQLite file `%LOCALAPPDATA%\RetailPlus\smaretail.db`, and seeds a store, the `POS` location, tender types and station 1. Station settings live in `%LOCALAPPDATA%\RetailPlus\station.json` (Setup › This station, where the database can also be switched to a shared SQL Server one); `--db <file>`, `--localdb <name>`, `--station <n>` and `--config <file>` override them. When the database has users, the app asks for a login; users imported from Retail Plus choose a new password on their first login, because the old passwords are not carried over.

Screens: Point of Sale, Sales Journal, Inventory, Clients, Suppliers, Invoices & A/R, Purchase Orders, Orders & RMAs, Stock counts, Transfers, Gift cards, Reports, Staff (time clock, commission rules and commission), RFID readers, Tools (import, backup, restore, rebuild, recurring billing, year end), Audit log, Setup and Help. Each user sees the screens their role allows.

## Users, roles and the audit log

Every user has a role (Setup › Users and Setup › Roles). A role is a set of permissions from `Core.Rules.Permissions`, grouped as Sales, Stock, Customers, Staff, Reports and Back office; the seven standard roles can be changed but not renamed or deleted. Users sign in with a name and password, or with a staff code and PIN; five wrong tries lock the account for five minutes, and the login window says how long is left. When a user's role lacks a permission an action needs (a discount, a void, a return, End of Day…), another user can approve it at the till: their role must have both `pos.approve` and that permission.

Sales, voids, drawer movements and sessions, stock movements, invoices and their payments, purchase orders, RMAs and customer orders record the user's id next to the name they always kept. The audit log (Audit log screen, `audit.view`) holds every change to the tables listed in `AuditedTables` (items, clients, suppliers, users and roles, store settings, tenders, taxes, stations…) with the values before and after, plus sign-ins and failed sign-ins, approvals, voids, no-sales, End of Day, backups, restores and imports. `RetailPlusDbContext.SuppressAudit` holds the automatic lines back while rows are written in bulk (an import, a sync package).

Staff punch in and out from the login window or the Staff screen; a supervisor corrects an entry with a reason, which stays on the entry and is audited. Commission comes from commission rules (one item, then a category, a department or all items; a salesperson's own rule before everyone's; the latest start) and, where no rule applies, from the item's commission percent as before. Reports › Staff has hours by staff member, day and week, and commission by staff member.
Stock and money: stock counts (by code, RFID tag or CSV file; count sheet, variance review, post or cancel), transfers to another location or store (ship, then receive what arrived; missing items are created at the receiving location), break case, gift cards and certificates (sold at the POS, spent with the Gift Card tender, balance = the sum of their transactions), bonus points kept as a history per client, tenders in a foreign currency (change in home currency), and Setup › Loyalty, Accounting (account codes and a balanced journal per business day, optionally saved at End of Day) and Branding (store logo and watermark). Every stock change of these writes a stock movement (Count, TransferOut/In, CaseBreakOut/In).

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
Ctrl+1 … Ctrl+0 for the screens; Enter and Esc on OK and Cancel). Ctrl+K goes to any screen, report, action or help
topic by typing a few letters; Ctrl+Shift+H opens the help and Ctrl+Shift+K lists the keys; F1, F2 and F3 are left
to Windows.

Inventory CSV files (*Import CSV*, *Export CSV*, *CSV template*) use Retail Plus's INVENTORY columns with the RFID
ones: EPC (one row per tagged piece, so an item with three tags is three rows with the same PLU), PWEIGHT and TOLRANCE
("90 GRAM", "2 GRAM"), SSTATUS (0 in stock, 1 sold) and RECEIPT#, followed by BARCODE, BRAND and SUPPCODE. Items can
have a picture (kept in the database, at most 800 pixels a side), shown in the item editor and at the POS.

## RFID

Tagged items ring up by themselves at the POS, alongside barcodes. Readers (D2184, TCP/IP or serial), their four
antennas and the station each antenna serves are set up in *RFID readers*; a site with many readers on an Ethernet
switch runs the RFID hub service on one PC:

```
dotnet run --project src/RetailPlus.RfidHub -- --db "%LOCALAPPDATA%\RetailPlus\smaretail.db"
```

and every station points at it in *Setup › This station › RFID*. Design, installation and capacity:
[docs/RFID.md](docs/RFID.md).

## Import a store's Retail Plus data

From the app: Tools › Import from Retail Plus 5. From the command line:

```
dotnet run --project src/RetailPlus.Import -- --source <data folder> [--db <file> | --connection <string> | --localdb <name>] [--ini <Plus50.INI>] [--force]
```

Without a database option the import goes into the app's default SQLite file. It runs in one transaction, replaces everything but the store itself, records each item's opening stock as its first stock movement, checks control totals (record counts, stock, balances) against the source, and writes `report.md` and `report.json`. Imported documents keep their Retail Plus numbers; new ones are numbered per station from station × 10,000,000 + 1.

## Web back office

`web/` is the browser side for administrators and super administrators, in the desktop app's design: dashboard across stores, tills and packages, sales, stock, clients, suppliers, gift cards, reports, staff, users and roles, stores, setup and the audit log, each page shown only to the roles that may open it. It is a Next.js static export (`cd web && npm install && npm run build` writes `web/out`, which the server serves) and uses only the server API in [docs/API.md](docs/API.md); `npm test` and `npm run e2e` check it. Running it against a local server: [web/README.md](web/README.md).

## Build and test

Requires the .NET 10 SDK (see `global.json`), SQL Server LocalDB for the SQL Server half of the database tests, and Python 3 for the live oracle test.

```
dotnet build RetailPlus.sln
dotnet test RetailPlus.sln
```

- **Database tests** run twice: on a SQLite file in the temp folder (always) and on their own database on `(localdb)\MSSQLLocalDB`. Set `RETAILPLUS_TEST_SQL` to a connection string (without a database name) to use another server. Without a server the SQL Server half is skipped.
- **The live oracle test** reads every table in `spike-data/baseline` and `spike-data/run2`, or in the folders listed in `RETAILPLUS_DBF_SAMPLES` (separated by `;`), and compares the reader with `tools/dbf/dbf_to_json.py`. Without data or Python it's skipped; the fixture tests always run.

## Database migrations

```
dotnet tool restore
dotnet ef migrations add <Name> --project src/RetailPlus.Data.Sqlite --startup-project src/RetailPlus.Data.Sqlite --output-dir Migrations
dotnet ef migrations add <Name> --project src/RetailPlus.Data.SqlServer --startup-project src/RetailPlus.Data.SqlServer --output-dir Migrations
```

Every model change gets a migration in both projects (a schema test fails otherwise). The app applies them on start. The SQL Server tools target the LocalDB database `RetailPlus_Dev` unless `RETAILPLUS_DESIGN_DB` holds a connection string.

The vendor installer (`Retail52.exe`) and manual (`Retail Plus 50.pdf`) in this folder are reference material only and are excluded from git.
