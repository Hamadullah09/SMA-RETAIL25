import { describe, expect, it } from 'vitest';
import {
  accountTree,
  billLine,
  billTotals,
  budgetChanges,
  budgetGrid,
  cellKey,
  comparisonLabel,
  dueDate,
  exportName,
  fileSize,
  headerChoices,
  journalTotals,
  kindLabel,
  label,
  matchCandidates,
  monthLabel,
  naturalBalance,
  periodAction,
  postingAccounts,
  reportInputs,
  roundMoney,
  spread,
  statementRow,
  statusTone,
  suspenseReason,
  withholdingFor,
} from './finance';
import { parseNumber } from './forms';
import type { FinanceAccount } from './types';

const account = (id: string, code: string, parentId: string | null, isPosting = true, balance = 0, type: FinanceAccount['type'] = 'Expense'): FinanceAccount => ({
  id,
  version: 'v',
  code,
  name: `Account ${code}`,
  type,
  parentId,
  isPosting,
  isActive: true,
  role: null,
  description: null,
  cashFlow: null,
  balance,
});

describe('journal totals', () => {
  it('balances when debits equal credits on two good lines', () => {
    expect(journalTotals([{ debit: 100, credit: null }, { debit: null, credit: 100 }])).toMatchObject({ debit: 100, credit: 100, difference: 0, balanced: true });
  });

  it('says how far off an entry is and which lines are wrong', () => {
    const t = journalTotals([{ debit: 100, credit: 0 }, { debit: 0, credit: 90.005 }, { debit: 5, credit: 5 }, { debit: null, credit: null }]);
    expect([t.debit, t.credit, t.difference]).toEqual([105, 95.01, 9.99]);
    expect(t.balanced).toBe(false);
    expect(t.badLines).toEqual([2, 3]);
  });

  it('needs something on it', () => {
    expect(journalTotals([]).balanced).toBe(false);
    expect(journalTotals([{ debit: 0, credit: 0 }, { debit: 0, credit: 0 }]).balanced).toBe(false);
  });

  it('rounds to cents like the server, halves away from zero', () => {
    expect(roundMoney(1.005)).toBe(1.01);
    expect(roundMoney(-1.005)).toBe(-1.01);
    expect(roundMoney(0.1 + 0.2)).toBe(0.3);
  });
});

describe('bills', () => {
  it('works out a line from its rate, or takes a fixed tax without one', () => {
    expect(billLine({ quantity: 4, unitPrice: 50, taxRate: 18 })).toEqual({ amount: 200, tax: 36, total: 236 });
    expect(billLine({ quantity: 1, unitPrice: 1000, taxRate: 0, taxAmount: 170 })).toEqual({ amount: 1000, tax: 170, total: 1170 });
    expect(billTotals([{ quantity: 1, unitPrice: 1000, taxRate: 18 }, { quantity: 3, unitPrice: 0.335, taxRate: 0 }])).toEqual({ subtotal: 1001.01, tax: 180, total: 1181.01 });
  });

  it('is due after the supplier’s terms, across months and years', () => {
    expect(dueDate('2026-11-15', 30)).toBe('2026-12-15');
    expect(dueDate('2026-12-20', 30)).toBe('2027-01-19');
    expect(dueDate('2026-11-15', 0)).toBe('2026-11-15');
  });

  it('withholds on the goods before sales tax, less debit notes, and nothing under the threshold', () => {
    const bills = [
      { amount: 118_000, subtotal: 100_000, total: 118_000, kind: 'Bill' as const },
      { amount: 11_800, subtotal: 10_000, total: 11_800, kind: 'DebitNote' as const },
    ];
    expect(withholdingFor(bills, 5, 75_000)).toEqual({ taxable: 90_000, withholding: 4_500, settled: 106_200, paid: 101_700 });
    expect(withholdingFor(bills, 5, 100_000).withholding).toBe(0);
    expect(withholdingFor(bills, 0).withholding).toBe(0);
    // A part payment withholds on its share.
    expect(withholdingFor([{ amount: 59_000, subtotal: 100_000, total: 118_000, kind: 'Bill' }], 10).withholding).toBe(5_000);
  });
});

