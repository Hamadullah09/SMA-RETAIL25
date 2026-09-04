'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';
import { Bookmark } from 'lucide-react';
import { cn } from '@/lib/utils';
import { ErrorState, Skeleton } from '@/components/ui/states';
import { describeError, isWorthRetrying } from '@/lib/errors';
import { Button } from '@/components/ui/button';
import { PromptDialog } from '@/components/ui/confirm-dialog';

/**
 * The back-office grid (doc 08 §Back office).
 *
 * Two things drove the design. It is virtualized because a 50,000-row inventory is normal and
 * rendering that many DOM nodes makes scrolling unusable. And it patches rows in place from SignalR
 * rather than refetching — which is the direct answer to the legacy complaint that browse windows go
 * stale on a network (guide p.100–101).
 *
 * Saved views persist per user per grid, replacing the legacy "drag to reorder columns" and
 * "Flag By Search" workflows with something that survives a restart.
 */

export interface DataGridColumn<TRow> {
  key: string;
  header: string;
  /** Fixed pixel width. The grid is columnar, so widths are declared rather than measured. */
  width: number;
  /** Right-align and use tabular figures — mandatory for any quantity or price (doc 08). */
  numeric?: boolean;
  render: (row: TRow) => React.ReactNode;
  sortValue?: (row: TRow) => string | number;
}

export interface SavedView {
  name: string;
  hiddenColumns: string[];
  sortKey?: string;
  sortDirection?: 'asc' | 'desc';
}

interface DataGridProps<TRow> {
  /** Stable identity per grid; saved views are keyed on it. */
  gridId: string;
  rows: TRow[];
  columns: DataGridColumn<TRow>[];
  /** A stable identity per row. Either type: a record key is a number, a synthetic key is a string. */
  rowKey: (row: TRow) => string | number;
  onRowActivate?: (row: TRow) => void;
  /** Rows changed since the last render, briefly highlighted so a live patch is visible once. */
  recentlyChanged?: ReadonlySet<string | number>;
  emptyMessage?: string;
  /**
   * A designed empty state, for when "Nothing to show." is not enough.
   *
   * Sixteen call sites wrote `emptyMessage={loading ? 'Loading…' : 'No products.'}`, which is three
   * different states — loading, empty, and failed — flattened into one string. A failed load showed
   * the empty copy, so "we could not reach the server" and "you have no products" were the same
   * sentence, and the only difference between them was whether pressing refresh helped.
   */
  empty?: React.ReactNode;
  /** True while the rows are being fetched. Renders held rows rather than an empty table. */
  loading?: boolean;
  /** Whatever the fetch threw. Renders a retryable error instead of empty copy. */
  error?: unknown;
  onRetry?: () => void;
  /** 32px comfortable, 28px compact (doc 08 §Density over air). */
  rowHeight?: number;
}

