'use client';

import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

/**
 * One column of a detail table.
 *
 * @property numeric Right-aligns and switches to tabular figures. Mandatory for any quantity, price
 *   or total (doc 08) — a column of proportional digits does not line up and cannot be scanned.
 */
export interface DetailColumn<TRow> {
  key: string;
  header: string;
  numeric?: boolean;
  width?: string;
  render: (row: TRow, index: number) => ReactNode;
}

/**
 * The small table that lists a record's lines inside a form panel.
 *
 * There were thirteen of these written by hand — a transfer's items, a count's variances, a
 * statement's invoices, a commission's detail. They had drifted into four different header
 * treatments, three row-border conventions, and only some of them right-aligned their money.
 *
 * This is not the browse grid. That one is virtualised, sortable and keyboard-navigable because it
 * holds fifty thousand rows; this holds twenty and is read, not driven.
 */
export function DetailTable<TRow>({
  columns,
  rows,
  rowKey,
  empty = 'Nothing here yet.',
  maxHeight,
  stackBelow,
}: {
  columns: DetailColumn<TRow>[];
  rows: TRow[];
  rowKey: (row: TRow, index: number) => string;
  empty?: ReactNode;

  /** Caps the height and scrolls inside, keeping the header visible. */
  maxHeight?: string;

  /**
   * Below this width the table becomes one card per row.
   *
   * A five-column table on a 390px phone is either a horizontal scroll nobody discovers or four
   * columns crushed to one word each. Stacking keeps every field readable and labelled — the column
   * header becomes the field's label, which is what a header was all along.
   */
  stackBelow?: 'sm' | 'md' | 'never';
}) {
  if (rows.length === 0) {
    return <p className="text-body text-ink-muted">{empty}</p>;
  }

  const stack = stackBelow ?? 'sm';
  const hiddenBelow = stack === 'never' ? '' : stack === 'md' ? 'hidden md:block' : 'hidden sm:block';
  const shownBelow = stack === 'never' ? 'hidden' : stack === 'md' ? 'md:hidden' : 'sm:hidden';

  return (
    <>
    <div
      className={cn('overflow-auto', maxHeight && 'rounded border border-subtle', hiddenBelow)}
      style={maxHeight ? { maxHeight } : undefined}
    >
      {/*
        A minimum width, so `overflow-auto` has something to overflow.
        Without it the table shrinks to whatever box it is in and the columns crush instead of
        scrolling — the container scrolls only when its content insists on being wider.
      */}
      <table className="w-full min-w-[36rem] text-body">
        <thead className="sticky top-0 bg-panel-sunken">
          <tr>
            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                data-numeric={column.numeric ? '' : undefined}
                style={column.width ? { width: column.width } : undefined}
                // Sentence case, muted, on the sunken tint. A header row is scaffolding for the
                // figures under it; uppercase gave it more presence than the data it labels.
                className={cn(
                  'border-b border-subtle px-3 py-2.5 text-label font-medium text-ink-muted',
                  column.numeric ? 'text-right' : 'text-left',
                )}
              >
                {column.header}
              </th>
            ))}
          </tr>
        </thead>

        <tbody>
          {rows.map((row, index) => (
            <tr
              key={rowKey(row, index)}
              className="border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover"
            >
              {columns.map((column) => (
                <td
                  key={column.key}
                  data-numeric={column.numeric ? '' : undefined}
                  className={cn('px-3 py-3 align-middle', column.numeric && 'text-right')}
                >
                  {column.render(row, index)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>

    {/*
      The same rows as cards, for a phone. Each field carries its column's header as a label, so
      nothing depends on remembering which column was which.
    */}
    <ul className={cn('space-y-2', shownBelow)}>
      {rows.map((row, index) => (
        <li key={rowKey(row, index)} className="rounded-lg border border-subtle bg-panel p-3">
          <dl className="grid grid-cols-[minmax(6rem,auto)_1fr] gap-x-3 gap-y-1.5">
            {columns.map((column) => (
              <div key={column.key} className="contents">
                <dt className="text-label text-ink-muted">{column.header}</dt>
                <dd className={cn('text-body text-ink', column.numeric && 'tabular-nums')}>
                  {column.render(row, index)}
                </dd>
              </div>
            ))}
          </dl>
        </li>
      ))}
    </ul>
    </>
  );
}
