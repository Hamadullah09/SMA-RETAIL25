'use client';

import type { ReactNode } from 'react';
import type { LucideIcon } from 'lucide-react';
import { cn, formatCurrency } from '@/lib/utils';

/**
 * The pieces the dashboard is assembled from.
 *
 * A dashboard is scanned, not read. So the rules here are about glanceability: the number is the
 * largest thing in its tile, figures are tabular so columns of digits line up, and anything that
 * needs attention says so in words as well as colour — a red number is invisible to eight percent of
 * men, and this is a screen a manager checks in five seconds on their way past.
 */

/**
 * A period-on-period change, shown as a tinted pill.
 *
 * `goodWhen` exists because up is not always good. Sales rising is a green arrow; debtor days rising
 * is the same arrow and the opposite news, and a tile that paints both green is a tile that has
 * stopped carrying information.
 */
export interface KpiDelta {
  /** Already a percentage: five percent is `5`. */
  percent: number;
  /** What the change is measured against, e.g. "vs yesterday 1,204.50". */
  comparison?: string;
  goodWhen?: 'up' | 'down';
}

export function KpiTile({
  label,
  value,
  hint,
  delta,
  tone = 'neutral',
  href,
}: {
  label: string;
  value: ReactNode;
  hint?: string;
  delta?: KpiDelta;
  /** Semantic, and separate from the accent hue — a tile is not styled by brand, it is styled by state. */
  tone?: 'neutral' | 'positive' | 'warning' | 'negative';
  href?: string;
}) {
  const Wrapper = href ? 'a' : 'div';

  const up = delta ? delta.percent >= 0 : false;
  const good = delta ? (delta.goodWhen === 'down' ? !up : up) : false;

  return (
    <Wrapper
      {...(href ? { href } : {})}
      className={cn(
        'pos-panel flex flex-col justify-between p-5',
        // A linked tile lifts rather than tints: the shadow step and half-pixel rise say
        // "clickable" without repainting the figure someone is trying to read.
        href &&
          'transition-all duration-150 hover:-translate-y-0.5 hover:border-strong hover:shadow-popover',
      )}
    >
      {/* Sentence case. The label names the figure; it is not a column heading on a form, and
          uppercase at 12px is measurably slower to read for no gain in a four-word phrase. */}
      <p className="text-body font-medium text-ink-muted">{label}</p>

      <p
        className={cn(
          'pos-amount pos-kpi-value mt-2',
          tone === 'positive' && 'text-positive',
          tone === 'warning' && 'text-warning',
          tone === 'negative' && 'text-negative',
          tone === 'neutral' && 'text-ink',
        )}
      >
        {value}
      </p>

      {/* The footer row: what it is compared against on the left, how it moved on the right. Kept on
          one baseline so a row of four tiles has one horizontal line through it rather than four. */}
      {hint || delta ? (
        <div className="mt-3 flex items-center justify-between gap-2">
          <p className="min-w-0 truncate text-caption text-ink-muted">{delta?.comparison ?? hint}</p>

          {delta ? (
            <span className={cn('pos-delta', good ? 'text-positive' : 'text-negative')}>
              <span aria-hidden>{up ? '↑' : '↓'}</span>
              {Math.abs(delta.percent).toFixed(2)}%
              <span className="sr-only">{up ? ' increase' : ' decrease'}</span>
            </span>
          ) : null}
        </div>
      ) : null}
    </Wrapper>
  );
}

export function Panel({
  title,
  icon: Icon,
  action,
  children,
  className,
}: {
  title: string;
  /** Optional, and it must earn its place — a glyph that only restates the title is decoration. */
  icon?: LucideIcon;
  action?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={cn('pos-panel flex min-h-0 flex-col', className)}>
      <header className="pos-panel-header">
        <span className="pos-panel-title">
          {Icon ? <Icon aria-hidden /> : null}
          {title}
        </span>
        {action ? <span className="pos-panel-header-action">{action}</span> : null}
      </header>
      <div className="min-h-0 flex-1 p-4">{children}</div>
    </section>
  );
}

/**
 * A ranked list with a proportional bar behind each row.
 *
 * The bar is the point: it turns "which of these is biggest" from arithmetic into a glance. Drawn
 * behind the text rather than beside it so the row stays one line at any width — this panel is a
 * third of the screen on a laptop and the full width on a phone.
 */
export function RankedBars({
  rows,
  empty,
}: {
  rows: Array<{ key: string; label: string; value: number; sub?: string }>;
  empty: string;
}) {
  if (rows.length === 0) {
    return <EmptyState>{empty}</EmptyState>;
  }

  const largest = Math.max(...rows.map((r) => Math.abs(r.value)), 1);

  return (
    <ol className="space-y-1">
      {rows.map((row) => (
        <li key={row.key} className="relative overflow-hidden rounded-sm">
          <div
            aria-hidden
            className="absolute inset-y-0 left-0 bg-accent/15"
            style={{ width: `${Math.max(2, (Math.abs(row.value) / largest) * 100)}%` }}
          />

          <div className="relative flex items-baseline justify-between gap-3 px-2 py-1.5">
            <span className="truncate text-body text-ink">{row.label}</span>

            <span className="shrink-0 text-right">
              <span className="pos-amount text-body tabular-nums text-ink">{formatCurrency(row.value)}</span>
              {row.sub ? <span className="ml-2 text-caption text-ink-faint">{row.sub}</span> : null}
            </span>
          </div>
        </li>
      ))}
    </ol>
  );
}

/**
 * Nothing to show, said so it reads as a state rather than a failure. The dashed border marks the
 * region a list would fill, so an empty panel still has a shape.
 */
export function EmptyState({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-[6rem] items-center justify-center rounded-md border border-dashed border-subtle bg-panel-sunken/60 px-4 py-6">
      <p className="text-center text-body text-ink-muted">{children}</p>
    </div>
  );
}

/** A skeleton that holds the tile's height, so the grid does not jump when figures land. */
export function TileSkeleton({ label }: { label: string }) {
  return (
    <div className="pos-panel flex flex-col justify-between p-5" aria-busy="true">
      <p className="text-body font-medium text-ink-muted">{label}</p>
      <p className="mt-2 h-9 w-28 animate-pulse rounded-sm bg-panel-sunken" />
      <p className="mt-3 h-3.5 w-32 animate-pulse rounded-sm bg-panel-sunken" />
    </div>
  );
}
