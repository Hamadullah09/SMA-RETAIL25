/**
 * Parsing what a cashier typed into the cash field.
 *
 * This exists because `Number(tendered) || due` did not. `Number('abc')` is `NaN`, `NaN` is falsy,
 * and the `||` turned it into the exact amount owed — so typing `abc` rang a fully-settled sale
 * with nothing in the drawer, and nothing anywhere said so. Any falsy result did it: an empty
 * string, whitespace, a stray letter from a barcode scanner.
 *
 * The rule this replaces it with: understand the input, or refuse it. Never substitute.
 *
 * The server enforces the same rules independently (see TenderCalculator) — this is here so the
 * cashier finds out before they hit Pay, not because the browser is trusted.
 */

/** A blank field means "the exact money" — which is what the placeholder has always offered. */
export type TenderParse =
  | { ok: true; exact: true; amount: null }
  | { ok: true; exact: false; amount: number }
  | { ok: false; message: string };

/** Two decimal places is the smallest unit any configured currency here settles to. */
const MAX_DECIMALS = 2;

/**
 * Larger than any single retail tender and far below the point where a double loses integer
 * precision, so a pasted or malicious value is refused rather than silently rounded.
 */
const MAX_TENDER = 100_000_000;

/**
 * Cashiers type what they see on the note, and what they see includes a currency mark and the
 * separators their locale prints. Accepting `Rs 1,500` costs nothing and refusing it teaches
 * people to distrust the field.
 */
const CURRENCY_PREFIX = /^(rs\.?|pkr|₨|\$|usd|£|gbp|€|eur)\s*/i;

export function parseTenderInput(raw: string, amountDue: number): TenderParse {
  const trimmed = (raw ?? '').trim();

  if (trimmed === '') {
    return { ok: true, exact: true, amount: null };
  }

  const withoutCurrency = trimmed.replace(CURRENCY_PREFIX, '').trim();

  if (withoutCurrency === '') {
    return { ok: false, message: 'Enter an amount.' };
  }

  // Thousands separators are stripped only where they sit in a plausible grouping position.
  // Removing every comma unconditionally would silently turn `1,2,3` into `123`, which is the
  // same class of mistake as the bug this file replaces.
  const grouped = /^\d{1,3}(,\d{3})+(\.\d+)?$/.test(withoutCurrency);
  const candidate = grouped ? withoutCurrency.replace(/,/g, '') : withoutCurrency;

  // One optional sign, digits, one optional decimal point with digits. Nothing else — this is what
  // rejects `abc`, `12abc`, `1..2`, `--`, `.`, `-`, `1,2,3`, `NaN` and `Infinity`, all of which
  // `Number()` either accepts or turns into a falsy value.
  if (!/^-?\d+(\.\d+)?$/.test(candidate)) {
    return { ok: false, message: `"${trimmed}" is not an amount.` };
  }

  const decimals = candidate.split('.')[1]?.length ?? 0;
  if (decimals > MAX_DECIMALS) {
    return { ok: false, message: `Amounts go to ${MAX_DECIMALS} decimal places.` };
  }

  const value = Number(candidate);

  // Belt and braces. The pattern above should make these unreachable; if a future edit widens it,
  // the money still does not move.
  if (!Number.isFinite(value)) {
    return { ok: false, message: `"${trimmed}" is not an amount.` };
  }

  if (value < 0) {
    return { ok: false, message: 'An amount cannot be negative.' };
  }

  if (value === 0) {
    return amountDue === 0
      ? { ok: true, exact: false, amount: 0 }
      : { ok: false, message: 'Enter the amount being paid.' };
  }

  if (value > MAX_TENDER) {
    return { ok: false, message: 'That amount is too large.' };
  }

  if (value < amountDue) {
    return { ok: false, message: `Short by ${(amountDue - value).toFixed(2)}.` };
  }

  return { ok: true, exact: false, amount: value };
}
