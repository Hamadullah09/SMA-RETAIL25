'use client';

import { useQuery } from '@tanstack/react-query';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { api } from '@/lib/api';
import { cx } from '@/lib/cx';
import { formatCount, formatMoney, formatPercent, formatQuantity } from '@/lib/format';
import { useStore } from '@/lib/store';
import type { SupplierPerformanceRow } from '@/lib/types';

/** On time, fill rate, price changes, returns and lead time of each supplier over a period. */
export default function SupplierPerformancePage() {
  const { storeId, currencySymbol: symbol } = useStore();
  const dates = useDateRange('last30');
  const query = useQuery({
    queryKey: ['supplier-performance', storeId, dates.range.from, dates.range.to],
    queryFn: () => api.supplierPerformance({ storeId, from: dates.range.from, to: dates.range.to }),
  });
  const rows = query.data?.rows;
  const percent = (value: number | null, good = 95, fair = 80) =>
    value === null ? '' : <span className={cx('font-semibold', value >= good ? 'text-ok' : value < fair && 'text-danger')}>{formatPercent(value)}</span>;

  return (
    <div>
      <PageHeader title="Supplier performance" />
      <Toolbar>
        <DateRangeBar state={dates} />
      </Toolbar>
      <p className="mb-3 px-1 text-ink">
        Orders received in these days: on time against their expected date (or the supplier’s lead time), pieces received against ordered, cost changes between receipts of an item, and returns to the supplier.
      </p>
      <DataTable<SupplierPerformanceRow>
        rows={rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r, i) => r.supplierId ?? `${r.supplier}-${i}`}
        caption="Supplier performance"
        empty={<EmptyState title="No orders received in these days" />}
        columns={[
          { key: 'supplier', header: 'Supplier', cell: (r) => <span className="font-semibold">{r.supplier}</span>, className: 'wrap min-w-[140px]' },
          { key: 'orders', header: 'Orders', numeric: true, cell: (r) => formatCount(r.orders) },
          { key: 'ontime', header: 'On time', numeric: true, cell: (r) => percent(r.onTimePercent) },
          { key: 'fill', header: 'Fill rate', numeric: true, cell: (r) => percent(r.fillRatePercent, 98, 90) },
          { key: 'pieces', header: 'Received of ordered', numeric: true, cell: (r) => `${formatQuantity(r.receivedPieces)} of ${formatQuantity(r.orderedPieces)}`, hideBelow: 'xl' },
          { key: 'up', header: 'Cost up', numeric: true, cell: (r) => formatCount(r.priceIncreases), hideBelow: 'md' },
          { key: 'down', header: 'Cost down', numeric: true, cell: (r) => formatCount(r.priceDecreases), hideBelow: 'lg' },
          { key: 'change', header: 'Average change', numeric: true, cell: (r) => (r.averagePriceChangePercent === null ? '' : formatPercent(r.averagePriceChangePercent, undefined, 2)), hideBelow: 'md' },
          { key: 'returns', header: 'Returns', numeric: true, cell: (r) => (r.returns ? `${formatCount(r.returns)} · ${formatMoney(r.returnedValue, symbol)}` : '0'), hideBelow: 'sm' },
          { key: 'lead', header: 'Lead days', numeric: true, cell: (r) => (r.averageLeadDays === null ? '' : formatQuantity(r.averageLeadDays)), hideBelow: 'sm' },
          { key: 'value', header: 'Received', numeric: true, cell: (r) => formatMoney(r.receivedValue, symbol), hideBelow: 'lg' },
        ]}
      />
    </div>
  );
}
