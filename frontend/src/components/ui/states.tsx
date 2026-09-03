import type { LucideIcon, } from 'lucide-react';
import { CircleAlert, Inbox, RotateCw } from 'lucide-react';
import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

/**
 * The three things a region can be instead of showing data: empty, loading, or broken.
 *
 * There were at least twelve distinct empty treatments — seven near-identical local `EmptyState`s,
 * six of them byte-for-byte the same in admin files, one drifted, and an eighth of a different
 * shape entirely. None of them accepted an action, so a screen that was empty because nothing had
 * been created yet looked exactly like one that was empty because the filters excluded everything,
 * and neither offered the way out.
 *
 * Loading was expressed six ways and errors mostly not at all: a failed load rendered the empty
 * copy, so "we could not reach the server" and "you have no products" were the same sentence.
 */

/**
 * Nothing here — and what to do about it.
 *
 * The action is the part that was missing everywhere. "No products found." is a statement; "No
 * products yet. Add your first one." is a screen somebody can use.
 */
export function EmptyState({
  icon: Icon = Inbox,
  title,
  description,
  action,
  secondaryAction,
  className,
}: {
  icon?: LucideIcon;
  title: string;
  description?: string;
  action?: ReactNode;
  secondaryAction?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cn('flex flex-col items-center gap-2 px-6 py-14 text-center', className)}>
      {/* On the accent tint rather than bare, so an empty region reads as a considered state and
          not as a region that failed to paint. */}
      <span
        className="mb-1 flex h-14 w-14 items-center justify-center rounded-full bg-accent-soft text-accent-text"
        aria-hidden
      >
        <Icon className="h-7 w-7" />
      </span>

      <p className="text-body-lg font-semibold text-ink">{title}</p>

      {description ? <p className="max-w-[52ch] text-body text-ink-muted">{description}</p> : null}

      {action || secondaryAction ? (
        <div className="mt-3 flex flex-wrap items-center justify-center gap-2">
          {action}
          {secondaryAction}
        </div>
      ) : null}
    </div>
  );
}

/**
 * A shape being held while its data arrives.
 *
 * Shaped, not a spinner: a block the size of the thing that is coming stops the page reflowing under
 * somebody's cursor the moment it lands, which is how a mis-click happens on a till.
 *
 * `aria-busy` and the status line are not decoration. A screen reader otherwise gets silence during
 * the load and then a table appearing with no announcement that anything happened.
 */
export function Skeleton({
  label,
  rows = 1,
  className,
}: {
  /** What is loading, for the announcement. "Loading products…" beats "Loading…". */
  label: string;
  rows?: number;
  className?: string;
}) {
  return (
    <div aria-busy="true" className={cn('space-y-2', className)}>
      <span className="sr-only" role="status">
        Loading {label}…
      </span>

      {Array.from({ length: rows }, (_, index) => (
        <div key={index} className="h-control animate-pulse rounded-sm bg-panel-sunken" />
      ))}
    </div>
  );
}

/**
 * Something went wrong, said in a way that admits what and offers a way on.
 *
 * Deliberately not the empty state. A region that failed to load and a region with nothing in it
 * are different facts, and showing "No products" for a 500 teaches people the shop is empty.
 */
export function ErrorState({
  title = 'We could not load this',
  description,
  onRetry,
  retryLabel = 'Try again',
  className,
}: {
  title?: string;
  /** Already written for a person. Pass describeError's output, never a raw exception. */
  description?: string;
  onRetry?: () => void;
  retryLabel?: string;
  className?: string;
}) {
  return (
    <div
      role="alert"
      className={cn('flex flex-col items-center gap-2 px-6 py-14 text-center', className)}
    >
      <span
        className="mb-1 flex h-14 w-14 items-center justify-center rounded-full bg-negative-soft text-negative-text"
        aria-hidden
      >
        <CircleAlert className="h-7 w-7" />
      </span>

      <p className="text-body-lg font-semibold text-ink">{title}</p>

      {description ? <p className="max-w-[52ch] text-body text-ink-muted">{description}</p> : null}

      {onRetry ? (
        <button type="button" onClick={onRetry} className="pos-button mt-3">
          <RotateCw className="h-5 w-5" aria-hidden />
          {retryLabel}
        </button>
      ) : null}
    </div>
  );
}

/**
 * You are signed in, but not for this.
 *
 * Distinct from an error, because nothing is broken and retrying will not help. What somebody needs
 * here is to know who to ask.
 */
export function NotAuthorisedState({ what, className }: { what: string; className?: string }) {
  return (
    <EmptyState
      icon={CircleAlert}
      title={`You do not have permission to ${what}`}
      description="Ask an administrator if you need this. Nothing is broken — this account is not set up for it."
      className={className}
    />
  );
}
