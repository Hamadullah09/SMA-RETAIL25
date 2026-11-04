/**
 * The finance pages' arithmetic and wording, kept here so it is tested: journal totals, bill lines, the account tree,
 * withholding on a payment, budget grids, statuses and labels. The server checks everything again; this only lets the
 * pages say what will happen before they ask.
 */
import type {
  AccountType,
  BillKind,
  BillStatus,
  BudgetAmount,
  ExpenseStatus,
  FinanceAccount,
  FiscalInvoiceStatus,
  FiscalStatus,
  JournalKind,
  JournalStatus,
  StatementLineStatus,
} from './types';

/** Money rounded to cents, halves away from zero (as the server's Money.Round). */
export function roundMoney(value: number): number {
  const sign = value < 0 ? -1 : 1;
  return (sign * Math.round(Math.abs(value) * 100 + 1e-9)) / 100;
}

// Journal entries --------------------------------------------------------------------------------------------------

export interface JournalLineInput {
  debit: number | null;
  credit: number | null;
}

export interface JournalTotals {
  debit: number;
  credit: number;
  /** Debits less credits; zero when the entry balances. */
  difference: number;
  balanced: boolean;
  /** Lines that are both a debit and a credit, or neither. */
  badLines: number[];
}

/** What an entry's lines add up to, and whether it can be saved (two good lines at least, debits equal to credits). */
export function journalTotals(lines: readonly JournalLineInput[]): JournalTotals {
  let debit = 0;
  let credit = 0;
  const badLines: number[] = [];
  lines.forEach((line, i) => {
    const d = roundMoney(line.debit ?? 0);
    const c = roundMoney(line.credit ?? 0);
    if (d < 0 || c < 0 || (d === 0) === (c === 0)) badLines.push(i);
    debit += d;
    credit += c;
  });
  debit = roundMoney(debit);
  credit = roundMoney(credit);
  const difference = roundMoney(debit - credit);
  const good = lines.length - badLines.length;
  return { debit, credit, difference, balanced: difference === 0 && good >= 2 && badLines.length === 0 && debit > 0, badLines };
}

// Bills ------------------------------------------------------------------------------------------------------------

export interface BillLineInput {
  quantity: number;
  unitPrice: number;
  taxRate: number;
  /** A fixed tax amount, used when the rate is zero. */
  taxAmount?: number;
}

/** A bill line's amount and tax, worked out as the server does (tax from the rate, or the amount given without one). */
export function billLine(line: BillLineInput): { amount: number; tax: number; total: number } {
  const amount = roundMoney(line.quantity * line.unitPrice);
  const tax = line.taxRate === 0 ? roundMoney(line.taxAmount ?? 0) : roundMoney((amount * line.taxRate) / 100);
  return { amount, tax, total: roundMoney(amount + tax) };
}

export function billTotals(lines: readonly BillLineInput[]): { subtotal: number; tax: number; total: number } {
  let subtotal = 0;
  let tax = 0;
  for (const line of lines) {
    const l = billLine(line);
    subtotal += l.amount;
    tax += l.tax;
  }
  return { subtotal: roundMoney(subtotal), tax: roundMoney(tax), total: roundMoney(subtotal + tax) };
}

/** The due date from the bill date and the supplier's terms (`yyyy-MM-dd`). */
export function dueDate(billDate: string, termsDays: number): string {
  const [y, m, d] = billDate.split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d + termsDays));
  return date.toISOString().slice(0, 10);
}

/**
 * The withholding a payment carries, as the server works it out: on the bills' amounts before sales tax (debit notes
 * taken off), at the rate, and nothing below the threshold.
 */
export function withholdingFor(
  bills: ReadonlyArray<{ amount: number; subtotal: number; total: number; kind: BillKind }>,
  rate: number,
  threshold = 0,
  exchangeRate = 1,
): { taxable: number; withholding: number; settled: number; paid: number } {
  let taxable = 0;
  let settled = 0;
  for (const b of bills) {
    const sign = b.kind === 'DebitNote' ? -1 : 1;
    settled += sign * b.amount;
    taxable += b.total === 0 ? 0 : (sign * b.amount * b.subtotal) / b.total;
  }
  taxable = roundMoney(taxable);
  settled = roundMoney(settled);
  const withholding = rate === 0 || (threshold > 0 && taxable * exchangeRate < threshold) ? 0 : roundMoney((taxable * rate) / 100);
  return { taxable, withholding, settled, paid: roundMoney(settled - withholding) };
}

// The chart of accounts --------------------------------------------------------------------------------------------

export interface AccountNode {
  account: FinanceAccount;
  depth: number;
  /** Headers add up the accounts under them. */
  total: number;
}

/**
 * The chart as a tree in reading order: each top account by code, the accounts under it after it (one level deeper),
 * and every header with the total of what is under it. Accounts whose header is missing are shown at the top.
 */
export function accountTree(accounts: readonly FinanceAccount[]): AccountNode[] {
  const byParent = new Map<string | null, FinanceAccount[]>();
  const ids = new Set(accounts.map((a) => a.id));
  for (const a of accounts) {
    const parent = a.parentId && ids.has(a.parentId) ? a.parentId : null;
    const list = byParent.get(parent) ?? [];
    list.push(a);
    byParent.set(parent, list);
  }
  for (const list of byParent.values()) list.sort((a, b) => a.code.localeCompare(b.code, undefined, { numeric: true }));
  const out: AccountNode[] = [];
  const totals = new Map<string, number>();
  function total(a: FinanceAccount, seen: Set<string>): number {
    if (totals.has(a.id)) return totals.get(a.id)!;
    if (seen.has(a.id)) return 0;
    seen.add(a.id);
    const value = a.balance + (byParent.get(a.id) ?? []).reduce((sum, child) => sum + total(child, seen), 0);
    totals.set(a.id, roundMoney(value));
    return totals.get(a.id)!;
  }
  function walk(parent: string | null, depth: number, seen: Set<string>) {
    for (const a of byParent.get(parent) ?? []) {
      if (seen.has(a.id)) continue;
      seen.add(a.id);
      out.push({ account: a, depth, total: total(a, new Set()) });
      walk(a.id, depth + 1, seen);
    }
  }
  walk(null, 0, new Set());
  return out;
}

