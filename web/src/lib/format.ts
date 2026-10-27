/**
 * Numbers and dates for people, with `Intl`. Money gets the store's currency symbol (from its settings) in front, as
 * the desktop prints it: `$1,234.50`, `-$3.00`. `locale` is the browser's unless a test passes one.
 */

type Locale = string | undefined;

const numberFormats = new Map<string, Intl.NumberFormat>();

function numberFormat(locale: Locale, minimum: number, maximum: number): Intl.NumberFormat {
  const key = `${locale ?? ''}|${minimum}|${maximum}`;
  let format = numberFormats.get(key);
  if (!format) {
    format = new Intl.NumberFormat(locale, { minimumFractionDigits: minimum, maximumFractionDigits: maximum });
    numberFormats.set(key, format);
  }
  return format;
}

function finite(value: number | null | undefined): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

/** `1,234.50` with the currency symbol in front, or `` for nothing. A negative amount keeps its sign first. */
export function formatMoney(value: number | null | undefined, symbol = '', locale?: Locale): string {
  const n = finite(value);
  if (n === null) return '';
  // -0.00 reads as 0.00.
  const rounded = Math.round(n * 100) / 100 || 0;
  const text = numberFormat(locale, 2, 2).format(Math.abs(rounded));
  return (rounded < 0 ? '-' : '') + symbol + text;
}

/** A quantity: up to three decimals, none when whole (`12`, `1.5`, `0.125`). */
export function formatQuantity(value: number | null | undefined, locale?: Locale): string {
  const n = finite(value);
  return n === null ? '' : numberFormat(locale, 0, 3).format(n || 0);
}

/** A count: a whole number with group separators. */
export function formatCount(value: number | null | undefined, locale?: Locale): string {
  const n = finite(value);
  return n === null ? '' : numberFormat(locale, 0, 0).format(Math.round(n) || 0);
}

/** A percentage already in percent (12.5 → `12.5%`), one decimal as the desktop reports print it. */
export function formatPercent(value: number | null | undefined, locale?: Locale, decimals = 1): string {
  const n = finite(value);
  return n === null ? '' : numberFormat(locale, decimals, decimals).format(n || 0) + '%';
}

/** Hours with two decimals (`7.50`). */
export function formatHours(value: number | null | undefined, locale?: Locale): string {
  const n = finite(value);
  return n === null ? '' : numberFormat(locale, 2, 2).format(n || 0);
}

/**
 * The change from `previous` to `current` in percent, or null when there was nothing before (a change from zero has
 * no percentage).
 */
export function percentChange(current: number, previous: number): number | null {
  if (!Number.isFinite(current) || !Number.isFinite(previous) || previous === 0) return null;
  return ((current - previous) / Math.abs(previous)) * 100;
}

/** `+12.5%`, `-3.0%`, or `New` when there was nothing before and `—` when both are zero. */
export function formatChange(current: number, previous: number, locale?: Locale): string {
  const change = percentChange(current, previous);
  if (change === null) return current === 0 ? '—' : 'New';
  const rounded = Math.round(change * 10) / 10 || 0;
  return (rounded > 0 ? '+' : '') + formatPercent(rounded, locale);
}

// Dates -----------------------------------------------------------------------------------------------------------

/**
 * A business date `yyyy-MM-dd` as a local Date at midnight. `new Date('2026-10-28')` would be midnight UTC, which is
 * the day before west of Greenwich.
 */
export function parseBusinessDate(value: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (!match) return null;
  return new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
}

/** A store-local date-time without an offset (`2026-10-28T13:05:00`) as a local Date. */
export function parseLocalDateTime(value: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2}))?)?/.exec(value);
  if (!match) return null;
  return new Date(
    Number(match[1]),
    Number(match[2]) - 1,
    Number(match[3]),
    Number(match[4] ?? 0),
    Number(match[5] ?? 0),
    Number(match[6] ?? 0),
  );
}

