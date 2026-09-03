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
  icon: Icon,
  href,
}: {
  label: string;
  value: ReactNode;
  hint?: string;
  delta?: KpiDelta;
  /** Semantic, and separate from the accent hue — a tile is not styled by brand, it is styled by state. */
  tone?: 'neutral' | 'positive' | 'warning' | 'negative' | 'live' | 'special';
  /**
   * The glyph in the tile's corner chip.
   *
   * A tile with only a label and a number is four grey rectangles that have to be read to be told
   * apart. The chip gives each one a shape and a colour, so somebody scanning the row lands on the
   * right tile before reading a word of it — and the colour is the tone's, so it means something.
   */
  icon?: LucideIcon;
  href?: string;
}) {
  const Wrapper = href ? 'a' : 'div';

  const up = delta ? delta.percent >= 0 : false;
  const good = delta ? (delta.goodWhen === 'down' ? !up : up) : false;

  return (
    <Wrapper
      {...(href ? { href } : {})}
      className={cn(
        'pos-panel relative flex flex-col justify-between overflow-hidden p-5',
        // The tone, as a band. Colour is never the only carrier — the chip's glyph and the label
        // say the same thing — but a band is what makes a row of four tiles four *different* tiles
        // at a glance instead of four rectangles to be read in turn.
        'before:absolute before:inset-x-0 before:top-0 before:h-1.5 before:content-[""]',
        tone === 'positive' && 'before:bg-positive',
        tone === 'warning' && 'before:bg-warning',
        tone === 'negative' && 'before:bg-negative',
        tone === 'live' && 'before:bg-live',
        tone === 'special' && 'before:bg-special',
        tone === 'neutral' && 'before:bg-accent',
        // A linked tile lifts rather than tints: the shadow step and half-pixel rise say
        // "clickable" without repainting the figure someone is trying to read.
        href &&
          'transition-all duration-150 hover:-translate-y-0.5 hover:border-strong hover:shadow-popover',
      )}
    >
      <div className="flex items-start justify-between gap-3">
        {/* Sentence case. The label names the figure; it is not a column heading on a form, and
            uppercase at 12px is measurably slower to read for no gain in a four-word phrase. */}
        <p className="text-body font-medium text-ink-muted">{label}</p>

        {Icon ? (
          <span
            className={cn(
              'flex h-12 w-12 shrink-0 items-center justify-center rounded-lg',
              tone === 'positive' && 'bg-positive-soft text-positive-text',
              tone === 'warning' && 'bg-warning-soft text-warning-text',
              tone === 'negative' && 'bg-negative-soft text-negative-text',
              tone === 'live' && 'bg-live-soft text-live-text',
              tone === 'special' && 'bg-special-soft text-special-text',
              tone === 'neutral' && 'bg-accent-soft text-accent-text',
            )}
            aria-hidden
          >
            <Icon className="h-6 w-6" />
          </span>
        ) : null}
      </div>

      <p
        className={cn(
          'pos-amount pos-kpi-value mt-2',
          // The -text tones, not the fills: these are words on a panel, and the fill lightnesses
          // are tuned to be seen rather than read.
          tone === 'positive' && 'text-positive-text',
          tone === 'warning' && 'text-warning-text',
          tone === 'negative' && 'text-negative-text',
          tone === 'live' && 'text-live-text',
          tone === 'special' && 'text-special-text',
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