/** The accounts that can be posted to (active, not headers), optionally of some types, by code. */
export function postingAccounts(accounts: readonly FinanceAccount[], types?: readonly AccountType[]): FinanceAccount[] {
  return accounts
    .filter((a) => a.isPosting && a.isActive && (!types || types.includes(a.type)))
    .sort((a, b) => a.code.localeCompare(b.code, undefined, { numeric: true }));
}

/** The headers an account may go under: the ones that are not itself or below it. */
export function headerChoices(accounts: readonly FinanceAccount[], self?: string | null): FinanceAccount[] {
  const below = new Set<string>();
  if (self) {
    const queue = [self];
    while (queue.length) {
      const id = queue.shift()!;
      below.add(id);
      for (const a of accounts) if (a.parentId === id && !below.has(a.id)) queue.push(a.id);
    }
  }
  return accounts.filter((a) => !a.isPosting && !below.has(a.id)).sort((a, b) => a.code.localeCompare(b.code, undefined, { numeric: true }));
}

/** A balance as people read it: debit balances of assets and expenses positive, credit balances of the others positive. */
export function naturalBalance(type: AccountType, balance: number): number {
  return type === 'Asset' || type === 'Expense' ? balance : -balance;
}

// Budgets ----------------------------------------------------------------------------------------------------------

/** Amounts by account and month, with each account's year total. */
export function budgetGrid(amounts: readonly BudgetAmount[]): Map<string, { months: Map<string, number>; total: number }> {
  const grid = new Map<string, { months: Map<string, number>; total: number }>();
  for (const a of amounts) {
    const row = grid.get(a.accountId) ?? { months: new Map<string, number>(), total: 0 };
    row.months.set(a.month, roundMoney((row.months.get(a.month) ?? 0) + a.amount));
    row.total = roundMoney(row.total + a.amount);
    grid.set(a.accountId, row);
  }
  return grid;
}

/** A year's amount spread over its months, the cents left over on the last month. */
export function spread(total: number, months: number): number[] {
  if (months <= 0) return [];
  const each = Math.trunc((total / months) * 100) / 100;
  const out = Array.from({ length: months }, () => each);
  out[months - 1] = roundMoney(total - each * (months - 1));
  return out;
}

// Wording ----------------------------------------------------------------------------------------------------------

export type Tone = 'ok' | 'danger' | 'warn' | 'plain' | 'dark';

export function statusTone(status: BillStatus | ExpenseStatus | JournalStatus | FiscalStatus | StatementLineStatus | FiscalInvoiceStatus | string): Tone {
  switch (status) {
    case 'Posted':
    case 'Paid':
    case 'Matched':
    case 'Entered':
    case 'Sent':
    case 'Open':
      return 'ok';
    case 'PartPaid':
    case 'Draft':
    case 'Pending':
    case 'Locked':
      return 'warn';
    case 'Void':
    case 'Reversed':
    case 'Failed':
    case 'Unmatched':
      return 'danger';
    case 'Closed':
      return 'dark';
    default:
      return 'plain';
  }
}

const STATUS_LABELS: Record<string, string> = { PartPaid: 'Part paid', DebitNote: 'Debit note', OnAccount: 'On account' };

/** An enum value as a few words ("PartPaid" → "Part paid"). */
export function label(value: string | null | undefined): string {
  if (!value) return '';
  return STATUS_LABELS[value] ?? value.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase()).replace(/ ([A-Z])/g, (m) => m.toLowerCase());
}

const KIND_LABELS: Partial<Record<JournalKind, string>> = {
  Manual: 'By hand',
  Daily: 'Business day',
  SupplierBill: 'Supplier bill',
  SupplierPayment: 'Supplier payment',
  Opening: 'Opening balances',
  Closing: 'Year close',
};

export function kindLabel(kind: JournalKind): string {
  return KIND_LABELS[kind] ?? label(kind);
}

/** "Jul 2026" for the first day of a month. */
export function monthLabel(month: string, locale?: string): string {
  const [y, m] = month.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, 1)).toLocaleDateString(locale ?? 'en-US', { month: 'short', year: 'numeric', timeZone: 'UTC' });
}

/**
 * What a suspense line is about, from its memo ("Takings Visa (no account UNMAPPED)"): the amount it was ("Takings
 * Visa"), and the account code the mapping named that the chart does not have (null when nothing was mapped).
 */
export function suspenseReason(memo: string): { what: string; code: string | null } {
  const match = /^(.*?)\s*\(no account (\S*)\s*(.*)\)$/.exec(memo.trim());
  if (!match) return { what: memo.trim(), code: null };
  return { what: match[1].trim() || memo.trim(), code: match[2] && match[2] !== 'UNMAPPED' ? match[2] : null };
}

/** A file size as people read it. */
export function fileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 102.4) / 10} KB`;
  return `${Math.round(bytes / (1024 * 102.4)) / 10} MB`;
}
