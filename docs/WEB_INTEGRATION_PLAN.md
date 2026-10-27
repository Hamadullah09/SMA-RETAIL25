# Desktop and web integration plan

Draft for discussion, 2026-10-27. Compares `desktopApp` at 7303495 with `main` (the web system, live at
pos.sma-techno.net) at 0a4d78e. The two branches share no history.

## 1. What was asked

- Cashiers, supervisors and managers work in the desktop app; admins and super admins work on the web and monitor.
- The desktop app keeps working with no internet, on a local SQLite database.
- At End of Day, or whenever someone asks, the desktop app sends its data to the server as one file ("an ISO image"),
  and the server stores it in the central SQL Server database.
- The web UI is redesigned to look and work like the desktop app.
- Web features that the desktop app lacks are added to the desktop app.
- The desktop app's data model is the reference; the web database is migrated to it.

## 2. The two systems today

| | Desktop (`desktopApp`) | Web (`main`) |
|---|---|---|
| UI | WPF, warm theme (cream to yellow, charcoal, Century Gothic) | Next.js 14, cool grey with an indigo accent, Onest font |
| Server | None. Every till opens the store's SQL Server database over the LAN | ASP.NET Core API (42 controllers, about 315 endpoints, 4 SignalR hubs) and a terminal agent per till |
| Database | EF Core 10 on SQL Server / LocalDB, 43 tables, 7 migrations | EF Core 10 on SQL Server, 104 tables, 12 migrations |
| Keys | `int` identity; invoice, PO, client and RMA numbers from 4 database-wide sequences | `bigint` identity; numbers from per-location sequences |
| Store and till on rows | No store. Location is a stock location. Station number only on Sale, DrawerSession, SuspendedSale | `location_id` on most tables, `station_id` on sales and drawers |
| Stock | `Item.OnHand` changed in place; no movement row for sales or receipts | `stock_ledger_entries` (every movement) plus `stock_levels` |
| Times | Local `DateTime`, `datetime2(0)` | `datetimeoffset` in UTC, created/modified columns |
| Users | `UserAccount` with access level 0–4 | Identity users, roles, 62 permission keys, staff code and PIN |
| Works offline | No (needs the LAN database) | No (the cart lives on the server) |
| Sync between sites | None | None |
| Tests | 159 pass, 2 skipped (run 2026-10-27) | About 1,027 test methods (not run here) |

## 3. Feature gaps

### 3.1 On the web, missing from the desktop app

| Area | Web has | Desktop has |
|---|---|---|
| Users and security | Roles with a permission editor (62 keys), staff code + PIN, PIN lockout, approval from another station | Levels 0–4, supervisor password |
| Audit | Audit log with before/after values per request | None |
| Staff | Time clock (punch in/out, edits), commission rules, hours and commission reports | Commission columns on items, staff/commission report |
| Stock | Stock ledger, stock counts (count sheet, CSV import, variance, post), transfers between stores (ship, in transit, receive), case break | Adjust stock, copy to location |
| Money | Gift cards, gift certificates, loyalty policy screen, multi-currency tenders, AR ledger | Points on clients, FX rates table |
| Orders | Layaways and quotes as their own records with payments and conversion | Invoices with kind Quote / Layaway |
| Catalog | Bulk changes with preview, catalogue PDF, envelopes, packing slip | Change prices, labels |
| Setup | Branding (logo, watermark), effective-dated taxes, pricing-rule order, numbering, stations with peripheral profiles, device enrolment, trolleys | Business, taxes, POS, users, tenders, lists, texts, fast keys, this station |
| Back office | Fiscal year close/reopen, accounting export (scheduled), backup download, undelete for all masters, data migration wizard | Year end, backup/restore, undelete for items |
| Help | Help centre (24 topics), Ctrl+K command palette | Key list (Ctrl+Shift+K) |
| Self-checkout | Shopper phone accounts and trolleys (from `mobile-dev`, merged into `main`) | None |

### 3.2 In the desktop app, missing from the web

- The design that is wanted (section 6).
- Split tender in the Pay window (the web API accepts several tenders but the web Pay dialog sends one).
- 21 reports with print, CSV and HTML (web: 8, CSV only), payment mix, by hour, voided sales, negative stock, no sales in period.
- RMAs (customer and supplier), recurring billing.
- Fast keys (product buttons) set up per store.
- RFID reader discovery on the network, one beep per tag, stock limit for tagged items.
- Inventory CSV in the Retail Plus layout with EPC, weight and tolerance.