/** A UTC date-time (`…Z`) as a Date. */
export function parseUtc(value: string): Date | null {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

const dateFormats = new Map<string, Intl.DateTimeFormat>();

function dateFormat(locale: Locale, options: Intl.DateTimeFormatOptions, timeZone?: string): Intl.DateTimeFormat {
  const key = `${locale ?? ''}|${JSON.stringify(options)}|${timeZone ?? ''}`;
  let format = dateFormats.get(key);
  if (!format) {
    format = new Intl.DateTimeFormat(locale, { ...options, timeZone });
    dateFormats.set(key, format);
  }
  return format;
}

/** `28 Oct 2026` (or the locale's way) for a business date. */
export function formatDate(value: string | null | undefined, locale?: Locale): string {
  if (!value) return '';
  const date = parseBusinessDate(value);
  return date ? dateFormat(locale, { dateStyle: 'medium' }).format(date) : value;
}

/** `28 Oct 2026, 13:05` for a store-local date-time. */
export function formatLocalDateTime(value: string | null | undefined, locale?: Locale): string {
  if (!value) return '';
  const date = parseLocalDateTime(value);
  return date ? dateFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(date) : value;
}

/** `28 Oct 2026, 13:05` for a UTC date-time, shown in the browser's time zone (or `timeZone`). */
export function formatUtc(value: string | null | undefined, locale?: Locale, timeZone?: string): string {
  if (!value) return '';
  const date = parseUtc(value);
  return date ? dateFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }, timeZone).format(date) : value;
}

/** `Oct 28, 1:05 PM` for a store-local date-time in a table: no year when it is this year. */
export function formatShortDateTime(value: string | null | undefined, locale?: Locale, now: Date = new Date()): string {
  if (!value) return '';
  const date = parseLocalDateTime(value);
  if (!date) return value;
  const options: Intl.DateTimeFormatOptions =
    date.getFullYear() === now.getFullYear()
      ? { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }
      : { year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' };
  return dateFormat(locale, options).format(date);
}

/** `Oct 28, 1:05 PM` for a UTC date-time in a table, in the browser's time zone. */
export function formatShortUtc(value: string | null | undefined, locale?: Locale, now: Date = new Date()): string {
  if (!value) return '';
  const date = parseUtc(value);
  if (!date) return value;
  const options: Intl.DateTimeFormatOptions =
    date.getFullYear() === now.getFullYear()
      ? { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }
      : { year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' };
  return dateFormat(locale, options).format(date);
}

/** `13:05` for a store-local date-time. */
export function formatTime(value: string | null | undefined, locale?: Locale): string {
  if (!value) return '';
  const date = parseLocalDateTime(value);
  return date ? dateFormat(locale, { timeStyle: 'short' }).format(date) : value;
}

/** `5 minutes ago`, `in 2 hours`, `yesterday`… between `then` and `now`. */
export function formatRelative(then: Date | null, now: Date = new Date(), locale?: Locale): string {
  if (!then) return '';
  const seconds = Math.round((then.getTime() - now.getTime()) / 1000);
  const format = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
  const abs = Math.abs(seconds);
  if (abs < 45) return format.format(0, 'second');
  if (abs < 45 * 60) return format.format(Math.round(seconds / 60), 'minute');
  if (abs < 22 * 3600) return format.format(Math.round(seconds / 3600), 'hour');
  if (abs < 26 * 86400) return format.format(Math.round(seconds / 86400), 'day');
  if (abs < 320 * 86400) return format.format(Math.round(seconds / (30 * 86400)), 'month');
  return format.format(Math.round(seconds / (365 * 86400)), 'year');
}

/** `5 minutes ago` for a UTC date-time, or `Never`. */
export function formatAgo(value: string | null | undefined, now: Date = new Date(), locale?: Locale): string {
  if (!value) return 'Never';
  return formatRelative(parseUtc(value), now, locale);
}

/** `13:00` for an hour of the day (0 to 23). */
export function formatHour(hour: number): string {
  return String(hour).padStart(2, '0') + ':00';
}

/** The first letter of a name, for the avatar circle (as the desktop shows it). */
export function initials(name: string | null | undefined): string {
  const text = (name ?? '').trim();
  return text ? text[0].toUpperCase() : '?';
}
