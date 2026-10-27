import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { colors, contrast, cssVar, rgbTriplet, textPairs, type ColorToken } from './tokens';

/** The real stylesheet, so the check cannot drift from what ships. */
const css = readFileSync(fileURLToPath(new URL('../app/globals.css', import.meta.url)), 'utf8');

function cssValue(token: ColorToken): string {
  const match = new RegExp(`${cssVar(token)}:\\s*(\\d+ \\d+ \\d+);`).exec(css);
  if (!match) throw new Error(`${cssVar(token)} is missing from globals.css`);
  return match[1];
}

describe('design tokens', () => {
  it.each(Object.keys(colors) as ColorToken[])('%s is the same in tokens.ts and globals.css', (token) => {
    expect(cssValue(token)).toBe(rgbTriplet(colors[token]));
  });

  it('keeps the desktop values of section 6', () => {
    expect(colors).toMatchObject({
      page0: '#ECEBE7',
      page45: '#F2F0EA',
      page80: '#F7ECC9',
      page100: '#F8E39C',
      card: '#FFFFFF',
      ink: '#1E1E1E',
      charcoal: '#2B2B2B',
      charcoalSoft: '#3A3A3A',
      sun: '#F7D35C',
      sunDark: '#E2B52A',
      sunLight: '#FFF2C2',
      cream: '#FAF7EE',
      line: '#E6E1D3',
      muted: '#6B675E',
      stone: '#9A968D',
      danger: '#C2412D',
      dangerLight: '#FBE4DF',
      ok: '#2F7D4E',
      okLight: '#DFF1E5',
    });
  });

  it('measures contrast the WCAG way', () => {
    expect(contrast('#000000', '#FFFFFF')).toBeCloseTo(21, 5);
    expect(contrast('#777777', '#FFFFFF')).toBeCloseTo(4.48, 2);
  });
});

describe('text contrast (WCAG AA, 4.5:1)', () => {
  it.each(textPairs.map(([text, ground]) => [text, ground] as const))('%s on %s', (text, ground) => {
    const ratio = contrast(colors[text], colors[ground]);
    expect(ratio, `${text} on ${ground} measures ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(4.5);
  });

  it('stone is never used for text', () => {
    expect(textPairs.some(([text]) => text === 'stone')).toBe(false);
    // Why: it measures under 3:1 on white.
    expect(contrast(colors.stone, colors.card)).toBeLessThan(4.5);
  });

  it('the desktop danger and ok pairs are too light for words, so their -ink shades carry text on the tints', () => {
    expect(contrast(colors.danger, colors.dangerLight)).toBeLessThan(4.5);
    expect(contrast(colors.ok, colors.okLight)).toBeLessThan(4.5);
    expect(contrast(colors.dangerInk, colors.dangerLight)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(colors.okInk, colors.okLight)).toBeGreaterThanOrEqual(4.5);
  });
});
