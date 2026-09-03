import Link from 'next/link';
import { Compass } from 'lucide-react';

/** Inside the chrome, so the menu is still there to go somewhere real. */
export default function DashboardNotFound() {
  return (
    <div className="flex h-below-header flex-col items-center justify-center gap-3 px-page text-center">
      <span
        className="flex h-14 w-14 items-center justify-center rounded-full bg-accent-soft text-accent-text"
        aria-hidden
      >
        <Compass className="h-7 w-7" />
      </span>

      <h1 className="text-h1 font-semibold text-ink">That screen does not exist</h1>

      <p className="max-w-[52ch] text-body leading-relaxed text-ink-muted">
        It may have been renamed, or the address mistyped. Everything is in the menu on the left, or
        press Ctrl+K and type what you are looking for.
      </p>

      <Link href="/dashboard" className="pos-button-primary mt-2">
        Go to the dashboard
      </Link>
    </div>
  );
}
