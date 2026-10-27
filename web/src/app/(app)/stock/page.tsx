'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { SidePanel } from '@/components/ui/dialog';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Select } from '@/components/ui/field';
import { Chip, Detail, PageHeader, Pager, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { api } from '@/lib/api';
import { cx } from '@/lib/cx';
import { formatCount, formatLocalDateTime, formatMoney, formatQuantity, formatShortDateTime } from '@/lib/format';
import { COUNT_STATUS, label, MOVEMENT_KINDS, TRANSFER_STATUS, words } from '@/lib/labels';
import { useStore } from '@/lib/store';
import type { StockCountLine, StockCountRow, StockMovementRow, TransferLine, TransferRow } from '@/lib/types';

type Tab = 'movements' | 'transfers' | 'counts';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'movements', label: 'Movements' },
  { key: 'transfers', label: 'Transfers' },
  { key: 'counts', label: 'Stock counts' },
];

export default function StockPage() {
  const [tab, setTab] = useState<Tab>('movements');
  return (
    <div>
      <PageHeader title="Stock" />
      <Tabs label="Stock" tabs={TABS} value={tab} onChange={setTab} className="mb-4" />
      {tab === 'movements' && <Movements />}
      {tab === 'transfers' && <Transfers />}
      {tab === 'counts' && <Counts />}
    </div>
  );
}

const PAGE_SIZE = 100;

function signed(n: number) {
  return (n > 0 ? '+' : '') + formatQuantity(n);
}

