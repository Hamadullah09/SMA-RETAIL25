import { describe, expect, it } from 'vitest';
import {
  formatAgo,
  formatChange,
  formatCount,
  formatDate,
  formatHour,
  formatHours,
  formatLocalDateTime,
  formatMoney,
  formatPercent,
  formatQuantity,
  formatShortDateTime,
  formatUtc,
  initials,
  parseBusinessDate,
  parseLocalDateTime,
  percentChange,
} from './format';

const en = 'en-US';

describe('money', () => {
  it('uses the store currency symbol and two decimals', () => {
    expect(formatMoney(1234.5, '$', en)).toBe('$1,234.50');
    expect(formatMoney(0, '€', en)).toBe('€0.00');
    expect(formatMoney(7, '', en)).toBe('7.00');
  });

  it('puts the minus sign first and never shows -0.00', () => {
    expect(formatMoney(-3, '$', en)).toBe('-$3.00');
    expect(formatMoney(-0.001, '$', en)).toBe('$0.00');
  });

  it('rounds to cents', () => {
    expect(formatMoney(2.005 + 0.0001, '', en)).toBe('2.01');
    expect(formatMoney(0.1 + 0.2, '', en)).toBe('0.30');
  });

  it('shows nothing for missing values', () => {
    expect(formatMoney(null, '$', en)).toBe('');
    expect(formatMoney(undefined, '$', en)).toBe('');
    expect(formatMoney(Number.NaN, '$', en)).toBe('');
  });

  it("follows the locale's separators", () => {
    expect(formatMoney(1234.5, '', 'de-DE')).toBe('1.234,50');
  });
});

describe('quantities, counts and percentages', () => {
  it('shows quantities with up to three decimals', () => {
    expect(formatQuantity(12, en)).toBe('12');
    expect(formatQuantity(1.5, en)).toBe('1.5');
    expect(formatQuantity(0.1255, en)).toBe('0.126');
    expect(formatQuantity(1500, en)).toBe('1,500');
  });

  it('shows counts as whole numbers', () => {
    expect(formatCount(1234, en)).toBe('1,234');
    expect(formatCount(2.6, en)).toBe('3');
  });

  it('shows percentages with one decimal', () => {
    expect(formatPercent(12.345, en)).toBe('12.3%');
    expect(formatPercent(0, en)).toBe('0.0%');
  });

  it('shows hours with two decimals', () => {
    expect(formatHours(7.5, en)).toBe('7.50');
  });
});

describe('change against the previous period', () => {
  it('works out the percentage', () => {
    expect(percentChange(110, 100)).toBeCloseTo(10);
    expect(percentChange(50, 100)).toBeCloseTo(-50);
    expect(percentChange(10, 0)).toBeNull();
  });

  it('formats it with a sign', () => {
    expect(formatChange(110, 100, en)).toBe('+10.0%');
    expect(formatChange(90, 100, en)).toBe('-10.0%');
    expect(formatChange(100, 100, en)).toBe('0.0%');
    expect(formatChange(5, 0, en)).toBe('New');
    expect(formatChange(0, 0, en)).toBe('—');
  });
});

describe('dates', () => {
  it('reads business dates as local days, not UTC midnight', () => {
    const date = parseBusinessDate('2026-10-28')!;
    expect(date.getFullYear()).toBe(2026);
    expect(date.getMonth()).toBe(9);
    expect(date.getDate()).toBe(28);
    expect(date.getHours()).toBe(0);
  });

  it('reads store-local date-times without moving them', () => {
    const date = parseLocalDateTime('2026-10-28T13:05:00')!;
    expect(date.getHours()).toBe(13);
    expect(date.getMinutes()).toBe(5);
  });

  it('formats business dates and local date-times', () => {
    expect(formatDate('2026-10-28', en)).toBe('Oct 28, 2026');
    expect(formatLocalDateTime('2026-10-28T13:05:00', en)).toMatch(/^Oct 28, 2026,? 1:05\sPM$/);
    expect(formatDate(null, en)).toBe('');
  });

  it('shortens date-times in tables', () => {
    const now = new Date(2026, 9, 28);
    expect(formatShortDateTime('2026-10-28T13:05:00', en, now)).toMatch(/^Oct 28,? 1:05\sPM$/);
    expect(formatShortDateTime('2025-12-31T09:00:00', en, now)).toMatch(/^Dec 31, 2025,? 9:00\sAM$/);
    expect(formatShortDateTime(null, en, now)).toBe('');
  });

  it('formats UTC times in a given time zone', () => {
    expect(formatUtc('2026-10-28T12:00:00Z', en, 'UTC')).toMatch(/^Oct 28, 2026,? 12:00\sPM$/);
    expect(formatUtc('2026-10-28T12:00:00Z', en, 'America/Toronto')).toMatch(/^Oct 28, 2026,? 8:00\sAM$/);
    // A UTC field that comes without its Z is still UTC.
    expect(formatUtc('2026-10-28T12:00:00', en, 'UTC')).toMatch(/^Oct 28, 2026,? 12:00\sPM$/);
    expect(formatUtc('2026-10-28T12:00:00.123+00:00', en, 'UTC')).toMatch(/^Oct 28, 2026,? 12:00\sPM$/);
  });

  it('says how long ago something happened', () => {
    const now = new Date('2026-10-28T12:00:00Z');
    expect(formatAgo('2026-10-28T11:55:00Z', now, en)).toBe('5 minutes ago');
    expect(formatAgo('2026-10-28T09:00:00Z', now, en)).toBe('3 hours ago');
    expect(formatAgo('2026-10-27T12:00:00Z', now, en)).toBe('yesterday');
    expect(formatAgo('2026-10-28T11:59:50Z', now, en)).toBe('now');
    expect(formatAgo(null, now, en)).toBe('Never');
  });

  it('writes hours of the day as 24-hour times', () => {
    expect(formatHour(0)).toBe('00:00');
    expect(formatHour(13)).toBe('13:00');
  });
});

describe('initials', () => {
  it('takes the first letter, as the desktop avatar does', () => {
    expect(initials('admin')).toBe('A');
    expect(initials('  maria lopez')).toBe('M');
    expect(initials('')).toBe('?');
    expect(initials(null)).toBe('?');
  });
});
