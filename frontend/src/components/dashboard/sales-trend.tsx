'use client';

import {
  Bar,
  BarChart,
  Cell,
  CartesianGrid,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { formatCurrency } from '@/lib/utils';
import type { SalesAnalysisRow } from '@/types/masters';

/**
 * Fourteen days of takings, one bar a day.
 *
 * Bars rather than an area, because a day's takings is a quantity and not a reading on the way to
 * somewhere else. A filled curve draws a value for two o'clock on Tuesday that nobody measured, and
 * on a fortnight with two trading days in it that invention is the whole picture — it came out as a
 * diagonal line between the only two points, which is a shape, not information. Bars sit apart, are
 * compared by height directly, and a shop that was shut simply has no bar.
 *
 * The dashed line is the daily average across the period. It is the question the eye is already
 * asking of a row of bars — was that a good day — answered without arithmetic.
 *
 * Colours come from the token layer rather than literals, so the chart follows the theme toggle
 * instead of staying stubbornly light when everything around it goes dark.
 */
export function SalesTrend({ rows, todayKey }: { rows: SalesAnalysisRow[]; todayKey?: string }) {
  if (rows.length === 0) {
    return (
      <p className="flex h-full min-h-[12rem] items-center justify-center text-body text-ink-muted">
        No sales in this period yet.
      </p>
    );
  }

  const data = rows.map((row) => ({
    key: row.groupKey,
    label: row.groupLabel,
    net: row.netSales,
    transactions: row.transactionCount,
  }));

  const takings = data.filter((d) => d.net > 0);
  const average = takings.length > 0 ? takings.reduce((sum, d) => sum + d.net, 0) / takings.length : 0;

  return (
    <div className="h-full min-h-[12rem] w-full text-ink-faint">
      <ResponsiveContainer width="100%" height="100%" minHeight={192}>
        <BarChart data={data} margin={{ top: 8, right: 4, bottom: 0, left: 4 }} barCategoryGap="22%">
          {/* Horizontal only. Vertical gridlines on a time axis add ink without adding meaning. */}
          <CartesianGrid vertical={false} stroke="rgb(var(--border))" strokeDasharray="2 4" />

          <XAxis
            dataKey="label"
            tick={{ fontSize: 11, fill: 'currentColor' }}
            tickLine={false}
            axisLine={{ stroke: 'rgb(var(--border))' }}
            minTickGap={12}
          />

          <YAxis
            tick={{ fontSize: 11, fill: 'currentColor' }}
            tickLine={false}
            axisLine={false}
            width={52}
            tickFormatter={(value: number) => compact(value)}
          />

          <Tooltip
            // A block the width of the bar, not a crosshair: it says which day is being read.
            cursor={{ fill: 'rgb(var(--panel-hover))' }}
            contentStyle={{
              background: 'rgb(var(--panel))',
              border: '1px solid rgb(var(--border))',
              borderRadius: 10,
              boxShadow: 'var(--shadow-2)',
              fontSize: 13,
              color: 'rgb(var(--text))',
            }}
            labelStyle={{ color: 'rgb(var(--text-muted))', marginBottom: 2 }}
            formatter={(value: number, name) =>
              name === 'net' ? [formatCurrency(value), 'Net sales'] : [value, 'Transactions']
            }
          />

          {average > 0 ? (
            <ReferenceLine
              y={average}
              stroke="rgb(var(--border-strong))"
              strokeDasharray="4 4"
              ifOverflow="extendDomain"
              label={{
                value: `avg ${compact(average)}`,
                position: 'insideTopRight',
                fill: 'currentColor',
                fontSize: 10,
              }}
            />
          ) : null}

          <Bar dataKey="net" radius={[4, 4, 0, 0]} maxBarSize={44}>
            {data.map((entry) => (
              <Cell
                key={entry.key}
                // Today is the bar being asked about; the rest are context for it.
                fill={
                  todayKey && entry.key === todayKey
                    ? 'oklch(var(--accent))'
                    : 'oklch(var(--accent) / 0.32)'
                }
              />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

/** Axis labels only. Full figures belong in the tooltip and the tiles, where precision matters. */
function compact(value: number): string {
  if (Math.abs(value) >= 1_000_000) return `${(value / 1_000_000).toFixed(1)}M`;
  if (Math.abs(value) >= 1_000) return `${(value / 1_000).toFixed(0)}k`;
  return String(Math.round(value));
}
