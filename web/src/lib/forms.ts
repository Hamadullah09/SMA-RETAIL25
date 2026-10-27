/**
 * Small helpers for the editors: numbers are typed as text and checked here, so a half-typed "12." is not lost while
 * the user types.
 */

/** A number from a box, or null when it is not one. Accepts a comma as the decimal mark. */
export function parseNumber(text: string): number | null {
  const clean = text.trim().replace(/\s/g, '').replace(',', '.');
  if (clean === '') return null;
  if (!/^-?\d*\.?\d*$/.test(clean) || clean === '-' || clean === '.') return null;
  const n = Number(clean);
  return Number.isFinite(n) ? n : null;
}

/** A number as a box shows it (no grouping, the decimals it has). */
export function numberText(value: number | null | undefined): string {
  return value === null || value === undefined || !Number.isFinite(value) ? '' : String(value);
}

/** Empty strings become null, other text is trimmed. */
export function textOrNull(text: string | null | undefined): string | null {
  const t = (text ?? '').trim();
  return t === '' ? null : t;
}

export type Errors<K extends string> = Partial<Record<K, string>>;

/** Checks a money or quantity box: required or not, not negative, at most `decimals` decimals. */
export function checkNumber(
  text: string,
  { required = false, min = 0, decimals = 2, label = 'This' }: { required?: boolean; min?: number | null; decimals?: number; label?: string } = {},
): string | undefined {
  if (text.trim() === '') return required ? `${label} is needed.` : undefined;
  const n = parseNumber(text);
  if (n === null) return `${label} is not a number.`;
  if (min !== null && n < min) return min === 0 ? `${label} cannot be negative.` : `${label} is at least ${min}.`;
  const fraction = text.trim().replace(',', '.').split('.')[1] ?? '';
  if (fraction.length > decimals) return `${label} has at most ${decimals} decimals.`;
  return undefined;
}

/** A simple e-mail check: something@something.something. */
export function checkEmail(text: string): string | undefined {
  const t = text.trim();
  if (!t) return undefined;
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(t) ? undefined : 'This is not an e-mail address.';
}
