'use client';

import { useCallback, useMemo, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { DateRangeFilter, ReportShell, isoDate, useReport } from '@/components/reports/report-shell';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { formatCurrency } from '@/lib/utils';
import type { StockReceivedPage as StockReceivedPageData, StockReceivedRow } from '@/types/masters';

/**
 * What actually arrived, in a window (guide p.21).
 *
 * Read from the stock ledger rather than the purchase orders: a posted receipt is a fact about
 * stock, and the ledger is where facts about stock live. Freight shows as the receipt's total
 * rather than split across lines — the split only ever lands in the item's average cost, and
 * re-deriving it here would be a second, differently-rounded answer to the same question.
 */
export default function StockReceivedPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;

  const [from, setFrom] = useState(() => isoDate(-30));
  const [to, setTo] = useState(() => isoDate());

  const load = useCallback(() => {
    if (!locationId) return undefined;
    return mastersApi.reports.stockReceived(locationId, from, to, undefined, 0, 1000);
  }, [locationId, from, to]);

  const { data, loading } = useReport<StockReceivedPageData>(load, 'Could not load the receiving history');

  const columns = useMemo<DataGridColumn<StockReceivedRow>[]>(
    () => [
      {
        key: 'when',
        header: 'Received',
        width: 160,
        render: (r) => new Date(r.occurredAt).toLocaleString(),
        sortValue: (r) => r.occurredAt,
      },
      { key: 'po', header: 'PO #', width: 80, numeric: true, render: (r) => r.poNumber ?? '—' },
      { key: 'supplier', header: 'Supplier', width: 200, render: (r) => r.supplierName || '—' },
      { key: 'code', header: 'Code', width: 130, render: (r) => r.stockCode },
      { key: 'name', header: 'Product', width: 240, render: (r) => r.productName },
      {
        key: 'qty',
        header: 'Quantity',
        width: 100,
        numeric: true,
        render: (r) => r.qtyReceived,
        sortValue: (r) => r.qtyReceived,
      },
      { key: 'cost', header: 'Unit cost', width: 110, numeric: true, render: (r) => formatCurrency(r.unitCost) },
      {
        key: 'ext',
        header: 'Ext. cost',
        width: 120,
        numeric: true,
        render: (r) => formatCurrency(r.extendedCost),
        sortValue: (r) => r.extendedCost,
      },
      {
        key: 'freight',
        header: 'Receipt freight',
        width: 130,
        numeric: true,
        render: (r) => (r.receiptFreightTotal === 0 ? '—' : formatCurrency(r.receiptFreightTotal)),
      },
    ],
    [],
  );

  if (!locationId) {
    return <p className="text-sm text-[rgb(var(--text-muted))]">No location is attached to this session.</p>;
  }

  return (
    <ReportShell
      title="Stock received"
      exportHref={mastersApi.reports.stockReceivedExportUrl(locationId, from, to)}
      filters={<DateRangeFilter from={from} to={to} onFrom={setFrom} onTo={setTo} />}
      grid={
        <DataGrid
          gridId="stock-received"
          rows={data?.rows ?? []}
          columns={columns}
          rowKey={(row) => `${row.occurredAt}-${row.stockCode}`}
          emptyMessage={loading ? 'Loading…' : 'Nothing was received in this window.'}
        />
      }
      summary={
        data ? (
          <span className="flex flex-wrap items-center gap-4">
            <span>
              {data.totalCount} receipt line{data.totalCount === 1 ? '' : 's'}
            </span>
            <span>
              Goods received <span className="pos-amount">{formatCurrency(data.totalCost)}</span>
            </span>
            <span>Freight is the whole receipt&rsquo;s, not this line&rsquo;s share.</span>
          </span>
        ) : null
      }
    />
  );
}
