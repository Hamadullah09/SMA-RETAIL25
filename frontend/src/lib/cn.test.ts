import { describe, expect, it } from 'vitest';
import { createRequire } from 'node:module';
import { cn, __tokenLists } from './utils';

const require = createRequire(import.meta.url);

/**
 * What `cn()` is not allowed to throw away.
 *
 * tailwind-merge resolves conflicts by keeping the last class in a group, which is exactly right
 * for `text-sm text-lg` and exactly wrong for `text-body text-ink-muted` — those are a size and a
 * colour, and a merger that has never heard of this design system files them together and drops
 * one. It did, in production, until this file existed.
 *
 * These tests are pinned to real call sites rather than invented pairs, so a regression reads as
 * "the POS payment field lost its size" instead of "a string comparison failed".
 */
describe('cn keeps a size and a colour together', () => {
  it.each([
    ['text-body', 'text-ink-muted'],
    ['text-label', 'text-ink-faint'],
    ['text-h3', 'text-negative'],
    ['text-body-lg', 'text-positive'],
    ['text-caption', 'text-warning'],
    ['text-h1', 'text-accent'],
    ['text-display', 'text-ink'],
  ])('%s + %s', (size, colour) => {
    const result = cn(size, colour).split(' ');

    expect(result).toContain(size);
    expect(result).toContain(colour);
  });

  /**
   * The exact shape at components/pos/dialogs.tsx:336. The tendered-amount field sets its size in
   * the base string and its colour conditionally, so before this fix the figure shrank from 16px to
   * inherited the moment a tender error appeared — the one moment it most needed to be readable.
   */
  it('keeps the POS tendered-amount size when a tender error colours it', () => {
    const withError = cn('pos-amount w-32 bg-transparent text-right text-h3 outline-none', 'text-negative');

    expect(withError).toContain('text-h3');
    expect(withError).toContain('text-negative');
  });

  /** Both classes in one string argument, as at components/shell/detail-table.tsx:66. */
  it('keeps a size and colour written in the same argument', () => {
    const header = cn('border-b border-subtle px-3 py-2.5 text-label font-medium text-ink-muted', 'text-right');

    expect(header).toContain('text-label');
    expect(header).toContain('text-ink-muted');
  });
});

describe('cn still merges genuine conflicts', () => {
  it('keeps only the last of two sizes', () => {
    expect(cn('text-body', 'text-h2')).toBe('text-h2');
  });

  it('keeps only the last of two colours', () => {
    expect(cn('text-ink-muted', 'text-negative')).toBe('text-negative');
  });

  /** Stock Tailwind behaviour must survive the extension. */
  it('still merges stock utilities', () => {
    expect(cn('px-2', 'px-4')).toBe('px-4');
    expect(cn('bg-panel', 'bg-surface')).toBe('bg-surface');
  });

  it('drops falsy input the way clsx does', () => {
    expect(cn('text-body', false && 'text-h2', undefined, null)).toBe('text-body');
  });
});

/**
 * The lists in utils.ts are duplicated from tailwind.config.js — importing the config there would
 * drag it into the browser bundle to answer a build-time question. Duplication is only safe if
 * something notices when the two drift, so this is that something: add a font size or a colour to
 * the config without telling the merger, and this fails rather than the class silently vanishing at
 * the next call site that combines it with a colour.
 */
describe('the token lists match tailwind.config.js', () => {
  const config = require('../../tailwind.config.js');
  const extend = config.theme.extend;

  it('covers every font size', () => {
    expect([...__tokenLists.FONT_SIZES].sort()).toEqual(Object.keys(extend.fontSize).sort());
  });

  it('covers every colour that can appear as text-…', () => {
    const flattened: string[] = [];

    for (const [name, value] of Object.entries(extend.colors)) {
      if (typeof value === 'string') {
        flattened.push(name);
        continue;
      }

      for (const shade of Object.keys(value as Record<string, string>)) {
        flattened.push(shade === 'DEFAULT' ? name : `${name}-${shade}`);
      }
    }

    expect([...__tokenLists.TEXT_COLOURS].sort()).toEqual(flattened.sort());
  });
});