describe('the chart of accounts', () => {
  const chart = [
    account('h6', '6000', null, false),
    account('rent', '6100', 'h6', true, 300),
    account('power', '6110', 'h6', true, 200),
    account('h5', '5000', null, false),
    account('cogs', '5010', 'h5', true, 1000),
    account('lost', '7000', 'gone', true, 5),
  ];

  it('reads as a tree by code, headers adding up what is under them', () => {
    const tree = accountTree(chart);
    expect(tree.map((n) => `${'-'.repeat(n.depth)}${n.account.code}`)).toEqual(['5000', '-5010', '6000', '-6100', '-6110', '7000']);
    expect(tree.find((n) => n.account.id === 'h6')!.total).toBe(500);
  });

  it('offers only active posting accounts to post to, and headers that are not below an account to move it under', () => {
    expect(postingAccounts(chart).map((a) => a.code)).toEqual(['5010', '6100', '6110', '7000']);
    expect(postingAccounts([...chart, { ...account('x', '6120', 'h6'), isActive: false }]).map((a) => a.code)).not.toContain('6120');
    const nested = [...chart, account('h61', '6150', 'h6', false)];
    expect(headerChoices(nested, 'h6').map((a) => a.code)).toEqual(['5000']);
    expect(headerChoices(nested).map((a) => a.code)).toEqual(['5000', '6000', '6150']);
  });

  it('shows credit balances of income, liabilities and equity as positive', () => {
    expect(naturalBalance('Income', -500)).toBe(500);
    expect(naturalBalance('Asset', 500)).toBe(500);
  });
});

describe('budgets', () => {
  it('adds amounts up by account and month', () => {
    const grid = budgetGrid([
      { accountId: 'rent', month: '2026-07-01', amount: 100 },
      { accountId: 'rent', month: '2026-08-01', amount: 120.5 },
      { accountId: 'power', month: '2026-07-01', amount: 30 },
    ]);
    expect(grid.get('rent')!.total).toBe(220.5);
    expect(grid.get('rent')!.months.get('2026-08-01')).toBe(120.5);
  });

  it('spreads a year over its months with the cents on the last one', () => {
    expect(spread(1000, 12)).toHaveLength(12);
    expect(spread(1000, 12).reduce((a, b) => a + b, 0)).toBeCloseTo(1000, 6);
    expect(spread(1000, 12)[11]).toBe(83.37);
    expect(spread(5, 0)).toEqual([]);
  });
});

describe('wording', () => {
  it('turns statuses and kinds into words and tones', () => {
    expect(label('PartPaid')).toBe('Part paid');
    expect(label('DebitNote')).toBe('Debit note');
    expect(label('previousYear')).toBe('Previous year');
    expect(kindLabel('SupplierPayment')).toBe('Supplier payment');
    expect(kindLabel('Daily')).toBe('Business day');
    expect(statusTone('Paid')).toBe('ok');
    expect(statusTone('PartPaid')).toBe('warn');
    expect(statusTone('Void')).toBe('danger');
    expect(statusTone('Closed')).toBe('dark');
  });

  it('names months and file sizes', () => {
    expect(monthLabel('2026-07-01')).toBe('Jul 2026');
    expect(fileSize(512)).toBe('512 B');
    expect(fileSize(2048)).toBe('2 KB');
    expect(fileSize(5 * 1024 * 1024)).toBe('5 MB');
  });

  it('reads what a suspense line was about', () => {
    expect(suspenseReason('Takings Visa (no account UNMAPPED)')).toEqual({ what: 'Takings Visa', code: null });
    expect(suspenseReason('Sales FOOD (no account 4015 Food sales)')).toEqual({ what: 'Sales FOOD', code: '4015' });
    expect(suspenseReason('Something else')).toEqual({ what: 'Something else', code: null });
  });
});

