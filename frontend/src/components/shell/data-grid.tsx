'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';
import { cn } from '@/lib/utils';

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
  rowKey: (row: TRow) => string;
  onRowActivate?: (row: TRow) => void;
  /** Rows changed since the last render, briefly highlighted so a live patch is visible once. */
  recentlyChanged?: ReadonlySet<string>;
  emptyMessage?: string;
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
  rowHeight = 32,
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

  const toggleSort = (key: string) => {
    if (sortKey === key) {
      setSortDirection((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortKey(key);
      setSortDirection('asc');
    }
  };

  const saveView = () => {
    const name = window.prompt('Name this view');
    if (!name) return;

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
    <div className="pos-panel flex h-full min-h-0 flex-col">
      <div className="pos-panel-header gap-2">
        <span>{sorted.length} rows</span>

        <span className="flex items-center gap-2 normal-case">
          {views.map((view) => (
            <button key={view.name} type="button" className="underline" onClick={() => applyView(view)}>
              {view.name}
            </button>
          ))}
          <button type="button" className="underline" onClick={saveView}>
            Save view
          </button>
        </span>
      </div>

      <div
        role="grid"
        aria-rowcount={sorted.length}
        className="grid border-b border-[rgb(var(--border))] text-[10px] font-medium uppercase tracking-wide text-[rgb(var(--text-muted))]"
        style={{ gridTemplateColumns: visibleColumns.map((c) => `${c.width}px`).join(' '), minWidth: totalWidth }}
      >
        {visibleColumns.map((column) => (
          <button
            key={column.key}
            type="button"
            onClick={() => toggleSort(column.key)}
            className={cn('px-2 py-1 text-left', column.numeric && 'text-right')}
          >
            {column.header}
            {sortKey === column.key ? (sortDirection === 'asc' ? ' ↑' : ' ↓') : ''}
          </button>
        ))}
      </div>

      <div ref={scrollRef} className="min-h-0 flex-1 overflow-auto">
        {sorted.length === 0 ? (
          <p className="px-3 py-8 text-center text-sm text-[rgb(var(--text-muted))]">{emptyMessage}</p>
        ) : (
          <div style={{ height: virtualizer.getTotalSize(), position: 'relative', minWidth: totalWidth }}>
            {virtualizer.getVirtualItems().map((virtualRow) => {
              const row = sorted[virtualRow.index];
              const key = rowKey(row);

              return (
                <div
                  key={key}
                  role="row"
                  onDoubleClick={() => onRowActivate?.(row)}
                  className={cn(
                    'grid items-center border-b border-[rgb(var(--border))] text-sm hover:bg-[rgb(var(--surface))]',
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
                  {visibleColumns.map((column) => (
                    <div
                      key={column.key}
                      role="gridcell"
                      className={cn('truncate px-2', column.numeric && 'pos-amount text-right')}
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

      <details className="border-t border-[rgb(var(--border))] px-3 py-1.5 text-xs">
        <summary className="cursor-pointer text-[rgb(var(--text-muted))]">Columns</summary>
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
