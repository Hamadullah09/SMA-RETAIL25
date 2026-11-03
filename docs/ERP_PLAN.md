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

## 5. Decisions needed

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
