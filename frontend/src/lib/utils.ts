import { clsx, type ClassValue } from 'clsx';
import { extendTailwindMerge } from 'tailwind-merge';

/**
 * The design system's own font sizes, named so tailwind-merge can tell them from a text colour.
 *
 * Kept in step with tailwind.config.js by a test rather than by discipline — see cn.test.ts, which
 * reads the config and fails if either list drifts. The lists are duplicated here on purpose: the
 * alternative is importing the config, which would drag it into the browser bundle to answer a
 * question that only matters at build time.
 */
const FONT_SIZES = [
  'caption', 'label', 'body', 'body-lg', 'h3', 'h2', 'h1', 'value', 'value-lg', 'display',
] as const;

/** Every colour token that can appear as `text-…`. */
const TEXT_COLOURS = [
  'ink', 'ink-muted', 'ink-faint',
  'accent', 'accent-strong', 'accent-soft', 'accent-text', 'accent-foreground',
  'positive', 'warning', 'negative', 'live',
  'positive-text', 'warning-text', 'negative-text', 'live-text',
  'surface', 'panel', 'panel-hover', 'panel-sunken', 'subtle', 'strong', 'control',
] as const;

/**
 * Class merging that knows this design system.
 *
 * Plain `twMerge` does not. tailwind-merge ships knowing Tailwind's stock scale — `text-sm`,
 * `text-red-500` — and resolves a conflict by keeping the last class in a group. It has no way to
 * know that `text-body` is a size and `text-ink-muted` is a colour, so it files them together and
 * throws one away:
 *
 *     twMerge('text-body text-ink-muted')  ->  'text-ink-muted'
 *
 * That has been happening in production. The size is dropped and the element silently inherits
 * whatever its parent had. The sharpest instance was the POS tendered-amount field, written as
 * `cn('… text-h3', tenderError && 'text-negative')` — so the figure shrank at exactly the moment a
 * cashier had mistyped the money, which is the worst possible moment for a number to get smaller.
 *
 * Teaching the merger the two groups fixes every such pair at once, and matters more the moment
 * sizes are set deliberately: without this, any explicit size added to a shared component is
 * deleted the first time that component also sets a colour.
 */
const twMerge = extendTailwindMerge({
  extend: {
    classGroups: {
      'font-size': [{ text: [...FONT_SIZES] }],
      'text-color': [{ text: [...TEXT_COLOURS] }],
    },
  },
});

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

/** Exported for the drift test only. */
export const __tokenLists = { FONT_SIZES, TEXT_COLOURS };

/**
 * Re-exported so the eighteen screens that already import it from here keep working, while the money
 * itself is decided in one place. This was `Intl.NumberFormat('en-AU', { currency: 'AUD' })` — every
 * back-office figure printed in Australian dollars no matter what the shop had configured.
 */
export { formatCurrency } from './currency';

export function formatDate(date: string | Date): string {
  return new Intl.DateTimeFormat('en-AU', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(date));
}

/**
 * The record id a `<select>` or `<input>` just reported.
 *
 * A DOM control always hands back a string, whatever was put into the option. Empty stays empty
 * rather than becoming 0: '' is "nothing chosen", and 0 is the id a form holds while it is creating
 * a record — collapsing the two would turn an unset filter into "the new one".
 */
export function recordIdFrom(value: string): number | '' {
  return value === '' ? '' : Number(value);
}