### 3.3 Needed for admin / super admin monitoring, in neither

- One dashboard across all stores, and a comparison between stores.
- Sales per till and per cashier, drawer variance per till.
- When each till last sent its data, which tills are behind, failed packages.
- Super admin over several stores (or several companies, see decision 2). The web has one location per user and no
  check that a user only sees their own location.

## 4. Database: what the desktop model needs to be the central model

The desktop model is the right business model (it follows Retail Plus field by field), but it was designed for one
database shared by the tills of one store. With one SQLite file per till merged on a central server, these changes
are needed whatever else is decided:

1. **A global id on every row a till creates.** Local `int` ids collide between tills. Add a `Guid SyncId` (unique) to
   every table; the `int` key can stay as the local primary key.
2. **Store and station on every transaction.** Add a `Store` table and `StoreId`; add `StationId` where it is missing
   (Invoice, InvoicePayment, Rma, CustomerOrder, StockAdjustment, DrawerMovement).
3. **Numbers per till.** Database-wide sequences would give two tills the same invoice number. Each station gets its own
   counter (for example station 3 issues 3000001, 3000002, …), kept in a counters table because SQLite has no sequences.
4. **Stock as movements.** Sales and receipts change `OnHand` without writing a movement, so the server cannot add up
   several tills. Add a `StockMovement` table (sale, return, receipt, adjustment, transfer, count); `OnHand` stays as a
   snapshot computed from it. The web's `stock_ledger_entries` is the model for this.
5. **UTC times and change tracking.** `DateTimeOffset` in UTC, `CreatedAt`/`UpdatedAt` on every table, soft delete on
   master data so that deletions travel too.
6. **Users by id.** `Sale.Staff`, `VoidedBy`, `DrawerMovement.User` and others hold user names; add the user id.
7. **SQLite blockers in the current code:** the 4 sequences and `NEXT VALUE FOR`; `rowversion` on 6 tables;
   `BACKUP DATABASE`, `RESTORE`, `DBCC CHECKDB` in `BackupService`; the case-insensitive collation (logins, unique codes,
   searches); decimal sums and ordering inside SQL in the reports and dashboard (to be tested on SQLite); the unique
   index that relies on NULLs being equal (`DocumentText`); the DBF importer is SQL Server only. All 7 migrations are SQL
   Server-specific, so SQLite needs its own migration set.
8. **New tables for the web features in 3.1:** Roles, Permissions, RolePermissions, AuditLog, TimeClockEntries,
   CommissionRules, StockCounts, StockTransfers, GiftCards, LoyaltyPolicy, Stores, Devices, SyncBatches.

Migrating the web database "to the desktop model" without items 1–5 and 8 would lose what the web has today
(multi-location, ledgers, permissions, audit) and would still not allow tills to be merged.

## 5. Proposed architecture

```
 Till PC (desktop app)                                Server                          Browser
 ┌──────────────────────┐   sync package (.smapkg)   ┌───────────────────────┐       ┌──────────────┐
 │ WPF app              │ ── HTTPS, or a file on ──▶ │ ASP.NET Core API      │ ◀──── │ Web (Next.js)│
 │ SQLite (local)       │    USB uploaded on the web │ SQL Server (central)  │       │ admin, super │
 │ outbox of changes    │ ◀── master data package ── │ same model library    │       │ admin        │
 └──────────────────────┘   (items, prices, users,   └───────────────────────┘       └──────────────┘
                             taxes, settings)
```

- **One model library for both.** `RetailPlus.Core` and `RetailPlus.Data` are used by the desktop app (SQLite provider)
  and by the server (SQL Server provider), each with its own migration set. This is what makes the web database the
  desktop model, table for table.
- **The "ISO image" is a sync package.** An `.iso` file is a CD/DVD image; it cannot be merged into a database row by
  row. Proposed instead: one `.smapkg` file (a zip) per send, holding `manifest.json` (store, station, batch id, time
  range, row counts, checksums) and the changed rows. It is signed with the till's key, and the server ignores a batch id
  it already has, so sending twice is harmless. It goes automatically at End of Day, with a *Send to server* button at
  any time, waits in a queue while there is no internet, and can be carried on a USB stick and uploaded from the web.
  If a full daily backup is also wanted, the package can carry a copy of the SQLite file as well.
