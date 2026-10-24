# Retail Plus rewrite plan

Draft 1, 2026-10-24. Inputs: the Retail Plus 5.2 user guide (`Retail Plus 50.pdf`, 104 pages), the files inside `Retail52.exe`, and the copy installed at `C:\Retail Plus 52`. The data side is specified in [DATA_FORMATS.md](DATA_FORMATS.md).

> **Status (2026-10-24):** Phase 0 is done. No DBF engine is safe with several writers ([spike report](spikes/phase0-rdd-spike.md)), so the decision is a **one-time migration per store at cutover**. The new app runs on SQL Server Express, and a DBF importer brings each store's Retail Plus data across once. Design: [DATA_LAYER_PLAN.md](DATA_LAYER_PLAN.md). This plan has been updated to match.

## 1. Goal and ground rules

1. **Feature parity with Retail Plus 5.2.** Every menu command, screen, setting and report described in the user guide works the same way. Menu names, tab names, field labels and keyboard shortcuts stay the same, so staff don't need retraining.
2. **Complete one-time import.** Each store's Retail Plus data (every location, station, client, invoice, PO and setting) is imported into the new database at cutover, verified by control totals, and signed off before the first sale. The two programs never write the same data. Exchanging files with stores still on Retail Plus is a conditional extra ([DATA_LAYER_PLAN.md](DATA_LAYER_PLAN.md) section 8).
3. **Clean-room build.** The specification is the user guide, the data files and the observed behaviour of the original. No vendor code, help text, report layouts, images or sample data go into the product. The vendor's licence names Retail Plus POS Inc. as owner, so if the result will be sold or distributed, get a legal review before release.
4. **New features come after parity**, built on the same data layer. Because the old program never touches the new database, new features can change the schema freely through migrations.

## 2. What is being replaced

| Original component | What it does | Replacement |
|---|---|---|
| `Plus50.exe`, CA-Visual Objects 2.8 (native Win32) | The whole application | C# on .NET 10 (LTS), WPF desktop app |
| VO DBFCDX RDD (`_dbfcdx.rdd`, `CAVORDDB.DLL`) and the shared data folder | DBF + CDX + FPT storage, locking, multi-user access | SQL Server Express on the host PC, EF Core 10; a read-only DBF/FPT reader of our own for the one-time import ([DATA_LAYER_PLAN.md](DATA_LAYER_PLAN.md)) |
| bBrowser 3 (`bBrw30.dll`) | Grid "browse view" with flag column and reorderable columns | WPF DataGrid (or a commercial grid if virtualisation becomes a problem) |
| ReportPro 2 (`Rp2*.dll`, ~40 `.RPT` layouts) | Reports, invoices, statements, labels | QuestPDF report classes with on-screen preview. Check the QuestPDF licence: the free Community tier has a revenue cap |
| Windows print driver + control strings | Receipts, cutter, red/black ribbon, cash drawer kick | Raw ESC/POS through the Windows spooler (RAW), using the control strings from settings |
| Serial devices | Pole display (PD3000, LD9000, ESC/POS), Mettler-Toledo scale, serial cash drawer | `System.IO.Ports` device drivers |
| `.lbl` label templates (EPL2, TSPL, CPL) | Zebra, Wasp and Cognitive label printers | Same template files, filled and sent raw |
| WinINet FTP window | Store-to-store data exchange | FluentFTP (FTP/FTPS, with SFTP as an option). Uses the old DBF file formats only while some stores still run Retail Plus |
| Thunderbird / Live Mail / Outlook launch | Email PDF invoices | SMTP send, or open the default mail client with the PDF attached |
| "Open in MS-Excel" (opens the DBF read-only) | Ad hoc analysis | Export to `.xlsx` with ClosedXML and open it |
| `Plus50.chm` + `?` buttons | Help | New help pages written from scratch |
| Card processing (Global, Worldpay, NETePay, X-Charge, Mercury, ICVerify) | Integrated payments | `IPaymentProvider` interface, one provider at a time (needs processor certification) |
| Licence key / 60-session demo | Vendor licensing | Not replicated. Use your own licensing if you distribute it |

**Why WPF on .NET 10:** the product is a Windows-only, touch-capable, keyboard-first counter app that talks to printers, serial ports and a store database on the LAN. WPF is mature for exactly that, and .NET 10 is the current LTS release.

