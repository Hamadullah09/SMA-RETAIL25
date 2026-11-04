# ERP plan: one system for the whole business

Draft for discussion, 2026-11-03.

## 1. What was asked

One system that runs the whole business, covering the five areas of the brief:

1. Finance and accounting
2. Inventory and supply chain management
3. Human resources management
4. Sales, CRM and marketing
5. Manufacturing and production

SMA RETAIL already has tills (offline, SQLite), a central server (SQL Server), a web back office, one data model, roles
and permissions, an audit log, and sync between them (see [WEB_INTEGRATION_PLAN.md](WEB_INTEGRATION_PLAN.md)). This plan
says what of the five areas is already there, what is missing, and in which order to add it.

## 2. How to add it

| Option | Meaning | Trade-off |
|---|---|---|
| **A (recommended)** | Add each module to SMA RETAIL's own model: new tables, services, web pages, and desktop screens where staff on the shop floor need them | One database, one login, one set of roles, one audit log. Sales, stock and purchases post straight into the books. Each module is new work, built the way phases 1 to 6 were |
| B | Adopt an open-source ERP (Odoo, ERPNext) for accounting, HR, CRM and manufacturing, and send it the tills' sales and stock | Its modules are ready-made, but there are two systems, two logins and two item lists to keep in step, plus a second server to host and maintain. Customising it means learning its framework |
| C | Keep SMA RETAIL for retail and buy accounting and payroll software (QuickBooks, Tally and the like), exporting to it | Least to build. The business still runs on several programs, which the brief asks to avoid |

## 3. What is there today and what is missing

### 3.1 Finance and accounting

| There | Missing |
|---|---|
| Account mapping and a balanced daily journal (sales, tenders, taxes, receivables, gift cards, cost of goods, stock moves, purchases), exported as CSV | Chart of accounts and a general ledger inside the system, with postings made automatically |
| Customer invoices, payments, statements, aged receivables, credit limits | Supplier bills and payments (accounts payable), with ageing and due dates |
| Sales tax on sales; foreign-currency tenders and exchange rates | Expenses (rent, utilities, salaries) with receipts attached |
| End of Day drawer counts | Bank and cash accounts, transfers between them, bank reconciliation |
| | Fiscal years and closing; journal entries by hand, with approval |
| | Trial balance, profit and loss, balance sheet, cash flow (per store and for the company), budgets against actuals |
| | Input tax on purchases and a sales tax return. FBR POS integration (sending each sale to FBR) if the shops fall under it |

### 3.2 Inventory and supply chain

| There | Missing |
|---|---|
| Items with matrix, kits, serial numbers, RFID tags, pictures, several suppliers each | Batch or lot numbers and expiry dates (first-expiry-first-out, alerts) |
| Stock per location with a movement ledger; counts by code, RFID or CSV; transfers; case break | Warehouses with bins or shelves, and picking for orders |
| Purchase orders and receiving; reorder point and quantity; supplier list | Purchase requisitions, with approval before an order is placed |
| Bulk price changes; transfers between stores | Supplier bill matched to the order and the receipt (three-way match); landed costs (freight and duty spread over the items) |
| | Reorder suggestions from sales history (forecast), and a supplier performance report (on time, short, price changes) |

### 3.3 Human resources

| There | Missing |
|---|---|
| Users with roles and permissions, staff codes and PINs | Employee records: CNIC, address, emergency contact, joining date, designation, department, bank account, documents |
| Time clock with supervisor corrections; hours by day and week | Shifts and rosters; attendance with late arrival and absence, built from the time clock |
| Commission rules and commission reports | Leave types, balances, requests and approvals |
| | Payroll: salary structure, allowances, deductions, overtime from the time clock, commission from the rules, income tax slabs, EOBI and provincial social security, loans and advances |
| | Payslips; salary sheet; posting payroll to the ledger; final settlement when someone leaves |

### 3.4 Sales, CRM and marketing

| There | Missing |
|---|---|
| POS sales, returns, voids; invoices, quotes, layaways, customer orders | Leads and opportunities in a pipeline, with stages and expected value |
| Clients with balances and purchase history; loyalty points; gift cards | Activities: calls, visits, follow-up tasks and reminders per client |
| Sales reports by item, department, staff, hour, tender | Customer segments (big spenders, lapsed, birthdays) worked out from sales |
| Dashboard across stores | Campaigns by SMS, WhatsApp or email to a segment, with opt-in and results |
| | Promotions at the till: dates, buy X get Y, coupon codes, happy hours |
| | Sales targets per store and salesperson against actuals |

### 3.5 Manufacturing and production

