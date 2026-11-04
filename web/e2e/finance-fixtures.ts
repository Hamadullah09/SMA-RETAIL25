/**
 * The finance answers (phase E1, /api/finance) for the end-to-end tests: a small company's books in the shapes of
 * src/lib/types.ts, so a contract change that breaks them fails `npm run typecheck`.
 */
import type {
  BankAccountRow,
  BillDetail,
  BillRow,
  BudgetInfo,
  ExpenseRow,
  FinanceAccount,
  FinanceHome,
  FinanceReportInfo,
  FinanceSettings,
  FiscalQueue,
  JournalDetail,
  JournalRow,
  MappingRow,
  PaymentRow,
  Reconciliation,
  RecurringExpense,
  ReportTable,
  StatementFormat,
  StoreFinance,
  SuspenseRow,
  TaxRuleSet,
  UserInfo,
  WithholdingInfo,
  YearInfo,
} from '../src/lib/types';
import { superAdmin } from './fixtures';

const pad = (n: number) => String(n).padStart(2, '0');
const day = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const daysFrom = (n: number) => {
  const d = new Date();
  d.setDate(d.getDate() + n);
  return day(d);
};
export const today = day(new Date());

/** The permissions of the Accountant role, on top of the super administrator's. */
export const financePermissions = [
  'finance.view',
  'finance.post',
  'finance.approve',
  'finance.close',
  'finance.setup',
  'finance.payables',
  'finance.bank',
  'finance.budget',
  'accounting.export',
];

export const accountant: UserInfo = { ...superAdmin, id: 'u-9', userName: 'nadia', fullName: 'Nadia Qureshi', role: 'Accountant', permissions: [...superAdmin.permissions, ...financePermissions] };

/** May look at the books but change nothing. */
export const financeViewer: UserInfo = {
  ...accountant,
  id: 'u-10',
  userName: 'imran',
  fullName: 'Imran Ali',
  role: 'Auditor',
  permissions: ['web.signin', 'finance.view'],
  storeId: 's-main',
  storeName: 'Downtown',
  canChangeShared: false,
};

// The chart --------------------------------------------------------------------------------------------------------

function account(id: string, code: string, name: string, type: FinanceAccount['type'], parentId: string | null, balance = 0, extra: Partial<FinanceAccount> = {}): FinanceAccount {
  return { id, version: 'v-1', code, name, type, parentId, isPosting: true, isActive: true, role: null, description: null, cashFlow: null, balance, ...extra };
}

export const financeAccounts: FinanceAccount[] = [
  account('h-1', '1000', 'Assets', 'Asset', null, 0, { isPosting: false }),
  account('a-cash', '1010', 'Cash in hand', 'Asset', 'h-1', 1500, { role: 'Cash' }),
  account('a-bank', '1020', 'HBL current account', 'Asset', 'h-1', 25000),
  account('a-stock', '1200', 'Inventory', 'Asset', 'h-1', 40000, { role: 'Inventory' }),
  account('h-2', '2000', 'Liabilities', 'Liability', null, 0, { isPosting: false }),
  account('l-ap', '2010', 'Accounts payable', 'Liability', 'h-2', -8260, { role: 'AccountsPayable' }),
  account('q-capital', '3010', 'Owner’s capital', 'Equity', null, -50000),
  account('h-4', '4000', 'Income', 'Income', null, 0, { isPosting: false }),
  account('i-sales', '4010', 'Sales', 'Income', 'h-4', -12000),
  account('h-6', '6000', 'Expenses', 'Expense', null, 0, { isPosting: false }),
  account('e-rent', '6100', 'Rent', 'Expense', 'h-6', 700),
  account('e-power', '6110', 'Electricity', 'Expense', 'h-6', 300),
  account('s-suspense', '9999', 'Suspense (to sort out)', 'Asset', null, 120, { role: 'Suspense' }),
];

// Bank and cash ----------------------------------------------------------------------------------------------------

export const bankAccounts: BankAccountRow[] = [
  {
    id: 'b-hbl', version: 'v-1', name: 'HBL current', kind: 'Bank', bankName: 'HBL', accountNumber: '0042-7788', iban: null, currency: 'PKR',
    ledgerAccountId: 'a-bank', ledgerCode: '1020', storeId: null, storeCode: null, openingBalance: 20000, openingDate: '2026-07-01', isActive: true, balance: 25000,
  },
  {
    id: 'b-box', version: 'v-1', name: 'Downtown cash box', kind: 'Cash', bankName: null, accountNumber: null, iban: null, currency: 'PKR',
    ledgerAccountId: 'a-cash', ledgerCode: '1010', storeId: 's-main', storeCode: 'MAIN', openingBalance: 0, openingDate: null, isActive: true, balance: 1500,
  },
];

