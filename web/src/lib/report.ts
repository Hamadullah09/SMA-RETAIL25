import { formatCount, formatDate, formatLocalDateTime, formatMoney, formatPercent, formatQuantity } from './format';
import type { ReportCell, ReportColumn, ReportTable } from './types';

/** Numbers sit on the right, text and dates on the left (the desktop's `ReportColumn.RightAligned`). */
export function isRightAligned(column: Pick<ReportColumn, 'kind'>): boolean {
  return column.kind !== 'Text' && column.kind !== 'Date';
}

function toNumber(value: ReportCell): number {
  if (typeof value === 'number') return Number.isFinite(value) ? value : 0;
  if (typeof value === 'string' && value.trim() !== '' && !Number.isNaN(Number(value))) return Number(value);
  return 0;
}

/**
 * The totals row, as the desktop's `ReportTable.TotalsRow`: the sum of every column marked `total`, a whole number for
 * counts and money rounded to cents, and "Total" in the first column when that one is not summed. Null when no column
 * has a total.
 */
export function reportTotals(columns: ReportColumn[], rows: ReportCell[][]): ReportCell[] | null {
  if (!columns.some((c) => c.total)) return null;
  const totals: ReportCell[] = columns.map(() => null);
  columns.forEach((column, c) => {
    if (!column.total) return;
    // Sum in thousandths so 0.1 + 0.2 stays 0.3.
    let sum = 0;
    for (const row of rows) sum += Math.round(toNumber(row[c] ?? null) * 1000);
    let value = sum / 1000;
    if (column.kind === 'Count') value = Math.trunc(value);
    else if (column.kind === 'Money') value = Math.round(value * 100) / 100;
    totals[c] = value;
  });
  if (columns.length > 0 && !columns[0].total) totals[0] = 'Total';
  return totals;
}

/** The totals the server sent, or the ones worked out here when it sent none. */
export function totalsOf(table: ReportTable): ReportCell[] | null {
  if (table.totals && table.totals.length === table.columns.length && table.totals.some((v) => v !== null && v !== '')) {
    return table.totals;
  }
  return reportTotals(table.columns, table.rows);
}

/** A cell as text, the way the desktop formats its column kind. */
export function formatCell(column: Pick<ReportColumn, 'kind'>, value: ReportCell, currencySymbol = '', locale?: string): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'boolean') return value ? 'Yes' : '';
  switch (column.kind) {
    case 'Money':
      return typeof value === 'number' ? formatMoney(value, currencySymbol, locale) : String(value);
    case 'Quantity':
      return typeof value === 'number' ? formatQuantity(value, locale) : String(value);
    case 'Count':
      return typeof value === 'number' ? formatCount(value, locale) : String(value);
    case 'Percent':
      return typeof value === 'number' ? formatPercent(value, locale) : String(value);
    case 'Date':
      if (typeof value === 'string') return value.length > 10 ? formatLocalDateTime(value, locale) : formatDate(value, locale);
      return String(value);
    default:
      return typeof value === 'number' ? formatQuantity(value, locale) : String(value);
  }
}
