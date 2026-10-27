'use client';

import { useCallback, useState } from 'react';
import { DATE_PRESETS, normalizeRange, presetOf, presetRange, type DatePreset, type DateRange } from '@/lib/dates';
import { DateInput } from './field';
import { Tabs } from './tabs';

export interface DateRangeState {
  preset: DatePreset;
  range: DateRange;
  setPreset(preset: DatePreset): void;
  setRange(range: DateRange): void;
}

/** A date range with its preset; typing a date makes it custom. `start` (e.g. from the address) wins over the preset. */
export function useDateRange(initial: DatePreset = 'today', start?: DateRange | null): DateRangeState {
  const [preset, setPresetState] = useState<DatePreset>(() => (start ? presetOf(start) : initial));
  const [range, setRangeState] = useState<DateRange>(() => start ?? presetRange(initial));

  const setPreset = useCallback((next: DatePreset) => {
    setPresetState(next);
    setRangeState((current) => presetRange(next, new Date(), current));
  }, []);

  const setRange = useCallback((next: DateRange) => {
    setPresetState('custom');
    setRangeState(normalizeRange(next.from, next.to));
  }, []);

  return { preset, range, setPreset, setRange };
}

/**
 * The preset pills (Today, Yesterday, This week, This month, Last 30 days, Custom) and, for Custom, the From and To
 * boxes. `compact` shows a dropdown and the two boxes instead, for toolbars.
 */
export function DateRangeBar({ state, compact }: { state: DateRangeState; compact?: boolean }) {
  const { preset, range, setPreset, setRange } = state;
  const boxes = (
    <div className="flex flex-wrap items-center gap-2.5">
      <DateInput label="From" value={range.from} onChange={(from) => from && setRange({ from, to: range.to })} />
      <DateInput label="To" value={range.to} onChange={(to) => to && setRange({ from: range.from, to })} />
    </div>
  );

  if (compact) {
    return (
      <div className="flex flex-wrap items-center gap-2.5">
        <label className="sr-only" htmlFor="date-preset">
          Dates
        </label>
        <select id="date-preset" className="input w-[170px]" value={preset} onChange={(e) => setPreset(e.target.value as DatePreset)}>
          {DATE_PRESETS.map((p) => (
            <option key={p.key} value={p.key}>
              {p.label}
            </option>
          ))}
        </select>
        {boxes}
      </div>
    );
  }

  return (
    <div className="flex flex-wrap items-center gap-3">
      <Tabs label="Dates" tabs={DATE_PRESETS} value={preset} onChange={setPreset} className="max-md:hidden" />
      <div className="md:hidden">
        <label className="sr-only" htmlFor="date-preset-phone">
          Dates
        </label>
        <select id="date-preset-phone" className="input w-[200px]" value={preset} onChange={(e) => setPreset(e.target.value as DatePreset)}>
          {DATE_PRESETS.map((p) => (
            <option key={p.key} value={p.key}>
              {p.label}
            </option>
          ))}
        </select>
      </div>
      {preset === 'custom' && boxes}
    </div>
  );
}