## 3. Architecture

```
RetailPlus.sln
  src/
    RetailPlus.Core           Domain model and business rules (pricing, tax, tender,
                              PO quantity formulas, bonus points, late charges,
                              random-weight barcodes). No I/O; fully unit-tested.
    RetailPlus.Data           EF Core 10 on SQL Server: schema, migrations,
                              repositories, sequences, Backup/Restore/Rebuild.
    RetailPlus.Migration      Read-only DBF/FPT reader, the one-time importer
                              (discover, validate, transform, load, verify,
                              report) and its command-line tool.
    RetailPlus.Settings       Store settings and per-station settings, stored in
                              the database; station.json for the connection.
    RetailPlus.Devices        Receipt printer, cash drawer, pole display, scale,
                              label printers.
    RetailPlus.Reports        QuestPDF documents: invoices, statements, POs,
                              reports, label sheets, envelopes.
    RetailPlus.Integrations   FTP exchange, CSV import/export, accounting exports,
                              email, web shop, payment providers.
    RetailPlus.App            WPF shell, main menu, module windows, POS screen.
  tests/
    RetailPlus.Core.Tests
    RetailPlus.Data.Tests     Integration tests on LocalDB.
    RetailPlus.Migration.Tests Reader tests (Python tools as oracle), importer
                              tests on sample data, edge cases and real backups.
    RetailPlus.Parity         Scenario runner: imports the old program's result
                              and compares it with the new app's database.
  tools/dbf/                  Python schema/dump/CDX tools (already present).
```

The data-layer design (engine choice, topology, schema, import rules, backup, cutover) is in [DATA_LAYER_PLAN.md](DATA_LAYER_PLAN.md). The source formats the importer reads are in [DATA_FORMATS.md](DATA_FORMATS.md).

## 4. Feature inventory (the parity checklist)

Page numbers refer to the printed page numbers in the user guide. Every line becomes a tracked work item with an acceptance test.

