'use client';

import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { formatCurrency } from '@/lib/utils';
import type { SalesAnalysisRow } from '@/types/masters';

/**
 * Fourteen days of takings.
 *
 * An area rather than a line: the shape of a trading fortnight is what a manager reads here — which
 * days are quiet, whether last weekend beat the one before — and a filled area carries that at a
 * glance where a hairline does not.
 *
 * Colours come from the token layer rather than literals, so the chart follows the theme toggle
 * instead of staying stubbornly light when everything around it goes dark. `currentColor` on the
 * axes does the same job for text.
 */
export function SalesTrend({ rows }: { rows: SalesAnalysisRow[] }) {
  if (rows.length === 0) {
    return (
      <p className="flex h-full min-h-[12rem] items-center justify-center text-body text-ink-muted">
        No sales in this period yet.
      </p>
    );
  }

  const data = rows.map((row) => ({
    label: row.groupLabel,
    net: row.netSales,
    transactions: row.transactionCount,
  }));

  return (
    <div className="h-full min-h-[12rem] w-full text-ink-faint">
      <ResponsiveContainer width="100%" height="100%" minHeight={192}>
        <AreaChart data={data} margin={{ top: 4, right: 4, bottom: 0, left: 4 }}>
          <defs>
            <linearGradient id="salesFill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="oklch(var(--accent))" stopOpacity={0.35} />
              <stop offset="100%" stopColor="oklch(var(--accent))" stopOpacity={0.02} />
            </linearGradient>
          </defs>

          {/* Horizontal only. Vertical gridlines on a time axis add ink without adding meaning. */}
          <CartesianGrid vertical={false} stroke="rgb(var(--border))" />

          <XAxis
            dataKey="label"
            tick={{ fontSize: 11, fill: 'currentColor' }}
            tickLine={false}
            axisLine={{ stroke: 'rgb(var(--border))' }}
            minTickGap={16}
          />

          <YAxis
            tick={{ fontSize: 11, fill: 'currentColor' }}
            tickLine={false}
            axisLine={false}
            width={56}
            tickFormatter={(value: number) => compact(value)}
          />

          <Tooltip
            cursor={{ stroke: 'rgb(var(--border-strong))' }}
            contentStyle={{
              background: 'rgb(var(--panel))',
              border: '1px solid rgb(var(--border))',
              borderRadius: 4,
              fontSize: 13,
              color: 'rgb(var(--text))',
            }}
            labelStyle={{ color: 'rgb(var(--text-muted))' }}
            formatter={(value: number, name) =>
              name === 'net' ? [formatCurrency(value), 'Net sales'] : [value, 'Transactions']
            }
          />

          <Area
            type="monotone"
            dataKey="net"
            stroke="oklch(var(--accent))"
            strokeWidth={2}
            fill="url(#salesFill)"
          />
        </AreaChart>
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