export const financeHome: FinanceHome = {
  today,
  accounts: bankAccounts,
  cashInHand: 1500,
  payablesOpen: 8260,
  payablesDue: 5900,
  billsDue: 1,
  payablesOverdue: 5900,
  receivables: 3200,
  monthFrom: `${today.slice(0, 8)}01`,
  monthIncome: 12000,
  monthExpenses: 1000,
  monthProfit: 11000,
  suspense: 120,
  draftJournals: 1,
  fiscalEnabled: false,
  fiscalPending: 0,
  fiscalFailed: 0,
};

export const reconciliation: Reconciliation = {
  bankAccountId: 'b-hbl',
  asOf: today,
  statementBalance: 24950,
  ledgerBalance: 25000,
  uncleared: 0,
  unmatchedStatement: -50,
  unmatchedLines: 2,
  reconciledBalance: 24950,
  difference: 0,
  statementLines: [
    { id: 'sl-1', date: daysFrom(-6), description: 'CHQ 1042 Cotton Co', reference: '1042', amount: -5000, balance: 20000, status: 'Matched', journalLineId: 'jl-1', matchedBy: 'nadia' },
    { id: 'sl-2', date: daysFrom(-4), description: 'Cash deposit', reference: 'D-7', amount: 5050, balance: 25050, status: 'Unmatched', journalLineId: null, matchedBy: null },
    { id: 'sl-3', date: daysFrom(-2), description: 'Service charges', reference: null, amount: -100, balance: 24950, status: 'Unmatched', journalLineId: null, matchedBy: null },
  ],
  ledgerLines: [
    { journalLineId: 'jl-1', entryId: 'je-10', number: 10, date: daysFrom(-6), memo: 'Payment 7 to Cotton Co. Ltd', reference: '1042', amount: -5000, matched: true },
    { journalLineId: 'jl-2', entryId: 'je-11', number: 11, date: daysFrom(-5), memo: 'Deposit 3 of MAIN till cash', reference: 'D-7', amount: 5050, matched: false },
    { journalLineId: 'jl-3', entryId: 'je-12', number: 12, date: daysFrom(-25), memo: 'Deposit 1 of MAIN till cash', reference: 'D-1', amount: 5050, matched: false },
  ],
};

export const statementFormat: StatementFormat = {
  bankName: 'HBL', hasHeader: true, delimiter: ',', dateColumn: 'Date', dateFormat: 'dd/MM/yyyy', descriptionColumn: 'Narration', referenceColumn: 'Cheque',
  amountColumn: null, debitColumn: 'Withdrawal', creditColumn: 'Deposit', balanceColumn: 'Balance',
};

// Journals ---------------------------------------------------------------------------------------------------------

export const journals: JournalRow[] = [
  { id: 'je-20', number: 0, date: today, kind: 'Manual', status: 'Draft', storeCode: 'MAIN', memo: 'Accrue the electricity bill', reference: 'K-Electric', total: 300, createdBy: 'sara', postedBy: null, postedAtUtc: null },
  { id: 'je-12', number: 12, date: daysFrom(-1), kind: 'Daily', status: 'Posted', storeCode: 'MAIN', memo: 'Business day MAIN', reference: null, total: 12000, createdBy: null, postedBy: 'server', postedAtUtc: new Date().toISOString() },
];

export function journalDetail(id: string): JournalDetail {
  const row = journals.find((j) => j.id === id) ?? journals[0];
  return {
    id: row.id, version: 'v-1', number: row.number, date: row.date, kind: row.kind, status: row.status, storeId: 's-main', storeCode: 'MAIN', memo: row.memo,
    reference: row.reference, source: null, createdBy: row.createdBy, postedBy: row.postedBy, postedAtUtc: row.postedAtUtc, reversalOfId: null, reversedById: null,
    lines: [
      { lineNumber: 1, accountId: 'e-power', code: '6110', name: 'Electricity', storeId: 's-main', debit: row.total, credit: 0, memo: null, partyKind: null, partyId: null },
      { lineNumber: 2, accountId: 'l-ap', code: '2010', name: 'Accounts payable', storeId: 's-main', debit: 0, credit: row.total, memo: null, partyKind: null, partyId: null },
    ],
    attachments: [],
    needsApproval: row.status === 'Draft',
  };
}

