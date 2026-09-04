/**
 * Which part of the shop a screen belongs to, and the colour that says so.
 *
 * The application is eleven destinations deep and most of the people using it did not choose to.
 * A cashier covering a shift, an owner who opens the back office twice a week, somebody who has
 * never used a computer for work before — for all three, an interface where every icon is the same
 * grey means reading eleven labels every time, because nothing on the screen is a landmark.
 *
 * A tone is that landmark. Stock is amber on the rail, on its page header and on its cards; money
 * owed is rose everywhere it appears. It is learned once, by accident, and after a week the label
 * is confirmation rather than navigation.
 *
 * ## The rule that keeps this from wrecking the alarms
 *
 * A tone appears in **navigation chrome only** — a rail icon, the rule under a page title, an index
 * card's tile. It never appears inside data, a badge, a status or a total. The four semantic
 * colours (positive, warning, negative, live) own those, and they keep meaning exactly what they
 * mean today.
 *
 * That separation is what makes it safe for Stock to be amber while amber also means "warning":
 * the two can never appear on the same surface, so neither has to be read in the other's context.
 * Break the rule — tint a table row by domain, colour a badge by section — and colour stops being
 * an alarm anywhere in the application. There is no partial version of that failure.
 *
 * Tones are also never the only carrier. Every place one is used has a label or a glyph beside it,
 * so nothing here is load-bearing for somebody who cannot separate the hues.
 */

export type Tone =
  | 'home'
  | 'sell'
  | 'catalog'
  | 'stock'
  | 'people'
  | 'supply'
  | 'money'
  | 'neutral';

/**
 * Path prefix → tone, longest match wins.
 *
 * Derived from the path rather than declared on each of the thirty-three routes, because a field
 * repeated thirty-three times is a field that will be wrong on the thirty-fourth. A new screen
 * under `/inventory` is amber without anybody remembering to say so, which is the only way this
 * stays true a year from now.
 */
const PREFIX_TONES: readonly (readonly [string, Tone])[] = [
  ['/dashboard', 'home'],

  // The till, what it has already rung, and what it has promised to ring. One tone: from the
  // counter these are the same job at three stages, and the guide treats them that way too.
  ['/pos', 'sell'],
  ['/sales', 'sell'],
  ['/orders', 'sell'],

  ['/catalog', 'catalog'],
  ['/inventory', 'stock'],
  ['/customers', 'people'],

  // Suppliers and purchase orders: money going out, against `sell`'s money coming in.
  ['/purchasing', 'supply'],

  ['/receivables', 'money'],

  /*
    A report wears the colour of the thing it reports on, not the colour of "reports".

    `/reports` itself is neutral -- it is an index, and an index is not a destination anybody
    navigates to by colour. Its leaves are: somebody looking for last month's stock valuation is
    looking for a *stock* thing, and finding it among nine identical grey cards is the exact
    failure the tones exist to fix. It also means the report's own page header carries the same
    amber as the Stock screen it is about, which is the connection worth drawing.
  */
  ['/reports/sales', 'sell'],
  ['/reports/sales-analysis', 'sell'],
  ['/reports/stock-position', 'stock'],
  ['/reports/stock-value', 'stock'],
  ['/reports/stock-received', 'stock'],
  ['/reports/on-order', 'supply'],
  ['/reports/reward-points', 'people'],
  ['/reports/tax', 'money'],

  // Reports' own index, Administration and Help stay neutral. They are the utility drawer rather
  // than a place you go by muscle memory, and more hues would leave the seven that matter competing
  // with furniture. A tone nobody navigates by is decoration, which is the thing this system is
  // specifically not.
];

/** The tone for a path. Anything unclaimed is neutral, which is a working default, not a gap. */
export function toneFor(href: string): Tone {
  let best: Tone = 'neutral';
  let bestLength = 0;

  for (const [prefix, tone] of PREFIX_TONES) {
    if ((href === prefix || href.startsWith(`${prefix}/`)) && prefix.length > bestLength) {
      best = tone;
      bestLength = prefix.length;
    }
  }

  return best;
}

/**
 * How a tone is drawn.
 *
 * Written out as whole class names rather than composed as `text-tone-${tone}`. Tailwind's scanner
 * reads source text and generates only the classes it literally sees; an interpolated class is
 * never generated and silently does nothing — the same trap the sidebar's rail widths document.
 */
export interface ToneClasses {
  /** The tone as ink: a rail glyph, a header rule, a card's icon. */
  text: string;
  /** The tint it sits on: an icon tile, a chip. */
  soft: string;
  /** The tone as a fill, for a rule or a bar that carries no text. */
  bg: string;
}

export const TONE: Record<Tone, ToneClasses> = {
  home: { text: 'text-tone-home', soft: 'bg-tone-home-soft', bg: 'bg-tone-home' },
  sell: { text: 'text-tone-sell', soft: 'bg-tone-sell-soft', bg: 'bg-tone-sell' },
  catalog: { text: 'text-tone-catalog', soft: 'bg-tone-catalog-soft', bg: 'bg-tone-catalog' },
  stock: { text: 'text-tone-stock', soft: 'bg-tone-stock-soft', bg: 'bg-tone-stock' },
  people: { text: 'text-tone-people', soft: 'bg-tone-people-soft', bg: 'bg-tone-people' },
  supply: { text: 'text-tone-supply', soft: 'bg-tone-supply-soft', bg: 'bg-tone-supply' },
  money: { text: 'text-tone-money', soft: 'bg-tone-money-soft', bg: 'bg-tone-money' },

  // The utility drawer. Muted ink on the ordinary sunken ground — visible, but not a landmark,
  // because it is not somewhere anybody navigates to by colour.
  neutral: { text: 'text-ink-muted', soft: 'bg-panel-sunken', bg: 'bg-strong' },
};

/** Both halves of a tone at once, for the icon tiles that are most of the uses. */
export function toneClasses(href: string): ToneClasses {
  return TONE[toneFor(href)];
}