| There | Missing |
|---|---|
| Kits: selling one item takes its components out of stock | Bills of materials (several levels, with quantities, yield and scrap) |
| Stock ledger and item costs (average and last) | Work centres (machines, lines, people) and the steps of making each product |
| | Production orders: plan, issue materials, report output and scrap, close |
| | Product cost: materials, labour and overhead, and work in progress in the ledger |
| | Planning: what to make and buy from orders, stock and reorder points (MRP) |
| | Quality checks on incoming goods and finished products |

## 4. Order of work

Accounting comes first, because every other module posts into it. Payroll posts salaries, production posts work in
progress, and supplier bills post payables.

| # | Phase | Main work | Size, against phases 1 to 6 |
|---|---|---|---|
| E1 | Finance core | Chart of accounts; general ledger fed by the existing journal rules (sales, payments, stock, gift cards); supplier bills and payments; expenses; bank and cash accounts with reconciliation; manual journals with approval; fiscal years and closing; trial balance, P&L, balance sheet, cash flow per store and company; the CSV export stays | Large |
| E2 | Supply chain | Batches and expiry; bins; requisitions with approval; three-way match into E1's payables; landed costs; reorder suggestions from sales; supplier performance | Medium |
| E3 | HR and payroll | Employee records; shifts and attendance from the time clock; leave; payroll with Pakistan's rules (income tax slabs, EOBI, social security) kept as settings; payslips; posting to E1 | Large |
| E4 | CRM and marketing | Leads, pipeline, activities; segments; SMS, WhatsApp and email campaigns through a gateway; till promotions and coupons; targets | Medium |
| E5 | Manufacturing | BOMs; work centres and routes; production orders on the web and on a shop-floor screen; costing into E1; MRP suggestions; quality checks | Large |

Every phase follows the rules phases 1 to 6 kept:
- one model on both engines, with migrations;
- each table's place in the sync (most ERP data stays on the server; promotions, employees' PINs and BOMs needed on the
  floor go to the tills);
- permission keys and standard roles extended, for example Accountant and HR officer;
- the audit log;
- web pages in the desktop design, and desktop screens only where shop-floor staff need them;
- tests on SQLite and SQL Server, web tests, and the smoke check.

## 5. Decisions

Taken 2026-11-03:

1. **Option A**: every module is built into SMA RETAIL's own model.
2. **Accounting: both.** The books are kept in this system (E1), and the export to an accountant's program stays and
   gains Tally and QuickBooks formats.
3. **Country rules: Pakistan first, changeable.** Taxes, payroll deductions and statutory contributions are data (rule
   sets per country, Pakistan seeded), not code, so another country is a new rule set. FBR POS integration is optional.
4. **Manufacturing: any kind.** Assembly and process work, made to order and to stock; bills of materials with yields,
   scrap and by-products.
5. **Marketing channel: WhatsApp** (the WhatsApp Business Cloud API), behind a provider interface so others can be added.
6. **All five phases now**, with the general ledger built first so the other phases can post to it.

The questions as they were put:


1. **Option A, B or C** of section 2. *Recommended: A.*
2. **Accounting:**
   - Does the business keep its books in this system from now on?
   - Or does it keep an accountant's program (which one?) and use E1 for management figures only?
3. **Country rules:** are all stores in Pakistan? This decides:
   - the income tax slabs and EOBI or social security in payroll;
   - whether FBR POS integration is needed (it applies to Tier-1 retailers).
4. **Manufacturing:** what does the business make?
   - Assembled goods, food or bakery, garments, or something else?
   - Does it make goods to order, or to stock?
5. **Marketing channels:** which SMS or WhatsApp provider does the business use or prefer? Sending costs money per
   message.
6. **Order:** E1 to E5 as in section 4, or does one area matter more now? For example, manufacturing may be needed
   first.

## 6. The general ledger foundation (2026-11-03)

Built before the phases so that they can all post to one ledger:

| Area | What is there |
|---|---|
| Chart of accounts | `LedgerAccount`: code, name, type (asset, liability, equity, income, expense), header accounts, active, and a role (`LedgerRoles`) for the accounts the program posts to by itself. A standard chart of 47 accounts is seeded on the server's first start, and every store's daily-journal mapping (Setup › Accounting) gets the matching standard account where it had none |
| Journal entries | `JournalEntry` and `JournalLine`: date, kind, source, store, memo, reference; lines per account and store, with an optional party (client, supplier, employee). Numbered on the server from its own range |
| Posting | `LedgerService.PostAsync` checks that the entry balances, that every account exists, is posted to and is active, and that the date is in an open period. Posting the same source again replaces the entry while its period is open, and posts only the difference, dated in the first open period, once it is locked |
| Hand entries | Drafts, approved and posted by a second person; reversal by a mirror entry |
| Years and periods | Financial years (Pakistan: July to June, a setting) made with twelve monthly periods when first needed; periods locked and opened again |
| Reading | Balances per account (opening, debits, credits, closing) per store or company; an account's ledger with its running balance |
| Daily posting | `PostDailyAsync` turns each store's business day of the daily journal (sales, payments on account, stock movements, gift cards) into one entry per store and day; amounts whose account is missing go to Suspense |
| Permissions | `finance.view`, `finance.post`, `finance.approve`, `finance.close`, `finance.setup`, and a standard Accountant role |

