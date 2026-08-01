'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { KpiTile, Panel, RankedBars, TileSkeleton } from '@/components/dashboard/kpi';
import { SalesTrend } from '@/components/dashboard/sales-trend';
import { formatCurrency } from '@/lib/utils';

/**
 * The trading day, at a glance.
 *
 * This replaced a page of navigation cards. Navigation is what the sidebar and Ctrl+K are for; a
 * landing screen that only offers links tells a manager nothing they did not already know, and this
 * is the screen they see first every morning.
 *
 * Every figure here is computed by a Phase 6 report query — the same queries behind the full report
 * screens, so a number shown here and a number shown there cannot disagree. Nothing is counted from
 * the first page of a list, which is how the previous version told a shop with forty thousand items
 * that it had a hundred. A wrong number on a dashboard is worse than no number, because people quote
 * it.
 *
 * Each panel is gated on the permission its underlying report needs. A cashier sees the till and
 * their own figures; they do not see the shop's margin because the server would refuse to send it.
 */
export default function DashboardPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const enabled = Boolean(locationId);

  const today = isoDate(0);
  const fortnightAgo = isoDate(-13);

  // Today's takings, and the fortnight behind it, from one query grouped by day. Two queries would
  // be two chances for the headline figure and the last point on the chart to disagree.
  const trend = useQuery({
    queryKey: ['dash.trend', locationId, fortnightAgo, today],
    queryFn: () =>
      mastersApi.reports.salesAnalysis({
        locationId: locationId!,
        from: fortnightAgo,
        to: today,
        groupBy: 'Day',
      }),
    enabled: enabled && auth.can('reports.sales'),
  });

  const topSellers = useQuery({
    queryKey: ['dash.top', locationId, today],
    queryFn: () =>
      mastersApi.reports.salesAnalysis({
        locationId: locationId!,
        from: fortnightAgo,
        to: today,
        groupBy: 'Product',
        top: 6,
        sortBy: 'NetSales',
      }),
    enabled: enabled && auth.can('reports.sales'),
  });

  const stock = useQuery({
    queryKey: ['dash.understock', locationId],
    queryFn: () => mastersApi.reports.stockPosition(locationId!, undefined, 'Understock'),
    enabled: enabled && auth.can('reports.inventory'),
  });

  const onOrder = useQuery({
    queryKey: ['dash.onorder', locationId],
    queryFn: () => mastersApi.reports.onOrder(locationId!),
    enabled: enabled && auth.can('reports.inventory'),
  });

  const aging = useQuery({
    queryKey: ['dash.aging', locationId],
    queryFn: () => mastersApi.receivables.aging(locationId!),
    enabled: enabled && auth.can('ar.read'),
  });

  // The last row of the day-grouped series is today. Falling back to zero rather than to the last
  // day that had sales: "nothing yet" is the truth at nine in the morning, and showing yesterday's
  // total under a "Today" label would be a lie a manager acts on.
  const rows = trend.data?.rows ?? [];
  const todayRow = rows.find((r) => r.groupKey === today);

  const outstanding = (aging.data ?? []).reduce((sum, row) => sum + row.total, 0);
  const overdue = (aging.data ?? []).reduce((sum, row) => sum + row.days30 + row.days60 + row.days90Plus, 0);
  const outstandingPos = onOrder.data ?? [];
  const understocked = stock.data ?? [];

  return (
    <div className="space-y-3 p-4">
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <h1>Today</h1>
          <p className="mt-0.5 text-body text-ink-muted">
            {new Date().toLocaleDateString([], { weekday: 'long', day: 'numeric', month: 'long' })}
            {auth.user?.name ? ` · ${auth.user.name}` : ''}
          </p>
        </div>

        <Link href="/pos" className="pos-button-primary">
          Open the till
        </Link>
      </header>

      {/* One column on a phone, two on a tablet, four on a desktop. The tiles are the summary, so
          they come first at every width. */}
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {auth.can('reports.sales') ? (
          trend.isPending ? (
            <TileSkeleton label="Sales today" />
          ) : (
            <KpiTile
              label="Sales today"
              value={formatCurrency(todayRow?.netSales ?? 0)}
              hint={`${todayRow?.transactionCount ?? 0} transaction${todayRow?.transactionCount === 1 ? '' : 's'}`}
              href="/reports/sales"
            />
          )
        ) : null}

        {auth.can('reports.sales') ? (
          trend.isPending ? (
            <TileSkeleton label="Last 14 days" />
          ) : (
            <KpiTile
              label="Last 14 days"
              value={formatCurrency(trend.data?.grandNetSales ?? 0)}
              hint={
                trend.data?.grandGrossMargin != null
                  ? `${formatCurrency(trend.data.grandGrossMargin)} margin`
                  : `${rows.length} trading day${rows.length === 1 ? '' : 's'}`
              }
              href="/reports/sales-analysis"
            />
          )
        ) : null}

        {auth.can('reports.inventory') ? (
          stock.isPending ? (
            <TileSkeleton label="Below reorder" />
          ) : (
            <KpiTile
              label="Below reorder"
              value={understocked.length}
              // Stated in words as well as colour: a red number says nothing to anyone who cannot
              // see red, and this is the tile that is supposed to prompt an action.
              hint={understocked.length === 0 ? 'Nothing needs ordering' : 'Needs a purchase order'}
              tone={understocked.length === 0 ? 'neutral' : 'warning'}
              href="/reports/stock-position"
            />
          )
        ) : null}

        {auth.can('ar.read') ? (
          aging.isPending ? (
            <TileSkeleton label="Owed to you" />
          ) : (
            <KpiTile
              label="Owed to you"
              value={formatCurrency(outstanding)}
              hint={overdue > 0 ? `${formatCurrency(overdue)} overdue` : 'None overdue'}
              tone={overdue > 0 ? 'negative' : 'neutral'}
              href="/receivables"
            />
          )
        ) : null}
      </div>

      <div className="grid gap-3 xl:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        {auth.can('reports.sales') ? (
          <Panel
            title="Sales, last 14 days"
            action={
              <Link href="/reports/sales-analysis" className="text-ink-muted underline">
                Full report
              </Link>
            }
            className="min-h-[16rem]"
          >
            {trend.isPending ? (
              <div className="h-48 animate-pulse rounded-sm bg-panel-sunken" />
            ) : (
              <SalesTrend rows={rows} />
            )}
          </Panel>
        ) : null}

        {auth.can('reports.sales') ? (
          <Panel title="Top sellers" className="min-h-[16rem]">
            {topSellers.isPending ? (
              <div className="h-48 animate-pulse rounded-sm bg-panel-sunken" />
            ) : (
              <RankedBars
                empty="Nothing sold in this period."
                rows={(topSellers.data?.rows ?? []).map((row) => ({
                  key: row.groupKey,
                  label: row.groupLabel,
                  value: row.netSales,
                  sub: `${row.quantity}×`,
                }))}
              />
            )}
          </Panel>
        ) : null}
      </div>

      <div className="grid gap-3 xl:grid-cols-2">
        {auth.can('reports.inventory') ? (
          <Panel
            title="Needs reordering"
            action={
              <Link href="/reports/stock-position" className="text-ink-muted underline">
                All items
              </Link>
            }
          >
            {stock.isPending ? (
              <div className="h-32 animate-pulse rounded-sm bg-panel-sunken" />
            ) : understocked.length === 0 ? (
              <p className="py-6 text-center text-body text-ink-muted">
                Every line is above its reorder point.
              </p>
            ) : (
              <ul className="space-y-0.5">
                {understocked.slice(0, 6).map((row) => (
                  <li
                    key={row.productId}
                    className="flex items-baseline justify-between gap-3 rounded-sm px-2 py-1.5 text-body odd:bg-panel-sunken"
                  >
                    <span className="truncate">
                      <span className="text-ink-faint">{row.stockCode}</span> {row.name}
                    </span>
                    <span className="pos-amount shrink-0 tabular-nums text-warning">
                      {row.onHand} / {row.reorderPoint}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        ) : null}

        {auth.can('reports.inventory') ? (
          <Panel
            title="On order"
            action={
              <Link href="/reports/on-order" className="text-ink-muted underline">
                All orders
              </Link>
            }
          >
            {onOrder.isPending ? (
              <div className="h-32 animate-pulse rounded-sm bg-panel-sunken" />
            ) : outstandingPos.length === 0 ? (
              <p className="py-6 text-center text-body text-ink-muted">Nothing outstanding.</p>
            ) : (
              <>
                <p className="mb-2 text-body text-ink-muted">
                  <span className="pos-amount font-medium text-ink">
                    {formatCurrency(outstandingPos.reduce((sum, r) => sum + r.expectedValue, 0))}
                  </span>{' '}
                  across {outstandingPos.length} line{outstandingPos.length === 1 ? '' : 's'}
                </p>

                <RankedBars
                  empty="Nothing outstanding."
                  rows={outstandingPos.slice(0, 5).map((row) => ({
                    key: `${row.poNumber}-${row.productId}`,
                    label: `${row.name} · ${row.supplierName}`,
                    value: row.expectedValue,
                    sub: `${row.qtyOutstanding} due`,
                  }))}
                />
              </>
            )}
          </Panel>
        ) : null}
      </div>

      {/*
        A cashier holds none of the report permissions above, so without this the screen they land on
        every morning would be blank.
      */}
      {!auth.can('reports.sales') && !auth.can('reports.inventory') && !auth.can('ar.read') ? (
        <Panel title="Welcome">
          <p className="text-body text-ink-muted">
            Your account does not have the reporting permissions this screen shows. Open the till from
            the button above, or press <kbd className="pos-kbd">Ctrl</kbd>+<kbd className="pos-kbd">K</kbd> to
            go anywhere you do have access to.
          </p>
        </Panel>
      ) : null}
    </div>
  );
}

/** A date `offset` days from today, as `YYYY-MM-DD` in local time — the shop's day, not UTC's. */
function isoDate(offset: number): string {
  const date = new Date();
  date.setDate(date.getDate() + offset);

  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}
