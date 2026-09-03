'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { FailedTile, KpiTile, Panel, PanelNote, RankedBars, TileSkeleton } from '@/components/dashboard/kpi';
import { Banknote, Eye, EyeOff, HandCoins, PackageSearch, TrendingUp } from 'lucide-react';
import { SalesTrend } from '@/components/dashboard/sales-trend';
import { HIDDEN_AMOUNT, useAmountVisibility } from '@/lib/amount-visibility';
import { formatCurrency } from '@/lib/utils';
import { PageHeader } from '@/components/shell/page-header';

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

  // Yesterday is already inside the fortnight the trend query returned, so the comparison is free —
  // no second request, and it cannot disagree with the headline because it is the same array.
  //
  // Left off entirely when yesterday took nothing. A percentage against zero is either a division by
  // zero or a fabricated "100%", and a shop that was shut yesterday should not be told it is up.
  const { visible: amountsVisible, toggle: toggleAmounts } = useAmountVisibility();

  /**
   * Hidden money is never rendered, only replaced.
   *
   * Blurring or colouring the real figure leaves it in the DOM, where it is one inspector — or one
   * screen reader — away from being read out. The number does not reach the page at all.
   */
  /**
   * Every figure of money on this screen, without exception.
   *
   * "Hide takings" covered the two sales tiles and the two charts and left the rest: what customers
   * owe, what is overdue, and the value of everything on order. Those are the same fact about the
   * business as the day's takings, on the same monitor, which on this screen is quite likely to be
   * facing the shop floor. A control that hides some of the money teaches people the money is
   * hidden.
   */
  const amount = (value: number) => (amountsVisible ? formatCurrency(value) : HIDDEN_AMOUNT);

  const yesterdayNet = rows.find((r) => r.groupKey === isoDate(-1))?.netSales ?? 0;
  const todayNet = todayRow?.netSales ?? 0;
  const salesDelta = yesterdayNet > 0 ? ((todayNet - yesterdayNet) / yesterdayNet) * 100 : undefined;

  const outstanding = (aging.data ?? []).reduce((sum, row) => sum + row.total, 0);
  const overdue = (aging.data ?? []).reduce((sum, row) => sum + row.days30 + row.days60 + row.days90Plus, 0);
  const outstandingPos = onOrder.data ?? [];

  // Only lines that have a reorder point to be below.
  //
  // The report returns everything it considers understocked, and with no reorder point set that
  // test reduces to "on hand is nought or less" — so every item the shop has never stocked was
  // being counted. The panel read `0 / 0` fifty-two times over, which is not a shortage, and the
  // tile above it invited a purchase order for nothing. An item nobody has given a reorder point
  // is an item nobody has asked to be told about; it is still on the stock position report.
  const managed = (stock.data ?? []).filter((row) => row.reorderPoint > 0);
  const unmanaged = (stock.data ?? []).length - managed.length;
  const understocked = managed;

  return (
    <div className="flex flex-col">
      <PageHeader
        title="Today"
        description={`${new Date().toLocaleDateString([], { weekday: 'long', day: 'numeric', month: 'long' })}${auth.user?.name ? ` · ${auth.user.name}` : ''}`}
        actions={
          <>
            {/*
              Only offered to somebody who could see the figures anyway. A hide control on a screen
              that never shows takings is a switch that does nothing, and it would imply there is
              something behind it.
            */}
            {auth.can('reports.sales') ? (
              <button
                type="button"
                onClick={toggleAmounts}
                className="pos-button"
                aria-pressed={amountsVisible}
                aria-label={amountsVisible ? 'Hide takings' : 'Show takings'}
              >
                {amountsVisible ? <EyeOff className="h-5 w-5" aria-hidden /> : <Eye className="h-5 w-5" aria-hidden />}
                {amountsVisible ? 'Hide takings' : 'Show takings'}
              </button>
            ) : null}

            <Link href="/pos" className="pos-button-primary">
              Open the till
            </Link>
          </>
        }
      />

      <div className="space-y-3 px-page py-panel">
        {/* One column on a phone, two on a tablet, four on a desktop. The tiles are the summary, so
            they come first at every width. */}
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {auth.can('reports.sales') ? (
          trend.isPending ? (
            <TileSkeleton label="Sales today" />
          ) : trend.isError ? (
            <FailedTile label="Sales today" onRetry={() => void trend.refetch()} />
          ) : (
            <KpiTile
              icon={Banknote}
              tone={todayNet > 0 ? 'positive' : 'neutral'}
              label="Sales today"
              value={amount(todayNet)}
              hint={`${todayRow?.transactionCount ?? 0} transaction${todayRow?.transactionCount === 1 ? '' : 's'}`}
              delta={
                salesDelta != null && amountsVisible
                  ? { percent: salesDelta, comparison: `vs yesterday ${formatCurrency(yesterdayNet)}` }
                  : undefined
              }
              href="/reports/sales"
            />
          )
        ) : null}

        {auth.can('reports.sales') ? (
          trend.isPending ? (
            <TileSkeleton label="Last 14 days" />
          ) : trend.isError ? (
            <FailedTile label="Last 14 days" onRetry={() => void trend.refetch()} />
          ) : (
            <KpiTile
              icon={TrendingUp}
              tone="live"
              label="Last 14 days"
              value={amount(trend.data?.grandNetSales ?? 0)}
              hint={
                trend.data?.grandGrossMargin != null && amountsVisible
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
          ) : stock.isError ? (
            <FailedTile label="Below reorder" onRetry={() => void stock.refetch()} />
          ) : (
            <KpiTile
              icon={PackageSearch}
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
          ) : aging.isError ? (
            <FailedTile label="Owed to you" onRetry={() => void aging.refetch()} />
          ) : (
            <KpiTile
              icon={HandCoins}
              label="Owed to you"
              value={amount(outstanding)}
              hint={overdue > 0 ? `${amount(overdue)} overdue` : 'None overdue'}
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
            ) : amountsVisible ? (
              <SalesTrend rows={rows} todayKey={today} />
            ) : (
              <HiddenNote />
            )}
          </Panel>
        ) : null}

        {auth.can('reports.sales') ? (
          <Panel title="Top sellers" className="min-h-[16rem]">
            {topSellers.isPending ? (
              <div className="h-48 animate-pulse rounded-sm bg-panel-sunken" />
            ) : !amountsVisible ? (
              <HiddenNote />
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
              <PanelNote>
                {unmanaged > 0
                  ? `Every line with a reorder point is above it. ${unmanaged} item${
                      unmanaged === 1 ? ' has' : 's have'
                    } no reorder point set.`
                  : 'Every line is above its reorder point.'}
              </PanelNote>
            ) : (
              <ul className="space-y-1">
                {understocked.slice(0, 6).map((row) => {
                  const short = Math.max(0, row.reorderPoint - row.onHand);

                  return (
                    <li
                      key={row.productId}
                      className="flex items-start justify-between gap-3 rounded-md px-2.5 py-2 odd:bg-panel-sunken"
                    >
                      <span className="min-w-0">
                        <span className="block truncate text-body font-medium text-ink">{row.name}</span>
                        {/*
                          Spelled out rather than left as "0 / 5". The slash was read as a fraction,
                          a score and a date by three different people, and the one thing it never
                          said was which number was the shortage.
                        */}
                        <span className="mt-0.5 block truncate text-caption text-ink-muted">
                          {row.stockCode} · {row.onHand} on hand, reorder at {row.reorderPoint}
                          {row.onOrder > 0 ? ` · ${row.onOrder} on order` : ''}
                        </span>
                      </span>

                      <span className="pos-badge shrink-0 whitespace-nowrap text-warning">
                        short {short}
                      </span>
                    </li>
                  );
                })}
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
              <PanelNote>
                Nothing on order. Anything short above has to be ordered before it arrives.
              </PanelNote>
            ) : (
              <>
                {/* The total first, because the question is usually what is committed rather than
                    which line is biggest. */}
                <p className="mb-2.5 flex items-baseline gap-2 border-b border-subtle pb-2.5">
                  <span className="pos-kpi-value text-h2">
                    {amount(outstandingPos.reduce((sum, r) => sum + r.expectedValue, 0))}
                  </span>
                  <span className="text-caption text-ink-muted">
                    across {outstandingPos.length} line{outstandingPos.length === 1 ? '' : 's'}
                  </span>
                </p>

                <ul className="space-y-1">
                  {outstandingPos.slice(0, 5).map((row) => (
                    <li
                      key={`${row.poNumber}-${row.productId}`}
                      className="flex items-start justify-between gap-3 rounded-md px-2.5 py-2 odd:bg-panel-sunken"
                    >
                      <span className="min-w-0">
                        <span className="block truncate text-body font-medium text-ink">{row.name}</span>
                        <span className="mt-0.5 block truncate text-caption text-ink-muted">
                          PO {row.poNumber} · {row.supplierName}
                          {row.dueOn ? ` · due ${formatDueDate(row.dueOn)}` : ' · no date given'}
                        </span>
                      </span>

                      <span className="shrink-0 text-right">
                        <span className="pos-amount block text-body font-medium text-ink">
                          {amount(row.expectedValue)}
                        </span>
                        <span className="mt-0.5 block text-caption text-ink-muted">
                          {row.qtyOutstanding} due
                        </span>
                      </span>
                    </li>
                  ))}
                </ul>
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
    </div>
  );
}

/** Day and month only. The year on a purchase order due this week is noise. */
function formatDueDate(iso: string): string {
  const date = new Date(iso);

  return Number.isNaN(date.getTime())
    ? iso
    : date.toLocaleDateString([], { day: 'numeric', month: 'short' });
}

/** A date `offset` days from today, as `YYYY-MM-DD` in local time — the shop's day, not UTC's. */
function isoDate(offset: number): string {
  const date = new Date();
  date.setDate(date.getDate() + offset);

  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

/**
 * What a panel shows in place of its figures.
 *
 * The panels go too, not just the tiles. A chart of the last fortnight has a labelled axis and a
 * tooltip naming the exact money, and the top-seller bars are ranked and sized by revenue — a
 * screen with those still on it has not hidden the takings, it has only made them take a second
 * longer to read.
 */
function HiddenNote() {
  return (
    <div className="flex h-48 flex-col items-center justify-center gap-1 text-center text-body text-ink-muted">
      <EyeOff className="h-5 w-5" aria-hidden />
      <p>Takings hidden.</p>
      <p className="text-caption">Use &ldquo;Show takings&rdquo; above.</p>
    </div>
  );
}
