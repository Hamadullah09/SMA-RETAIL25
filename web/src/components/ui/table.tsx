'use client';

import type { ReactNode } from 'react';
import { cx } from '@/lib/cx';
import { EmptyState, ErrorState, TableSkeleton } from './states';

export interface Column<T> {
  key: string;
  header: ReactNode;
  /** The cell; numbers should already be formatted. */
  cell(row: T, index: number): ReactNode;
  /** Right-aligned with tabular figures (money, quantities, counts). */
  numeric?: boolean;
  /** The totals row's cell. */
  total?: ReactNode;
  className?: string;
  /** Hidden below this width, to keep a phone's table short. */
  hideBelow?: 'sm' | 'md' | 'lg' | 'xl' | '2xl';
}

const HIDE = { sm: 'max-sm:hidden', md: 'max-md:hidden', lg: 'max-lg:hidden', xl: 'max-xl:hidden', '2xl': 'max-2xl:hidden' };

/**
 * A grid like the desktop's DataGrid inside a white card: cream header, line separators, right-aligned numbers, a
 * totals row, and the loading, empty and error states. Wide tables scroll inside the card, never the page.
 */
export function DataTable<T>({
  columns,
  rows,
  rowKey,
  loading,
  error,
  onRetry,
  onRowClick,
  selectedKey,
  empty,
  showTotals,
  footer,
  className,
  caption,
  maxHeight,
}: {
  columns: Column<T>[];
  rows: T[] | undefined;
  rowKey(row: T, index: number): string;
  loading?: boolean;
  error?: unknown;
  onRetry?: () => void;
  onRowClick?: (row: T) => void;
  selectedKey?: string | null;
  empty?: ReactNode;
  showTotals?: boolean;
  footer?: ReactNode;
  className?: string;
  caption?: string;
  /** Scroll the rows inside the card past this height (CSS length). */
  maxHeight?: string;
}) {
  let body: ReactNode;
  if (error) {
    body = <ErrorState error={error} onRetry={onRetry} />;
  } else if (loading || !rows) {
    body = <TableSkeleton columns={Math.min(columns.length, 6)} />;
  } else if (rows.length === 0) {
    body = empty ?? <EmptyState title="Nothing to show" />;
  } else {
    body = (
      <div className="grid-scroll" style={maxHeight ? { maxHeight, overflowY: 'auto' } : undefined}>
        <table className="grid-table">
          {caption && <caption className="sr-only">{caption}</caption>}
          <thead>
            <tr>
              {columns.map((c) => (
                <th key={c.key} scope="col" className={cx(c.numeric && 'num', c.hideBelow && HIDE[c.hideBelow], c.className)}>
                  {c.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => {
              const key = rowKey(row, index);
              return (
                <tr
                  key={key}
                  data-clickable={onRowClick ? 'true' : undefined}
                  aria-selected={selectedKey === key || undefined}
                  tabIndex={onRowClick ? 0 : undefined}
                  onClick={onRowClick ? () => onRowClick(row) : undefined}
                  onKeyDown={
                    onRowClick
                      ? (e) => {
                          if (e.key === 'Enter' || e.key === ' ') {
                            e.preventDefault();
                            onRowClick(row);
                          }
                        }
                      : undefined
                  }
                >
                  {columns.map((c) => (
                    <td key={c.key} className={cx(c.numeric && 'num', c.hideBelow && HIDE[c.hideBelow], c.className)}>
                      {c.cell(row, index)}
                    </td>
                  ))}
                </tr>
              );
            })}
          </tbody>
          {showTotals && (
            <tfoot>
              <tr>
                {columns.map((c, i) => (
                  <td key={c.key} className={cx(c.numeric && 'num', c.hideBelow && HIDE[c.hideBelow], c.className)}>
                    {c.total ?? (i === 0 ? 'Total' : '')}
                  </td>
                ))}
              </tr>
            </tfoot>
          )}
        </table>
      </div>
    );
  }

  return (
    <div className={cx('grid-card', className)}>
      {body}
      {footer}
    </div>
  );
}
