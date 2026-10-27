/**
 * Business dates (`yyyy-MM-dd`) and the date range presets. "Today" is the browser's date: the server's own default
 * is today in the store's time zone, and the two only differ for someone watching a store in another time zone around
 * midnight. Weeks start on Monday, as in the desktop's "Hours worked by week".
 */

export type DatePreset = 'today' | 'yesterday' | 'week' | 'month' | 'last30' | 'custom';

export interface DateRange {
  from: string;
  to: string;
}

export const DATE_PRESETS: ReadonlyArray<{ key: DatePreset; label: string }> = [
  { key: 'today', label: 'Today' },
  { key: 'yesterday', label: 'Yesterday' },
  { key: 'week', label: 'This week' },
  { key: 'month', label: 'This month' },
  { key: 'last30', label: 'Last 30 days' },
  { key: 'custom', label: 'Custom' },
];

/** `yyyy-MM-dd` of a local Date. */
export function toBusinessDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/** The local Date at midnight of `yyyy-MM-dd`. */
export function fromBusinessDate(value: string): Date {
  const [y, m, d] = value.split('-').map(Number);
  return new Date(y, (m || 1) - 1, d || 1);
}

/** `days` later (or earlier) than a business date, across months and daylight-saving changes. */
export function addDays(value: string, days: number): string {
  const date = fromBusinessDate(value);
  date.setDate(date.getDate() + days);
  return toBusinessDate(date);
}

/** Days from `from` to `to`, both counted (a single day is 1). */
export function daysInRange(range: DateRange): number {
  const ms = fromBusinessDate(range.to).getTime() - fromBusinessDate(range.from).getTime();
  return Math.round(ms / 86_400_000) + 1;
}

/** Monday of the week `value` is in. */
export function startOfWeek(value: string): string {
  const date = fromBusinessDate(value);
  const sinceMonday = (date.getDay() + 6) % 7;
  return addDays(value, -sinceMonday);
}

/** The range of a preset, as of `now`. `custom` keeps `current` (or today when there is none). */
export function presetRange(preset: DatePreset, now: Date = new Date(), current?: DateRange): DateRange {
  const today = toBusinessDate(now);
  switch (preset) {
    case 'today':
      return { from: today, to: today };
    case 'yesterday': {
      const yesterday = addDays(today, -1);
      return { from: yesterday, to: yesterday };
    }
    case 'week':
      return { from: startOfWeek(today), to: today };
    case 'month':
      return { from: today.slice(0, 8) + '01', to: today };
    case 'last30':
      return { from: addDays(today, -29), to: today };
    case 'custom':
      return current ?? { from: today, to: today };
  }
}

/** Which preset a range is, as of `now`; `custom` when none matches. */
export function presetOf(range: DateRange, now: Date = new Date()): DatePreset {
  for (const { key } of DATE_PRESETS) {
    if (key === 'custom') continue;
    const r = presetRange(key, now);
    if (r.from === range.from && r.to === range.to) return key;
  }
  return 'custom';
}

/** Whether a string is a real `yyyy-MM-dd` date. */
export function isBusinessDate(value: string | null | undefined): value is string {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  return toBusinessDate(fromBusinessDate(value)) === value;
}

/** A valid range from user input: both dates real, `from` not after `to` (they swap). */
export function normalizeRange(from: string, to: string, now: Date = new Date()): DateRange {
  const today = toBusinessDate(now);
  const a = isBusinessDate(from) ? from : today;
  const b = isBusinessDate(to) ? to : a;
  return a <= b ? { from: a, to: b } : { from: b, to: a };
}

/** The range of the same length just before `range` (what the dashboard compares with). */
export function previousRange(range: DateRange): DateRange {
  const days = daysInRange(range);
  return { from: addDays(range.from, -days), to: addDays(range.from, -1) };
}
