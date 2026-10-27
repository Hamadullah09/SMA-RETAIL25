import { describe, expect, it } from 'vitest';
import { formatCell, isRightAligned, reportTotals, totalsOf } from './report';
import type { ReportColumn, ReportTable } from './types';

const columns: ReportColumn[] = [
  { header: 'Date', kind: 'Date', total: false },
  { header: 'Customers', kind: 'Count', total: true },
  { header: 'Net sales', kind: 'Money', total: true },
  { header: 'Units', kind: 'Quantity', total: true },
  { header: 'Margin', kind: 'Percent', total: false },
];

const rows = [
  ['2026-10-26', 3, 10.1, 1.5, 40],
  ['2026-10-27', 2, 20.2, 2.25, 50],
  ['2026-10-28', 0, 0.01, 0.125, null],
];

describe('report totals', () => {
  it('sums the columns marked total and writes Total in the first column', () => {
    expect(reportTotals(columns, rows)).toEqual(['Total', 5, 30.31, 3.875, null]);
  });

  it('adds money without floating-point dust', () => {
    const money: ReportColumn[] = [
      { header: 'What', kind: 'Text', total: false },
      { header: 'Amount', kind: 'Money', total: true },
    ];
    expect(reportTotals(money, [['a', 0.1], ['b', 0.2]])).toEqual(['Total', 0.3]);
  });

  it('counts are whole numbers', () => {
    const count: ReportColumn[] = [{ header: 'Sales', kind: 'Count', total: true }];
    expect(reportTotals(count, [[2], [3]])).toEqual([5]);
  });

  it('keeps the first column when it is summed itself', () => {
    const first: ReportColumn[] = [
      { header: 'Units', kind: 'Quantity', total: true },
      { header: 'Note', kind: 'Text', total: false },
    ];
    expect(reportTotals(first, [[1, 'x'], [2, 'y']])).toEqual([3, null]);
  });

  it('reads numbers sent as text and skips blanks', () => {
    expect(reportTotals(columns, [['2026-10-28', '4', '1.50', null, null]])).toEqual(['Total', 4, 1.5, 0, null]);
  });

  it('has no totals row when no column is totalled', () => {
    expect(reportTotals([{ header: 'Name', kind: 'Text', total: false }], [['a']])).toBeNull();
  });

  it("uses the server's totals when it sends them", () => {
    const table: ReportTable = { title: 'T', subtitle: null, columns, rows, totals: ['Total', 99, 1, 1, null] };
    expect(totalsOf(table)).toEqual(['Total', 99, 1, 1, null]);
    expect(totalsOf({ ...table, totals: null })).toEqual(['Total', 5, 30.31, 3.875, null]);
    expect(totalsOf({ ...table, totals: [] })).toEqual(['Total', 5, 30.31, 3.875, null]);
  });
});

describe('report cells', () => {
  it('aligns numbers right and text and dates left', () => {
    expect(columns.map(isRightAligned)).toEqual([false, true, true, true, true]);
  });

  it('formats each column kind', () => {
    expect(formatCell({ kind: 'Money' }, 1234.5, '$', 'en-US')).toBe('$1,234.50');
    expect(formatCell({ kind: 'Quantity' }, 1.25, '', 'en-US')).toBe('1.25');
    expect(formatCell({ kind: 'Count' }, 12, '', 'en-US')).toBe('12');
    expect(formatCell({ kind: 'Percent' }, 33.333, '', 'en-US')).toBe('33.3%');
    expect(formatCell({ kind: 'Date' }, '2026-10-28', '', 'en-US')).toBe('Oct 28, 2026');
    expect(formatCell({ kind: 'Text' }, 'Shoes', '', 'en-US')).toBe('Shoes');
    expect(formatCell({ kind: 'Text' }, true, '', 'en-US')).toBe('Yes');
    expect(formatCell({ kind: 'Money' }, null, '$', 'en-US')).toBe('');
  });
});