| Module | Features | Guide pages |
|---|---|---|
| Shell | Main menu and toolbar, location shown in title, go-to-POS on startup, password login, access levels 0–4, trainee mode, `?` help buttons | 3, 21, 82 |
| Browse/Form framework | Grid with flag column, edit in cell, column drag with saved order and reset, tabbed form view, previous/next/flag buttons, Find, Sort, All/Flagged view, Get Info, open in Excel, Set Flags (flag by search, prefix match, substring in notes, cumulative passes) | 20–21, 26, 28–29 |
| POS sale entry | Stock code / scan / Find, Item Detail window (qty, price, discount, tax override, price level, product info, scale buttons, keypad), Fast Scan Mode with Detail on/off, Delete last item, edit item list in place, Cash Sale, subtotal Discount, item messages on screen and on invoice, tag-along items, substitutes, serial number pick, matrix barcode scan, random-weight barcodes, THC limit, kits | 4–10, 14, 43–44, 78, 96–97 |
| POS FastKeys | 625 keys (25 per panel, category keys open sub-panels), setup window, fixed or prompted price, credit keys, taxes, Qty multiplier, keep window open, hide undefined keys | 8–9, 78 |
| POS client panel | New/Find/Edit client, history and notes, invoices/quotes/layaways lists, photo, Ship To, shipping label, envelope, clear, recurring charges, copy to clipboard, delete, send email | 5–7, 15 |
| POS options | Suspend/Recall, Void (supervisor password), New unknown item (optionally added to inventory), drawer: Float, View, Print, End of Day, Pay Out, Pay In, Pop; notes on receipt; change taxes (12 out-of-state configs); gift receipt; reprint last 500 receipts; printer output (full page, 40-col, 20-col, off); header/footer; printer select; change date for one sale | 9–12 |
| POS payment | Total Sale window, split tender, gift/debit/credit card via provider, F5/F6 tender cycling, number pad and cash denomination buttons, foreign currency, copies, Enter/Print, auto save vs Save, save without print, print preview, recalc, email PDF invoice, manual card entry, cash payment discount, minimum tender rounding (cash only), signature line/copy, card-approval auto print | 12–14, 78–79, 84 |
| POS credits | Credit or coupon, Return (optional restock, optional RMA), gift certificate, bottle return, trade-in, gift card balance | 14–15, 18–19 |
| POS history | Itemized sales log (delete current/flagged/old/all with F6–F9, void saved transaction with F2, clear flags, print, Excel), Cash Drawer history (unflag, delete, print flagged, cash flow, tax report), customer order log (fill order, delete, print, reindex), POS reports menu | 15–18 |
| POS tools | RMA (customer and supplier), payment options list, foreign exchange rates, gift card balance, delete old quotes, invoice header, get info, sales export to a master computer | 18–19 |
| Inventory | Open/New/Select/Delete inventory (location), receive stock (manual or data collector file), transfer out/in (incl. internet), adjust stock (batch price % or fixed per price level; batch on-hand from CSV with variance view, shrinkage value, update, print; manual on-hand adjustments), print (item, labels/price tags, price list, catalogue, print to file), add/find/clone/delete/delete flagged/undelete, department and category lists | 21–25, 45 |
| Inventory views/reports/tools | All/flagged, sort, get info, full-screen browse, Excel; reports: sales, inventory (location/all locations, combined/separate, hide on-hand), top sellers, analysis, stock value, understock/overstock, on order, stock received; tools: set flags, set taxes, set reorder points, recalc margins, stock updates write/read, import CSV (12 fields), export DTA (13 fields), duplicate check, year-end with archive, rebuild indexes | 26–31 |
| Inventory detail tabs | Detail (9 item types, dept, category, brand, taxes, bin, scale, web shop flag, qty, last sold, last/avg cost, price, margin, suggest price), Pricing (4 levels, 3 break points, dated sale pricing + apply to flagged, buy X get Y), Ordering (base stock, ROP, ROQ, on order, last order/shipment fields, supplier list with add/edit/remove/promote/demote), Notes & photos, Matrix (up to 3 columns + in stock, CODE column, clone, clear), Kit, Special (serial numbers, substitute, tag-along, parent/child case breaking, case qty, ship weight, THC weight, POS and invoice messages) | 32–45 |
| Clients | Print (client, list, envelope, Avery 5160/8160 labels, 8163 shipping labels, open layaways, reward points), new/delete/find/delete flagged, views, sort, get info, Excel, reset columns, set flags, duplicate check, import CSV (16 fields), export CSV (optional header), reindex; tabs: Detail, History (notes, account no., credit limit, invoices/quotes/layaways, recurring charges incl. apply to flagged), Options (points, usual discount, tax exemptions, price level, ship-to, photo) | 46–54, 95 |
| Invoices / A/R | Print current/flagged, account statements by date range, receivables, email; find (case-insensitive prefix), delete, delete flagged, header/footer; views (all, flagged, non-zero balance), sort, get info, Excel; flag/unflag all, calculate late charges, reindex; Details tab (void, refund, print); Payment tab (amount, method, date, distribute oldest-first); late charge rules (grace period, monthly rate, payments go to penalty first) | 55–60 |
| Suppliers | Print (supplier, list, labels, envelope), add/delete/delete flagged/find, views, sort, get info, Excel, reset columns, flag/unflag all, duplicate check, import CSV (15 fields), reindex | 61–64 |
| Purchase orders | New PO (items: one supplier / flagged / customer orders / all; supplier: preferred / lowest cost; quantity: blank, 1 week, 2 weeks, reorder points (two modes), monthly sales; case rounding), PO file naming and numbering, open, delete, post order, post shipment (flag received, unflag previous, distribute shipping cost into avg cost), print per supplier (address, supplier stock code, PO header), add/delete/delete flagged items, PO header text, find, views, sort, Excel, get info, set flags, recalc order costs, export CSV, Detail tab, Matrix tab | 65–73 |
| Accounting exports | Cash drawer CSV by date range, daily sales line items, client/inventory/supplier lists | 74 |
| Setup | Taxes tab (business ID, inclusive/exclusive, registration no., 2 taxes, compound, out-of-state table), POS tab (about 25 options), Printers tab, Hardware tab (drawer, pole, scale), Security tab (users, levels, password rules, supervisor void, staff tracking, trainee), Options tab (number counters, gift card max, bonus points, interest, minimum tender, UK date, auto backup) | 75–84 |
| Backup / Restore / Rebuild | Backup to a folder or USB stick, restore, rebuild (integrity check, purge deleted records, index maintenance), automatic backup (at End of Day instead of on exit). The manual's "upgrade from older versions" path becomes the importer | 85–88, 100 |
| Communications | FTP setup (server, proxy, user, password, HTTP fallback), send/view sales history, send/view stock levels with search, stock transfers, stock updates (with a chance to halt), generic file transfer window | 89–94 |
| Networking | Multi-user through the store database, per-workstation settings, browse refresh behaviour | 98–99 |
| Import from Retail Plus 5.x | One-time import of a store's data folder or Backup set, dry-run report, verification totals, sign-off report ([DATA_LAYER_PLAN.md](DATA_LAYER_PLAN.md) sections 5 and 7) | new |