function Movements() {
  const { storeId, currencySymbol: symbol } = useStore();
  const dates = useDateRange('week');
  const [kind, setKind] = useState('');
  const [page, setPage] = useState(1);
  const filters = { storeId, from: dates.range.from, to: dates.range.to, kind };
  useEffect(() => setPage(1), [storeId, dates.range.from, dates.range.to, kind]);
  const query = useQuery({
    queryKey: ['movements', filters, page],
    queryFn: ({ signal }) => api.movements({ ...filters, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <Toolbar>
        <DateRangeBar state={dates} compact />
        <label className="sr-only" htmlFor="movement-kind">
          What
        </label>
        <Select
          id="movement-kind"
          className="w-[200px]"
          value={kind}
          onChange={(e) => setKind(e.target.value)}
        >
          <option value="">Every movement</option>
          {Object.entries(MOVEMENT_KINDS).map(([key, text]) => (
            <option key={key} value={key}>
              {text}
            </option>
          ))}
        </Select>
      </Toolbar>
      <DataTable<StockMovementRow>
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(m, i) => `${m.at}-${m.plu}-${i}`}
        caption="Stock movements"
        empty={<EmptyState title="No stock moved in these days" />}
        columns={[
          { key: 'at', header: 'When', cell: (m) => formatShortDateTime(m.at) },
          { key: 'where', header: 'Where', cell: (m) => `${m.storeCode} · ${m.locationCode}`, hideBelow: 'lg' },
          { key: 'plu', header: 'Stock code', cell: (m) => m.plu, hideBelow: 'md' },
          { key: 'name', header: 'Item', cell: (m) => m.name, className: 'wrap min-w-[180px]' },
          { key: 'kind', header: 'What', cell: (m) => label(MOVEMENT_KINDS, m.kind) },
          {
            key: 'qty',
            header: 'Quantity',
            numeric: true,
            cell: (m) => <span className={cx('font-semibold', m.quantity < 0 ? 'text-danger' : 'text-ok')}>{signed(m.quantity)}</span>,
          },
          { key: 'after', header: 'On hand after', numeric: true, cell: (m) => formatQuantity(m.onHandAfter), hideBelow: 'sm' },
          { key: 'cost', header: 'Unit cost', numeric: true, cell: (m) => (m.unitCost === null ? '' : formatMoney(m.unitCost, symbol)), hideBelow: 'xl' },
          { key: 'doc', header: 'Document', cell: (m) => m.documentNumber ?? '', hideBelow: 'xl' },
          { key: 'reason', header: 'Reason', cell: (m) => m.reason ?? '', hideBelow: 'xl' },
          { key: 'user', header: 'By', cell: (m) => m.user ?? '', hideBelow: 'lg' },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="movements" />}
      />
    </>
  );
}

function StatusFilter({ id, value, onChange, options }: { id: string; value: string; onChange(v: string): void; options: Record<string, { label: string }> }) {
  return (
    <>
      <label className="sr-only" htmlFor={id}>
        Status
      </label>
      <Select id={id} className="w-[200px]" value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">Every status</option>
        {Object.entries(options).map(([key, o]) => (
          <option key={key} value={key}>
            {o.label}
          </option>
        ))}
      </Select>
    </>
  );
}

function Transfers() {
  const { storeId, currencySymbol: symbol } = useStore();
  const [status, setStatus] = useState('');
  const [open, setOpen] = useState<TransferRow | null>(null);
  const query = useQuery({ queryKey: ['transfers', storeId, status], queryFn: () => api.transfers({ storeId, status }) });
  const rows = query.data;

  return (
    <>
      <Toolbar>
        <StatusFilter id="transfer-status" value={status} onChange={setStatus} options={TRANSFER_STATUS} />
        <span className="text-ink">A transfer shows in both the sending and the receiving store.</span>
      </Toolbar>
      <DataTable<TransferRow>
        rows={rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(t) => t.id}
        onRowClick={setOpen}
        caption="Transfers"
        showTotals={!!rows && rows.length > 1}
        empty={<EmptyState title="No transfers">Transfers are made at the tills (Transfers screen).</EmptyState>}
        columns={[
          { key: 'n', header: 'Transfer', cell: (t) => <span className="font-semibold">{t.number}</span> },
          { key: 'status', header: 'Status', cell: (t) => <Chip tone={TRANSFER_STATUS[t.status]?.tone ?? 'plain'}>{TRANSFER_STATUS[t.status]?.label ?? words(t.status)}</Chip> },
          { key: 'from', header: 'From', cell: (t) => `${t.fromStore} · ${t.fromLocation}` },
          { key: 'to', header: 'To', cell: (t) => `${t.toStore} · ${t.toLocation}` },
          { key: 'shipped', header: 'Shipped', cell: (t) => formatShortDateTime(t.shippedAt), hideBelow: 'lg' },
          { key: 'received', header: 'Received', cell: (t) => formatShortDateTime(t.receivedAt), hideBelow: '2xl' },
          { key: 'lines', header: 'Lines', numeric: true, cell: (t) => formatCount(t.lines), hideBelow: 'md' },
          { key: 'units', header: 'Units', numeric: true, cell: (t) => formatQuantity(t.units), hideBelow: 'sm', total: formatQuantity((rows ?? []).reduce((s, t) => s + t.units, 0)) },
          { key: 'value', header: 'Value', numeric: true, cell: (t) => formatMoney(t.value, symbol), total: formatMoney((rows ?? []).reduce((s, t) => s + t.value, 0), symbol) },
        ]}
      />
      {open && <TransferPanel row={open} onClose={() => setOpen(null)} />}
    </>
  );
}

function TransferPanel({ row, onClose }: { row: TransferRow; onClose(): void }) {
  const symbol = useStore().currencySymbol;
  const query = useQuery({ queryKey: ['transfer', row.id], queryFn: () => api.transfer(row.id) });
  const t = query.data;
  return (
    <SidePanel
      open
      wide
      title={`Transfer ${row.number}`}
      subtitle={`${row.fromStore} · ${row.fromLocation} to ${row.toStore} · ${row.toLocation}`}
      onClose={onClose}
    >
      {query.error ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : !t ? (
        <Skeleton className="h-64 rounded-card" />
      ) : (
        <div className="space-y-4">
          <dl className="card grid grid-cols-2 gap-4 p-5 md:grid-cols-4">
            <Detail label="Status">{TRANSFER_STATUS[t.status]?.label ?? words(t.status)}</Detail>
            <Detail label="Made">{formatLocalDateTime(t.createdAt)}</Detail>
            <Detail label="Shipped">{formatLocalDateTime(t.shippedAt)}</Detail>
            <Detail label="Received">{formatLocalDateTime(t.receivedAt)}</Detail>
          </dl>
          <DataTable<TransferLine>
            rows={t.lines}
            rowKey={(l, i) => `${l.plu}-${i}`}
            caption="Lines"
            showTotals
            columns={[
              { key: 'plu', header: 'Stock code', cell: (l) => l.plu },
              { key: 'desc', header: 'Description', cell: (l) => l.description ?? '', hideBelow: 'sm' },
              { key: 'ship', header: 'Shipped', numeric: true, cell: (l) => formatQuantity(l.quantityShipped), total: formatQuantity(t.lines.reduce((s, l) => s + l.quantityShipped, 0)) },
              {
                key: 'recv',
                header: 'Received',
                numeric: true,
                cell: (l) => <span className={cx(t.status === 'Received' && l.quantityReceived !== l.quantityShipped && 'font-semibold text-danger')}>{formatQuantity(l.quantityReceived)}</span>,
                total: formatQuantity(t.lines.reduce((s, l) => s + l.quantityReceived, 0)),
              },
              { key: 'cost', header: 'Unit cost', numeric: true, cell: (l) => formatMoney(l.unitCost, symbol), hideBelow: 'md' },
              { key: 'value', header: 'Value', numeric: true, cell: (l) => formatMoney(l.unitCost * l.quantityShipped, symbol), total: formatMoney(t.lines.reduce((s, l) => s + l.unitCost * l.quantityShipped, 0), symbol) },
            ]}
          />
        </div>
      )}
    </SidePanel>
  );
}

function Counts() {
  const { storeId, currencySymbol: symbol } = useStore();
  const [status, setStatus] = useState('');
  const [open, setOpen] = useState<StockCountRow | null>(null);
  const query = useQuery({ queryKey: ['counts', storeId, status], queryFn: () => api.counts({ storeId, status }) });

  return (
    <>
      <Toolbar>
        <StatusFilter id="count-status" value={status} onChange={setStatus} options={COUNT_STATUS} />
      </Toolbar>
      <DataTable<StockCountRow>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(c) => c.id}
        onRowClick={setOpen}
        caption="Stock counts"
        empty={<EmptyState title="No stock counts">Counts are made at the tills (Stock counts screen).</EmptyState>}
        columns={[
          { key: 'n', header: 'Count', cell: (c) => <span className="font-semibold">{c.number}</span> },
          { key: 'status', header: 'Status', cell: (c) => <Chip tone={COUNT_STATUS[c.status]?.tone ?? 'plain'}>{COUNT_STATUS[c.status]?.label ?? words(c.status)}</Chip> },
          { key: 'where', header: 'Location', cell: (c) => `${c.storeCode} · ${c.locationCode}` },
          { key: 'started', header: 'Started', cell: (c) => formatShortDateTime(c.startedAt), hideBelow: 'md' },
          { key: 'posted', header: 'Posted', cell: (c) => formatShortDateTime(c.postedAt), hideBelow: 'lg' },
          { key: 'lines', header: 'Lines', numeric: true, cell: (c) => formatCount(c.lines), hideBelow: 'sm' },
          { key: 'vu', header: 'Variance', numeric: true, cell: (c) => <Variance n={c.varianceUnits} text={signed(c.varianceUnits)} />, hideBelow: 'sm' },
          { key: 'vv', header: 'Variance value', numeric: true, cell: (c) => <Variance n={c.varianceValue} text={formatMoney(c.varianceValue, symbol)} /> },
        ]}
      />
      {open && <CountPanel row={open} onClose={() => setOpen(null)} />}
    </>
  );
}

function Variance({ n, text }: { n: number; text: string }) {
  return <span className={cx(n < 0 && 'font-semibold text-danger', n > 0 && 'font-semibold text-ok')}>{text}</span>;
}

function CountPanel({ row, onClose }: { row: StockCountRow; onClose(): void }) {
  const symbol = useStore().currencySymbol;
  const [differences, setDifferences] = useState(false);
  const query = useQuery({ queryKey: ['count', row.id], queryFn: () => api.count(row.id) });
  const c = query.data;
  const lines = (c?.lines ?? []).filter((l) => !differences || l.variance !== 0 || l.notCounted);
  return (
    <SidePanel open wide title={`Stock count ${row.number}`} subtitle={`${row.storeCode} · ${row.locationCode}`} onClose={onClose}>
      {query.error ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : !c ? (
        <Skeleton className="h-64 rounded-card" />
      ) : (
        <div className="space-y-4">
          <dl className="card grid grid-cols-2 gap-4 p-5 md:grid-cols-4">
            <Detail label="Status">{COUNT_STATUS[c.status]?.label ?? words(c.status)}</Detail>
            <Detail label="Started">{formatLocalDateTime(c.startedAt)}</Detail>
            <Detail label="Posted">{formatLocalDateTime(c.postedAt)}</Detail>
            <Detail label="Variance value">{formatMoney(c.varianceValue, symbol)}</Detail>
          </dl>
          <label className="flex cursor-pointer items-center gap-2.5">
            <input type="checkbox" className="check" checked={differences} onChange={(e) => setDifferences(e.target.checked)} />
            Differences only
          </label>
          <DataTable<StockCountLine>
            rows={lines}
            rowKey={(l, i) => `${l.plu}-${i}`}
            caption="Count lines"
            showTotals
            empty={<p className="p-4 text-muted">No differences.</p>}
            columns={[
              { key: 'plu', header: 'Stock code', cell: (l) => l.plu },
              { key: 'desc', header: 'Description', cell: (l) => l.description ?? '', hideBelow: 'sm' },
              { key: 'exp', header: 'Expected', numeric: true, cell: (l) => formatQuantity(l.expected) },
              { key: 'cnt', header: 'Counted', numeric: true, cell: (l) => (l.notCounted ? <Chip>Not counted</Chip> : formatQuantity(l.counted)) },
              { key: 'var', header: 'Variance', numeric: true, cell: (l) => <Variance n={l.variance} text={signed(l.variance)} />, total: signed(lines.reduce((s, l) => s + l.variance, 0)) },
              {
                key: 'cost',
                header: 'Value',
                numeric: true,
                cell: (l) => <Variance n={l.varianceCost} text={formatMoney(l.varianceCost, symbol)} />,
                total: formatMoney(lines.reduce((s, l) => s + l.varianceCost, 0), symbol),
                hideBelow: 'sm',
              },
            ]}
          />
        </div>
      )}
    </SidePanel>
  );
}
