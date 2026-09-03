import Link from 'next/link';
import type { ComponentType, ReactNode } from 'react';
import {
  AlertCircle,
  BarChart3,
  Boxes,
  CheckCircle2,
  Nfc,
  Receipt,
  ScanBarcode,
  Server,
  Truck,
} from 'lucide-react';
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
 * What the product does, in six.
 *
 * Kept here rather than passed in: every account screen shows the same six, and a hero that changed
 * its argument depending on whether you were resetting a password would be odd.
 *
 * Every line names something the system actually has a screen for. A landing page that promises a
 * capability the menu does not contain is a support call on the first morning, and the person
 * reading this is usually about to start a shift rather than about to buy anything.
 */
const FEATURES: ReadonlyArray<{
  icon: ComponentType<{ className?: string }>;
  term: string;
  detail: string;
}> = [
  {
    icon: ScanBarcode,
    term: 'Point of sale',
    detail: 'Scan, tender, hold and refund. Every action on a key, the drawer counted at close.',
  },
  {
    icon: Boxes,
    term: 'Live stock',
    detail: 'One count across every store, kept as a ledger so it can always be explained.',
  },
  {
    icon: Nfc,
    term: 'RFID tags',
    detail: 'Read a whole basket at the counter instead of scanning it item by item.',
  },
  {
    icon: Truck,
    term: 'Purchasing',
    detail: 'Suppliers, orders and receiving, with cost carried through to margin.',
  },
  {
    icon: Receipt,
    term: 'Customer accounts',
    detail: 'Invoices, layaways, statements and what is owed, aged.',
  },
  {
    icon: BarChart3,
    term: 'Reporting',
    detail: 'Sales, margin and stock position, through to the year-end close.',
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
      {/* Removed from the document below `lg` rather than reordered: on a phone the form is the
          whole screen, and a hero the thumb has to scroll past to reach it is a tax.

          Laid out top-down with the closing line pushed down by `mt-auto`, rather than the two
          halves being forced apart by `justify-between`. That split left a hand's width of nothing
          across the middle of the first screen anyone sees, which reads as a page that failed to
          finish loading. */}
      <aside className="auth-hero relative hidden flex-col overflow-hidden border-r border-subtle p-10 lg:flex xl:p-14">
        <BrandLockup />

        <h2 className="mt-10 max-w-lg text-display font-semibold leading-tight tracking-tight text-ink xl:mt-12">
          Everything the shop runs on, in one system.
        </h2>
        <p className="mt-4 max-w-lg text-body-lg leading-relaxed text-ink-muted">
          The counter, the stockroom and the books, kept in step. What a cashier rings up at the till
          is what the back office reads a second later — one set of figures, not three that have to
          be reconciled at the end of the week.
        </p>

        <ul className="mt-9 grid max-w-2xl gap-2.5 sm:grid-cols-2">
          {FEATURES.map(({ icon: Icon, term, detail }) => (
            <li
              key={term}
              className="rounded-lg border border-subtle bg-panel/70 p-3.5 shadow-raised backdrop-blur-sm"
            >
              <span className="flex items-center gap-2">
                <span
                  className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-accent-soft text-accent-text"
                  aria-hidden
                >
                  <Icon className="h-5 w-5" />
                </span>
                <span className="text-body font-semibold text-ink">{term}</span>
              </span>
              <span className="mt-1.5 block text-caption leading-relaxed text-ink-muted">
                {detail}
              </span>
            </li>
          ))}
        </ul>

        {/* The one claim worth making on a sign-in page, because it is the one a shop owner asks
            about first and it is true: this runs on their hardware, not on someone else's. */}
        <p className="mt-auto flex items-start gap-2 pt-10 text-caption leading-relaxed text-ink-muted">
          <Server className="mt-px h-5 w-5 shrink-0" aria-hidden />
          <span>
            Runs on your own server. The takings, the stock and the customer list stay in the shop.
          </span>
        </p>
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
