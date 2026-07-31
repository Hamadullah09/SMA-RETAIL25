'use client';

import { useCallback, useMemo } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { ReportShell, useReport } from '@/components/reports/report-shell';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { formatCurrency } from '@/lib/utils';
import type { OnOrderRow } from '@/types/masters';

/**
 * Everything bought but not yet on the shelf (guide p.19) — the other half of the reorder picture,
 * and the answer to "did we already order that?" before someone orders it twice.
 */
export default function OnOrderPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;

  const load = useCallback(() => {
    if (!locationId) return undefined;
    return mastersApi.reports.onOrder(locationId);
  }, [locationId]);

  const { data, loading } = useReport<OnOrderRow[]>(load, 'Could not load what is on order');

  const columns = useMemo<DataGridColumn<OnOrderRow>[]>(
    () => [
      { key: 'supplier', header: 'Supplier', width: 200, render: (r) => r.supplierName },
      { key: 'po', header: 'PO #', width: 80, numeric: true, render: (r) => r.poNumber },
      { key: 'code', header: 'Code', width: 130, render: (r) => r.stockCode },
      { key: 'name', header: 'Product', width: 240, render: (r) => r.name },
      { key: 'ordered', header: 'Ordered', width: 100, numeric: true, render: (r) => r.orderQty },
      { key: 'received', header: 'Received', width: 100, numeric: true, render: (r) => r.qtyReceived },
      {
        key: 'outstanding',
        header: 'Outstanding',
        width: 120,
        numeric: true,
        render: (r) => r.qtyOutstanding,
        sortValue: (r) => r.qtyOutstanding,
      },
      { key: 'cost', header: 'Cost each', width: 110, numeric: true, render: (r) => formatCurrency(r.costEach) },
      {
        key: 'value',
        header: 'Value due',
        width: 120,
        numeric: true,
        render: (r) => formatCurrency(r.expectedValue),
        sortValue: (r) => r.expectedValue,
      },
      { key: 'due', header: 'Due', width: 110, render: (r) => r.dueOn ?? '—' },
    ],
    [],
  );

  if (!locationId) {
    return <p className="text-sm text-[rgb(var(--text-muted))]">No location is attached to this session.</p>;
  }

  const total = data?.reduce((sum, row) => sum + row.expectedValue, 0) ?? 0;

  return (
    <ReportShell
      title="On order"
      exportHref={mastersApi.reports.onOrderExportUrl(locationId)}
      grid={
        <DataGrid
          gridId="on-order"
          rows={data ?? []}
          columns={columns}
          rowKey={(row) => `${row.poNumber}-${row.productId}`}
          emptyMessage={loading ? 'Loading…' : 'Nothing is on order.'}
        />
      }
      summary={
        data ? (
          <span className="flex flex-wrap items-center gap-4">
            <span>
              {data.length} open line{data.length === 1 ? '' : 's'}
            </span>
            <span>
              Value still to arrive <span className="pos-amount">{formatCurrency(total)}</span>
            </span>
          </span>
        ) : null
      }
    />
  );
}
