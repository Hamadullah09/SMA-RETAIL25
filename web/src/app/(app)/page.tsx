'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ArrowDownRight, ArrowUpRight, Minus } from 'lucide-react';
import Link from 'next/link';
import type { ReactNode } from 'react';
import { Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis, type TooltipProps } from 'recharts';
import { Card } from '@/components/ui/card';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Chip } from '@/components/ui/page';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states';
import { DataTable, type Column } from '@/components/ui/table';
import { api } from '@/lib/api';
import { cx } from '@/lib/cx';
import { DATE_PRESETS } from '@/lib/dates';
import {
  formatAgo,
  formatChange,
  formatCount,
  formatDate,
  formatHour,
  formatShortDateTime,
  formatMoney,
  formatQuantity,
  parseBusinessDate,
  percentChange,
} from '@/lib/format';
import { useNow } from '@/lib/hooks';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import { colors } from '@/lib/tokens';
import type { Dashboard } from '@/lib/types';

function greeting(now: Date | null): string {
  if (!now) return 'Hello';
  const hour = now.getHours();
  return hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
}

export default function DashboardPage() {
  const { user } = useSession();
  const { storeId, store } = useStore();
  const dates = useDateRange('today');
  const now = useNow(60_000);
  const query = useQuery({
    queryKey: ['dashboard', storeId, dates.range.from, dates.range.to],
    queryFn: ({ signal }) => api.dashboard({ storeId, from: dates.range.from, to: dates.range.to }, signal),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  });
  const data = query.data;
  const name = (user.fullName || user.userName).split(/\s+/)[0];
  const presetLabel = DATE_PRESETS.find((p) => p.key === dates.preset)?.label.toLowerCase();
  const period =
    dates.range.from === dates.range.to ? formatDate(dates.range.from) : `${formatDate(dates.range.from)} to ${formatDate(dates.range.to)}`;

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="min-w-0">
          <h1 className="truncate font-heading text-big text-ink max-sm:text-[34px] max-sm:leading-[42px]" suppressHydrationWarning>
            {greeting(now)}, {name}
          </h1>
          <p className="text-muted">
            {store ? `${store.name}: ` : 'All stores: '}
            {dates.preset === 'custom' ? period : `${presetLabel}, ${period}`}
          </p>
        </div>
      </header>

      <div className="no-print">
        <DateRangeBar state={dates} />
      </div>

      {query.error && !data ? (
        <div className="card">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      ) : (
        <DashboardBody data={data} loading={query.isLoading} stale={query.isPlaceholderData} now={now} />
      )}
    </div>
  );
}

