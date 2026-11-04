/**
 * Sales targets: months to choose from, the share of a target reached, where the month should be by today, and the
 * colour that says whether a store is on its way.
 */
import type { ChipTone } from '@/components/ui/page';
import { MONTHS } from './segments';

/** Actual against target in percent, one decimal; 0 without a target. */
export function percentOf(actual: number, target: number): number {
  if (!(target > 0)) return 0;
  return Math.round((actual / target) * 1000) / 10;
}

/** The share of the month gone by the end of `day`, in percent: where sales should be by now. */
export function expectedPercent(day: number, daysInMonth: number): number {
  if (!(daysInMonth > 0)) return 0;
  return Math.round((Math.min(Math.max(day, 0), daysInMonth) / daysInMonth) * 1000) / 10;
}

/** Reached (ok), on pace (ok), a little behind (warn), well behind (danger). */
export function paceTone(percent: number, expected: number): ChipTone {
  if (percent >= 100 || percent >= expected) return 'ok';
  if (percent >= expected * 0.8) return 'warn';
  return 'danger';
}

/** "November 2026". */
export function monthLabel(year: number, month: number): string {
  return `${MONTHS[month - 1] ?? month} ${year}`;
}

/** A month some months away, across years. */
export function shiftMonth(year: number, month: number, by: number): { year: number; month: number } {
  const index = year * 12 + (month - 1) + by;
  return { year: Math.floor(index / 12), month: (index % 12) + 1 };
}

/** The width of a progress bar, 0 to 100. */
export function barWidth(percent: number): number {
  return Math.max(0, Math.min(100, percent));
}
