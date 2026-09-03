'use client';

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { toast } from '@/components/ui/toaster';
import { PageHeader } from '@/components/shell/page-header';
import { describeError } from '@/lib/errors';

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
    <div className="flex h-below-header min-h-0 flex-col gap-2">
      <PageHeader
        title={title}
        actions={
          exportHref ? (
            <a className="pos-button" href={exportHref} download>
              Export CSV
            </a>
          ) : null
        }
      />

      {filters ? (
        <div className="flex flex-wrap items-end gap-3 border-b border-subtle bg-panel-sunken px-page py-3 text-body">
          {filters}
        </div>
      ) : null}

      <div className="min-h-0 flex-1 px-page py-panel">{grid}</div>

      {/* A live region: the totals under a report change when the dates do, and that change was
          announced to nobody. */}
      {summary ? (
        <div role="status" aria-live="polite" className="border-t border-subtle px-page py-2 text-body text-ink-muted">
          {summary}
        </div>
      ) : null}
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
  const [error, setError] = useState<unknown>(null);

  const run = useCallback(async () => {
    const promise = load();
    if (!promise) return;

    setLoading(true);
    setError(null);

    try {
      setData(await promise);
    } catch (caught) {
      setError(caught);

      // Cleared, not kept.
      //
      // The figures on screen belong to the inputs that produced them. When a request for new dates
      // fails and the old rows stay, the screen shows last period's money under this period's
      // dates, with a toast that has already faded — a report that is confidently wrong is worse
      // than one that is empty, because nobody checks a number that is already there.
      setData(null);

      toast({
        title: failureTitle,
        description: describeError(caught),
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

  return { data, loading, error, reload: run };
}

/** Today, or a number of days either side of it, as the yyyy-mm-dd inputs want it. */
export function isoDate(offsetDays = 0): string {
  const date = new Date();
  date.setDate(date.getDate() + offsetDays);
  return date.toISOString().slice(0, 10);
}