function DashboardBody({ data, loading, stale, now }: { data: Dashboard | undefined; loading: boolean; stale: boolean; now: Date | null }) {
  const symbol = useStore().currencySymbol;
  const { allStores } = useSession();
  const t = data?.totals;
  const p = data?.previous;
  const money = (n: number) => formatMoney(n, symbol);

  return (
    <div className={cx('space-y-5 transition-opacity', stale && 'opacity-60')} aria-busy={loading || stale}>
      <div className="grid grid-cols-2 gap-3 sm:gap-4 lg:grid-cols-4">
        <Tile dark label="Net sales" value={t && money(t.netSales)} current={t?.netSales} previous={p?.netSales} previousText={p && money(p.netSales)} />
        <Tile label="Transactions" value={t && formatCount(t.transactions)} current={t?.transactions} previous={p?.transactions} previousText={p && formatCount(p.transactions)} />
        <Tile label="Average sale" value={t && money(t.averageSale)} current={t?.averageSale} previous={p?.averageSale} previousText={p && money(p.averageSale)} />
        <Tile label="Units" value={t && formatQuantity(t.units)} current={t?.units} previous={p?.units} previousText={p && formatQuantity(p.units)} />
      </div>

      {t && (
        <div className="flex flex-wrap gap-x-6 gap-y-1 px-1 text-ink">
          <span>
            Tax <strong className="tabular-nums">{money(t.tax)}</strong>
          </span>
          <span>
            Total <strong className="tabular-nums">{money(t.total)}</strong>
          </span>
          <span>
            Gross profit <strong className="tabular-nums">{money(t.grossProfit)}</strong>
          </span>
          <span>
            Discounts <strong className="tabular-nums">{money(t.discounts)}</strong>
          </span>
          <span>
            Returns <strong className="tabular-nums">{money(t.returns)}</strong>
          </span>
          <span>
            Voids <strong className="tabular-nums">{formatCount(t.voids)}</strong>
          </span>
        </div>
      )}

      <Card title="Payment mix" aside={data && `${formatCount(data.paymentMix.reduce((s, m) => s + m.count, 0))} payments`}>
        {loading || !data ? <Skeleton className="h-12 rounded-full" /> : <PaymentMix mix={data.paymentMix} symbol={symbol} />}
      </Card>

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <Card title="Sales by hour" aside="Net sales">
          {loading || !data ? (
            <Skeleton className="h-[240px] rounded-card" />
          ) : data.byHour.every((h) => h.netSales === 0) ? (
            <EmptyState title="Nothing sold in these days" className="h-[240px] py-0" />
          ) : (
            <MoneyBars
              data={data.byHour.map((h) => ({ label: formatHour(h.hour), value: h.netSales, count: h.transactions }))}
              color={colors.charcoal}
              symbol={symbol}
            />
          )}
        </Card>
        <Card title="Sales by day" aside="Net sales">
          {loading || !data ? (
            <Skeleton className="h-[240px] rounded-card" />
          ) : data.byDay.length === 0 || data.byDay.every((d) => d.netSales === 0) ? (
            <EmptyState title="Nothing sold in these days" className="h-[240px] py-0" />
          ) : (
            <MoneyBars
              data={data.byDay.map((d) => ({ label: shortDay(d.date), value: d.netSales, count: d.transactions }))}
              color={colors.sun}
              stroke={colors.sunDark}
              symbol={symbol}
            />
          )}
        </Card>
      </div>

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <ListCard
          title="Top items"
          loading={loading}
          rows={data?.topItems}
          rowKey={(r, i) => `${r.itemId ?? r.plu ?? r.name}-${i}`}
          empty="No items sold yet."
          columns={[
            { key: 'plu', header: 'Stock code', cell: (r) => r.plu ?? '', hideBelow: 'sm' },
            { key: 'name', header: 'Item', cell: (r) => r.name, className: 'wrap' },
            { key: 'units', header: 'Units', numeric: true, cell: (r) => formatQuantity(r.units) },
            { key: 'net', header: 'Net sales', numeric: true, cell: (r) => money(r.netSales) },
          ]}
        />
        {allStores && (
          <ListCard
            title="By store"
            loading={loading}
            rows={data?.byStore}
            rowKey={(r) => r.storeId}
            empty="No stores yet."
            columns={[
              { key: 'store', header: 'Store', cell: (r) => <span title={r.name}>{r.code} · {r.name}</span> },
              { key: 'net', header: 'Net sales', numeric: true, cell: (r) => money(r.netSales) },
              { key: 'n', header: 'Sales', numeric: true, cell: (r) => formatCount(r.transactions) },
              { key: 'avg', header: 'Average', numeric: true, cell: (r) => money(r.averageSale), hideBelow: 'sm' },
              { key: 'up', header: 'Last upload', cell: (r) => (now ? formatAgo(r.lastUploadAtUtc, now) : ''), hideBelow: 'md' },
            ]}
          />
        )}
        <ListCard
          title="By cashier"
          loading={loading}
          rows={data?.byCashier}
          rowKey={(r, i) => (r.userId ?? r.userName) + i}
          empty="No sales by anyone yet."
          columns={[
            { key: 'who', header: 'Cashier', cell: (r) => r.userName },
            { key: 'n', header: 'Sales', numeric: true, cell: (r) => formatCount(r.transactions) },
            { key: 'net', header: 'Net sales', numeric: true, cell: (r) => money(r.netSales) },
          ]}
        />
        <ListCard
          title="By till"
          loading={loading}
          rows={data?.byStation}
          rowKey={(r) => `${r.storeId}-${r.stationNumber}`}
          empty="No till has sold yet."
          aside={
            <Link href="/tills/" className="font-semibold text-ink underline decoration-sun-dark decoration-2 underline-offset-4">
              Tills
            </Link>
          }
          columns={[
            { key: 'till', header: 'Till', cell: (r) => `${r.storeCode} · ${String(r.stationNumber).padStart(3, '0')}` },
            { key: 'n', header: 'Sales', numeric: true, cell: (r) => formatCount(r.transactions) },
            { key: 'net', header: 'Net sales', numeric: true, cell: (r) => money(r.netSales) },
          ]}
        />
        <ListCard
          title="Drawer variances"
          className="xl:col-span-2"
          loading={loading}
          rows={data?.drawers}
          rowKey={(r, i) => `${r.storeCode}-${r.stationNumber}-${r.closedAt}-${i}`}
          empty="No drawer was closed in these days."
          aside={<span className="text-note text-muted">Counted cash shows once the tills record it.</span>}
          columns={[
            { key: 'till', header: 'Till', cell: (r) => `${r.storeCode} · ${String(r.stationNumber).padStart(3, '0')}` },
            { key: 'at', header: 'Closed', cell: (r) => formatShortDateTime(r.closedAt) },
            { key: 'by', header: 'Closed by', cell: (r) => r.closedBy ?? '', hideBelow: 'sm' },
            { key: 'exp', header: 'Expected', numeric: true, cell: (r) => money(r.expected), hideBelow: 'md' },
            { key: 'cnt', header: 'Counted', numeric: true, cell: (r) => (r.counted === null ? '—' : money(r.counted)), hideBelow: 'md' },
            {
              key: 'var',
              header: 'Variance',
              numeric: true,
              cell: (r) =>
                r.variance === null ? (
                  '—'
                ) : (
                  <span className={cx('font-semibold', Math.abs(r.variance) >= 0.005 ? 'text-danger' : 'text-ok')}>
                    {r.variance > 0 ? '+' : ''}
                    {money(r.variance)}
                  </span>
                ),
            },
          ]}
        />
      </div>
    </div>
  );
}

