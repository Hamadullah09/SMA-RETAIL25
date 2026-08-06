import Link from 'next/link';
import type { ComponentType, ReactNode } from 'react';
import { AlertCircle, Boxes, CheckCircle2, Nfc, ScanBarcode, ShieldCheck } from 'lucide-react';
import { SmaMark } from '@/components/layout/logo';

/**
 * The frame every account screen sits in.
 *
 * Two columns on a desktop and one on a phone, which is not decoration: these screens are reached
 * from a manager's laptop, from the till itself, and from a phone holding a reset link out of an
 * email. The form column is the one that is always there, and it is never wider than a comfortable
 * reading measure even on a 27-inch monitor.
 *
 * The panel beside it states what the system is. It is allowed to be handsome — this is the first
 * screen anyone sees — but it stays quiet, because someone reaching it is trying to get to work.
 */

/**
 * What the product does, in four lines.
 *
 * Kept here rather than passed in: every account screen shows the same four, and a hero that
 * changed its argument depending on whether you were resetting a password would be odd.
 */
const FEATURES: ReadonlyArray<{
  icon: ComponentType<{ className?: string }>;
  term: string;
  detail: string;
}> = [
  {
    icon: ScanBarcode,
    term: 'Till',
    detail: 'Keyboard-first, and it keeps selling when the network does not.',
  },
  {
    icon: Boxes,
    term: 'Stock',
    detail: 'One live count across every store, counted from a ledger rather than a guess.',
  },
  { icon: Nfc, term: 'Tags', detail: 'Read a whole basket of RFID tags at the counter.' },
  {
    icon: ShieldCheck,
    term: 'Accounts',
    detail: 'Receivables, statements and an audit trail that reconciles.',
  },
];

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
    <div className="grid min-h-screen bg-surface lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      {/* Removed from the document below `lg` rather than reordered: on a phone the form is the
          whole screen, and a hero the thumb has to scroll past to reach it is a tax. */}
      <aside className="auth-hero relative hidden flex-col justify-between overflow-hidden border-r border-subtle p-10 lg:flex xl:p-14">
        <div>
          <BrandLockup />

          <p className="mt-12 max-w-md text-display font-semibold leading-tight tracking-tight text-ink">
            Point of sale, inventory and accounts in one place.
          </p>
          <p className="mt-4 max-w-md text-body-lg leading-relaxed text-ink-muted">
            Built for the counter first. Every till action is reachable from the keyboard, and the
            back office reads the same figures the moment they change.
          </p>
        </div>

        <ul className="mt-12 grid max-w-md gap-4">
          {FEATURES.map(({ icon: Icon, term, detail }) => (
            <li key={term} className="flex items-start gap-3">
              <span
                className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent-text"
                aria-hidden
              >
                <Icon className="h-4 w-4" />
              </span>
              <span className="min-w-0">
                <span className="block text-body-lg font-medium text-ink">{term}</span>
                <span className="mt-0.5 block text-body leading-relaxed text-ink-muted">
                  {detail}
                </span>
              </span>
            </li>
          ))}
        </ul>
      </aside>

      <main className="flex items-center justify-center px-5 py-10 sm:px-8">
        <div className="w-full max-w-[25rem]">
          {/* The hero's job on a phone, in one row. Without it the form arrives with no idea what
              it belongs to, which is the wrong first impression for a screen asking for a password. */}
          <div className="mb-8 flex justify-center lg:hidden">
            <BrandLockup compact />
          </div>

          <div className="animate-slide-up rounded-lg border border-subtle bg-panel p-6 shadow-popover sm:p-8">
            <h1 className="text-h1 font-semibold tracking-tight text-ink">{title}</h1>
            <p className="mt-2 text-body-lg leading-relaxed text-ink-muted">{lead}</p>

            {/* `.auth-form` sizes the controls inside for a screen that is typed into once, rather
                than for the dense grids `.pos-input` was drawn for. */}
            <div className="auth-form mt-7">{children}</div>
          </div>

          {footer ? (
            <div className="mt-6 text-center text-body leading-relaxed text-ink-muted">{footer}</div>
          ) : null}
        </div>
      </main>
    </div>
  );
}

/**
 * The product's signature: the mark, the name, and what it is.
 *
 * The mark is the only place the brand orange appears. The accent everything else is drawn in stays
 * indigo, so the eye still knows which thing on the screen is the button.
 */
function BrandLockup({ compact = false }: { compact?: boolean }) {
  return (
    <span className="flex items-center gap-3">
      {/* Both lockups are in the document at every width — one is hidden by a breakpoint rather
          than unmounted — so the two marks cannot share a gradient id. */}
      <SmaMark
        className={`${compact ? 'h-9 w-9' : 'h-11 w-11'} rounded-[23%] shadow-raised`}
        gradientId={compact ? 'sma-mark-auth-compact' : 'sma-mark-auth-hero'}
      />
      <span className="leading-tight">
        <span className="block text-h3 font-semibold tracking-tight text-ink">SMA Retail</span>
        <span className="block text-label text-ink-muted">Retail management</span>
      </span>
    </span>
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
    <div className="mb-5">
      <label htmlFor={id} className="mb-1.5 block text-body font-medium text-ink">
        {label}
      </label>
      {children}
      {/* `text-ink-faint` is a decorative token and lands near 3:1 on the panel. A hint telling
          someone how long their password has to be is not decoration, so it is muted, not faint. */}
      {hint ? (
        <p id={`${id}-hint`} className="mt-1.5 text-caption text-ink-muted">
          {hint}
        </p>
      ) : null}
    </div>
  );
}

/**
 * A message the user must not miss.
 *
 * `role="alert"` so it is announced when it appears, and the tone is carried by a worded label as
 * well as by colour — a red border alone says nothing to anyone who cannot see it. The icon is
 * `aria-hidden`, so it decorates the label rather than replacing it.
 */
export function AuthNotice({ tone, children }: { tone: 'error' | 'success'; children: ReactNode }) {
  const isError = tone === 'error';
  const Icon = isError ? AlertCircle : CheckCircle2;

  return (
    <p
      role="alert"
      className={`mb-5 flex items-start gap-2.5 rounded border px-3.5 py-3 text-body leading-relaxed ${
        isError
          ? 'border-negative/30 bg-negative/10 text-negative'
          : 'border-positive/30 bg-positive/10 text-positive'
      }`}
    >
      <Icon className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
      <span>
        <span className="font-medium">{isError ? 'Not done. ' : 'Done. '}</span>
        {children}
      </span>
    </p>
  );
}

export function AuthLink({ href, children }: { href: string; children: ReactNode }) {
  return (
    <Link
      href={href}
      className="rounded-sm font-medium text-accent-text underline decoration-accent-text/40 underline-offset-2 transition-colors hover:decoration-current"
    >
      {children}
    </Link>
  );
}
