import { describe, expect, it } from 'vitest';
import {
  accountTree,
  billLine,
  billTotals,
  budgetGrid,
  dueDate,
  fileSize,
  headerChoices,
  journalTotals,
  kindLabel,
  label,
  monthLabel,
  naturalBalance,
  postingAccounts,
  roundMoney,
  spread,
  statusTone,
  suspenseReason,
  withholdingFor,
} from './finance';
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