function shortDay(date: string): string {
  const d = parseBusinessDate(date);
  return d ? d.toLocaleDateString(undefined, { weekday: 'short', day: 'numeric' }) : date;
}

/** A figure with its change against the previous period of the same length. */
function Tile({
  label,
  value,
  current,
  previous,
  previousText,
  dark,
}: {
  label: string;
  value: string | undefined;
  current: number | undefined;
  previous: number | undefined;
  previousText: string | undefined;
  dark?: boolean;
}) {
  const change = current !== undefined && previous !== undefined ? percentChange(current, previous) : null;
  const up = change !== null && change > 0.05;
  const down = change !== null && change < -0.05;
  const Arrow = up ? ArrowUpRight : down ? ArrowDownRight : Minus;
  return (
    <section className={cx('flex min-h-[150px] min-w-0 flex-col justify-between gap-1 rounded-card p-4 shadow-card sm:min-h-[176px] sm:p-5', dark ? 'bg-charcoal text-white' : 'bg-card')}>
      <h2 className={cx('font-heading text-lead sm:text-section', dark ? 'text-white' : 'text-ink')}>{label}</h2>
      {value === undefined ? (
        <Skeleton className="my-2 h-12 w-3/4" />
      ) : (
        <p className="truncate font-heading text-[clamp(24px,2.5vw,40px)] leading-tight tabular-nums" data-testid={`tile-${label}`}>
          {value}
        </p>
      )}
      <div className="flex flex-wrap items-center gap-2">
        {current !== undefined && previous !== undefined && (
          <Chip tone={up ? 'ok' : down ? 'danger' : dark ? 'dark' : 'plain'} className={cx(dark && !up && !down && 'bg-charcoal-soft')}>
            <Arrow className="h-4 w-4" aria-hidden="true" />
            {formatChange(current, previous)}
          </Chip>
        )}
        {previousText !== undefined && <span className={cx('text-note', dark ? 'text-cream' : 'text-muted')}>before: {previousText}</span>}
      </div>
    </section>
  );
}

const MIX_STYLES = ['bg-charcoal text-white', 'bg-sun text-ink', 'bg-stripes text-ink', 'bg-sun-light text-ink', 'bg-stone-light text-ink', 'bg-charcoal-soft text-white'];