- **No conflicts on transactions.** Sales, payments, drawer sessions and stock movements are only ever added, never
  edited, so tills cannot overwrite each other. Master data (items, prices, taxes, users) is edited on the web by the
  admin and flows down to the tills; tills may add clients (with a `SyncId`), see decision 5.
- **Near-live when online.** While the server can be reached, the same package is sent every few minutes in the
  background, so the admin sees today's sales during the day; End of Day is the guaranteed send.
- **Roles.** Desktop: Cashier, Supervisor, Manager. Web: Admin (one store), Super Admin (all stores). Users and password
  hashes come down with the master data, so a cashier can log in with no internet.

### What happens to the current web backend (Retail25)

| Option | Meaning | Trade-off |
|---|---|---|
| **A (recommended)** | New server API on the shared desktop model. Keep from Retail25 what does not depend on its tables: JWT sign-in, the role/permission ideas, the audit approach, the Next.js shell and pages (restyled). The server-side cart and POS flow is retired, because tills sell in the desktop app | One model, one set of business rules. Retail25's API and tests are not carried over as they are |
| B | Keep Retail25 and migrate its 104 tables to the desktop model | Most of its 278 handlers and about 1,000 tests would be rewritten against new tables; more work than A for the same result |
| C | Keep both models; translate in the upload endpoint | Least work now, but two models and two sets of rules for good, which is what was asked to avoid |

## 6. Web UI in the desktop design

Tokens to carry over from `src/RetailPlus.App/App.xaml`:

| Token | Value |
|---|---|
| Page background | gradient `#ECEBE7` → `#F2F0EA` (45%) → `#F7ECC9` (80%) → `#F8E39C` |
| Card | `#FFFFFF`, radius 14 (dialogs) / 10 (controls) |
| Ink / Charcoal / Charcoal soft | `#1E1E1E` / `#2B2B2B` / `#3A3A3A` (current choice pills, navigation) |
| Sun / Sun dark / Sun light | `#F7D35C` / `#E2B52A` / `#FFF2C2` (main action on each screen, focus) |
| Cream / Line / Muted / Stone | `#FAF7EE` / `#E6E1D3` / `#6B675E` / `#9A968D` |
| Danger / Ok | `#C2412D` on `#FBE4DF` / `#2F7D4E` on `#DFF1E5` |
| Type | Headings Century Gothic, text Segoe UI, 17 px body |
| Controls | Buttons at least 48 px with an icon and a key badge |

The web's `contrast.test.ts` and `tone.test.ts` check the current palette and will need new expected values; the
Playwright layout screenshots will change.

## 7. Phases

| # | Phase | Main work |
|---|---|---|
| 1 | Shared model and SQLite | Items 1–7 of section 4; SQLite provider and migrations for the desktop; reports and dashboard tested on SQLite; backup by file copy |
| 2 | Sync | Outbox, `.smapkg` export at End of Day and on demand, upload queue; server upload endpoint (idempotent, verified by counts and checksums); master data package down to tills |
| 3 | Server and web admin | Server API on the shared model, sign-in with roles, stores and tills, cross-store dashboard, till monitor, reports |
| 4 | Web redesign | Section 6 applied to the web shell and every page |
| 5 | Web features into the desktop | Section 3.1: permissions, audit, time clock and commissions, stock counts, transfers, gift cards, loyalty, accounting export, branding, help |
| 6 | Data and cutover | Move any real data from the current web database into the new model; switch pos.sma-techno.net over |

## 8. Decisions needed

1. **"ISO image":** is the sync package in section 5 what is meant, or a full copy of the database each day, or both?
2. **Super admin:** one company with several branches, or several companies each with their own stores?
3. **Live data:** does pos.sma-techno.net hold real shop data that must be carried over?
4. ~~**Web backend:** option A, B or C in section 5.~~ **Decided 2026-10-27: option A.**
5. **Master data:** are items and prices edited only on the web by the admin, or also by a manager on the desktop?
6. **Repository:** `desktopApp` and `main` have no common history. Proposal: the server and the web app move into the
   desktop tree (one repository, one model library) on a new branch.

## 9. Phase 1 status (2026-10-27)

Done, on branch `phase1-shared-model-sqlite`:

