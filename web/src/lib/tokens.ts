/**
 * The desktop app's colour tokens (src/RetailPlus.App/App.xaml, section 6 of docs/WEB_INTEGRATION_PLAN.md).
 *
 * The same values are CSS variables in `src/app/globals.css` (Tailwind reads those) and are checked against each other
 * and for contrast in `tokens.test.ts`. Charts use the hex values from here, because SVG attributes cannot read CSS
 * variables everywhere.
 */
export const colors = {
  /** The page gradient, top left to bottom right. */
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
  stoneLight: '#EDEAE2',
  danger: '#C2412D',
  dangerLight: '#FBE4DF',
  ok: '#2F7D4E',
  okLight: '#DFF1E5',
  /**
   * Danger and Ok as text on their own light tints. The desktop pairs measure 4.2:1 and 4.3:1, under the 4.5:1 that
   * WCAG AA asks of body text, so words on a tint use these slightly darker shades; fills, icons and borders keep the
   * desktop values.
   */
  dangerInk: '#A33622',
  okInk: '#256B41',
  /** Alternate and hover rows of a grid (the desktop DataGrid row style). */
  rowAlt: '#FBF9F3',
  rowHover: '#F6F1E1',
} as const;

export type ColorToken = keyof typeof colors;

/** CSS variable name of a token: `sunDark` → `--rp-sun-dark`. */
export function cssVar(token: ColorToken): string {
  return '--rp-' + token.replace(/[A-Z]/g, (m) => '-' + m.toLowerCase());
}

/** `#RRGGBB` → `R G B`, the form the CSS variables hold so Tailwind can add an alpha. */
export function rgbTriplet(hex: string): string {
  const n = parseInt(hex.slice(1), 16);
  return `${(n >> 16) & 255} ${(n >> 8) & 255} ${n & 255}`;
}

export const radius = {
  /** Panels, cards and dialogs. */
  card: '14px',
  /** Inputs and other controls. */
  control: '10px',
} as const;

export const fonts = {
  heading: '"Century Gothic", "URW Gothic L", "Avant Garde", Futura, "Trebuchet MS", sans-serif',
  body: '"Segoe UI", system-ui, -apple-system, sans-serif',
} as const;

/**
 * The colour pairs the app writes text in: [text, ground]. `tokens.test.ts` measures every one against WCAG AA
 * (4.5:1). Add a pair here before using a new combination for words.
 */
export const textPairs: ReadonlyArray<readonly [ColorToken, ColorToken]> = [
  ['ink', 'card'],
  ['ink', 'cream'],
  ['ink', 'rowAlt'],
  ['ink', 'rowHover'],
  ['ink', 'sun'],
  ['ink', 'sunLight'],
  ['ink', 'stoneLight'],
  ['ink', 'page0'],
  ['ink', 'page45'],
  ['ink', 'page80'],
  ['ink', 'page100'],
  ['muted', 'card'],
  ['muted', 'cream'],
  ['muted', 'rowAlt'],
  ['muted', 'sunLight'],
  ['muted', 'page0'],
  ['muted', 'page45'],
  ['card', 'charcoal'],
  ['card', 'charcoalSoft'],
  ['cream', 'charcoal'],
  ['sunLight', 'charcoal'],
  ['danger', 'card'],
  ['ok', 'card'],
  ['dangerInk', 'dangerLight'],
  ['okInk', 'okLight'],
];

/** WCAG relative luminance of `#RRGGBB`. */
export function luminance(hex: string): number {
  const n = parseInt(hex.slice(1), 16);
  const channel = (v: number) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel((n >> 16) & 255) + 0.7152 * channel((n >> 8) & 255) + 0.0722 * channel(n & 255);
}

/** WCAG contrast ratio of two colours, 1 to 21. */
export function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}
