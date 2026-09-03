import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

/**
 * The colour tokens, measured rather than asserted.
 *
 * Contrast is the one design property that can be checked arithmetically, so there is no reason to
 * take it on trust — and taking it on trust is exactly what went wrong: `--border-control` was
 * specified at "3.1:1 on panel", added for the sole purpose of satisfying WCAG 1.4.11, and measured
 * 2.81:1 on panel and 2.53:1 on surface. It failed the rule it existed to meet, and nothing would
 * have said so.
 *
 * This reads the real stylesheet, so it cannot drift from what ships.
 */
const CSS = readFileSync(
  fileURLToPath(new URL('../app/globals.css', import.meta.url)),
  'utf8',
);

/**
 * One theme.
 *
 * The dark theme is gone: this is a shop-floor system whose users are told which screen to look at,
 * not a personal tool where a preference is worth the second palette. Two palettes also meant every
 * colour decision had to be made twice and verified twice, and the second one was where the
 * contrast failures hid.
 */
const LIGHT = CSS;

function token(block: string, name: string): [number, number, number] {
  const match = new RegExp(`--${name}:\\s*(\\d+)\\s+(\\d+)\\s+(\\d+)\\s*;`).exec(block);

  if (!match) throw new Error(`--${name} not found`);

  return [Number(match[1]), Number(match[2]), Number(match[3])];
}

/** WCAG relative luminance. */
function luminance([r, g, b]: [number, number, number]): number {
  const channel = (value: number) => {
    const v = value / 255;
    return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
  };

  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

function contrast(a: [number, number, number], b: [number, number, number]): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** Every ground a token can legitimately sit on. A pass on one is not a pass. */
const GROUNDS = ['panel', 'surface', 'panel-hover'] as const;

const THEMES = [['light', LIGHT]] as const;

describe.each(THEMES)('%s theme', (_theme, block) => {
  /**
   * The faintest text in the system. It was justified as "decorative only" at 3.17:1, but it is
   * what placeholders, empty states and disabled labels are written in — and a placeholder is an
   * instruction, not decoration.
   */
  describe.each(GROUNDS)('text-faint on %s', (ground) => {
    it('clears AA for body text', () => {
      const ratio = contrast(token(block, 'text-faint'), token(block, ground));

      expect(ratio, `measured ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(4.5);
    });
  });

  /**
   * WCAG 1.4.11: the boundary of something you can operate needs 3:1. A divider tuned to be quiet
   * cannot also be that, which is why this token is separate from `--border`.
   */
  describe.each(GROUNDS)('border-control on %s', (ground) => {
    it('clears the non-text contrast minimum', () => {
      const ratio = contrast(token(block, 'border-control'), token(block, ground));

      expect(ratio, `measured ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(3);
    });
  });

  /** Body and muted text carry almost every word in the app. */
  describe.each(['text', 'text-muted'] as const)('%s on panel', (name) => {
    it('clears AA', () => {
      const ratio = contrast(token(block, name), token(block, 'panel'));

      expect(ratio, `measured ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(4.5);
    });
  });
});
