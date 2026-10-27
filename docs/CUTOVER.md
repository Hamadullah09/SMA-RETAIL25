# Cutover: from Retail25 to SMA RETAIL

How pos.sma-techno.net moves from Retail25 (the web system: ASP.NET Core API, Next.js, SQL Server, 104 tables) to the new
server, the new web app and the desktop tills, which share the desktop data model (docs/WEB_INTEGRATION_PLAN.md, option A).
Part 1 is the tool, part 2 the runbook, part 3 the mapping and the decisions behind it.

Nothing in this document is run against the live site by the developers: the tool was built and tested against LocalDB
copies of Retail25's demo database (`Retail25_Source`) only.

## 1. The cutover tool

```
RetailPlus.Cutover --source "<Retail25 SQL Server connection>" --target "<connection string or .db file>"
                   [--provider sqlserver|sqlite] [--dry-run] [--report <file.md>] [--store-code <code>]
```

| Option | Meaning |
|---|---|
| `--source` | The Retail25 database. It is only read (plain ADO.NET, `ApplicationIntent=ReadOnly`); no Retail25 code is used. |
| `--target` | The new database: the server's SQL Server database (a connection string), or a SQLite file. It is created or upgraded first (`DataContext.MigrateAsync`). |
| `--provider` | `sqlserver` or `sqlite`. Default: SQLite for a file name, SQL Server for a connection string. |
| `--dry-run` | Reads, maps and checks everything, and writes nothing (see below). The report is made all the same. |
| `--report` | Where the report goes (Markdown, with a `.json` copy beside it). It is also printed. Default: `%LOCALAPPDATA%\RetailPlus\CutoverReports\cutover-<time>.md`. |
| `--store-code` | Only the Retail25 location with this code (`TST`, `NTH`…), as one store. Without it, every location. |

Exit codes: **0** done and every check matches; **2** done, but a check differs (read the report before using the
database); **1** failed or refused (nothing was written); **64** wrong options.

Build it with the solution (`dotnet build RetailPlus.sln`) or publish it on its own:
`dotnet publish src/RetailPlus.Cutover -c Release -o <folder>`.

### What a run does

1. Reads the Retail25 tables it maps (about 60) into memory, and the row count of every table.
2. Creates or upgrades the target. A dry run does neither (see below).
3. Opens one connection and **one transaction** on the target, and writes through `RetailPlusDbContext` with
   `SuppressAudit = true`, area by area in the order the rows depend on each other: stores, locations, stations and
   settings; tenders, exchange rates, departments and categories; roles and users; suppliers; items with their prices,
   suppliers, matrix, kits, serial numbers, RFID tags and pictures; clients; sales; invoices, layaways and quotes; opening
   stock; gift cards and loyalty points; purchase orders; customer orders; transfers in transit; commission rules and the
   time clock.
