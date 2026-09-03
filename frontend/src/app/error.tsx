'use client';

import { useEffect } from 'react';
import { RotateCw } from 'lucide-react';

/**
 * The last thing between a thrown render error and a blank white page.
 *
 * There was no error boundary anywhere in this application. A component that threw during render —
 * a null a type said could not be null, a malformed date, a response shaped differently from its
 * type — dropped straight through to Next's own "Application error: a client-side exception has
 * occurred", unstyled, with no navigation and nothing to press. On a till, that is indistinguishable
 * from the machine having broken.
 *
 * Deliberately plain and self-contained: this renders when something in the tree below has already
 * failed, so it cannot rely on the design system's providers being healthy, and every class it uses
 * resolves without one.
 */
export default function AppError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    // The console is the only place this can go from here. The digest is what ties it to the entry
    // in the server log, which is the difference between "something broke" and a fixable report.
    console.error('Unhandled error', error);
  }, [error]);

  return (
    <main className="mx-auto flex min-h-[60vh] max-w-lg flex-col items-center justify-center gap-3 px-6 text-center">
      <h1 className="text-h1 font-semibold text-ink">Something went wrong on this screen</h1>

      <p className="text-body leading-relaxed text-ink-muted">
        Nothing you were doing has been lost from the server — this is the screen failing to draw,
        not the shop failing to record. Try again, and if it keeps happening tell whoever looks after
        this system.
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

        <a href="/dashboard" className="pos-button">
          Go to the dashboard
        </a>
      </div>
    </main>
  );
}
