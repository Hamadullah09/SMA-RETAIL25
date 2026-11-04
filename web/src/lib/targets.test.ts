import { describe, expect, it } from 'vitest';
import { barWidth, expectedPercent, monthLabel, paceTone, percentOf, shiftMonth } from './targets';

describe('sales targets', () => {
  it('works out the share reached', () => {
    expect(percentOf(7500, 10000)).toBe(75);
    expect(percentOf(1, 3)).toBe(33.3);
    expect(percentOf(500, 0)).toBe(0);
  });

  it('says where the month should be by today', () => {
    expect(expectedPercent(15, 30)).toBe(50);
    expect(expectedPercent(31, 30)).toBe(100);
    expect(expectedPercent(3, 0)).toBe(0);
  });

  it('colours the pace', () => {
    expect(paceTone(55, 50)).toBe('ok');
    expect(paceTone(45, 50)).toBe('warn');
    expect(paceTone(20, 50)).toBe('danger');
    expect(paceTone(100, 100)).toBe('ok');
  });

  it('moves between months across years', () => {
    expect(shiftMonth(2026, 12, 1)).toEqual({ year: 2027, month: 1 });
    expect(shiftMonth(2026, 1, -1)).toEqual({ year: 2025, month: 12 });
    expect(shiftMonth(2026, 11, 0)).toEqual({ year: 2026, month: 11 });
    expect(monthLabel(2026, 11)).toBe('November 2026');
  });

  it('keeps bars inside their track', () => {
    expect(barWidth(140)).toBe(100);
    expect(barWidth(-3)).toBe(0);
    expect(barWidth(42.5)).toBe(42.5);
  });
});