4. Works out the checks (part 1.3) from Retail25 and again from the target, inside the same transaction.
5. Writes one **Import** line to the audit log ("Retail25 cutover from …: n rows added, n changed, n removed; all n checks
   match"), and commits. A failure rolls everything back.
6. Records the run as an `ImportBatch` (status, the row count of every Retail25 table, the full report as JSON), like the
   Retail Plus importer does.

### Running it again

The cutover is **idempotent**. Retail25 keys its rows with bigint identities, so each row's id here is made from its
Retail25 table and key (a name-based UUID, version 8: `StableIds`), and the same Retail25 row always lands on the same row.
A row that is not there yet is added; a row that is there is updated with the values that differ, and left alone (its
`UpdatedAtUtc` too) when nothing differs. So:

- a second run over unchanged data adds, changes and removes nothing (the report says `0 rows added, 0 changed, 0 removed`);
- a run after Retail25 changed updates what changed, adds what is new, and removes the children of carried rows that
  Retail25 no longer has (a supplier taken off an item, a kit component, an invoice line). Only rows the cutover itself
  made (version 8 ids) are ever removed;
- what the new system owns stays: a password or PIN set in the new program (or a Retail25 hash already upgraded at a
  sign-in), failed sign-ins and locks, a time clock entry a supervisor corrected, the flag on an item or client;
- rows deleted outright in Retail25 between two runs (not soft-deleted) stay in the target.

Each run adds an `ImportBatch` row and an Import audit line; those are the record of the runs, not data.

### Dry run

`--dry-run` does everything a real run does inside the transaction and then **rolls it back**, so the report shows what
would be written and whether the checks would match. If the target does not exist yet or is not up to date, it is
neither created nor upgraded: the run is staged in a temporary SQLite file that is deleted afterwards (the report says so).
No import batch or audit line is kept.

### The report

For each area: the Retail25 tables it reads, rows read, added, changed, unchanged, removed and skipped, with the reason
for every skip. Then the checks, what is not carried over (every Retail25 table with rows that the tool does not read,
and the parts of read tables that stay behind, with counts and the reason), values made to fit (text cut to the column
size, amounts rounded to the column's decimals), the decisions taken on this data (renumbered stations, renamed
departments, which role a Retail25 role became…) and every warning.

### Checks

Each is worked out from Retail25 and again from the new database; the exit code is 2 when one differs.

| Area | Check |
|---|---|
| Stock, per store | units on hand; units in the stock movements (on hand equals the ledger); value at cost (on hand × average cost, last cost when there is none) |
| Receivables | open invoice balances; owing on open layaways; client balances (customer account balances plus layaways owing) |
| Gift cards | liability: what is left on cards and certificates |
| Loyalty | points held by clients; points in the loyalty transactions |
| Sales, per store | sales (completed and voided); net total of completed sales; sales and net total per business day (every day up to 31 days, otherwise the days that differ and the number of days alike); amount per tender |
| Rows | items, clients, suppliers, users and sales written |

### Time

On the demo database plus the test data (about 3,000 rows) a run takes 15–20 seconds, most of it creating the database.
Progress lines carry the elapsed time; time the trial run on a copy of the real database to size the freeze window.
Everything is read into memory first: allow some hundreds of megabytes for a large shop (item pictures included).

## 2. Runbook: switching pos.sma-techno.net

Times below are an example; adjust to the trial run.

### 2.1 Preconditions (the week before)

- [ ] The new server is ready: Windows Server with IIS and the .NET 10 Hosting Bundle (or a Linux host with a reverse
      proxy), SQL Server 2019 or later, and `RetailPlus.Server` and the web app built from the release branch.
- [ ] A SQL login for the cutover that can read the Retail25 database (`db_datareader`) and create the new database
      (`dbcreator`), or owns an empty one.
- [ ] `RetailPlus.Cutover` published to the server (`dotnet publish src/RetailPlus.Cutover -c Release`).
- [ ] The desktop app is installed on every till PC (it can run on its own SQLite file until it is enrolled).
- [ ] Each Retail25 location has its three-letter code (`locations.legacy_code`); each becomes a store with that code.
- [ ] In Retail25, tidy what the report of the trial run complains about (long stock codes, duplicate names) or accept it.
- [ ] Staff are told: they sign in with their Retail25 user name and password; **PINs must be set again**; Retail25's
      "Clerk" is "Stock clerk" here.
- [ ] The DNS record of pos.sma-techno.net has a short TTL (5 minutes) from the day before, if the server changes.
- [ ] A rollback owner and a go/no-go time are agreed (2.11).

### 2.2 Full backup of the Retail25 database

On the Retail25 SQL Server, before anything else and again at the start of the freeze:

```sql
BACKUP DATABASE [Retail25] TO DISK = N'D:\Backups\Retail25-precutover.bak'
    WITH COPY_ONLY, CHECKSUM, INIT, NAME = N'Retail25 before the cutover';
RESTORE VERIFYONLY FROM DISK = N'D:\Backups\Retail25-precutover.bak' WITH CHECKSUM;
```

Copy the file off the server. Keep it for at least a year: Retail25's audit log, drawer history and sales archives are
not carried over and stay in it.

### 2.3 Trial run on a copy (days before)

1. Restore the backup on a staging SQL Server (never on the live one) as `Retail25_Trial`.
2. Dry run, and read the report from top to bottom:
   ```
   RetailPlus.Cutover --source "Server=staging;Database=Retail25_Trial;Trusted_Connection=True;TrustServerCertificate=True"
                      --target "Server=staging;Database=SmaRetail_Staging;Trusted_Connection=True;TrustServerCertificate=True"
                      --dry-run --report trial-dry.md
   ```
   The result line must say all checks match. Look at "Not carried over", "Values made to fit", the decisions and the
   warnings: each is either fixed in Retail25 (and the trial repeated) or accepted.
3. Real run into the staging database (same command without `--dry-run`, report `trial.md`); then run it again: the
   second report must say `0 rows added, 0 changed, 0 removed`.
4. Point a staging `RetailPlus.Server` and one test till at `SmaRetail_Staging`. Sign in as a carried-over user with their
   Retail25 password (the hash is upgraded at that sign-in); ring up a sale; look at a few items' stock, a client's
   balance and statement, a gift card's balance, a client's points, yesterday's sales report against Retail25's.
5. Note how long the run took; the freeze window is the run time plus the checks in 2.10, with margin.

### 2.4 The freeze (start of the cutover window, after closing)

1. At every Retail25 station: End of Day (close the drawer). Post or cancel stock counts in progress; receive transfers
   that have arrived (transfers still in transit are carried over and received in the new system); finish or void
   sales left in a cart.
2. **Stop sales on the web POS**: stop the Retail25 site and its app pool in IIS (or put `app_offline.htm` in its folder),
   and stop the terminal agents on the tills. Nothing may write to Retail25 from here on.
3. Take the second full backup (2.2), named `Retail25-final.bak`.
4. Optional: `ALTER DATABASE [Retail25] SET READ_ONLY WITH ROLLBACK IMMEDIATE;` makes sure of it.

### 2.5 The final run

Into a new, empty production database (recommended):

```
RetailPlus.Cutover --source "Server=sql01;Database=Retail25;Trusted_Connection=True;TrustServerCertificate=True"
                   --target "Server=sql01;Database=SmaRetail;Trusted_Connection=True;TrustServerCertificate=True"
                   --report D:\Cutover\final.md
```

- Exit code **0**: go on. **2**: read the checks that differ; decide (2.11) before going on. **1**: nothing was written;
  read the error, fix, run again.
- To shorten the freeze, the production database can be loaded the day before (a real run while Retail25 still sells);
  the run during the freeze then only writes what changed since. Do not do this once anyone works in the new system.
- Back up the new database straight away (`BACKUP DATABASE [SmaRetail] … WITH COPY_ONLY`) and keep the report and the
  final Retail25 backup with it.

### 2.6 Deploying RetailPlus.Server and the web app

- Publish the server (`dotnet publish src/RetailPlus.Server -c Release -o <folder>`) and build the web app
  (`npm ci && npm run build` in `web/`, which writes the static export to `web/out`). Copy the server to the web server
  and the contents of `web/out` to a folder beside it. The server serves the web app itself (`Server:WebRoot`), so one
  site serves both the API (`/api`) and the web pages.
- Settings (the server's README lists them all), in `appsettings.Production.json` or as environment variables:
  - `ConnectionStrings:RetailPlus`: the `SmaRetail` database the cutover filled.
  - `Server:WebRoot`: the folder holding the web app's files.
  - `Server:DataFolder`: a folder that is backed up. It keeps the token signing key and the keys that protect the
    tills' keys; losing it means every till joins again.
  - `Jwt:Key`: at least 32 random characters (or leave it empty and let the server keep one in the data folder).
  - `Bootstrap:AdminUserName`: the carried-over user who looks after every store (the owner). No carried-over user has
    the Super administrator role, and a store administrator cannot give it, so on its first start the server makes this
    user super administrator (they keep their Retail25 password). Remove the setting afterwards.
  - `Cors:Origins`: not needed in production (the web app comes from the same site).
- **IIS**: a site with an application pool of "No Managed Code", the settings above in `appsettings.Production.json` or
  environment variables of the app pool, the HTTPS binding with the pos.sma-techno.net certificate, and the app pool
  identity given a login on SQL Server with access to `SmaRetail` only.
- **Reverse proxy** (nginx, Caddy, Apache) on Linux or in front of Kestrel: run the server as a service on a local port,
  terminate TLS at the proxy, forward `X-Forwarded-For` and `X-Forwarded-Proto`, and allow request bodies large enough for
  the tills' sync packages.
- Start it and check, before the switch, that the site answers on the server itself and that an administrator can sign in
  on the web with their Retail25 password.

### 2.7 Switching the site

- Same server: remove the `pos.sma-techno.net` binding from the Retail25 site and add it to the new site.
- New server: change the DNS record to the new server's address.
- Keep Retail25 stopped, its database read-only, for lookups and for the rollback.

### 2.8 Enrolling the tills

On each till, in the desktop app (Setup › This station):

1. Set the station number **as the report says**: Retail25 station `001` of the first store is station 1; a clashing code
   at another store got another number (listed under the decisions). Station numbers are unique across the company, as
   each station numbers its documents from its own range (station × 10,000,000 + 1).
2. Join the server: on the web, *Tills* › *Add a till* (or *New code* for a till already listed) shows an enrolment
   code; at the till, *Setup › Server* takes the server's address (https://pos.sma-techno.net) and the code, then
   *Join the server*. The till's data moves to the server's store and the station number the server gave.
3. Set up the hardware again (printer, drawer, scale, pole display, RFID readers): Retail25's device profiles are not
   carried over.

### 2.9 The first master-data download

Each enrolled till downloads the master data (items, prices, clients, users and roles, settings) of its store. Check on
each till that the number of items and clients matches the report's, then sign in with a Retail25 user name and password,
set the PINs (Setup › Users), and ring up a test sale and void it.

### 2.10 Checks after the switch

- [ ] The web app at https://pos.sma-techno.net loads; an administrator signs in.
- [ ] The user named in `Bootstrap:AdminUserName` signs in and sees every store (the server log says it made them super
      administrator); then take the setting out.
- [ ] At each till: a real sale reaches the server; stock, client balance and points change as they should.
- [ ] A few items' stock and value, every gift card balance asked about, and the receivables total agree with the report.
- [ ] Yesterday's sales report (per day and per tender) agrees with Retail25's for the same day.
- [ ] The audit log shows the Import line of the final run.
- [ ] Enter what the report lists as not carried over and still wanted: future tax changes, commission caps, kits that
      should not reduce stock, the watermark strength, hardware settings.
- [ ] End of Day at every till at closing.

### 2.11 Rollback

- **Before anyone sells in the new system** (the usual go/no-go point: the end of 2.10): put the binding or the DNS record
  back, `ALTER DATABASE [Retail25] SET READ_WRITE`, start the Retail25 site and the terminal agents. Nothing is lost;
  the new database can simply be dropped and the cutover run again later.
- **After sales were made in the new system**: those sales, payments, stock movements and new clients exist only there.
  Either stay and fix forward (preferred), or go back and re-key them in Retail25 from the new system's sales journal for
  the period since the switch. Agree the point after which only fixing forward is allowed (for example the first End of
  Day).
- Keep `Retail25-final.bak`, the new database's first backup and the final report together.

## 3. Mapping and decisions

### 3.1 Mapping

| Retail25 | New model | Notes |
|---|---|---|
| `locations` | `Store` + `Location` | One location is one store with one stock location, both with the three-letter code. The IANA time zone becomes the Windows zone id (`America/Toronto` → `Eastern Standard Time`). A store or location already in the target with the code is reused. |
| `stations` | `Station` | Station code `001` → number 1. A number already used by another store's station gets the first free number (decision 4). |
| `business_profiles`, current `tax_configurations`, `pos_policies`, `loyalty_policies`, `late_charge_policies`, base `currencies` | `StoreSettings` | Business name and address; the tax configuration in effect today (tax names, rates, compound, inclusive, registration number); POS flags (staff may discount, supervisor for voids, track staff sales, carry over city, random-weight barcodes); loyalty (points per dollar, points needed, percent or fixed reward); late charges (monthly rate, grace days); the smallest coin. |
| other settings | `StoreSettingExtra` | Kept by name (`R25_EMAIL`, `R25_WEBSITE`, `R25_LICENCE`, `R25_CURRENCY`, `R25_DAY_START`, `R25_TIME_CLOCK`, `R25_ADD_ON_CHARGE`…). |
| `branding_assets` | `StoreImage` | CompanyLogo → Logo, Watermark → Watermark. PNG and JPEG only (the tills cannot show WebP). |
| `currencies` (not the home currency, active) | `FxRate` | Same meaning: units of the currency per unit of home currency. |
| `tender_types` | `TenderType` | Display name (16 characters). Behaviour Cash → Cash, Card → Card, GiftCard and GiftCertificate → Gift card, OnAccount → On account, Manual → Cheque when named like one, else Other. The tender's currency. Deleted ones come over inactive (old payments name them). |
| `departments`, `categories` | `Department`, `Category` | Company-wide here: the same name at two locations is one entry. Names in capitals, as the program keeps them, cut to 15 characters (decision 6). |
| `AspNetRoles`, `role_permissions` | `Role`, `RoleGrant` | Trainee, Cashier, Clerk, Supervisor, Administrator → the standard roles Trainee, Cashier, Stock clerk, Supervisor, Administrator. Any other role → a custom role with the keys its Retail25 keys translate to (`PermissionMap`), plus `pos.approve` (decision 16). The seven standard roles are created when the target has none. |
| `AspNetUsers`, `AspNetUserRoles`, `staff_profiles` | `UserAccount` | User name (Retail25's, usually an e-mail address), full name, the password hash as it is (decision 8), the role with the most rights, the home location's store, the staff code, active. |
| `suppliers` | `Supplier` | Company, contact, address, phones, e-mail, number (decision 5). |
| `products` with `product_prices`, `price_breaks`, `sale_pricings`, `bonus_pricings` | `Item` | Stock code (16 characters, decision 7), name, type (same names), UPC/EAN as the barcode, department, category, tax flags, regular price (level 1) and levels 2–4, break quantities, last and average cost, margin, base stock, reorder point and quantity, on order, case quantity, ship weight, bin, messages, notes (description and notes), substitute, tag-along and case (parent) item, the current or next sale price (percent off, dates), bonus pricing (Retail25's "buy 3, 1 free" is "buy 2, get 1"), deleted. |
| `product_suppliers` | `ItemSupplier` | By rank; the first is the item's supplier and its reorder number the item's supplier code. |
| `matrix_dimensions`, `product_variants` | `ItemMatrixColumn`, `ItemVariant` | Columns by position; lines with their values, their UPC (or variant code) as the code, and their stock. |
| `kit_components` | `KitComponent` | Linked to the component item. |
| `serialized_units` | `ItemSerial`, `ItemTag` | The serial number and state; the EPC as an RFID tag (InStock/Provisioned/InCart/Reserved/Returned → in stock, Sold/Transferred → sold, Lost/Void → lost). |
| `product_images` | `ItemPicture` | The latest PNG or JPEG of each product. |
| `stock_levels` | `StockMovement` (Opening), `Item.OnHand`, `ItemVariant.OnHand` | One opening movement per level (item or matrix line), valued at the average cost; on hand is the sum of the movements (decision 10). |
| `customers`, `customer_accounts`, `customer_pricing_profiles` | `Client` | Name, company, billing address, ship-to address (as text), phones, e-mail, type, birthday, notes, last purchase and mailing; customer and account number (decision 5); credit limit; usual discount, price level (Retail25's level 1 is level 0 here), tax exemptions; points; balance = open invoices + layaways owing. |
| `loyalty_ledger_entries` / `reward_points` | `LoyaltyTransaction` (Opening) | One opening transaction per client with points (decision 13). |
| `gift_cards`, `gift_certificates` | `GiftCard` (Card, Certificate), `GiftCardTransaction` | Number (normalised as the till types it), issued, expiry, active, client; one opening transaction of what is left (decision 12). |
| `sales_transactions`, `sale_lines`, `sale_tenders`, `sale_tax_snapshots`, `sale_adjustments`, `commission_ledger_entries` | `Sale`, `SaleLine`, `SaleLineTag`, `SalePayment` | Number, station, time (store local), staff, client, totals (Total = grand total + cash rounding), tax names and rates, points, voids; lines with kind (Sale, Return, TradeIn; returned quantities negative), item, matrix line, serial number, EPC, price, discount, taxes, cost, commission; payments with tendered, change, authorisation, reference, currency and rate (decision 9). History does not move stock again. |
| `invoices`, `invoice_payments` | `Invoice` (Invoice), `InvoicePayment` | Every invoice of a carried store: number, client, date, total, balance, late charges, last payment, salesperson, the sale and its lines, the tax rates; void ones as voided. |
| open `layaways`, `layaway_lines`, `layaway_payments` | `Invoice` (Layaway), `InvoiceLine`, `InvoicePayment` | With the deposits; balance = total − paid; the goods are taken out of stock (decision 10). |
| open `price_quotes`, `price_quote_lines` | `Invoice` (Quote), `InvoiceLine` | |
| `purchase_orders` (draft, posted, partly received), `purchase_order_lines` | `PurchaseOrder`, `PurchaseOrderLine` | Draft → Draft; posted and partly received → Posted (with what was received on each line). The order's supplier on each line. |
| open `customer_orders`, `customer_order_lines` | `CustomerOrder` | One row per item still to be supplied (ordered − filled). |
| `stock_transfers` in transit, `stock_transfer_lines` | `StockTransfer` (Shipped), `StockTransferLine` | Received in the new system. |
| `commission_rules` | `CommissionRule` | Percentage → percent of net sales, Fixed → amount per unit; one item, one department or all items, for the salesperson. |
| `time_clock_entries` | `TimeClockEntry` | In store local time; at most one open entry per person (the latest). |

### 3.2 Decisions

1. **Ids.** Retail25's keys are bigint identities, not GUIDs, so no Retail25 id could be kept as it is. Each row's id is a
   name-based UUID (version 8) of its Retail25 table and key, the same on every run. Version 8 keeps them apart from the
   version 7 ids the program makes, which is also how a re-run knows which rows it may remove.
2. **One transaction, one audit line.** All rows are written in one transaction with the automatic audit lines held back,
   and the run writes one Import line. A dry run rolls the transaction back.
3. **Stores.** One Retail25 location is one store with one stock location (Retail25 has no locations within a location).
   `--store-code` carries over one location: its items, stock, sales and documents; users of other stores are left out;
   clients, suppliers and gift cards are company-wide here and are carried over unless they belong to another store only.
4. **Station numbers** are unique across the company here (each station numbers documents from its own range), while
   Retail25's station codes repeat per location. A clash gets the first free number, and keeps it on later runs.
5. **Numbers that must be unique.** Customer, account and supplier numbers are unique across the company here and per
   location in Retail25: a repeat gets its location's code in front (`NTH-1001`). Retail25's document numbers (sales,
   invoices, layaways, quotes, purchase orders) are kept; new ones start at station × 10,000,000 + 1 (a warning says if
   Retail25's numbers ever reach that).
6. **Lists.** Departments and categories become company-wide, in capitals, at most 15 characters (Retail Plus's size).
   Two different names that are the same in 15 characters are told apart with `#2`.
7. **Text sizes.** The model keeps Retail Plus's column sizes (item name 30, stock code 16, client names 12 and 16…).
   Longer Retail25 values are cut and listed in the report; an item's full name and a cut stock code go into its notes,
   and an item whose stock code was cut or clashed is flagged for review.
8. **Passwords and PINs.** Password hashes are carried over as they are (ASP.NET Identity version 3). The sign-in accepts
   them and replaces each with the program's own format at the first sign-in that proves it; a later cutover run keeps
   the new hash. Retail25's PINs are Argon2id hashes, which .NET cannot check without another package: staff codes are
   kept, PINs are set again.
9. **Sales history.** Practice (training) sales are left out, as the program does not keep trainees' sales. A voided sale
   is one voided sale here: Retail25's reversal transaction is how it recorded the void, so the reversal is not carried
   over and its time and user become the sale's void time and voided-by. Retail25's add-on charge has no column here and
   becomes a line of its own, so each sale still adds up. Sale-level adjustments (discounts, coupons) are the subtotal
   discount, with their labels in the sale's notes.
10. **Stock.** Each item starts with one opening movement per Retail25 stock level, so on hand is the sum of the movements
    from the first day. Retail25 counts goods on open layaways in stock (as committed); here a layaway is a sale, so the
    cutover takes them out with one movement per layaway line, and the report says how many units that is. Retail25's
    stock ledger history is not carried over. Costs and quantities keep 3 decimals, money 2.
11. **Receivables.** Every invoice of a carried store comes over (paid ones too, for the statements), with its payments;
    a client's balance is what their open invoices and layaways owe, as the program keeps it. A Retail25 account balance
    that is not the sum of its invoices is reported, and the receivables check then fails on purpose.
12. **Gift cards.** Retail25 keeps only the value left on a card, so each card gets one opening transaction of that value
    (of the kind Issue, dated the day it was issued, with the original value in the note); a card with nothing left gets
    none. A certificate whose number is also a card's gets a `GC-` in front.
13. **Loyalty.** A client's points are the balance on their Retail25 pricing profile (what the till showed), as one
    Opening transaction; a ledger that disagrees is reported. The earn and redeem history stays in Retail25.
14. **Business days.** Times are the store's local time. Retail25's business day can start after midnight
    (`business_day_start`); the sales check groups sales by that day on both sides, and the start time is kept as the
    setting `R25_DAY_START`.
15. **Not carried over** (part 3.3) is listed in every report with counts, so nothing is lost silently.
16. **Roles.** Retail25's five standard roles are the standard roles of the same job here, with this program's
    permissions; where Retail25's role could do more, the report says what (Setup › Roles can add it). Another Retail25
    role becomes a custom role with its keys translated (`PermissionMap`: several Retail25 keys can be one key here, a few
    have none, and an unknown key is reported). In Retail25 anyone may approve, at another user's till, what their own role
    may do; here approving also takes `pos.approve`, so custom roles get it. A user with several roles gets the one with the
    most rights; a user without a role gets the standard role of their staff access level.

### 3.3 Not carried over

| Retail25 | Why |
|---|---|
| `audit_log_entries` | Retail25's audit log stays with Retail25 (keep its backup); the new log starts with the Import line. |
| `drawer_sessions`, `drawer_ledger_entries` | Drawer sessions are per till here; close every drawer before the freeze. |
| `stock_ledger_entries` | Stock history; each item starts with an opening movement. |
| `stock_counts`, `stock_count_lines` | Counts in progress: post or cancel them before the freeze. |
| received or cancelled transfers, drafts | History, or not shipped. |
| received, closed and cancelled purchase orders; `purchase_order_receipts` | History; the stock levels include them. |
| paid-off and cancelled layaways; converted, expired and cancelled quotes; filled customer orders | History. |
| `loyalty_ledger_entries` | Each client's balance comes over as one transaction. |
| `ar_ledger_entries` | Invoices come over with balances, late charges and payments. |
| Staff PINs | Argon2id (decision 8). |
| Tax configurations not in effect today | One current tax setup per store here; enter a future change when it is due. |
| "Percent of profit" commission rules; commission caps | No such rule here; caps are set per item. |
| "Do not reduce stock" on kit components | Kits always reduce their components here. |
| `supervisor_approvals`, `fiscal_years`, `sales_history_archives` | Moments past, a fresh year end, archived years (they stay in Retail25). |
| `pricing_rule_settings`, `number_sequences`, `permissions` | Fixed in the program, per station here, and the program's own permission list. |
| `devices`, `device_enrolments`, `printer_profiles`, `scale_profiles`, `pole_display_profiles`, `reader_profiles`, `rfid_readers`, `reader_antenna_assignments` | Hardware is set up again at each till. |
| `shoppers`, `shopper_devices`, `trolleys`, `trolley_sessions` | Self-checkout has no counterpart in the new program yet. |
| `carts` and their lines, `cached_*` | Unfinished web POS sales and caches. |
| `external_entity_maps`, `sync_logs`, `migration_batches`, `migration_staging_rows` | Retail25's accounting export and its own import. |
| Identity and OpenIddict tables, `data_protection_keys`, Hangfire tables | Sign-in tokens and background jobs of the web system. |

## 4. Tests

`tests/RetailPlus.Cutover.Tests` copies `Retail25_Source` with a copy-only backup and a restore under a new name, adds the
rows the demo seed lacks with `Fixtures/retail25-enrich.sql` (a second store with a clashing station code, branding, a
future tax change, a foreign currency, suppliers, matrix, kit, serialized, case and gift card items, clients with
accounts and clashing numbers, users with real Identity password hashes and a custom role, sales of every kind,
invoices, layaways, quotes, gift cards and certificates, loyalty, purchase orders, customer orders, transfers,
commission rules, the time clock, a stock count in progress and a closed drawer), and runs the cutover into a new SQLite
file and a new LocalDB database. It checks what lands where, that a second run changes nothing, that a run after Retail25
changed updates only that, that a dry run writes nothing, that a carried-over user signs in with the old password and is
rehashed, and that one store can be carried over on its own. Without LocalDB or `Retail25_Source` the tests skip
(`RETAILPLUS_R25_SOURCE` names another source database, `RETAILPLUS_TEST_SQL` another server).