The ledger tables live on the server only and are never synced to the tills.

## 7. Status (2026-11-04): the five phases are merged

Each phase was finished on its own branch and merged into `integration-all-phases` one at a time, in this order: E1,
E3, E4, E5, E2. E2 came last because its three-way match reads E1's supplier bills; that link was made in the E2 merge.
Both `InitialCreate` migrations were made again after each merge, and the checks below were run after each one.

| Phase | Branch (last commit) | Merge | What it brings | Described in |
|---|---|---|---|---|
| E1 Finance | `erp-e1-finance` (288479b) | e928534 | Payables (supplier bills, debit notes, payments and payment runs), expenses, bank accounts and reconciliation, statements, budgets, financial years, the Tally and QuickBooks exports, FBR behind a switch (off) | `ERP_E1_FINANCE.md` |
| E3 HR and payroll | `erp-e3-hr` (0afc04e) | eb9065c | Employees, attendance, leave, and payroll with Pakistan's rules, posted to the ledger | `ERP_E3_HR.md` |
| E4 CRM and marketing | `erp-e4-crm` (f28153b) | e865c13 | Leads and the pipeline, segments, WhatsApp campaigns, promotions and coupons at the till, sales targets | `ERP_E4_CRM.md` |
| E5 Manufacturing | `erp-e5-manufacturing` (2e34ddc) | a5de594 | Bills of materials, production orders, planning, quality, costing posted to the ledger, the Production screen at the till | `ERP_E5_MANUFACTURING.md` |
| E2 Supply chain | `erp-e2-supply` (a74ae02) | ded0afc | Batches and expiry (first expiry first out at the till), bins, requisitions, the three-way match, landed costs | `ERP_E2_SUPPLY.md` |

What the E2 merge joined up with E1:
- The three-way match reads the lines of posted supplier bills and debit notes that name a purchase order line
  (`SupplierBillLineSource`). The server uses it in place of the stand-in it had while E1 was apart.
- Posting or voiding a supplier bill runs the match for that bill.
- A bill the match holds cannot be paid, on its own or in a payment run, until it is accepted or rejected under Matching.

Checks on the merged branch (f89b880):

| Check | Result |
|---|---|
| `dotnet build RetailPlus.sln` | Succeeded from scratch (`--no-incremental`): 0 warnings, 0 errors |
| `dotnet test RetailPlus.sln` | All pass: Core 112, Rfid 48, Migration 42 (3 skipped), Cutover 3 (14 skipped: they need Retail Plus files), Data 320 (1 skipped), Sync 20, Server 121 |
| `web/`: typecheck, lint, unit tests | Pass; 249 unit tests |
| `web/`: build | Pass; 75 pages |
| `web/`: e2e | 241 passed, 3 skipped |
| Desktop `--smoke` on a new database | Passed |

When the whole solution's tests run in parallel on a busy machine, a few tests that depend on timing (sync, the RFID
driver, PIN hashing) failed now and then on the earlier merges. They passed each time when run again on their own.

What is not done yet (each phase's document says more):
- **E1:** FBR and QuickBooks Online have not been tried against the real services. Pakistan's tax rates must be checked
  each budget year. Payments in a currency other than the bank account's are refused. A document and its ledger entry
  are saved one after the other, not in one database transaction.
- **E2:** bins, batch lists and the put-away, pick and count lists are on the web only. The POS has no key to choose a
  batch by hand (it always sells first expiry first).
- **E3:** there is no rule set for tax year 2027 (from 1 July 2026), so payroll for July 2026 onwards pays nobody until
  HR enters it. The PESSI limit, leave entitlements and holidays need checking by a tax adviser. The gratuity exemption
  and the provinces' own rules are not modelled. Anyone with `hr.view` sees an employee's CNIC and IBAN.
- **E4:** promotions are set up on the web only. WhatsApp is the only channel, and the real sender has not been tried
  against Meta's API. **A returned item is refunded at the price the cashier rings up, not the promotion price it was
  sold at.** This should be fixed before promotions are used in a shop.
- **E5:** production does not choose or record batches yet. Labour is absorbed at the work centres' rates and the
  difference from payroll stays in Manufacturing overhead. Scrapping a backflushed component lowers what is taken at
  the end. The order screens and the ledger round to the cent at different steps, so they can differ by a cent.

Local databases made before the merges must be made again, because the migrations were regenerated:
- the server's LocalDB `RetailPlus_E2E`: drop it and run the cutover from `Retail25_E2E` again;
- the till's `%LOCALAPPDATA%\RetailPlus\smaretail.db`: move it aside and let the app make a new one.
