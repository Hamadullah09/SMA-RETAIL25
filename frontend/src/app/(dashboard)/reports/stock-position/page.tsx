'use client';

import { useCallback, useMemo, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { ReportShell, filterInputClass, useReport } from '@/components/reports/report-shell';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import type { StockPositionKind, StockPositionRow } from '@/types/masters';

/**
 * What is running short and what is drowning the shelf (guide p.25–27), using the legacy heuristic:
 * three weeks of demand, weighed against base stock and whatever is already on order.
 *
 * Healthy items are left out by default — a report that lists everything is a list nobody reads.
 */
export default function StockPositionPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;

  const [only, setOnly] = useState<StockPositionKind | ''>('');

  const load = useCallback(() => {
    if (!locationId) return undefined;
    return mastersApi.reports.stockPosition(locationId, undefined, only || undefined);
  }, [locationId, only]);

  const { data, loading } = useReport<StockPositionRow[]>(load, 'Could not read the stock position');

  const columns = useMemo<DataGridColumn<StockPositionRow>[]>(
    () => [
      {
        key: 'position',
        header: 'Position',
        width: 120,
        // Shape as well as colour: an "Understock" label reads the same to everyone.
        render: (r) => (
          <span className="pos-badge" data-position={r.position}>
            {r.position === 'Understock' ? '▼ Short' : r.position === 'Overstock' ? '▲ Excess' : 'OK'}
          </span>
        ),
        sortValue: (r) => r.position,
      },
      { key: 'code', header: 'Code', width: 130, render: (r) => r.stockCode },
      { key: 'name', header: 'Product', width: 240, render: (r) => r.name },
      { key: 'dept', header: 'Department', width: 150, render: (r) => r.departmentName },
      { key: 'onHand', header: 'On hand', width: 100, numeric: true, render: (r) => r.onHand },
      { key: 'onOrder', header: 'On order', width: 100, numeric: true, render: (r) => r.onOrder },
      { key: 'reorder', header: 'Reorder pt', width: 110, numeric: true, render: (r) => r.reorderPoint },
      { key: 'base', header: 'Base stock', width: 110, numeric: true, render: (r) => r.baseStock },
      { key: 'weekly', header: 'Sold/week', width: 110, numeric: true, render: (r) => r.avgWeeklySales },
      {
        key: 'cover',
        header: 'Weeks cover',
        width: 120,
        numeric: true,
        render: (r) => (r.avgWeeklySales === 0 ? '—' : r.weeksOfSupply),
        sortValue: (r) => r.weeksOfSupply,
      },
    ],
    [],
  );

  if (!locationId) {
    return <p className="text-body text-ink-muted">No location is attached to this session.</p>;
  }

  const short = data?.filter((r) => r.position === 'Understock').length ?? 0;
  const excess = data?.filter((r) => r.position === 'Overstock').length ?? 0;

  return (
    <ReportShell
      title="Understock and overstock"
      exportHref={mastersApi.reports.stockPositionExportUrl(locationId, undefined, only || undefined)}
      filters={
        <select
          className={filterInputClass}
          value={only}
          onChange={(e) => setOnly(e.target.value as StockPositionKind | '')}
          aria-label="Show"
        >
          <option value="">Short and excess</option>
          <option value="Understock">Short only</option>
          <option value="Overstock">Excess only</option>
          <option value="Normal">Healthy only</option>
        </select>
      }
      grid={
        <DataGrid
          gridId="stock-position"
          rows={data ?? []}
          columns={columns}
          rowKey={(row) => row.productId}
          loading={loading}
          emptyMessage="Nothing is short or overstocked."
        />
      }
      summary={
        data ? (
          <span className="flex flex-wrap items-center gap-4">
            <span>{short} short</span>
            <span>{excess} overstocked</span>
            <span>Weeks of cover is on hand divided by the last three weeks&rsquo; average sales.</span>
          </span>
        ) : null
      }
    />
  );
}