/** The tenders as pills sized by their share, like the desktop dashboard's Cash / Card / On account row. */
function PaymentMix({ mix, symbol }: { mix: Dashboard['paymentMix']; symbol: string }) {
  const total = mix.reduce((sum, m) => sum + Math.max(0, m.amount), 0);
  if (mix.length === 0 || total === 0) {
    return <p className="text-muted">No payments in these days.</p>;
  }
  const sorted = [...mix].sort((a, b) => b.amount - a.amount);
  return (
    <div>
      <div className="flex gap-2" role="img" aria-label={sorted.map((m) => `${m.tender} ${Math.round((m.amount / total) * 100)}%`).join(', ')}>
        {sorted.map((m, i) => {
          const share = Math.max(0, m.amount) / total;
          return (
            <div
              key={m.tender}
              className={cx('flex h-12 min-w-[56px] items-center rounded-full border border-line/60 px-4 font-semibold', MIX_STYLES[i % MIX_STYLES.length])}
              style={{ flexGrow: Math.max(share, 0.04), flexBasis: 0 }}
              title={`${m.tender}: ${formatMoney(m.amount, symbol)}`}
            >
              <span className="truncate">{Math.round(share * 100)}%</span>
            </div>
          );
        })}
      </div>
      <ul className="mt-4 grid grid-cols-1 gap-x-6 gap-y-2 sm:grid-cols-2 lg:grid-cols-3">
        {sorted.map((m, i) => (
          <li key={m.tender} className="flex items-center gap-3">
            <span className={cx('h-4 w-4 shrink-0 rounded-full border border-line', MIX_STYLES[i % MIX_STYLES.length])} aria-hidden="true" />
            <span className="flex-1 truncate">{m.tender}</span>
            <span className="tabular-nums text-muted">{formatCount(m.count)}×</span>
            <span className="w-[110px] text-right font-semibold tabular-nums">{formatMoney(m.amount, symbol)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function ChartTooltip({ active, payload, label, symbol }: TooltipProps<number, string> & { symbol: string }) {
  if (!active || !payload?.length) return null;
  const point = payload[0].payload as { value: number; count: number };
  return (
    <div className="rounded-control border border-line bg-card px-3 py-2 shadow-card">
      <p className="font-semibold">{label}</p>
      <p className="tabular-nums">{formatMoney(point.value, symbol)}</p>
      <p className="text-note text-muted">{formatCount(point.count)} sales</p>
    </div>
  );
}

function MoneyBars({ data, color, stroke, symbol }: { data: { label: string; value: number; count: number }[]; color: string; stroke?: string; symbol: string }) {
  const max = Math.max(...data.map((d) => d.value));
  return (
    <div className="h-[240px] w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} margin={{ top: 8, right: 4, bottom: 0, left: 0 }}>
          <CartesianGrid vertical={false} stroke={colors.line} />
          <XAxis dataKey="label" tickLine={false} axisLine={{ stroke: colors.line }} tick={{ fill: colors.muted, fontSize: 13 }} interval="preserveStartEnd" minTickGap={8} />
          <YAxis
            tickLine={false}
            axisLine={false}
            width={64}
            tick={{ fill: colors.muted, fontSize: 13 }}
            tickFormatter={(v: number) => (Math.abs(v) >= 1000 ? `${Math.round(v / 100) / 10}k` : String(Math.round(v)))}
          />
          <Tooltip cursor={{ fill: colors.sunLight, opacity: 0.6 }} content={<ChartTooltip symbol={symbol} />} />
          <Bar dataKey="value" radius={[6, 6, 0, 0]} maxBarSize={40} isAnimationActive={false}>
            {data.map((d, i) => (
              <Cell key={i} fill={d.value === max && color === colors.charcoal ? colors.sun : color} stroke={stroke} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

function ListCard<T>({
  title,
  rows,
  rowKey,
  columns,
  loading,
  empty,
  aside,
  className,
}: {
  title: string;
  rows: T[] | undefined;
  rowKey(row: T, index: number): string;
  columns: Column<T>[];
  loading: boolean;
  empty: string;
  aside?: ReactNode;
  className?: string;
}) {
  return (
    <section className={cx('rounded-card bg-card shadow-card', className)}>
      <div className="flex items-center justify-between px-5 pb-2 pt-4">
        <h2 className="font-heading text-section">{title}</h2>
        {aside}
      </div>
      <DataTable
        className="rounded-t-none shadow-none"
        columns={columns}
        rows={loading ? undefined : rows}
        rowKey={rowKey}
        loading={loading}
        empty={<p className="px-5 pb-6 pt-2 text-muted">{empty}</p>}
        maxHeight="360px"
      />
    </section>
  );
}