// Payables ---------------------------------------------------------------------------------------------------------

export const bills: BillRow[] = [
  {
    id: 'bill-1', number: 101, kind: 'Bill', status: 'Posted', supplierId: 'sp-1', supplier: 'Cotton Co. Ltd', storeId: 's-main', storeCode: 'MAIN', billDate: daysFrom(-40),
    dueDate: daysFrom(-10), supplierReference: 'INV-5521', currency: 'PKR', total: 118000, paid: 0, open: 118000, daysOverdue: 10, subtotal: 100000,
  },
  {
    id: 'bill-2', number: 102, kind: 'Bill', status: 'PartPaid', supplierId: 'sp-2', supplier: 'Sole Brothers', storeId: 's-main', storeCode: 'MAIN', billDate: daysFrom(-5),
    dueDate: daysFrom(25), supplierReference: 'SB-88', currency: 'PKR', total: 5900, paid: 900, open: 5000, daysOverdue: 0, subtotal: 5000,
  },
  {
    id: 'dn-1', number: 103, kind: 'DebitNote', status: 'Posted', supplierId: 'sp-1', supplier: 'Cotton Co. Ltd', storeId: 's-main', storeCode: 'MAIN', billDate: daysFrom(-3),
    dueDate: daysFrom(-3), supplierReference: 'RET-2', currency: 'PKR', total: 11800, paid: 0, open: 11800, daysOverdue: 0, subtotal: 10000,
  },
];

export function billDetail(id: string): BillDetail {
  const row = bills.find((b) => b.id === id) ?? bills[0];
  return {
    ...row, version: 'v-1', exchangeRate: 1, notes: null, taxTotal: row.total - row.subtotal, originalBillId: null, expenseId: null, journalEntryId: 'je-12',
    createdBy: 'nadia', postedBy: 'nadia', postedAtUtc: new Date().toISOString(), voidReason: null,
    lines: [
      { lineNumber: 1, accountId: 'e-rent', accountCode: '6100', itemId: null, description: 'Goods', quantity: 1, unitPrice: row.subtotal, amount: row.subtotal, taxRate: 18,
        taxAmount: row.total - row.subtotal, purchaseOrderId: null, purchaseOrderLineId: null },
    ],
    payments: [],
    attachments: [],
  };
}

export const withholding: WithholdingInfo = { kind: 'Goods', rate: 5, threshold: 75000, section: '153(1)(a)', ruleSet: 'Pakistan 2026-27' };

export const payments: PaymentRow[] = [
  { id: 'pay-7', number: 7, date: daysFrom(-6), supplierId: 'sp-1', supplier: 'Cotton Co. Ltd', storeCode: 'MAIN', bankAccountId: 'b-hbl', bankAccount: 'HBL current', currency: 'PKR',
    amount: 5000, withholdingAmount: 0, reference: '1042', status: 'Posted', paymentRunId: null },
];

// Expenses ---------------------------------------------------------------------------------------------------------

export const expenses: ExpenseRow[] = [
  { id: 'ex-1', version: 'v-1', number: 31, date: daysFrom(-2), storeId: 's-main', storeCode: 'MAIN', payee: 'K-Electric', supplierId: null, accountId: 'e-power', accountCode: '6110',
    accountName: 'Electricity', description: 'October bill', amount: 300, taxAmount: 0, total: 300, paidBy: 'Bank', bankAccountId: 'b-hbl', reference: 'KE-1', status: 'Posted',
    supplierBillId: null, recurringExpenseId: null, journalEntryId: 'je-13', attachments: 1 },
];

export const recurringExpenses: RecurringExpense[] = [
  { id: 'rx-1', version: 'v-1', name: 'Shop rent', storeId: 's-main', payee: 'Malik Estates', supplierId: null, accountId: 'e-rent', amount: 700, taxAmount: 0, paidBy: 'Bank',
    bankAccountId: 'b-hbl', frequency: 'Monthly', nextDate: daysFrom(20), endDate: null, postAutomatically: false, isActive: true, lastMadeOn: daysFrom(-10) },
];

// Statements -------------------------------------------------------------------------------------------------------