export function DataGrid<TRow>({
  gridId,
  rows,
  columns,
  rowKey,
  onRowActivate,
  recentlyChanged,
  emptyMessage = 'Nothing to show.',
  empty,
  loading = false,
  error,
  onRetry,
  // 48, matching --grid-row-height. A 32px row cannot hold 16px type with any breathing room,
  // and the virtualiser measures every row from this one number.
  rowHeight = 48,
}: DataGridProps<TRow>) {
  const scrollRef = useRef<HTMLDivElement>(null);

  const [hidden, setHidden] = useState<Set<string>>(new Set());
  const [sortKey, setSortKey] = useState<string | undefined>();
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc');
  const [views, setViews] = useState<SavedView[]>([]);

  const storageKey = `r25.grid.${gridId}`;

  useEffect(() => {
    try {
      const stored = localStorage.getItem(storageKey);
      if (!stored) return;

      const saved = JSON.parse(stored) as { views: SavedView[]; active?: SavedView };
      setViews(saved.views ?? []);

      if (saved.active) {
        setHidden(new Set(saved.active.hiddenColumns));
        setSortKey(saved.active.sortKey);
        setSortDirection(saved.active.sortDirection ?? 'asc');
      }
    } catch {
      // A corrupt saved view should cost the layout, not the screen.
    }
  }, [storageKey]);

  const persist = useCallback(
    (nextViews: SavedView[], active: SavedView) => {
      try {
        localStorage.setItem(storageKey, JSON.stringify({ views: nextViews, active }));
      } catch {
        // Storage disabled: the grid still works, the layout just does not survive a reload.
      }
    },
    [storageKey],
  );

  const visibleColumns = useMemo(
    () => columns.filter((column) => !hidden.has(column.key)),
    [columns, hidden],
  );

  const sorted = useMemo(() => {
    if (!sortKey) return rows;

    const column = columns.find((c) => c.key === sortKey);
    if (!column?.sortValue) return rows;

    // A copy: sorting the caller's array in place would mutate state they own.
    return [...rows].sort((a, b) => {
      const left = column.sortValue!(a);
      const right = column.sortValue!(b);
      const order = left < right ? -1 : left > right ? 1 : 0;
      return sortDirection === 'asc' ? order : -order;
    });
  }, [rows, columns, sortKey, sortDirection]);

  const virtualizer = useVirtualizer({
    count: sorted.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => rowHeight,
    // A few rows either side, so a fast scroll does not show blank space.
    overscan: 12,
  });

  const totalWidth = visibleColumns.reduce((sum, column) => sum + column.width, 0);

  /**
   * Which row holds the keyboard's place in the grid.
   *
   * Null until someone tabs in, at which point the first row becomes the entry point — so a grid
   * that has never been touched is one tab stop, not one per row.
   */
  const [focusedKey, setFocusedKey] = useState<string | number | null>(null);

  useEffect(() => {
    // The focused row can disappear when a filter changes underneath it.
    if (focusedKey && !sorted.some((row) => rowKey(row) === focusedKey)) {
      setFocusedKey(null);
    }
  }, [sorted, focusedKey, rowKey]);

  const entryKey = focusedKey ?? (sorted.length > 0 ? rowKey(sorted[0]) : null);

  /**
   * Arrow keys move, Enter and Space open, Home and End jump.
   *
   * The row has to be scrolled into view by the virtualiser before it can take focus — it may not be
   * in the DOM at all — so the focus call waits a frame.
   */
  const onRowKeyDown = (event: React.KeyboardEvent, index: number, row: TRow) => {
    const move = (to: number) => {
      const clamped = Math.max(0, Math.min(sorted.length - 1, to));

      event.preventDefault();
      setFocusedKey(rowKey(sorted[clamped]));
      virtualizer.scrollToIndex(clamped, { align: 'auto' });

      requestAnimationFrame(() => {
        const next = scrollRef.current?.querySelector<HTMLElement>(`[aria-rowindex="${clamped + 1}"]`);
        next?.focus();
      });
    };

    switch (event.key) {
      case 'ArrowDown':
        move(index + 1);
        break;
      case 'ArrowUp':
        move(index - 1);
        break;
      case 'PageDown':
        move(index + 10);
        break;
      case 'PageUp':
        move(index - 10);
        break;
      case 'Home':
        move(0);
        break;
      case 'End':
        move(sorted.length - 1);
        break;
      case 'Enter':
      case ' ':
        event.preventDefault();
        onRowActivate?.(row);
        break;
      default:
        break;
    }
  };

  const toggleSort = (key: string) => {
    if (sortKey === key) {
      setSortDirection((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortKey(key);
      setSortDirection('asc');
    }
  };

  const [namingView, setNamingView] = useState(false);

  const saveView = (name: string) => {
    const view: SavedView = {
      name,
      hiddenColumns: [...hidden],
      sortKey,
      sortDirection,
    };

    const next = [...views.filter((v) => v.name !== name), view];
    setViews(next);
    persist(next, view);
  };

  const applyView = (view: SavedView) => {
    setHidden(new Set(view.hiddenColumns));
    setSortKey(view.sortKey);
    setSortDirection(view.sortDirection ?? 'asc');
    persist(views, view);
  };

  return (
    <div
      role="grid"
      aria-rowcount={sorted.length}
      className="pos-panel flex h-full min-h-0 flex-col"
    >
      <div className="pos-panel-header">
        <span className="pos-panel-title">{sorted.length} rows</span>

        {/*
          Saved views, as things to press.

          These were underlined words: a target of whatever the view happened to be named, which
          for a view called "PO" is about eighteen pixels wide. They are the shortcut a buyer uses
          twenty times a day, and they sat next to "Save view" looking exactly like it, so choosing
          a view and creating one were the same gesture with the same appearance.
        */}
        <span className="pos-panel-header-action flex flex-wrap items-center gap-2">
          {views.map((view) => (
            <Button key={view.name} variant="outline" size="sm" onClick={() => applyView(view)}>
              {view.name}
            </Button>
          ))}
          <Button variant="ghost" size="sm" onClick={() => setNamingView(true)}>
            <Bookmark className="h-5 w-5 shrink-0" aria-hidden />
            Save view
          </Button>
        </span>
      </div>

      {/* A named dialog rather than window.prompt, which cannot be styled and, in a browser running
          kiosk-mode on a till, may not appear at all. */}
      <PromptDialog
        open={namingView}
        onOpenChange={setNamingView}
        title="Save this view"
        label="Name"
        hint="Which columns are showing, and how they are sorted."
        verb="Save view"
        onSubmit={(name) => {
          saveView(name);
          setNamingView(false);
        }}
      />

      {/*
        One scroll box, with the header inside it.

        The header used to be a sibling of the scroller, and both carried minWidth: totalWidth. A
        grid wider than its panel therefore pushed the header out of the panel and put a horizontal
        scrollbar on the *document* — the whole page slid sideways to reveal a column. Inside the
        scroller and stuck to its top, the same grid scrolls within its own box and the page does
        not move, which is what "wide content scrolls in its own container" means.
      */}
      <div ref={scrollRef} className="pos-scroll-hint min-h-0 flex-1 overflow-auto" role="rowgroup">
        <div
          role="row"
          className="sticky top-0 z-sticky grid border-b border-subtle bg-panel-sunken text-label font-medium text-ink-muted"
          style={{ gridTemplateColumns: visibleColumns.map((c) => `${c.width}px`).join(' '), minWidth: totalWidth }}
        >
          {visibleColumns.map((column, index) => {
            const sorted_ = sortKey === column.key;

            return (
              <div
                key={column.key}
                role="columnheader"

                // Announced rather than left to the arrow glyph, which a screen reader reads as
                // "up arrow" or skips entirely.
                aria-sort={sorted_ ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none'}
                className={cn('min-w-0', index === 0 && 'pos-grid-pin')}
              >
                <button
                  type="button"
                  onClick={() => toggleSort(column.key)}
                  className={cn(
                    'flex w-full items-center gap-1 px-2 py-1.5 transition-colors hover:text-ink',
                    column.numeric ? 'justify-end' : 'justify-start',
                  )}
                >
                  <span className="truncate">{column.header}</span>
                  {sorted_ ? <span aria-hidden>{sortDirection === 'asc' ? '↑' : '↓'}</span> : null}
                </button>
              </div>
            );
          })}
        </div>

        {/*
          Three states, told apart. In order of precedence: a failure is the most important thing to
          say, a load in progress is the second, and empty is only empty once we know it is.
        */}
        {error ? (
          <ErrorState
            description={describeError(error)}
            onRetry={onRetry && isWorthRetrying(error) ? onRetry : undefined}
          />
        ) : loading ? (
          <div className="p-3">
            <Skeleton label="rows" rows={8} />
          </div>
        ) : sorted.length === 0 ? (
          empty ?? <p className="px-3 py-12 text-center text-body text-ink-muted">{emptyMessage}</p>
        ) : (
          <div style={{ height: virtualizer.getTotalSize(), position: 'relative', minWidth: totalWidth }}>
            {virtualizer.getVirtualItems().map((virtualRow) => {
              const row = sorted[virtualRow.index];
              const key = rowKey(row);

              return (
                <div
                  key={key}
                  role="row"

                  // Reachable and openable from the keyboard.
                  //
                  // Rows were `onDoubleClick` only — no tabIndex, no Enter handler — so the entire
                  // back office was unusable without a mouse. Roving focus (only the focused row is
                  // tabbable) keeps a ten-thousand-row grid from being ten thousand tab stops.
                  tabIndex={entryKey === key ? 0 : -1}
                  aria-rowindex={virtualRow.index + 1}
                  onFocus={() => setFocusedKey(key)}
                  onClick={() => {
                    setFocusedKey(key);
                    // One click opens it.
                    //
                    // This was double-click only, which is undiscoverable — the grid had to tell
                    // people "" in its own footer — and is not a
                    // gesture a touch screen has at all. Every back-office row was unopenable on
                    // the tablets these shops actually use.
                    onRowActivate?.(row);
                  }}
                  onKeyDown={(event) => onRowKeyDown(event, virtualRow.index, row)}
                  className={cn(
                    // `bg-panel` is load-bearing rather than cosmetic: the pinned first cell
                    // inherits its background from the row, and a transparent row would leave that
                    // cell see-through with the columns it covers sliding underneath it.
                    'grid items-center border-b border-subtle bg-panel text-body transition-colors hover:bg-panel-hover',
                    onRowActivate ? 'cursor-pointer' : 'cursor-default',
                    'focus-visible:relative focus-visible:z-10',
                    focusedKey === key && 'bg-panel-hover',
                    recentlyChanged?.has(key) && 'pos-settling',
                  )}
                  style={{
                    position: 'absolute',
                    top: 0,
                    left: 0,
                    width: '100%',
                    height: virtualRow.size,
                    transform: `translateY(${virtualRow.start}px)`,
                    gridTemplateColumns: visibleColumns.map((c) => `${c.width}px`).join(' '),
                  }}
                >
                  {visibleColumns.map((column, index) => (
                    <div
                      key={column.key}
                      role="gridcell"
                      data-numeric={column.numeric ? '' : undefined}
                      className={cn(
                        'truncate px-2',
                        column.numeric && 'text-right',

                        // The identity column travels with the reader. See .pos-grid-pin.
                        index === 0 && 'pos-grid-pin',
                      )}
                    >
                      {column.render(row)}
                    </div>
                  ))}
                </div>
              );
            })}
          </div>
        )}
      </div>

      <details className="border-t border-subtle px-3 py-1.5 text-label">
        <summary className="cursor-pointer text-ink-muted">Columns</summary>
        <div className="flex flex-wrap gap-3 pt-2">
          {columns.map((column) => (
            <label key={column.key} className="flex items-center gap-1.5">
              <input
                type="checkbox"
                checked={!hidden.has(column.key)}
                onChange={() =>
                  setHidden((current) => {
                    const next = new Set(current);
                    if (next.has(column.key)) {
                      next.delete(column.key);
                    } else {
                      next.add(column.key);
                    }
                    return next;
                  })
                }
              />
              {column.header}
            </label>
          ))}
        </div>
      </details>
    </div>
  );
}