| Section 4 item | What was built |
|---|---|
| 1. Global ids | Every table's key is a version 7 GUID made by the app when the row is first saved (`Entity`, `Version7GuidGenerator`). Ordering never relies on the key; queries that did now order by date or line number |
| 2. Store and station | New `Store` table; `StoreId` on locations, stations, settings, texts and RFID readers; `StoreId` + `StationNumber` on sales, drawer sessions, suspended sales, invoices, invoice payments, customer orders, RMAs, purchase orders and stock movements. The DbContext fills them from the station it works for (`StationIdentity`), set by `SetupService.SeedAsync` |
| 3. Numbers per till | `NumberCounters` table: station *n* numbers invoices, customers, POs and RMAs from *n* × 10,000,000 + 1, so tills never repeat a number offline. Station numbers are unique across the company. Imported Retail Plus numbers are kept as they were |
| 4. Stock as movements | `StockMovement` (replaces `StockAdjustment`): opening stock, sale, return, void, receipt, adjustment and CSV import each write one, so an item's stock is the sum of its movements |
| 5. Times | `CreatedAtUtc` / `UpdatedAtUtc` (UTC, milliseconds, indexed) on every row, kept by the DbContext. Business times (when a sale was made) stay in the store's local time; `Store.TimeZoneId` says which |
| 7. SQLite | Both engines from one model: `RetailPlus.Data.Sqlite` and `RetailPlus.Data.SqlServer` hold one fresh `InitialCreate` each (the seven SQL Server migrations were replaced: no store database was in use yet). Sequences, rowversion (now an app-kept `Version` GUID), the SQL Server collation (NOCASE on SQLite) and the NULL-unique text index were replaced. Backup, restore and rebuild use SQLite's online backup and `integrity_check` on a till. The importer runs on either engine |

The desktop app now opens `%LOCALAPPDATA%\RetailPlus\smaretail.db` by default (Setup › This station can still point it at
SQL Server). An existing LocalDB database `RetailPlus_App` is not converted; the station starts with an empty file and the
data is imported again.

Checked: the solution builds with no warnings; 210 tests pass (every database test on SQLite and on LocalDB), 4 skip
(three need real Retail Plus data, one restores a SQL Server backup); `--smoke` passes on a SQLite file.

Left for the next phases:

- **Users by id** (item 6): sales and drawer movements still record the user name. This goes with the users and roles
  of phase 3.
- **What changed since the last send** (phase 2): updates made with `ExecuteUpdate` (stock counts, balances, flags) do not
  touch `UpdatedAtUtc`, and deleted rows leave no trace. The sync package needs a change log (or tombstones) for these.
- **Store scoping on the server**: on a till every query sees one store. Services that list clients, items or reports
  across stores get a store filter with the server API (phase 3).
- **RFID hub service** on SQLite reads the database file of the PC it runs on (`--db`); a hub serving several tills
  gets its reader list from the server in phase 2.

## 10. Phase 2 status: sync, and the core of the server (2026-10-28)

Done, on branch `phase2-sync-server`.

### What changed in the model

| Change | Why |
|---|---|
| `Tombstones` (entity, row id, time, store; `MergedIntoId` for merges) | Deletions travel. The DbContext writes one in the same save as every delete; the `ExecuteDelete` calls were replaced by load and remove |
| Shadow `EditedAtUtc` on every table | Last writer wins on what people edit. `UpdatedAtUtc` also moves when the system changes a count it keeps (a sale lowers the stock), so a till that sold an item before hearing of a new price would otherwise win with the old price. The later `EditedAtUtc` wins; between equal ones, the later `UpdatedAtUtc`. Columns the system keeps are listed in `SyncColumns` |
| Shadow `ChangeNumber` on every table, SQL Server only (`rowversion`) | The server's "everything changed since cursor X". See below |
| Every `ExecuteUpdate` sets `UpdatedAtUtc` (`.Touched()`), and `EditedAtUtc` too when it changes what people edit (`.Edited()`) | Changes made outside the change tracker are sent too |
| `ItemPeriodSales.StationNumber` (part of the unique key) | Two tills selling the same item in the same month each add to their own row, so neither overwrites the other; screens and reports add the rows up |
| Matrix lines keep their ids when an item is saved, each count change of a line writes its own stock movement, and the importer writes one opening movement per line | `Item.OnHand` and `ItemVariant.OnHand` are always the sums of their stock movements (tested for opening, sale, return, void, receipt, adjustment, matrix edits and CSV import) |
| `SyncBatches`, `SyncStates`, `StationLinks` (`ILocalOnly`, never sent) | Every package received or written; a till's progress; the server's side of each till (encrypted key, enrolment code, last upload and download, app version) |

