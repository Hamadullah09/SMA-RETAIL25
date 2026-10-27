'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Select } from '@/components/ui/field';
import { Chip, PageHeader, Pager, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { api } from '@/lib/api';
import { formatCount, formatMoney, formatQuantity, formatShortDateTime } from '@/lib/format';
import { useDebounced } from '@/lib/hooks';
import { staffName, useStaff } from '@/lib/staff';
import { useStore } from '@/lib/store';
import type { SaleRow } from '@/lib/types';

const PAGE_SIZE = 50;

export default function SalesJournalPage() {
  const router = useRouter();
  const { storeId, currencySymbol: symbol } = useStore();
  const dates = useDateRange('week');
  const [station, setStation] = useState('');
  const [userId, setUserId] = useState('');
  const [status, setStatus] = useState('');
  const [text, setText] = useState('');
  const [page, setPage] = useState(1);
  const search = useDebounced(text.trim());

  const filters = { storeId, from: dates.range.from, to: dates.range.to, station, userId, status, search };
  useEffect(() => setPage(1), [storeId, dates.range.from, dates.range.to, station, userId, status, search]);

  const query = useQuery({
    queryKey: ['sales', filters, page],
    queryFn: ({ signal }) => api.sales({ ...filters, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  const tills = useQuery({ queryKey: ['tills', storeId], queryFn: () => api.tills({ storeId }) });
  const staff = useStaff(storeId);

  const rows = query.data?.items;
  const sum = (pick: (s: SaleRow) => number) => (rows ?? []).filter((s) => !s.voided).reduce((t, s) => t + pick(s), 0);
  const voided = (rows ?? []).filter((s) => s.voided).length;

  return (
    <div>
      <PageHeader
        title="Sales Journal"
        aside={query.data && `${formatCount(query.data.total)} sales${voided ? ` · ${formatCount(voided)} voided on this page` : ''}`}
      />
      <Toolbar>
        <DateRangeBar state={dates} compact />
      </Toolbar>
      <Toolbar>
        <SearchBox value={text} onChange={setText} placeholder="Invoice, client or item code" />
        <label className="sr-only" htmlFor="sales-station">
          Till
        </label>
        <Select id="sales-station" className="w-[190px]" value={station} onChange={(e) => setStation(e.target.value)}>
          <option value="">Every till</option>
          {(tills.data ?? []).map((t) => (
            <option key={t.stationId} value={t.number}>
              {t.storeCode} · {String(t.number).padStart(3, '0')}
            </option>
          ))}
        </Select>
        {staff.data && (
          <>
            <label className="sr-only" htmlFor="sales-staff">
              Staff
            </label>
            <Select id="sales-staff" className="w-[190px]" value={userId} onChange={(e) => setUserId(e.target.value)}>
              <option value="">Everyone</option>
              {staff.data.map((u) => (
                <option key={u.id} value={u.id}>
                  {staffName(u)}
                </option>
              ))}
            </Select>
          </>
        )}
        <label className="sr-only" htmlFor="sales-status">
          Status
        </label>
        <Select id="sales-status" className="w-[170px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every sale</option>
          <option value="completed">Completed</option>
          <option value="voided">Voided</option>
        </Select>
      </Toolbar>

      <DataTable<SaleRow>
        rows={rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(s) => s.id}
        onRowClick={(s) => router.push(`/sales/view/?id=${encodeURIComponent(s.id)}`)}
        caption="Sales"
        empty={<EmptyState title="No sales in these days">Try other dates, another till or clear the search.</EmptyState>}
        showTotals={!!rows && rows.length > 0}
        columns={[
          { key: 'inv', header: 'Invoice', cell: (s) => <span className="font-semibold">{s.invoiceNumber}</span>, total: 'Page total' },
          { key: 'at', header: 'Date', cell: (s) => formatShortDateTime(s.at) },
          { key: 'till', header: 'Till', cell: (s) => `${s.storeCode} · ${String(s.stationNumber).padStart(3, '0')}`, hideBelow: 'md' },
          { key: 'staff', header: 'Staff', cell: (s) => s.staff ?? '', hideBelow: 'lg' },
          { key: 'client', header: 'Client', cell: (s) => s.client ?? '', hideBelow: '2xl' },
          { key: 'paid', header: 'Paid by', cell: (s) => s.tenders, hideBelow: 'lg', className: 'wrap' },
          { key: 'units', header: 'Units', numeric: true, cell: (s) => formatQuantity(s.units), hideBelow: '2xl', total: formatQuantity(sum((s) => s.units)) },
          { key: 'net', header: 'Subtotal', numeric: true, cell: (s) => formatMoney(s.netSales, symbol), hideBelow: 'sm', total: formatMoney(sum((s) => s.netSales), symbol) },
          { key: 'tax', header: 'Tax', numeric: true, cell: (s) => formatMoney(s.tax, symbol), hideBelow: '2xl', total: formatMoney(sum((s) => s.tax), symbol) },
          { key: 'total', header: 'Total', numeric: true, cell: (s) => formatMoney(s.total, symbol), total: formatMoney(sum((s) => s.total), symbol) },
          { key: 'status', header: 'Status', cell: (s) => (s.voided ? <Chip tone="danger">Voided</Chip> : <span className="text-muted">Completed</span>) },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="sales" />}
      />
      <p className="mt-2 px-1 text-ink">Page totals leave voided sales out. Choose a sale to see its lines and payments.</p>
    </div>
  );
}
