'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Send, Settings2 } from 'lucide-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { LINK, today, useMoney } from '@/components/finance';
import { Button, LinkButton } from '@/components/ui/button';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Dialog } from '@/components/ui/dialog';
import { DateInput } from '@/components/ui/field';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { addDays } from '@/lib/dates';
import { roundMoney, suspenseReason } from '@/lib/finance';
import { formatCount, formatDate } from '@/lib/format';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { PostDaysResult, SuspenseRow } from '@/lib/types';

export default function SuspensePage() {
  return (
    <Suspense>
      <SuspenseList />
    </Suspense>
  );
}

/**
 * The tills' business days go to the ledger by themselves (when a till's package arrives, and each night). An amount
 * whose account the store's daily journal mapping does not name, or names but the chart lacks, waits here in Suspense
 * until the mapping is fixed and the days are posted again.
 */
function SuspenseList() {
  const params = useSearchParams();
  const router = useRouter();
  const { can } = useSession();
  const { storeId } = useStore();
  const money = useMoney();
  const dates = useDateRange('month');
  const [posting, setPosting] = useState(false);
  useEffect(() => {
    if (params.get('post') === '1' && can(P.financePost)) {
      setPosting(true);
      router.replace('/finance/suspense/');
    }
  }, [params, can, router]);
  const query = useQuery({ queryKey: ['suspense', storeId, dates.range], queryFn: () => api.suspense({ storeId, ...dates.range }) });
  const rows = query.data;
  const debit = roundMoney((rows ?? []).reduce((sum, r) => sum + r.debit, 0));
  const credit = roundMoney((rows ?? []).reduce((sum, r) => sum + r.credit, 0));

  return (
    <div>
      <PageHeader title="Suspense" aside={rows && `${formatCount(rows.length)} amounts · ${money(roundMoney(debit - credit))} to sort out`} />
      <Toolbar>
        <DateRangeBar state={dates} compact />
        {can(P.financePost) && (
          <Button variant="sun" icon={Send} onClick={() => setPosting(true)}>
            Post days
          </Button>
        )}
        <LinkButton href="/finance/settings/?tab=mapping" icon={Settings2}>
          Account mapping
        </LinkButton>
      </Toolbar>
      <p className="mb-3 px-1 text-ink">
        These amounts of the tills’ days found no account. Give each its account in the store’s account mapping, then post the days again: the amounts move
        to their accounts and leave this list.
      </p>
      <DataTable<SuspenseRow>
        caption="Amounts in suspense"
        rows={rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r, i) => `${r.entryId}-${i}`}
        empty={<EmptyState title="Nothing in suspense">Every amount of these days found its account.</EmptyState>}
        showTotals={!!rows?.length}
        columns={[
          {
            key: 'entry',
            header: 'Entry',
            cell: (r) => (
              <Link className={LINK} href={`/finance/journals/edit/?id=${encodeURIComponent(r.entryId)}`}>
                {r.number}
              </Link>
            ),
          },
          { key: 'date', header: 'Day', cell: (r) => formatDate(r.date) },
          { key: 'store', header: 'Store', cell: (r) => r.storeCode, hideBelow: 'sm' },
          { key: 'what', header: 'What', cell: (r) => suspenseReason(r.memo).what, className: 'wrap' },
          {
            key: 'code',
            header: 'Account missing',
            hideBelow: 'md',
            cell: (r) => {
              const code = suspenseReason(r.memo).code;
              return code ? `${code} is not in the chart` : 'None mapped';
            },
          },
          { key: 'debit', header: 'Debit', numeric: true, cell: (r) => (r.debit ? money(r.debit) : ''), total: money(debit) },
          { key: 'credit', header: 'Credit', numeric: true, cell: (r) => (r.credit ? money(r.credit) : ''), total: money(credit) },
        ]}
      />
      {posting && <PostDaysDialog onClose={() => setPosting(false)} />}
    </div>
  );
}

/** Posts the tills' business days of the store chosen (or every store) again, after the mapping changed. */
function PostDaysDialog({ onClose }: { onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { storeId, store } = useStore();
  const [from, setFrom] = useState(addDays(today(), -7));
  const [to, setTo] = useState(today());
  const [result, setResult] = useState<PostDaysResult | null>(null);
  const post = useMutation({
    mutationFn: () => api.postDays(from <= to ? from : to, from <= to ? to : from, storeId),
    onSuccess: (r) => {
      setResult(r);
      toast.ok(`${formatCount(r.entries)} days were posted.`);
      for (const key of ['suspense', 'journals', 'finance-home', 'finance-accounts']) void queryClient.invalidateQueries({ queryKey: [key] });
    },
  });
  return (
    <Dialog
      open
      title="Post days to the ledger"
      onClose={onClose}
      onSubmit={() => (result ? onClose() : post.mutate())}
      footer={
        <>
          {!result && (
            <Button type="submit" variant="sun" icon={Send} shortcut="Enter" busy={post.isPending}>
              Post
            </Button>
          )}
          <Button onClick={onClose} shortcut="Esc">
            {result ? 'Close' : 'Cancel'}
          </Button>
        </>
      }
    >
      {result ? (
        <div className="space-y-2">
          <p className="text-ink">{formatCount(result.entries)} business days were posted (a day already posted changes only where its figures did).</p>
          {result.problems.length > 0 && (
            <>
              <p className="font-semibold text-danger">Some days were not posted:</p>
              <ul className="list-disc pl-6">
                {result.problems.map((p, i) => (
                  <li key={i}>{p}</li>
                ))}
              </ul>
            </>
          )}
        </div>
      ) : (
        <div className="space-y-3">
          <p className="text-ink">The tills’ days of {store ? `${store.code} ${store.name}` : 'every store'} from the first day to the last go to the ledger again.</p>
          <div className="flex flex-wrap gap-3">
            <DateInput label="From" value={from} onChange={setFrom} />
            <DateInput label="To" value={to} onChange={setTo} />
          </div>
          {post.error ? <FormError error={post.error} /> : null}
        </div>
      )}
    </Dialog>
  );
}
