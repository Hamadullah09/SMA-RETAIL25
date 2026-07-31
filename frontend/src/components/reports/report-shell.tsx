'use client';

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { toast } from '@/components/ui/toaster';
import { PosApiError } from '@/lib/pos-api';

/**
 * The parts every analytical report screen repeats: a filter bar, a CSV link, a grid, and a
 * summary line. Extracted rather than copied into nine pages so a change to how reports look or
 * how they report failure happens once.
 */
export function ReportShell({
  title,
  exportHref,
  filters,
  grid,
  summary,
}: {
  title: string;
  exportHref?: string;
  filters?: ReactNode;
  grid: ReactNode;
  summary?: ReactNode;
}) {
  return (
    <div className="flex h-[calc(100vh-8rem)] min-h-0 flex-col gap-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-h3 font-semibold">{title}</h1>
        {exportHref ? (
          <a className="pos-button" href={exportHref} download>
            Export CSV
          </a>
        ) : null}
      </div>

      {filters ? <div className="flex flex-wrap items-center gap-2 text-body">{filters}</div> : null}

      <div className="min-h-0 flex-1">{grid}</div>

      {summary ? <div className="text-label text-ink-muted">{summary}</div> : null}
    </div>
  );
}

/** The filter-bar control styling, in one place until the design-system pass replaces it. */
export const filterInputClass =
  'pos-input';

export function DateRangeFilter({
  from,
  to,
  onFrom,
  onTo,
}: {
  from: string;
  to: string;
  onFrom: (value: string) => void;
  onTo: (value: string) => void;
}) {
  return (
    <>
      <label className="flex items-center gap-1.5">
        From
        <input type="date" className={filterInputClass} value={from} onChange={(e) => onFrom(e.target.value)} />
      </label>
      <label className="flex items-center gap-1.5">
        To
        <input type="date" className={filterInputClass} value={to} onChange={(e) => onTo(e.target.value)} />
      </label>
    </>
  );
}

/**
 * Loads a report and surfaces failure as a toast rather than an empty grid — an empty grid and a
 * failed request look identical to a user, and the difference matters when the number is wrong.
 */
export function useReport<T>(load: () => Promise<T> | undefined, failureTitle: string) {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(false);

  const run = useCallback(async () => {
    const promise = load();
    if (!promise) return;

    setLoading(true);

    try {
      setData(await promise);
    } catch (error) {
      toast({
        title: failureTitle,
        description: error instanceof PosApiError ? error.problem.detail : 'Something went wrong.',
        variant: 'destructive',
      });
    } finally {
      setLoading(false);
    }
    // `load` is expected to be a useCallback in the calling page.
  }, [load, failureTitle]);

  useEffect(() => {
    void run();
  }, [run]);

  return { data, loading, reload: run };
}

/** Today, or a number of days either side of it, as the yyyy-mm-dd inputs want it. */
export function isoDate(offsetDays = 0): string {
  const date = new Date();
  date.setDate(date.getDate() + offsetDays);
  return date.toISOString().slice(0, 10);
}
