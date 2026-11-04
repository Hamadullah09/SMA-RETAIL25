'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { HandCoins, Plus, ReceiptText } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { StatusChip, today, useMoney } from '@/components/finance';
import { ReportGrid } from '@/components/report-grid';
import { Button, LinkButton } from '@/components/ui/button';
import { DateInput, Select } from '@/components/ui/field';
import { PageHeader, Pager, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { api } from '@/lib/api';
import { addDays } from '@/lib/dates';
import { label } from '@/lib/finance';
import { formatCount, formatDate } from '@/lib/format';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { BillRow } from '@/lib/types';

const PAGE_SIZE = 50;
type Tab = 'bills' | 'due' | 'ageing';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'bills', label: 'Bills and debit notes' },
  { key: 'due', label: 'Due to pay' },
  { key: 'ageing', label: 'Ageing' },
];

export default function BillsPage() {
  return (
    <Suspense>
      <Bills />
    </Suspense>
  );
}

/** Supplier bills and debit notes, what falls due, and how long what is owed has been owed. */
function Bills() {
  const params = useSearchParams();
  const { can } = useSession();
  const [tab, setTab] = useState<Tab>((params.get('tab') as Tab) || 'bills');
  return (
    <div>
      <PageHeader title="Bills" />
      <Toolbar>
        <Tabs label="Bills" tabs={TABS} value={tab} onChange={setTab} />
        {can(P.financePayables) && (
          <>
            <LinkButton href="/finance/bills/edit/" variant="sun" icon={Plus}>
              New bill
            </LinkButton>
            <LinkButton href="/finance/bills/edit/?kind=DebitNote" icon={ReceiptText}>
              New debit note
            </LinkButton>
          </>
        )}
      </Toolbar>
      {tab === 'bills' ? <BillList /> : tab === 'due' ? <Due /> : <Ageing />}
    </div>
  );
}

function billColumns(money: (n: number) => string) {
  return [
    { key: 'number', header: 'Bill', cell: (b: BillRow) => <span className="font-semibold">{b.number || 'Draft'}</span> },
    { key: 'supplier', header: 'Supplier', cell: (b: BillRow) => b.supplier },
    { key: 'ref', header: 'Their number', cell: (b: BillRow) => b.supplierReference ?? '', hideBelow: 'lg' as const },
    { key: 'date', header: 'Date', cell: (b: BillRow) => formatDate(b.billDate), hideBelow: 'sm' as const },
    { key: 'due', header: 'Due', cell: (b: BillRow) => (b.daysOverdue > 0 ? `${formatDate(b.dueDate)} (${b.daysOverdue} days over)` : formatDate(b.dueDate)), hideBelow: 'md' as const },
    { key: 'status', header: 'Status', cell: (b: BillRow) => <StatusChip status={b.kind === 'DebitNote' && b.status === 'Posted' ? 'Posted' : b.status} /> },
    { key: 'total', header: 'Total', numeric: true, cell: (b: BillRow) => `${b.kind === 'DebitNote' ? '−' : ''}${money(b.total)}` },
    { key: 'open', header: 'Open', numeric: true, cell: (b: BillRow) => money(b.open), hideBelow: 'sm' as const },
  ];
}

function BillList() {
  const router = useRouter();
  const { storeId } = useStore();
  const money = useMoney();
  const [status, setStatus] = useState('');
  const [kind, setKind] = useState('');
  const [text, setText] = useState('');
  const [page, setPage] = useState(1);
  const search = useDebounced(text.trim());
  useEffect(() => setPage(1), [storeId, status, kind, search]);
  const query = useQuery({
    queryKey: ['bills', storeId, status, kind, search, page],
    queryFn: ({ signal }) => api.bills({ storeId, status, kind, search, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  return (
    <>
      <Toolbar>
        <SearchBox value={text} onChange={setText} placeholder="Number, supplier or their number" />
        <label className="sr-only" htmlFor="bill-status">
          Status
        </label>
        <Select id="bill-status" className="w-[180px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every status</option>
          <option value="open">Open (to pay)</option>
          {['Draft', 'Posted', 'PartPaid', 'Paid', 'Void'].map((s) => (
            <option key={s} value={s}>
              {label(s)}
            </option>
          ))}
        </Select>
        <label className="sr-only" htmlFor="bill-kind">
          Kind
        </label>
        <Select id="bill-kind" className="w-[170px]" value={kind} onChange={(e) => setKind(e.target.value)}>
          <option value="">Bills and notes</option>
          <option value="Bill">Bills</option>
          <option value="DebitNote">Debit notes</option>
        </Select>
      </Toolbar>
      <DataTable<BillRow>
        caption="Bills"
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(b) => b.id}
        onRowClick={(b) => router.push(`/finance/bills/edit/?id=${encodeURIComponent(b.id)}`)}
        empty={<EmptyState title="No bills">{search ? 'Nothing matches the search.' : 'Enter a supplier’s bill with New bill.'}</EmptyState>}
        columns={billColumns(money)}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="bills" />}
      />
    </>
  );
}

function Due() {
  const router = useRouter();
  const { storeId } = useStore();
  const { can } = useSession();
  const money = useMoney();
  const [until, setUntil] = useState(addDays(today(), 7));
  const [chosen, setChosen] = useState<Set<string>>(new Set());
  const query = useQuery({ queryKey: ['bills-due', storeId, until], queryFn: () => api.billsDue({ storeId, until }) });
  const rows = query.data;
  const total = (rows ?? []).filter((b) => chosen.has(b.id)).reduce((sum, b) => sum + b.open, 0);
  const toggle = (id: string) =>
    setChosen((set) => {
      const next = new Set(set);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  return (
    <>
      <Toolbar>
        <DateInput label="Due by" value={until} onChange={(v) => v && setUntil(v)} />
        {can(P.financePayables) && (
          <Button
            variant="sun"
            icon={HandCoins}
            disabled={chosen.size === 0}
            onClick={() => router.push(`/finance/payments/?run=1&bills=${[...chosen].join(',')}`)}
          >
            {chosen.size ? `Pay ${formatCount(chosen.size)} bills (${money(total)})` : 'Pay the chosen bills'}
          </Button>
        )}
      </Toolbar>
      <DataTable<BillRow>
        caption="Bills due"
        rows={rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(b) => b.id}
        selectedKey={null}
        onRowClick={(b) => toggle(b.id)}
        empty={<EmptyState title="Nothing due">No open bill falls due by then.</EmptyState>}
        columns={[
          {
            key: 'pick',
            header: <span className="sr-only">Choose</span>,
            cell: (b) => <input type="checkbox" className="check" aria-label={`Pay bill ${b.number}`} checked={chosen.has(b.id)} onChange={() => toggle(b.id)} onClick={(e) => e.stopPropagation()} />,
          },
          ...billColumns(money),
        ]}
      />
    </>
  );
}

function Ageing() {
  const { storeId, currencySymbol } = useStore();
  const [asOf, setAsOf] = useState(today());
  const query = useQuery({ queryKey: ['finance-report', 'payables-ageing', storeId, asOf], queryFn: () => api.financeReport('payables-ageing', { storeId, from: asOf, to: asOf }) });
  return (
    <>
      <Toolbar>
        <DateInput label="As of" value={asOf} onChange={(v) => v && setAsOf(v)} />
      </Toolbar>
      {query.error ? (
        <div className="card">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      ) : !query.data ? (
        <div className="grid-card">
          <TableSkeleton />
        </div>
      ) : (
        <ReportGrid table={query.data} symbol={currencySymbol} empty="Nothing is owed to suppliers." />
      )}
    </>
  );
}
