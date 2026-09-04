import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { ROUTES } from './routes';
import { TONE, toneFor, type Tone } from './tone';

const CSS = readFileSync(fileURLToPath(new URL('../app/globals.css', import.meta.url)), 'utf8');

const COLOURED: readonly Tone[] = ['home', 'sell', 'catalog', 'stock', 'people', 'supply', 'money'];

describe('toneFor', () => {
  it('resolves every top-level destination in the rail', () => {
    // Not "every route has a tone" — neutral is a tone. The claim worth pinning is that the seven
    // screens somebody navigates to by colour actually have one, because a rail where two of the
    // eleven rows are grey teaches that the colours mean nothing.
    expect(toneFor('/dashboard')).toBe('home');
    expect(toneFor('/pos')).toBe('sell');
    expect(toneFor('/sales')).toBe('sell');
    expect(toneFor('/orders')).toBe('sell');
    expect(toneFor('/catalog/products')).toBe('catalog');
    expect(toneFor('/inventory')).toBe('stock');
    expect(toneFor('/customers')).toBe('people');
    expect(toneFor('/purchasing')).toBe('supply');
    expect(toneFor('/purchasing/suppliers')).toBe('supply');
    expect(toneFor('/receivables')).toBe('money');
  });

  it('gives a child screen its parent tone', () => {
    // The point of the prefix table: a screen added under an existing area is coloured without
    // anybody editing this file.
    expect(toneFor('/inventory/counts')).toBe('stock');
    expect(toneFor('/inventory/transfers')).toBe('stock');
    expect(toneFor('/catalog/bulk')).toBe('catalog');
    expect(toneFor('/inventory/some/screen/invented/later')).toBe('stock');
  });

  it('leaves the utility drawer neutral', () => {
    expect(toneFor('/reports')).toBe('neutral');
    expect(toneFor('/admin')).toBe('neutral');
    expect(toneFor('/admin/settings')).toBe('neutral');
    expect(toneFor('/help')).toBe('neutral');
  });

  it('colours a report for its subject, not for "reports"', () => {
    // The index is neutral; its leaves are not. Somebody hunting last month's stock valuation is
    // hunting a stock thing, and nine identical grey cards is the failure the tones exist to fix.
    expect(toneFor('/reports/stock-value')).toBe('stock');
    expect(toneFor('/reports/stock-position')).toBe('stock');
    expect(toneFor('/reports/sales')).toBe('sell');
    expect(toneFor('/reports/on-order')).toBe('supply');
    expect(toneFor('/reports/reward-points')).toBe('people');
    expect(toneFor('/reports/tax')).toBe('money');
  });

  it('does not let one report prefix swallow another', () => {
    // '/reports/sales' and '/reports/sales-analysis' are siblings, not parent and child. Without
    // the boundary check the first would claim the second, which happens to give the right colour
    // here and would give the wrong one the moment a '/reports/sales-tax' is added.
    expect(toneFor('/reports/sales-analysis')).toBe('sell');
  });

  it('does not match a prefix that is only a string prefix', () => {
    // '/salesperson' is not inside '/sales'. Without the boundary check it would be sell-toned.
    expect(toneFor('/salesperson')).toBe('neutral');
    expect(toneFor('/posture')).toBe('neutral');
  });

  it('never returns a tone with no class map', () => {
    for (const route of ROUTES) {
      expect(TONE[toneFor(route.href)], route.href).toBeDefined();
    }
  });
});

describe('the tone tokens', () => {
  /**
   * Every class the map names has to be a token that exists.
   *
   * This is the failure the whole indirection invites: a class name is a string, Tailwind generates
   * nothing for one it cannot resolve, and the result is not an error but an uncoloured icon that
   * looks like a design decision. Checking the names against the stylesheet is what turns that into
   * a failing test.
   */
  it('names only custom properties that are declared', () => {
    for (const tone of COLOURED) {
      expect(CSS, `--tone-${tone}`).toContain(`--tone-${tone}:`);
      expect(CSS, `--tone-${tone}-soft`).toContain(`--tone-${tone}-soft:`);
    }
  });

  it('keeps every domain hue distinct enough to tell apart', () => {
    // Two areas drawn 15 degrees apart are two areas nobody can tell apart across a counter, which
    // costs the whole scheme its point. 25 degrees is the floor these were spaced against.
    const hues = COLOURED.map((tone) => {
      const match = new RegExp(`--tone-${tone}:\\s*[\\d.]+\\s+[\\d.]+\\s+([\\d.]+)\\s*;`).exec(CSS);
      if (!match) throw new Error(`--tone-${tone} not found`);
      return { tone, hue: Number(match[1]) };
    });

    for (const a of hues) {
      for (const b of hues) {
        if (a.tone === b.tone) continue;

        const raw = Math.abs(a.hue - b.hue);
        const separation = Math.min(raw, 360 - raw);

        expect(separation, `${a.tone} (${a.hue}) vs ${b.tone} (${b.hue})`).toBeGreaterThanOrEqual(25);
      }
    }
  });

  it('stays quieter than a semantic colour', () => {
    // A domain tone is furniture and a semantic is an alarm. Held at equal chroma, an amber Stock
    // icon competes with an amber warning badge for the same attention, and the badge loses —
    // there are eleven rail icons on screen at all times and one badge.
    const chromaOf = (name: string) => {
      const match = new RegExp(`--${name}:\\s*[\\d.]+\\s+([\\d.]+)\\s+[\\d.]+\\s*;`).exec(CSS);
      if (!match) throw new Error(`--${name} not found`);
      return Number(match[1]);
    };

    const loudestTone = Math.max(...COLOURED.map((tone) => chromaOf(`tone-${tone}`)));
    const quietestSemantic = Math.min(
      ...['positive', 'warning', 'negative', 'live'].map(chromaOf),
    );

    expect(loudestTone, `tones at ${loudestTone}, semantics from ${quietestSemantic}`).toBeLessThan(
      quietestSemantic,
    );
  });
});
