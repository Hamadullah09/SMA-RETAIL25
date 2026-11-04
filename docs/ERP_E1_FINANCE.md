# E1 Finance: the books in SMA RETAIL

Phase E1 of the [ERP plan](ERP_PLAN.md) (sections 4 to 6), finished 2026-11-04 on the branch `erp-e1-finance`. The
business keeps its books here (decision 2) and can still send the ledger to an accountant's program. Everything is
built on the general ledger of section 6: every document posts through `LedgerService.PostAsync`, which refuses an
entry that does not balance, an account that is missing, a header or inactive, and a date in a locked or closed month.

## 1. What is there

| Area | Server (data layer, API) | Web page |
|---|---|---|
| Finance home | Cash in hand, bank balances, payables due this week and overdue, receivables, this month's income, expenses and profit, suspense, entries waiting for approval, the FBR queue | `/finance/` |
| Chart of accounts | 47 standard accounts seeded (section 6) plus the roles E1 added (withholding tax payable, bank charges); headers, roles, cash flow section, active or not; deleted only when never posted to | `/finance/accounts/` |
| Journals | Every entry the program posted, and entries made by hand: drafts, approval by a second person (a setting), reversal by a mirror entry, receipts attached | `/finance/journals/`, `/finance/journals/edit/?id=` |
| Supplier bills and debit notes | Lines for an expense account, a stock item or goods received on a purchase order, sales tax per line (input tax claimed when the store's rule set allows); due date from the supplier's terms; draft, posted, part paid, paid, void; ageing and bills due | `/finance/bills/`, `/finance/bills/edit/?id=` (the supplier's tax details from the bill) |
| Supplier payments | One supplier and store per payment, from a bank or cash account; debit notes set against bills; withholding tax at the filer or non-filer rate of the store's rule set, above its threshold; payment runs pay many suppliers at once; void | `/finance/payments/` |
| Expenses | Paid in cash, from a bank, or owed (a supplier bill is made and posted); receipts (pictures or PDF, size limit in the settings); recurring expenses made on their schedule, as drafts or posted | `/finance/expenses/` |
| Bank and cash | Accounts (bank, cash box, wallet) with their own ledger account and opening balance; transfers (two currencies with an exchange difference); End of Day till cash deposited once per drawer | `/finance/bank/` |
| Reconciliation | Statement CSV read with a layout kept per bank (one amount column or money in and out, any separator including tab, date format); lines matched by themselves (same amount, date within five days, reference) or by hand, left out, or entered (bank charges, other income or a chosen account) | `/finance/bank/reconcile/?id=` |
| Years and periods | Years of twelve months from the start month (July in Pakistan); months locked and opened; a year closed (income and expenses to retained earnings per store, the next year made) and opened again (closing entry reversed); opening balances for a company starting part way through a year | `/finance/years/` |
| Budgets | Per account, month and store, or the company's own; typed, spread from a year's amount, copied from last year's actuals or budget raised by a percentage | `/finance/budgets/` |
| Statements | Trial balance, profit and loss, balance sheet, cash flow (indirect), general ledger, account ledger, sales tax return, receivables and payables ageing, budget against actual; per store or the company; compared with the period before, last year or the budget; CSV and print | `/finance/reports/`, `/finance/reports/view/?key=` |
| Exports | Tally XML, QuickBooks Desktop IIF, QuickBooks Online CSV, CSV of every posted line, and the daily journal CSV as before | `/finance/reports/` |
| Suspense | Amounts of the tills' days whose account the store's mapping does not name (or names but the chart lacks); post the days again after fixing the mapping | `/finance/suspense/` |
| Finance setup | The books' settings, tax rule sets (Pakistan seeded), each store's rule set and deposit account, the daily journal's account mapping per store, the FBR queue | `/finance/settings/?tab=` |
| FBR | POS integration behind a switch, off by default (section 6 below) | FBR tab of Finance setup |

Every finance table lives on the server only: they are `ILocalOnly`, so the sync never sends them to the tills, and
the tills never send them up. The one thing that goes down is a sale's FBR number, on the sale itself.

## 2. How each document posts

All postings are in the base currency (`LedgerSettings.BaseCurrency`, PKR), one entry per document with a `Source`
(`bill:{id}`, `supplier-payment:{id}`, `expense:{id}` and so on). Posting the same source again while its month is open
replaces the entry; once the month is locked only the difference is posted, as an adjustment in the first open month.
A void or a reversal is a mirror entry on the date given; the original is marked Reversed.

| Document | Debit | Credit |
|---|---|---|
| Tills' business day (`PostDailyAsync`) | Tenders, cost of goods, receivables, stock moves, as the store's mapping says | Sales by department, taxes, gift card liability, inventory; what has no account goes to Suspense |
| Supplier bill | Each line's account (a stock item: Inventory; goods received on an order: Purchases clearing), input tax when claimable (else on the line) | Accounts payable, with the supplier as party |
| Debit note | Accounts payable | The same accounts as a bill |
| Supplier payment | Accounts payable (at the bills' own rates) | The bank or cash account; withholding tax payable; an exchange difference when rates differ |
| Expense, cash or bank | The expense account; input tax when claimable | Cash in hand (the store's) or the bank account |
| Expense on account | Through the supplier bill it makes | |
| Bank account's opening balance | The account's ledger account | Retained earnings |
| Transfer | The receiving account | The sending account (an exchange difference for two currencies) |
| Deposit of till cash | The bank account | Cash in hand |
| Statement line entered | Bank charges (money out) or the bank (money in) | The bank, or other income |
| Year close | Each income account (per store) | Each expense account; the result to retained earnings per store |
| Opening balances | What each account held; a difference to retained earnings only when asked | |

Hand entries need a second person to approve them while *Entries made by hand need a second person's approval* is on
(the default). The one who made a draft cannot approve it; until it is approved it counts in no balance.

## 3. Permissions, roles and the audit log

New keys in `Core/Rules/Permissions.cs` (group Finance): `finance.view`, `finance.post`, `finance.approve`,
`finance.close`, `finance.setup` (section 6) and, added by E1, `finance.payables`, `finance.bank`, `finance.budget`.
The standard Accountant role has all of them, `accounting.export`, `audit.view` and `stores.all`; the Manager role gets
`finance.view`; Administrator and Super administrator have everything as before.

Store scope follows the rest of the API: a store administrator works for their own store; what the company shares (the
chart, settings, tax rules, years and periods, company bank accounts and their statements, supplier tax details) is
changed by a user with `stores.all`, or by anyone allowed while there is one store. The audit log records bills posted
and voided, payments and voids, expenses posted, transfers, deposits, statements imported and matched, entries posted
and reversed, months locked and opened, years closed and opened again, days posted, FBR sends, and every change to the
chart, settings, rule sets, bank accounts, statement layouts and recurring expenses.

## 4. The jobs on the server

`FinanceJobs` runs every `Finance:JobIntervalSeconds` (60; 0 switches it off):

- the business days touched by packages applied since the last run are posted (when *Post a till's days as soon as its
  package arrives* is on), after giving a store added since the start the standard accounts of the mapping;
- each night at the hour set, the last days of every store are posted again (a late till);
- once a day, the recurring expenses that are due are made;
- when FBR is on, sales are queued, sent and their numbers stamped.

## 5. The exports

`GET /api/finance/export?format=&from=&to=&storeId=` (`accounting.export`):

| `format` | For | What |
|---|---|---|
| `tally` | Tally (Gateway › Import Data › Vouchers) | XML envelope of Journal vouchers; ledgers named "code name"; a debit is a negative amount marked deemed positive |
| `iif` | QuickBooks Desktop (File › Utilities › Import › IIF) | The accounts used (`!ACCNT`) then one GENERAL JOURNAL transaction per entry, dates MM/DD/YYYY |
| `qbo` | QuickBooks Online (Settings › Import data › Journal entries) | CSV: Journal No, Journal Date (dd/MM/yyyy: choose that format in the import), Account (the name), Debits, Credits, Description, Location (store code) |
| `csv` | A spreadsheet | Date, Number, Kind, Store, Account, Account name, Debit, Credit, Memo, Reference |

For one store, each entry carries only that store's lines; an entry shared with other stores (rent split by hand) gets
one more line on a ledger named **Between stores** (code `STORES`) for the other stores' part, so every voucher still
balances. The company's export has no such line. Accounts must exist in the other program under the same names (Tally
can make missing ledgers on import; QuickBooks asks). The QuickBooks Online layout follows Intuit's published import
and has not been tried against a live company: do one test import first.

## 6. FBR

Off by default. Server settings: `Fbr:Enabled`, `Fbr:PosId`, `Fbr:Token`, `Fbr:Url` (FBR's sandbox or live PostData
address), `Fbr:Stores` (store codes, empty for all), `Fbr:TimeoutSeconds`; `Fbr:UseFake` records what would be sent and
sends nothing. The client is written to FBR's published POS integration JSON and is **unverified**: test it against
FBR's sandbox before switching it on. Failed sends can be queued again from Finance setup › FBR.

## 7. The endpoints

All under `/api/finance`, listed with their permissions in the last section of [API.md](API.md).

## 8. Tests

- Data layer (`tests/RetailPlus.Data.Tests/FinanceTests.cs`, SQLite and LocalDB): the seeding, bills, payments and
  withholding, payment runs (checked whole before any payment is posted), foreign currency, expenses and receipts,
  bank accounts and transfers, statements and matching, year close and opening balances, budgets, statements agreeing
  with each other, the exports (a store's share balanced, QuickBooks Online), the sales tax return, FBR, posting days.
- Server (`tests/RetailPlus.Server.Tests/FinanceApiTests.cs`): every group of endpoints, refused without its
  permission, kept to the user's store, 409 on an old version; the jobs; statement lines matched, unmatched and left
  out; tab-separated statements; voids; recurring expenses made per store; budgets copied.
- Web: unit tests of `src/lib/finance.ts` (journal totals, bills, withholding, the chart tree, budgets, statement rows,
  report inputs, export names, reconciliation candidates, period actions); Playwright specs in `e2e/finance.spec.ts`
  for every finance page, answered from `e2e/finance-fixtures.ts` through `e2e/mock-finance.ts`.

## 9. What is left

- FBR and QuickBooks Online are written to published formats and not yet tried against the real services.
- Pakistan's tax rules (sales tax 18%, withholding under section 153) are seeded as data and must be checked each
  budget year; another country is a new rule set.
- Payroll (E3), production costing (E5) and the three-way match (E2) post through the same `LedgerService`; their
  postings are those phases' work. E2's `BillMatch.BillId` refers to E1's `SupplierBill.Id`.
- Payments in a currency other than the bank account's, and statement entries for a foreign-currency account, are
  refused; they are made by hand.
- A document's ledger entry and its own record are saved one after the other (as everywhere in the services), not in
  one database transaction.
