import Link from 'next/link';
import { Compass } from 'lucide-react';

/** A URL that names nothing. Says so, and offers the two places worth going. */
export default function NotFound() {
  return (
    <main className="mx-auto flex min-h-[60vh] max-w-lg flex-col items-center justify-center gap-3 px-6 text-center">
      <span
        className="flex h-14 w-14 items-center justify-center rounded-full bg-accent-soft text-accent-text"
        aria-hidden
      >
        <Compass className="h-7 w-7" />
      </span>

      <h1 className="text-h1 font-semibold text-ink">That screen does not exist</h1>

      <p className="text-body leading-relaxed text-ink-muted">
        The address may have been mistyped, or the screen may have been renamed. Everything is
        reachable from the menu, or by pressing Ctrl+K and typing what you are looking for.
      </p>

      <div className="mt-2 flex flex-wrap items-center justify-center gap-2">
        <Link href="/dashboard" className="pos-button-primary">
          Go to the dashboard
        </Link>
        <Link href="/help" className="pos-button">
          Help
        </Link>
      </div>
    </main>
  );
}
