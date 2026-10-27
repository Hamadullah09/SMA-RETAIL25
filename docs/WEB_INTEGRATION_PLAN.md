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

## 10. Phase 5 status: staff and security (2026-10-28)

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
