'use client';

import { useCallback, useMemo, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { DateRangeFilter, ReportShell, filterInputClass, isoDate, useReport } from '@/components/reports/report-shell';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { formatCurrency } from '@/lib/utils';
import type { SalesAnalysisFilters, SalesAnalysisGroupBy, SalesAnalysisResult, SalesAnalysisRow } from '@/types/masters';

const GROUPINGS: { value: SalesAnalysisGroupBy; label: string }[] = [
  { value: 'Product', label: 'By product' },
  { value: 'Department', label: 'By department' },
  { value: 'Client', label: 'By client' },
  { value: 'Day', label: 'By day' },
  { value: 'Week', label: 'By week' },
  { value: 'Month', label: 'By month' },
];

/**
 * Sales analysis (guide p.15–18) — one screen for what the legacy system split across several
 * reports. Change the grouping and it is sales-by-product, by department, by client or by period;
 * sort by quantity and cap the rows and it is the top-sellers list.
 *
 * Cost and margin appear only for a user the server grants cost visibility to; without it the
 * response never carries those numbers, so there is nothing here to accidentally reveal.
 */
export default function SalesAnalysisPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canSeeCost = auth.can('reports.cost_visibility');

  const [from, setFrom] = useState(() => isoDate(-30));
  const [to, setTo] = useState(() => isoDate());
  const [groupBy, setGroupBy] = useState<SalesAnalysisGroupBy>('Product');
  const [sortBy, setSortBy] = useState('net');
  const [top, setTop] = useState('');

  const filters = useMemo<SalesAnalysisFilters>(
    () => ({
      locationId: locationId ?? 0,
      from,
      to,
      groupBy,
      sortBy,
      top: top ? Number(top) : undefined,
    }),
    [locationId, from, to, groupBy, sortBy, top],
  );

  const load = useCallback(() => {
    if (!locationId) return undefined;
    return canSeeCost ? mastersApi.reports.margin(filters) : mastersApi.reports.salesAnalysis(filters);
  }, [locationId, canSeeCost, filters]);

  const { data, loading } = useReport<SalesAnalysisResult>(load, 'Could not run the sales analysis');

  const columns = useMemo<DataGridColumn<SalesAnalysisRow>[]>(() => {
    const base: DataGridColumn<SalesAnalysisRow>[] = [
      { key: 'group', header: 'Group', width: 280, render: (r) => r.groupLabel, sortValue: (r) => r.groupLabel },
      { key: 'qty', header: 'Qty', width: 90, numeric: true, render: (r) => r.quantity, sortValue: (r) => r.quantity },
      {
        key: 'net',
        header: 'Net sales',
        width: 120,
        numeric: true,
        render: (r) => formatCurrency(r.netSales),
        sortValue: (r) => r.netSales,
      },
      {
        key: 'discount',
        header: 'Discount',
        width: 110,
        numeric: true,
        render: (r) => (r.discountTotal === 0 ? '—' : formatCurrency(r.discountTotal)),
      },
      { key: 'tax', header: 'Tax', width: 110, numeric: true, render: (r) => formatCurrency(r.taxTotal) },
      { key: 'txns', header: 'Sales', width: 80, numeric: true, render: (r) => r.transactionCount },
    ];

    if (!canSeeCost) return base;

    return [
      ...base,
      {
        key: 'cogs',
        header: 'Cost',
        width: 110,
        numeric: true,
        render: (r) => (r.cogs === null ? '—' : formatCurrency(r.cogs)),
        sortValue: (r) => r.cogs ?? 0,
      },
      {
        key: 'margin',
        header: 'Margin',
        width: 110,
        numeric: true,
        render: (r) => (r.grossMargin === null ? '—' : formatCurrency(r.grossMargin)),
        sortValue: (r) => r.grossMargin ?? 0,
      },
      {
        key: 'marginPct',
        header: 'Margin %',
        width: 90,
        numeric: true,
        render: (r) => (r.grossMarginPct === null ? '—' : `${r.grossMarginPct.toFixed(1)}%`),
        sortValue: (r) => r.grossMarginPct ?? 0,
      },
    ];
  }, [canSeeCost]);

  if (!locationId) {
    return <p className="text-body text-ink-muted">No location is attached to this session.</p>;
  }

  const exportHref = canSeeCost
    ? mastersApi.reports.marginExportUrl(filters)
    : mastersApi.reports.salesAnalysisExportUrl(filters);

  return (
    <ReportShell
      title="Sales analysis"
      exportHref={exportHref}
      filters={
        <>
          <DateRangeFilter from={from} to={to} onFrom={setFrom} onTo={setTo} />

          <select
            className={filterInputClass}
            value={groupBy}
            onChange={(e) => setGroupBy(e.target.value as SalesAnalysisGroupBy)}
            aria-label="Group by"
          >
            {GROUPINGS.map((g) => (
              <option key={g.value} value={g.value}>
                {g.label}
              </option>
            ))}
          </select>

          <select className={filterInputClass} value={sortBy} onChange={(e) => setSortBy(e.target.value)} aria-label="Sort by">
            <option value="net">Sort: revenue</option>
            <option value="quantity">Sort: quantity</option>
            {canSeeCost ? <option value="margin">Sort: margin</option> : null}
            <option value="label">Sort: name</option>
          </select>

          <label className="flex items-center gap-1.5">
            Top
            <input
              type="number"
              min={1}
              className={`${filterInputClass} w-20`}
              value={top}
              placeholder="all"
              onChange={(e) => setTop(e.target.value)}
            />
          </label>
        </>
      }
      grid={
        <DataGrid
          gridId="sales-analysis"
          rows={data?.rows ?? []}
          columns={columns}
          rowKey={(row) => row.groupKey}
          emptyMessage={loading ? 'Loading…' : 'No sales in this window.'}
        />
      }
      summary={
        data ? (
          <span className="flex flex-wrap items-center gap-4">
            <span>
              {data.rows.length} group{data.rows.length === 1 ? '' : 's'}
            </span>
            <span>
              Units <span className="pos-amount">{data.grandQuantity}</span>
            </span>
            <span>
              Net sales <span className="pos-amount">{formatCurrency(data.grandNetSales)}</span>
            </span>
            {data.grandGrossMargin !== null ? (
              <span>
                Margin <span className="pos-amount">{formatCurrency(data.grandGrossMargin)}</span>
              </span>
            ) : null}
          </span>
        ) : null
      }
    />
  );
}
