'use client';

import { EmptyState } from '@/components/ui/states';
import { cx } from '@/lib/cx';
import { formatCell, isRightAligned, totalsOf } from '@/lib/report';
import type { ReportCell, ReportTable } from '@/lib/types';

/**
 * A report as a grid: the column kinds decide alignment and format, the totals row is the server's (or worked out
 * here), and it prints whole. The Reports page and the finance statements both show their tables with it; a statement
 * marks its group headings and totals (`rowKind`) so they stand out.
 */
export function ReportGrid({
  table,
  symbol,
  stale,
  empty,
  rowKind,
}: {
  table: ReportTable;
  symbol: string;
  stale?: boolean;
  empty?: string;
  rowKind?: (row: readonly ReportCell[]) => 'heading' | 'total' | 'plain';
}) {
  const totals = totalsOf(table);
  if (table.rows.length === 0) {
    return (
      <div className="card">
        <EmptyState title="Nothing to report">{empty ?? 'No rows for these days and this store.'}</EmptyState>
      </div>
    );
  }
  return (
    <div className={cx('grid-card transition-opacity', stale && 'opacity-60')}>
      <div className="grid-scroll max-h-[calc(100dvh-300px)] overflow-y-auto print:max-h-none">
        <table className="grid-table" aria-label={table.title}>
          <thead>
            <tr>
              {table.columns.map((c, i) => (
                <th key={i} scope="col" className={cx(isRightAligned(c) && 'num')}>
                  {c.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {table.rows.map((row, r) => {
              const kind = rowKind?.(row) ?? 'plain';
              return (
                <tr key={r} data-row={kind === 'plain' ? undefined : kind}>
                  {table.columns.map((c, i) => (
                    <td key={i} className={cx(isRightAligned(c) && 'num', kind !== 'plain' && 'font-semibold', kind === 'total' && 'border-t border-charcoal')}>
                      {formatCell(c, row[i] ?? null, symbol)}
                    </td>
                  ))}
                </tr>
              );
            })}
          </tbody>
          {totals && (
            <tfoot>
              <tr>
                {table.columns.map((c, i) => (
                  <td key={i} className={cx(isRightAligned(c) && 'num')} data-testid={`total-${i}`}>
                    {i === 0 && totals[0] === 'Total' ? 'Total' : formatCell(c, totals[i] ?? null, symbol)}
                  </td>
                ))}
              </tr>
            </tfoot>
          )}
        </table>
      </div>
    </div>
  );
}