## 5. Phases

Effort is a rough estimate for one experienced full-time developer. Two developers can run Phases 3–8 in parallel after Phase 2, which brings the total to about 6 months.

| Phase | Goal | Main deliverables | Exit criteria | Effort |
|---|---|---|---|---|
| 0. Foundations and spikes | Remove the biggest risks before building on them | **Done 2026-10-24.** Solution skeleton, CI file, DBF tools, data and lock spikes with X# 2.24 and 3.0.1 ([report](spikes/phase0-rdd-spike.md)). Outcome: one-time migration to SQL Server | Decision taken | 3 weeks (spent) |
| 1. Data layer, settings and importer | The store database, plus a verified import of a real store | Schema for every module, EF Core migrations, sequences, settings, Backup/Restore/Rebuild; DBF/FPT reader; importer with dry run, verification and report ([DATA_LAYER_PLAN.md](DATA_LAYER_PLAN.md) section 10). Format capture for the memo and file formats still marked "to confirm" moves here, since only the importer needs them | The sample data and at least two anonymised real backups import with matching control totals and no unexplained warnings | 6 weeks |
| 2. App shell and browse/form framework | The pattern every module reuses | Main menu, login and access levels, browse grid (flags, column order persistence, sort, find, flag by search), tabbed form view, Get Info, Excel export, help button plumbing | One table (Departments) fully usable through the framework | 3 weeks |
| 3. Inventory | Chapter 3 complete | All tabs, item types, menus, tools, import/export, year-end, stock adjustments, transfers (local) | Parity scenarios for inventory pass against the original | 5 weeks |
| 4. Clients and suppliers | Chapters 4 and 6 | Both databases incl. recurring charges, imports/exports, labels and envelopes | Parity scenarios pass | 2 weeks |
| 5. Point of sale | Chapter 2 complete | Sale entry, pricing engine, tax engine, FastKeys, tender, credits, suspend/recall, void, drawer and End of Day, customer orders, quotes, layaways, RMA, bonus points, staff tracking, trainee mode, receipts (40/20 col) and full-page invoices, reprint store, pole display, scale, random-weight barcodes | A scripted day of sales gives the same sales log, drawer totals, inventory, client and invoice changes as the original (compared after importing the original's result), and the same receipt text | 8 weeks |
| 6. Invoices / A/R | Chapter 5 | Payments, distribution, statements, late charges, void/refund, recurring billing run | Parity scenarios pass, incl. late charge edge cases | 2 weeks |
| 7. Purchase orders | Chapter 7 | All five quantity formulas, purchase orders (one record per PO instead of one file), post order/shipment, shipping cost distribution, print per supplier, export, matrix ordering | The same inputs give the same PO quantities and the same inventory changes after posting | 3 weeks |
| 8. Reports and labels | Every printed output | All reports listed in Section 4, label sheets (Avery 5160/8160/8163, bin labels, price tags), envelopes, label printer templates | Every report checked visually and numerically against the original's output | 4 weeks |
| 9. Communications and integrations | Chapter 10 plus exports | FTP exchange between stores, accounting exports, email PDFs, web shop feed, first payment provider. Only if some stores stay on Retail Plus: reading and writing the old DBF exchange files | New-app stores exchange transfers, updates and sales history (and with old-version stores, if needed) | 3 weeks (+1–2 weeks for the old exchange formats), plus about 2–4 weeks per payment provider (certification time is outside our control) |
| 10. Deployment, parity sign-off and pilot | Prove it in a real store | Installer (app, SQL Server Express, firewall, station setup), full parity run, cutover rehearsals on a copy of real data using the runbook ([DATA_LAYER_PLAN.md](DATA_LAYER_PLAN.md) section 7), then the real cutover of the pilot store | Two weeks of live use after cutover with no data discrepancies | 5 weeks |
| 11. New features | Your additions | To be planned once the feature list is known | | |

Total for parity: about 44 weeks for one developer, of which Phase 0 (3 weeks) is done, leaving roughly 10 months. Not counting payment certification or the optional old-format exchange.

## 6. Proving parity

- **Golden-master scenarios.** Each feature gets a scripted scenario: start data, steps, expected result. Run it in the original (inside a VM snapshot so it can be repeated) and in the new app, starting from the same data: the original from its data folder, the new app from that folder's import. Then compare.
- **Compare through the importer.** Import the original's resulting data folder into a scratch database, and compare it with the new app's database table by table. The comparison ignores generated IDs and time stamps, and reports every other difference. This needs the importer to be right first, which is why Phase 1 signs it off on real data.
- **Printed output.** Send receipts from both apps to a "Generic / Text Only" printer that writes to a file, and diff the text. Compare page reports by exporting both to PDF and checking totals, then by eye.
- **Cutover rehearsals.** Before the pilot store switches, the whole runbook is rehearsed on a copy of its data, including the verification report and the spot checks.
- **Regression.** Every scenario becomes an automated test against the new app, so new features can't silently break parity.

## 7. Risks

| Risk | Impact | How it's handled |
|---|---|---|
| Behaviour the manual doesn't describe (exact formulas, rounding, tax-inclusive maths, the order in which pricing rules apply) | Wrong totals | Capture it by black-box testing the original (Section 6). Every rule gets a test with values taken from the original |
| Memo and file formats not yet seen (invoice lines, purchase history, layaways, gift certificates, …) | Incomplete import | Get an anonymised backup from a store that uses these features early in Phase 1. The importer keeps every raw source record, so nothing is silently lost even before a format is understood |
| Data quality in real stores (duplicate codes, broken references, odd values) | Blocked or wrong import | Dry runs a week before cutover, with a report the store can act on in Retail Plus. Control totals must match before go-live |
| Rollback after cutover | Sales made in the new app would have to be re-keyed into the old one | Go/no-go before the first real sale; the old data stays untouched until then |
| SQL Server Express on shop PCs (install, firewall, backups without SQL Agent) | Support calls | Silent, scripted install with fixed settings; the app runs backups itself; installer checks run as part of the pilot |
| Payment processor integration and certification | Delays | Behind an interface. The first release can ship with "manual entry on the standalone terminal", as the original allows |
| Passwords in `USERS.DBF` may be stored in a weak form | Security | Never kept: hashed on import, or users set new ones at first login |
| Legal: replicating a commercial product | Takedown or claims if distributed | Clean-room rules (Section 1). No vendor assets. Legal review before any distribution |
| Scope creep from new features during parity work | Never reaching parity | New features wait for Phase 11, or are designed now and built later |

## 8. Decisions needed from you

1. **The new features.** The request ended before the list. They decide whether anything in the data model or architecture should change now.
2. ~~Side-by-side vs one-time migration.~~ **Decided 2026-10-24: one-time migration per store at cutover.** The follow-up questions it raises (all stores at once?, unique stock codes, passwords, history depth) are in [DATA_LAYER_PLAN.md](DATA_LAYER_PLAN.md) section 9.
3. **Who will use it:** your own store(s), or a product sold to others. That drives licensing, installer, branding and the legal review.
4. **Card processor and web shop** currently in use, if any.
5. **Hardware** at the counter: receipt printer model, cash drawer, scanner, scale, pole display, label printer.
6. **A real data backup** (anonymised if needed), ideally from a store that uses invoices, layaways, gift certificates and transfers. The importer and the parity tests are built against it; the installed copy only has sample data.

## 9. Next steps

1. ~~Restructure the skeleton for the new data layer.~~ Done 2026-10-24.
2. ~~Write the first schema migration (inventory, clients, suppliers, settings) and run it on LocalDB.~~ Done; the remaining modules' tables come next ([DATA_LAYER_PLAN.md](DATA_LAYER_PLAN.md) section 10).
3. ~~Build the DBF/FPT reader against the Python oracle.~~ Done. Next: the importer for the tables already understood, so the sample data imports end to end.
4. Get a real backup (decision 6) to capture the remaining formats.
5. Turn Section 4 into tracked work items with acceptance tests.
