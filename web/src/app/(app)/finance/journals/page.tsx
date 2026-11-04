'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { StatusChip, useMoney } from '@/components/finance';
import { LinkButton } from '@/components/ui/button';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Select } from '@/components/ui/field';
import { PageHeader, Pager, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { api } from '@/lib/api';
import { kindLabel } from '@/lib/finance';
import { formatCount, formatDate } from '@/lib/format';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { JournalRow } from '@/lib/types';

const PAGE_SIZE = 50;
const KINDS = ['Manual', 'Daily', 'SupplierBill', 'SupplierPayment', 'Expense', 'Bank', 'Opening', 'Closing', 'Adjustment', 'Reversal'];

export default function JournalsPage() {
  return (
    <Suspense>
      <Journals />
    </Suspense>
  );
}

/** Every entry of the ledger: those the program posted (days, bills, payments) and those made by hand. */
function Journals() {
  const router = useRouter();
  const params = useSearchParams();
  const { can } = useSession();
  const { storeId } = useStore();
  const money = useMoney();
  const dates = useDateRange('month');
  const [status, setStatus] = useState(params.get('status') ?? '');
  const [kind, setKind] = useState('');
  const [text, setText] = useState('');
  const [page, setPage] = useState(1);
  const search = useDebounced(text.trim());
  // Drafts waiting for approval are shown whatever their date.
  const range = status === 'Draft' ? {} : dates.range;
  useEffect(() => setPage(1), [storeId, status, kind, search, dates.range]);

  const query = useQuery({
    queryKey: ['journals', storeId, range, status, kind, search, page],
    queryFn: ({ signal }) => api.journals({ storeId, ...range, status, kind, search, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });

  return (
    <div>
      <PageHeader title="Journals" aside={query.data && `${formatCount(query.data.total)} entries`} />
      <Toolbar>
        {status !== 'Draft' && <DateRangeBar state={dates} compact />}
        <label className="sr-only" htmlFor="journal-status">
          Status
        </label>
        <Select id="journal-status" className="w-[190px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every status</option>
          <option value="Draft">Waiting for approval</option>
          <option value="Posted">Posted</option>
          <option value="Reversed">Reversed</option>
        </Select>
        <label className="sr-only" htmlFor="journal-kind">
          Kind
        </label>
        <Select id="journal-kind" className="w-[200px]" value={kind} onChange={(e) => setKind(e.target.value)}>
          <option value="">Every kind</option>
          {KINDS.map((k) => (
            <option key={k} value={k}>
              {kindLabel(k)}
            </option>
          ))}
        </Select>
        <SearchBox value={text} onChange={setText} placeholder="Number, description or reference" />
        {can(P.financePost) && (
          <LinkButton href="/finance/journals/edit/" variant="sun" icon={Plus}>
            New entry
          </LinkButton>
        )}
      </Toolbar>
      <DataTable<JournalRow>
        caption="Journal entries"
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(j) => j.id}
        onRowClick={(j) => router.push(`/finance/journals/edit/?id=${encodeURIComponent(j.id)}`)}
        empty={<EmptyState title="No entries">Nothing was posted on these days, or nothing matches.</EmptyState>}
        columns={[
          { key: 'number', header: 'Entry', cell: (j) => <span className="font-semibold">{j.number || 'Draft'}</span> },
          { key: 'date', header: 'Date', cell: (j) => formatDate(j.date) },
          { key: 'kind', header: 'Kind', cell: (j) => kindLabel(j.kind), hideBelow: 'md' },
          { key: 'memo', header: 'Description', cell: (j) => j.memo },
          { key: 'store', header: 'Store', cell: (j) => j.storeCode ?? 'Company', hideBelow: 'lg' },
          { key: 'status', header: 'Status', cell: (j) => <StatusChip status={j.status === 'Draft' ? 'Draft' : j.status} />, hideBelow: 'sm' },
          { key: 'total', header: 'Amount', numeric: true, cell: (j) => money(j.total) },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="entries" />}
      />
    </div>
  );
}