**Change numbers.** The brief proposed a `long` column filled from a counter row raised inside each save. That misses
every `ExecuteUpdate` and plain SQL statement unless each call site takes a number itself, and the web back office will
call the same services (delete an item, flag, barcode, balances) on the server. A `rowversion` is raised by SQL Server on
every insert and update whatever made it. Its catch, that numbers are taken when a row is written but become visible when
the transaction commits, is closed by reading only up to `MIN_ACTIVE_ROWVERSION() - 1`: no transaction still running can
commit a number below it, so a till's cursor never passes a row it has not seen. Tills have no such column (they never
serve downloads).

**Station keys** are not on `Station` (which travels to the tills) but in the server-only `StationLinks`, encrypted with
ASP.NET Core data protection; its key ring is in the server's data folder and is itself encrypted with DPAPI on Windows,
so a copy of the database alone gives no key away. The key leaves the server once, to the till that joins, which keeps
it in `station.json` encrypted with DPAPI for its Windows user.

### The package and its rules

A `.smapkg` is a zip: `manifest.json` (format, batch id, kind, store, station, app version, times, cursors, and per table
its row count and SHA-256), `tables/<Entity>.json` (rows with the CLR property names), `tombstones.json`, optionally
`database.sqlite`, and `manifest.sig` (HMAC-SHA256 of the manifest with the station's key). Requests from a till are
signed the same way (`X-Station`, `X-Station-Time`, `X-Station-Signature`).

The tables come from the EF model, parents before children. `SyncPolicy` holds every exception in one place, and a table
added later needs no code: rows with a store go up from the tills and down to the tills of that store; rows that belong
to a row of a store (lines, matrix lines, antennas) follow it through their required reference; rows without a store go
everywhere. The exceptions: stores, stations, roles and role grants only come down; a till receives its store's users and
the users of every store (with password and PIN hashes, for signing in offline) and its own number counters; a till
cannot change another store's users or give anyone a role for every store; a till may only send and delete rows of its
own store. `SyncPolicy.Table<T>(flow, receives)` is the place for a table such as a transfer that must reach two stores.

Applying a package checks the signature, every checksum and row count, the station, and that the batch was not applied
before; then, in one transaction, it deletes what the tombstones name (optional references are cleared, a required one
still in use refuses the package), inserts new rows, and updates rows whose copy is newer. A copy older than a deletion
does not bring the row back (a till's packages overlap by ten minutes, so it may send a row again). Columns the system
keeps are never taken for a row already there; afterwards `OnHand` is recomputed from the stock movements, a client's
balance from their invoices (on the server: every store's invoices; on a till: its store's), and bonus points, which
cannot be derived (a client may bring points from Retail Plus), are replayed from the sales that arrive: a new completed
sale adds what it earned less what was redeemed, a sale arriving voided takes it back. On order, customer orders, last
order and receipt dates, year-end counts, flags and sign-in failures stay each database's own.

**The same code made twice.** When a new row's unique value is taken (the same stock code made offline on two tills of
a store, a department, a tender, a location code, a user name in the same store, an RFID tag), the server keeps its row,
moves the new row's references to it, writes a merge tombstone (so the till's later packages are mapped and the till
replaces its row by the server's, moving its own references over), and notes it in the batch. Stock stays right: both
tills' movements end up on the one item. Any other clash (an invoice number, a staff code, a station number) refuses the
package with a message naming the rows. Merging was chosen over refusing because refusing would stop a till's sync until
someone repairs data by hand, and the clashes listed are, in practice, the same thing made twice.

### What the till and the server do

The till writes a package of everything changed since its last one (`UpdatedAtUtc`, ten minutes of overlap) into
`outbox` next to its database file, sends the outbox oldest first (a refused package goes to `outbox/refused` and the
others still go), then downloads what changed since its cursor and applies it. This runs after End of Day, from *Send to
server*, and every `SyncIntervalMinutes` (default 5). *Save package to file* writes one package with everything the
server has not confirmed, optionally with an online-backup copy of the database (the "image"); *Load package from file*
applies the server's package for this till. Joining moves the till's data from the store and station it was seeded with
to the server's, in the same transaction as the server's first package (where the same thing exists on both, the
server's is kept). The server records every package, refused and duplicate ones too, and lists each till with its last
upload and download, last batch, failures since the last good package, app version, and "behind" after
`Server:BehindAfterHours` (24) without an upload.

### Known limits

- Tills on a shared SQL Server database (the old LAN set-up) cannot join a server; the till side relies on SQLite
  checking references at the commit.
- Concurrent edits of one invoice at two tills (two payments before either synchronises) keep the later invoice row; both
  payments are kept, but the invoice balance is the later till's.
- On order and customer-order counts are each database's own (imported values cannot be worked out again).
- A till's first package after joining holds every row it has, including what the server just sent it; nothing changes
  on the server, but it is large.
- Downloads are built in memory; a store with a very large history makes a large first download.
- Uploads are applied one at a time per station on one server process; several server processes would need a lock in
  the database.
- Re-importing Retail Plus data into a till that already joined a server wipes rows without tombstones; import before
  joining.

Next: phase 3 (the web admin, reports and dashboards on these endpoints) and the merge with the other phase 5 branches,
whose new tables synchronise by the default rules.

## 11. Phase 5 status: staff and security (2026-10-28)

On branch `phase5-staff-security`:

| Area | What was built |
|---|---|
| Users and roles | Setup › Users (role, full name, home store, active, staff code and PIN, reset password) and Setup › Roles (permissions grouped as in `Permissions.All`, level; custom roles added, renamed and deleted; standard roles changed only). Nobody can remove the last active user who may manage users |
| Sign-in | Name and password, or staff code and PIN; a locked account says how many minutes are left; users of another store are refused |
| Permissions | Every screen and action checks a permission instead of an access level; a missing permission offers the supervisor approval (password or PIN; the approver needs `pos.approve` and the permission). `AppServices.Can(int)` is gone |
| Users by id (section 4 item 6) | Sale (staff, voided by), drawer sessions (closed by) and movements, stock movements, invoices (sales person) and payments (taken by), purchase orders (created, received by), RMAs, customer orders and imported staff periods keep the user's id next to the name |
| Audit log | `AuditEntries` (station-stamped): the DbContext writes added, changed and deleted rows of the tables in `AuditedTables` with the values before and after; sign-ins, failed sign-ins, approvals, voids, no-sales, End of Day, backups, restores, imports and time clock changes are written explicitly. `RetailPlusDbContext.SuppressAudit` holds the automatic lines back (imports; the sync can use it). Audit log screen behind `audit.view` |
| Time clock | `TimeClockEntries`: punch in and out at the login window or the Staff screen, one open entry per user, supervisor corrections with a reason (audited); hours reports by staff member, day and week |
| Commission | `CommissionRules` (everyone or one user; all items, a department, a category or an item; percent of net sales or amount per unit; effective dates). Lines no rule covers keep the item's commission percent. Commission report and per-salesperson detail |
| Help | Help screen with 22 searchable topics; Ctrl+K goes to any screen, report, action or help topic; Ctrl+Shift+H opens the help |

## 12. Phase 6 status: data and cutover (2026-10-28)

On branch `phase6-cutover`:

| Area | What was built |
|---|---|
| Cutover tool | `src/RetailPlus.Cutover` (`RetailPlus.Cutover --source … --target … [--dry-run] [--report …] [--store-code …]`): reads Retail25 with ADO.NET, writes the new model through `RetailPlusDbContext` in one transaction with the audit lines held back and one Import line per run, records an `ImportBatch`. Ids are name-based UUIDs of each Retail25 row, so a second run changes nothing and a later run updates what changed; a dry run rolls back (or stages in a temporary file when the target has no schema) |
| Mapping | Stores and locations, stations, settings, branding, tenders, exchange rates, departments, categories, roles (standard or custom with translated keys), users, suppliers, items with prices, suppliers, matrix, kits, serials, RFID tags and pictures, opening stock, clients with accounts, balances and points, gift cards and certificates, sales history, invoices, open layaways and quotes, open purchase orders, customer orders, transfers in transit, commission rules, time clock. Everything else is listed as not carried over, with counts |
| Checks | Stock per store (units, ledger, value at cost), receivables, layaways owing, client balances, gift card liability, loyalty points, sales per business day and per tender, row counts; exit code 2 when one differs |
| Passwords | `SetupService.VerifyPassword` accepts ASP.NET Identity v3 hashes and the sign-in stores them again in the program's format; Retail25's Argon2 PINs are not carried over |
| Runbook | [docs/CUTOVER.md](CUTOVER.md): backup, trial run on a copy, freeze, final run, deploying the server and web app, switching the site, enrolling tills, first download, checks, rollback, mapping and decisions |