describe('statements', () => {
  it('marks the headings and totals of the profit and loss and the balance sheet', () => {
    expect(statementRow('profit-and-loss', ['4000', 'Sales', null])).toBe('heading');
    expect(statementRow('profit-and-loss', ['4010', '  Sales of goods', 2500])).toBe('plain');
    expect(statementRow('profit-and-loss', ['', 'Total income', 2500])).toBe('total');
    expect(statementRow('profit-and-loss', ['', 'Net loss', -10, 5, -15])).toBe('total');
    expect(statementRow('balance-sheet', ['', 'Total liabilities and equity', 9000])).toBe('total');
    // A line without a code that is not a total stays plain.
    expect(statementRow('balance-sheet', ['', 'Profit not yet closed', 1800])).toBe('plain');
  });

  it('marks the cash flow’s nets, the tax due, and a ledger’s opening and closing', () => {
    expect(statementRow('cash-flow', ['Operating', null, 'Net cash from operating activities', 10])).toBe('total');
    expect(statementRow('cash-flow', ['Cash', null, 'Cash and bank at the end', 10])).toBe('total');
    expect(statementRow('cash-flow', ['Operating', '1200', 'More stock', -10])).toBe('plain');
    expect(statementRow('sales-tax', ['Net', 'Tax to pay', null, 50])).toBe('total');
    expect(statementRow('account-ledger', [null, null, null, 'Opening balance', null, null, null, 0])).toBe('heading');
    expect(statementRow('general-ledger', [null, null, '6100 Rent', 'Closing balance', null, 1, 0, 1])).toBe('total');
    expect(statementRow('trial-balance', ['1010', 'Cash', 'Asset', 0, 1, 0, 1, 0])).toBe('plain');
  });

  it('asks each report for what it needs', () => {
    const base = { usesComparison: false, usesAccount: false, asOfDate: false };
    expect(reportInputs({ ...base, key: 'trial-balance' })).toEqual({ oneDay: false, comparisons: [], account: 'none' });
    expect(reportInputs({ ...base, key: 'profit-and-loss', usesComparison: true }).comparisons).toEqual(['none', 'previousPeriod', 'previousYear', 'budget']);
    // The balance sheet keeps its days: their start is the "previous period" it is compared with.
    expect(reportInputs({ ...base, key: 'balance-sheet', usesComparison: true, asOfDate: true })).toEqual({
      oneDay: false,
      comparisons: ['none', 'previousPeriod', 'previousYear'],
      account: 'none',
    });
    expect(reportInputs({ ...base, key: 'payables-ageing', asOfDate: true }).oneDay).toBe(true);
    expect(reportInputs({ ...base, key: 'account-ledger', usesAccount: true }).account).toBe('required');
    expect(reportInputs({ ...base, key: 'general-ledger', usesAccount: true }).account).toBe('optional');
    expect(comparisonLabel('previousYear')).toBe('The same days last year');
  });

  it('names the export files as the server does', () => {
    expect(exportName('tally', '2026-07-01', '2026-07-31')).toBe('ledger-20260701-20260731.xml');
    expect(exportName('iif', '2026-07-01', '2026-07-31')).toBe('ledger-20260701-20260731.iif');
    expect(exportName('qbo', '2026-07-01', '2026-07-31')).toBe('ledger-quickbooks-online-20260701-20260731.csv');
    expect(exportName('csv', '2026-07-01', '2026-07-31')).toBe('ledger-20260701-20260731.csv');
  });
});

describe('budget changes', () => {
  const loaded = [
    { accountId: 'sales', month: '2026-07-01', amount: -5000 },
    { accountId: 'rent', month: '2026-07-01', amount: 700 },
  ];
  const credit = new Set(['sales']);

  it('sends only what changed, income as a credit, and a cleared box as zero', () => {
    const cells = new Map([
      [cellKey('sales', '2026-07-01'), '5000'],
      [cellKey('sales', '2026-08-01'), '5500'],
      [cellKey('rent', '2026-07-01'), ''],
      [cellKey('rent', '2026-08-01'), '700,50'],
    ]);
    expect(budgetChanges(loaded, cells, credit, parseNumber)).toEqual({
      amounts: [
        { accountId: 'sales', month: '2026-08-01', amount: -5500 },
        { accountId: 'rent', month: '2026-07-01', amount: 0 },
        { accountId: 'rent', month: '2026-08-01', amount: 700.5 },
      ],
      bad: [],
    });
  });

  it('holds back boxes that are not numbers', () => {
    const cells = new Map([[cellKey('rent', '2026-09-01'), '7OO']]);
    expect(budgetChanges(loaded, cells, credit, parseNumber)).toEqual({ amounts: [], bad: [cellKey('rent', '2026-09-01')] });
  });
});

describe('reconciliation', () => {
  const ledger = [
    { id: 'a', amount: -500, date: '2026-07-10', matched: false },
    { id: 'b', amount: -500, date: '2026-07-03', matched: false },
    { id: 'c', amount: -500, date: '2026-07-05', matched: true },
    { id: 'd', amount: -500, date: '2026-06-01', matched: false },
    { id: 'e', amount: 500, date: '2026-07-05', matched: false },
  ];

  it('offers unmatched lines of the same amount near the day, nearest first', () => {
    expect(matchCandidates({ amount: -500, date: '2026-07-04' }, ledger).map((l) => l.id)).toEqual(['b', 'a']);
    expect(matchCandidates({ amount: -500, date: '2026-07-04' }, ledger, 2).map((l) => l.id)).toEqual(['b']);
    expect(matchCandidates({ amount: -499.99, date: '2026-07-04' }, ledger)).toEqual([]);
  });
});

describe('years', () => {
  it('locks open months, opens locked ones, and leaves a closed year alone', () => {
    expect(periodAction({ status: 'Open' }, { status: 'Open' })).toBe('lock');
    expect(periodAction({ status: 'Locked' }, { status: 'Open' })).toBe('unlock');
    expect(periodAction({ status: 'Closed' }, { status: 'Closed' })).toBeNull();
    expect(periodAction({ status: 'Open' }, { status: 'Closed' })).toBeNull();
  });
});