export const financeReports: FinanceReportInfo[] = [
  ['trial-balance', 'Trial balance', false, false, false],
  ['profit-and-loss', 'Profit and loss', true, false, false],
  ['balance-sheet', 'Balance sheet', true, false, true],
  ['cash-flow', 'Cash flow', false, false, false],
  ['general-ledger', 'General ledger', false, true, false],
  ['account-ledger', 'Account ledger', false, true, false],
  ['sales-tax', 'Sales tax return', false, false, false],
  ['receivables-ageing', 'Receivables ageing', false, false, true],
  ['payables-ageing', 'Payables ageing', false, false, true],
  ['budget-vs-actual', 'Budget against actual', false, false, false],
].map(([key, title, usesComparison, usesAccount, asOfDate]) => ({ key, title, usesDates: true, usesComparison, usesAccount, asOfDate }) as FinanceReportInfo);

export const profitAndLoss: ReportTable = {
  title: 'Profit and loss',
  subtitle: null,
  columns: [
    { header: 'Code', kind: 'Text', total: false },
    { header: 'Account', kind: 'Text', total: false },
    { header: 'Amount', kind: 'Money', total: false },
  ],
  rows: [
    ['4000', 'Income', null],
    ['4010', '  Sales', 12000],
    ['', 'Total income', 12000],
    ['6000', 'Expenses', null],
    ['6100', '  Rent', 700],
    ['6110', '  Electricity', 300],
    ['', 'Total expenses', 1000],
    ['', 'Net profit', 11000],
  ],
  totals: [null, null, null],
};

// Years and budgets ------------------------------------------------------------------------------------------------

const MONTHS = ['July', 'August', 'September', 'October', 'November', 'December', 'January', 'February', 'March', 'April', 'May', 'June'];

/** The year July 2026 to June 2027, its first month locked. */
export const years: YearInfo[] = [
  {
    id: 'y-2627', name: 'FY 2026-27', startDate: '2026-07-01', endDate: '2027-06-30', status: 'Open', closedAtUtc: null, closedBy: null,
    periods: MONTHS.map((m, i) => {
      const year = i < 6 ? 2026 : 2027;
      const month = ((6 + i) % 12) + 1;
      const last = new Date(Date.UTC(year, month, 0)).getUTCDate();
      return { id: `p-${i + 1}`, number: i + 1, name: `${m} ${year}`, startDate: `${year}-${pad(month)}-01`, endDate: `${year}-${pad(month)}-${pad(last)}`, status: i === 0 ? 'Locked' : 'Open' };
    }),
  },
];

export const budget: BudgetInfo = {
  yearId: 'y-2627',
  yearName: 'FY 2026-27',
  storeId: null,
  months: years[0].periods.map((p) => p.startDate),
  amounts: [
    { accountId: 'i-sales', month: '2026-07-01', amount: -10000 },
    { accountId: 'e-rent', month: '2026-07-01', amount: 700 },
  ],
};

// Suspense and setup -----------------------------------------------------------------------------------------------

export const suspense: SuspenseRow[] = [
  { entryId: 'je-12', number: 12, date: daysFrom(-1), storeId: 's-main', storeCode: 'MAIN', memo: 'Takings JazzCash (no account UNMAPPED)', debit: 120, credit: 0 },
];

export const financeSettings: FinanceSettings = {
  version: 'v-1', baseCurrency: 'PKR', fiscalYearStartMonth: 7, country: 'PK', manualEntriesNeedApproval: true, autoPostOnSync: true, nightlyDays: 7, nightlyHour: 2,
  attachmentMaxKilobytes: 5120, fiscalEnabled: false,
};

export const taxRules: TaxRuleSet[] = [
  {
    id: 'tr-pk', version: 'v-1', country: 'PK', name: 'Pakistan 2026-27', inputTaxClaimable: true, notes: 'Check each budget year.', isActive: true,
    salesTaxRates: [{ name: 'Standard', rate: 18, isDefault: true }],
    withholdingRules: [
      { kind: 'Goods', section: '153(1)(a)', filerRate: 5, nonFilerRate: 10, threshold: 75000 },
      { kind: 'Services', section: '153(1)(b)', filerRate: 11, nonFilerRate: 22, threshold: 30000 },
    ],
  },
];

export const storeFinance = (storeId: string): StoreFinance => ({ storeId, taxRuleSetId: null, ruleSet: 'Pakistan 2026-27', depositBankAccountId: storeId === 's-main' ? 'b-hbl' : null });

export const mapping: MappingRow[] = [
  { kind: 'Sales', key: '', accountCode: '4010', accountName: 'Sales' },
  { kind: 'Tender', key: 'Cash', accountCode: '1010', accountName: 'Cash in hand' },
  { kind: 'Tender', key: 'JazzCash', accountCode: null, accountName: null },
];

export const fiscalQueue: FiscalQueue = { enabled: false, rows: [] };
