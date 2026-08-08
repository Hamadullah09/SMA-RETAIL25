'use client';

import { useCallback, useMemo, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { ReportShell, filterInputClass, useReport } from '@/components/reports/report-shell';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { formatCurrency } from '@/lib/utils';
import type {
  StockValuationDetailPage,
  StockValuationDetailRow,
  StockValuationResult,
  StockValuationRow,
} from '@/types/masters';

/**
 * What the shelves are worth, at cost and at retail (guide p.24).
 *
 * Deliberately as-of-now rather than as-of-a-date: on-hand and average cost are current state, and
 * reconstructing a historical valuation would mean replaying the whole stock ledger against a
 * moving average — a different and much heavier question than the one this screen answers.
 */
export default function StockValuationPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;

  const [detail, setDetail] = useState(false);

  const loadSummary = useCallback(() => {
    if (!locationId || detail) return undefined;
    return mastersApi.reports.stockValue(locationId);
  }, [locationId, detail]);

  const loadDetail = useCallback(() => {
    if (!locationId || !detail) return undefined;
    return mastersApi.reports.stockValueDetail(locationId, undefined, 0, 1000);
  }, [locationId, detail]);

  const summary = useReport<StockValuationResult>(loadSummary, 'Could not value the stock');
  const detailed = useReport<StockValuationDetailPage>(loadDetail, 'Could not load the valuation detail');

  const summaryColumns = useMemo<DataGridColumn<StockValuationRow>[]>(
    () => [
      { key: 'dept', header: 'Department', width: 220, render: (r) => r.departmentName },
      { key: 'items', header: 'Items', width: 90, numeric: true, render: (r) => r.productCount },
      { key: 'units', header: 'Units', width: 100, numeric: true, render: (r) => r.unitsOnHand },
      {
        key: 'cost',
        header: 'At cost',
        width: 140,
        numeric: true,
        render: (r) => formatCurrency(r.costValue),
        sortValue: (r) => r.costValue,
      },
      { key: 'retail', header: 'At retail', width: 140, numeric: true, render: (r) => formatCurrency(r.retailValue) },
      {
        key: 'margin',
        header: 'Potential margin',
        width: 150,
        numeric: true,
        render: (r) => formatCurrency(r.potentialMargin),
      },
    ],
    [],
  );

  const detailColumns = useMemo<DataGridColumn<StockValuationDetailRow>[]>(
    () => [
      { key: 'code', header: 'Code', width: 130, render: (r) => r.stockCode },
      { key: 'name', header: 'Product', width: 240, render: (r) => r.name },
      { key: 'dept', header: 'Department', width: 160, render: (r) => r.departmentName },
      { key: 'onHand', header: 'On hand', width: 100, numeric: true, render: (r) => r.onHand },
      { key: 'avgCost', header: 'Avg cost', width: 110, numeric: true, render: (r) => formatCurrency(r.avgCost) },
      {
        key: 'extCost',
        header: 'Ext. cost',
        width: 130,
        numeric: true,
        render: (r) => formatCurrency(r.extendedCost),
        sortValue: (r) => r.extendedCost,
      },
      { key: 'price', header: 'Price', width: 110, numeric: true, render: (r) => formatCurrency(r.regularPrice) },
      { key: 'extRetail', header: 'Ext. retail', width: 130, numeric: true, render: (r) => formatCurrency(r.extendedRetail) },
    ],
    [],
  );

  if (!locationId) {
    return <p className="text-body text-ink-muted">No location is attached to this session.</p>;
  }

  return (
    <ReportShell
      title="Stock valuation"
      exportHref={mastersApi.reports.stockValueExportUrl(locationId)}
      filters={
        <select
          className={filterInputClass}
          value={detail ? 'detail' : 'summary'}
          onChange={(e) => setDetail(e.target.value === 'detail')}
          aria-label="View"
        >
          <option value="summary">By department</option>
          <option value="detail">Item by item</option>
        </select>
      }
      grid={
        detail ? (
          <DataGrid
            gridId="stock-valuation-detail"
            rows={detailed.data?.rows ?? []}
            columns={detailColumns}
            rowKey={(row) => row.productId}
            emptyMessage={detailed.loading ? 'Loading…' : 'Nothing on hand.'}
          />
        ) : (
          <DataGrid
            gridId="stock-valuation"
            rows={summary.data?.rows ?? []}
            columns={summaryColumns}
            rowKey={(row) => row.departmentId ?? 'none'}
            emptyMessage={summary.loading ? 'Loading…' : 'Nothing on hand.'}
          />
        )
      }
      summary={
        summary.data ? (
          <span className="flex flex-wrap items-center gap-4">
            <span>
              Units <span className="pos-amount">{summary.data.totalUnits}</span>
            </span>
            <span>
              At cost <span className="pos-amount">{formatCurrency(summary.data.totalCostValue)}</span>
            </span>
            <span>
              At retail <span className="pos-amount">{formatCurrency(summary.data.totalRetailValue)}</span>
            </span>
          </span>
        ) : null
      }
    />
  );
}
