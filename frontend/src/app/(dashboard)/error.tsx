'use client';

import { useEffect } from 'react';
import Link from 'next/link';
import { RotateCw, TriangleAlert } from 'lucide-react';

/**
 * A screen that failed, inside the app's own chrome.
 *
 * Distinct from the root boundary: this one renders with the sidebar and header still around it, so
 * a failure on one screen leaves the rest of the application reachable rather than replacing the
 * whole window. Somebody whose stock report throws can still open the till.
 */
export default function DashboardError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error('Unhandled error on a dashboard screen', error);
  }, [error]);

  return (
    <div className="flex h-below-header flex-col items-center justify-center gap-3 px-page text-center">
      <span
        className="flex h-14 w-14 items-center justify-center rounded-full bg-negative-soft text-negative-text"
        aria-hidden
      >
        <TriangleAlert className="h-7 w-7" />
      </span>

      <h1 className="text-h1 font-semibold text-ink">This screen could not be drawn</h1>

      <p className="max-w-[52ch] text-body leading-relaxed text-ink-muted">
        Nothing has been lost — this is the screen failing, not the shop’s records. Try again, or use
        the menu to go somewhere else while it is looked at.
      </p>

      {error.digest ? (
        <p className="text-caption text-ink-muted">
          Reference <span className="font-mono">{error.digest}</span>
        </p>
      ) : null}

      <div className="mt-2 flex flex-wrap items-center justify-center gap-2">
        <button type="button" onClick={reset} className="pos-button-primary">
          <RotateCw className="h-5 w-5" aria-hidden />
          Try again
        </button>
        <Link href="/dashboard" className="pos-button">
          Dashboard
        </Link>
      </div>
    </div>
  );
}
