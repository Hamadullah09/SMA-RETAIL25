'use client';

import { CircleAlert, Inbox, Lock, RefreshCw } from 'lucide-react';
import type { ReactNode } from 'react';
import { ApiError, FORBIDDEN_MESSAGE } from '@/lib/api';
import { cx } from '@/lib/cx';
import { Button } from './button';

/** A grey bar that shimmers while data loads. */
export function Skeleton({ className }: { className?: string }) {
  return <div className={cx('skeleton h-5', className)} aria-hidden="true" />;
}

/** Placeholder rows for a table that is loading. */
export function TableSkeleton({ rows = 6, columns = 5 }: { rows?: number; columns?: number }) {
  return (
    <div className="space-y-3 p-4" role="status" aria-label="Loading">
      <div className="flex gap-4">
        {Array.from({ length: columns }, (_, c) => (
          <Skeleton key={c} className="h-4 flex-1 opacity-70" />
        ))}
      </div>
      {Array.from({ length: rows }, (_, r) => (
        <div key={r} className="flex gap-4">
          {Array.from({ length: columns }, (_, c) => (
            <Skeleton key={c} className={cx('h-6 flex-1', c === 0 && 'max-w-[28%]')} />
          ))}
        </div>
      ))}
    </div>
  );
}

/** "Nothing here yet", with what to do about it. */
export function EmptyState({ title, children, icon: Icon = Inbox, className }: { title: string; children?: ReactNode; icon?: typeof Inbox; className?: string }) {
  return (
    <div className={cx('flex flex-col items-center justify-center gap-2 px-6 py-12 text-center', className)}>
      <span className="grid h-14 w-14 place-items-center rounded-full bg-cream">
        <Icon className="h-6 w-6 text-muted" strokeWidth={1.6} />
      </span>
      <p className="font-heading text-section text-ink">{title}</p>
      {children && <div className="max-w-md text-muted">{children}</div>}
    </div>
  );
}

/**
 * What went wrong loading a page or a list. A 403 says "Your role cannot open this page."; anything else shows the
 * server's message with a Try again button.
 */
export function ErrorState({ error, onRetry, className }: { error: unknown; onRetry?: () => void; className?: string }) {
  const apiError = error instanceof ApiError ? error : null;
  if (apiError?.isForbidden) {
    return (
      <div className={cx('flex flex-col items-center justify-center gap-2 px-6 py-12 text-center', className)} role="alert">
        <span className="grid h-14 w-14 place-items-center rounded-full bg-sun-light">
          <Lock className="h-6 w-6 text-ink" strokeWidth={1.6} />
        </span>
        <p className="font-heading text-section text-ink">{FORBIDDEN_MESSAGE}</p>
        {apiError.message !== FORBIDDEN_MESSAGE && <p className="max-w-md text-muted">{apiError.message}</p>}
      </div>
    );
  }
  const message = apiError?.message ?? (error instanceof Error ? error.message : 'Something went wrong.');
  return (
    <div className={cx('flex flex-col items-center justify-center gap-3 px-6 py-12 text-center', className)} role="alert">
      <span className="grid h-14 w-14 place-items-center rounded-full bg-danger-light">
        <CircleAlert className="h-6 w-6 text-danger" strokeWidth={1.6} />
      </span>
      <p className="font-heading text-section text-ink">This did not load</p>
      <p className="max-w-md text-muted">{message}</p>
      {onRetry && (
        <Button icon={RefreshCw} onClick={onRetry}>
          Try again
        </Button>
      )}
    </div>
  );
}

/** The page body for a user whose role lacks the page's permission. */
export function Forbidden() {
  return (
    <div className="card mt-2">
      <ErrorState error={new ApiError(403, FORBIDDEN_MESSAGE)} />
    </div>
  );
}

/** A short line in Danger under a form: the server's reason a save failed. */
export function FormError({ error }: { error: unknown }) {
  if (!error) return null;
  const message = error instanceof Error ? error.message : String(error);
  return (
    <p className="mt-3 flex items-start gap-2 rounded-control bg-danger-light px-3 py-2 font-semibold text-danger-ink" role="alert">
      <CircleAlert className="mt-0.5 h-5 w-5 shrink-0" />
      {message}
    </p>
  );
}
