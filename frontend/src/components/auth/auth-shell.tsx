import Link from 'next/link';
import type { ReactNode } from 'react';

/**
 * The frame every account screen sits in.
 *
 * Two columns on a desktop and one on a phone, which is not decoration: these screens are reached
 * from a manager's laptop, from the till itself, and from a phone holding a reset link out of an
 * email. The form column is the one that is always there, and it is never wider than a comfortable
 * reading measure even on a 27-inch monitor.
 *
 * The panel beside it is deliberately quiet — a statement of what the system is, not a marketing
 * hero. Someone reaching this screen is trying to get to work.
 */
export function AuthShell({
  title,
  lead,
  children,
  footer,
}: {
  title: string;
  lead: string;
  children: ReactNode;
  footer?: ReactNode;
}) {
  return (
    <div className="grid min-h-screen bg-surface lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
      {/* Ordered second on a phone so the form is the first thing under the thumb, and first on a
          desktop so the eye lands left-to-right the way the language reads. */}
      <aside className="auth-hero order-2 hidden flex-col justify-between border-r border-subtle p-10 lg:order-1 lg:flex">
        <div>
          <p className="flex items-center gap-2.5">
            <span className="pos-brand-mark h-7 w-7 text-label" aria-hidden>
              25
            </span>
            <span className="text-label font-semibold uppercase tracking-widest text-ink-muted">
              Retail25
            </span>
          </p>
          <p className="mt-8 max-w-md text-display font-semibold leading-tight tracking-tight text-ink">
            Point of sale, inventory and accounts in one place.
          </p>
          <p className="mt-4 max-w-sm text-body-lg leading-relaxed text-ink-muted">
            Built for the counter first — every till action is reachable from the keyboard, and the
            back office reads the same data the moment it changes.
          </p>
        </div>

        <dl className="grid grid-cols-3 gap-3">
          {[
            ['Tills', 'Offline-tolerant'],
            ['Stock', 'Live across stores'],
            ['Tags', 'RFID at the counter'],
          ].map(([term, detail]) => (
            <div
              key={term}
              className="rounded-md border border-subtle bg-panel/70 px-3.5 py-3 shadow-raised backdrop-blur-sm"
            >
              <dt className="text-label font-medium uppercase tracking-wide text-accent-text">
                {term}
              </dt>
              <dd className="mt-1 text-body text-ink-muted">{detail}</dd>
            </div>
          ))}
        </dl>
      </aside>

      <main className="order-1 flex items-center justify-center p-6 lg:order-2">
        <div className="w-full max-w-sm">
          <div className="rounded-lg border border-subtle bg-panel p-6 shadow-popover sm:p-8">
            <h1 className="text-h2 font-semibold tracking-tight text-ink">{title}</h1>
            <p className="mt-1.5 text-body text-ink-muted">{lead}</p>

            <div className="mt-8">{children}</div>
          </div>

          {footer ? <div className="mt-6 px-1 text-body text-ink-muted">{footer}</div> : null}
        </div>
      </main>
    </div>
  );
}

/**
 * A labelled field.
 *
 * The label is a real `<label>` bound by id rather than a placeholder standing in for one —
 * placeholder-as-label disappears the moment someone types, which is exactly when they most want to
 * check what they are filling in.
 */
export function AuthField({
  id,
  label,
  hint,
  children,
}: {
  id: string;
  label: string;
  hint?: string;
  children: ReactNode;
}) {
  return (
    <div className="mb-4">
      <label htmlFor={id} className="mb-1 block text-label font-medium text-ink-muted">
        {label}
      </label>
      {children}
      {hint ? (
        <p id={`${id}-hint`} className="mt-1 text-caption text-ink-faint">
          {hint}
        </p>
      ) : null}
    </div>
  );
}

/**
 * A message the user must not miss.
 *
 * `role="alert"` so it is announced when it appears, and the tone is carried by an icon-free label
 * as well as by colour — a red border alone says nothing to anyone who cannot see it.
 */
export function AuthNotice({ tone, children }: { tone: 'error' | 'success'; children: ReactNode }) {
  const isError = tone === 'error';

  return (
    <p
      role="alert"
      className={`mb-4 rounded-sm border px-3 py-2 text-body ${
        isError
          ? 'border-negative/30 bg-negative/10 text-negative'
          : 'border-positive/30 bg-positive/10 text-positive'
      }`}
    >
      <span className="font-medium">{isError ? 'Not done. ' : 'Done. '}</span>
      {children}
    </p>
  );
}

export function AuthLink({ href, children }: { href: string; children: ReactNode }) {
  return (
    <Link href={href} className="font-medium text-accent underline underline-offset-2">
      {children}
    </Link>
  );
}
