import { describe, expect, it } from 'vitest';
import {
  addDays,
  daysInRange,
  isBusinessDate,
  normalizeRange,
  presetOf,
  presetRange,
  previousRange,
  startOfWeek,
  toBusinessDate,
} from './dates';

// Wednesday 28 October 2026, mid-afternoon local time.
const now = new Date(2026, 9, 28, 15, 30);

describe('date presets', () => {
  it('today and yesterday are single days', () => {
    expect(presetRange('today', now)).toEqual({ from: '2026-10-28', to: '2026-10-28' });
    expect(presetRange('yesterday', now)).toEqual({ from: '2026-10-27', to: '2026-10-27' });
  });

  it('this week starts on Monday', () => {
    expect(presetRange('week', now)).toEqual({ from: '2026-10-26', to: '2026-10-28' });
    // On a Sunday the week began six days earlier.
    expect(presetRange('week', new Date(2026, 10, 1))).toEqual({ from: '2026-10-26', to: '2026-11-01' });
    // On a Monday it is just today.
    expect(presetRange('week', new Date(2026, 9, 26))).toEqual({ from: '2026-10-26', to: '2026-10-26' });
  });

  it('this month starts on the first', () => {
    expect(presetRange('month', now)).toEqual({ from: '2026-10-01', to: '2026-10-28' });
  });

  it('last 30 days counts today', () => {
    const range = presetRange('last30', now);
    expect(range).toEqual({ from: '2026-09-29', to: '2026-10-28' });
    expect(daysInRange(range)).toBe(30);
  });

  it('custom keeps the range the user typed', () => {
    const typed = { from: '2026-01-01', to: '2026-01-31' };
    expect(presetRange('custom', now, typed)).toEqual(typed);
    expect(presetRange('custom', now)).toEqual({ from: '2026-10-28', to: '2026-10-28' });
  });

  it('recognises which preset a range is', () => {
    expect(presetOf({ from: '2026-10-28', to: '2026-10-28' }, now)).toBe('today');
    expect(presetOf({ from: '2026-10-01', to: '2026-10-28' }, now)).toBe('month');
    expect(presetOf({ from: '2026-10-02', to: '2026-10-28' }, now)).toBe('custom');
  });

  it('crosses month and year ends', () => {
    expect(presetRange('yesterday', new Date(2026, 0, 1))).toEqual({ from: '2025-12-31', to: '2025-12-31' });
    expect(presetRange('last30', new Date(2026, 2, 1))).toEqual({ from: '2026-01-31', to: '2026-03-01' });
  });
});

describe('business date helpers', () => {
  it('formats local dates as yyyy-MM-dd', () => {
    expect(toBusinessDate(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05');
  });

  it('adds days across daylight-saving changes', () => {
    expect(addDays('2026-03-07', 2)).toBe('2026-03-09');
    expect(addDays('2026-10-31', 2)).toBe('2026-11-02');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
  });

  it('finds the Monday of a week', () => {
    expect(startOfWeek('2026-10-28')).toBe('2026-10-26');
    expect(startOfWeek('2026-11-01')).toBe('2026-10-26');
  });

  it('checks dates', () => {
    expect(isBusinessDate('2026-02-28')).toBe(true);
    expect(isBusinessDate('2026-02-30')).toBe(false);
    expect(isBusinessDate('28/10/2026')).toBe(false);
    expect(isBusinessDate('')).toBe(false);
  });

  it('puts from before to', () => {
    expect(normalizeRange('2026-10-28', '2026-10-01', now)).toEqual({ from: '2026-10-01', to: '2026-10-28' });
    expect(normalizeRange('bad', '', now)).toEqual({ from: '2026-10-28', to: '2026-10-28' });
  });

  it('the previous period has the same length, just before', () => {
    expect(previousRange({ from: '2026-10-28', to: '2026-10-28' })).toEqual({ from: '2026-10-27', to: '2026-10-27' });
    expect(previousRange({ from: '2026-10-01', to: '2026-10-28' })).toEqual({ from: '2026-09-03', to: '2026-09-30' });
  });
});
