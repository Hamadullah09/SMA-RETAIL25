'use client';

import { keepPreviousData, useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ban, HandCoins } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useId, useMemo, useState } from 'react';
import { amountOf, BankSelect, MoneyInput, ReasonDialog, StatusChip, today, useBankAccounts, useMoney } from '@/components/finance';
import { Button } from '@/components/ui/button';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Dialog } from '@/components/ui/dialog';
import { Field, Input } from '@/components/ui/field';
import { Detail, PageHeader, Pager, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { label, roundMoney, withholdingFor } from '@/lib/finance';
import { formatCount, formatDate } from '@/lib/format';
import { numberText } from '@/lib/forms';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { BillRow, PaymentRow } from '@/lib/types';

const PAGE_SIZE = 50;

export default function PaymentsPage() {
  return (
    <Suspense>
      <Payments />
    </Suspense>
  );
}

/** Payments to suppliers, and paying many bills at once (a payment run). */
function Payments() {
  const params = useSearchParams();
  const router = useRouter();
  const { can } = useSession();
  const { storeId } = useStore();
  const money = useMoney();
  const dates = useDateRange('month');
  const [page, setPage] = useState(1);
  const [running, setRunning] = useState(false);
  const [preselected, setPreselected] = useState<string[]>([]);
  const [viewing, setViewing] = useState<string | null>(null);
  useEffect(() => setPage(1), [storeId, dates.range]);
  useEffect(() => {
    if (params.get('run') === '1' && can(P.financePayables)) {
      setPreselected((params.get('bills') ?? '').split(',').filter(Boolean));
      setRunning(true);
      router.replace('/finance/payments/');
    }
  }, [params, can, router]);

  const query = useQuery({
    queryKey: ['payments', storeId, dates.range, page],
    queryFn: ({ signal }) => api.payments({ storeId, ...dates.range, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });

  return (
    <div>
      <PageHeader title="Supplier payments" aside={query.data && `${formatCount(query.data.total)} payments`} />
      <Toolbar>
        <DateRangeBar state={dates} compact />
        {can(P.financePayables) && (
          <Button variant="sun" icon={HandCoins} shortcut="Ctrl+N" onClick={() => setRunning(true)}>
            Pay bills
          </Button>
        )}
      </Toolbar>
      <DataTable<PaymentRow>
        caption="Supplier payments"
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(p) => p.id}
        onRowClick={(p) => setViewing(p.id)}
        empty={<EmptyState title="No payments">Nothing was paid on these days.</EmptyState>}
        columns={[
          { key: 'number', header: 'Payment', cell: (p) => <span className="font-semibold">{p.number}</span> },
          { key: 'date', header: 'Date', cell: (p) => formatDate(p.date) },
          { key: 'supplier', header: 'Supplier', cell: (p) => p.supplier },
          { key: 'bank', header: 'From', cell: (p) => p.bankAccount, hideBelow: 'md' },
          { key: 'ref', header: 'Reference', cell: (p) => p.reference ?? '', hideBelow: 'lg' },
          { key: 'status', header: 'Status', cell: (p) => <StatusChip status={p.status} />, hideBelow: 'sm' },
          { key: 'wht', header: 'Withheld', numeric: true, cell: (p) => money(p.withholdingAmount), hideBelow: 'md' },
          { key: 'amount', header: 'Paid', numeric: true, cell: (p) => money(p.amount) },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="payments" />}
      />
      {running && <PaymentRun preselected={preselected} onClose={() => setRunning(false)} />}
      {viewing && <PaymentView id={viewing} onClose={() => setViewing(null)} />}
    </div>
  );
}

/** Choose open bills (and debit notes), the account to pay from, and pay them: one payment per supplier and store. */
function PaymentRun({ preselected, onClose }: { preselected: string[]; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const money = useMoney();
  const { storeId } = useStore();
  const banks = useBankAccounts();
  const ids = { bank: useId(), date: useId(), ref: useId() };
  const open = useQuery({ queryKey: ['bills', storeId, 'open', 'run'], queryFn: () => api.bills({ storeId, status: 'open', pageSize: 500 }) });
  const [bankId, setBankId] = useState<string | null>(null);
  const [date, setDate] = useState(today());
  const [reference, setReference] = useState('');
  const [amounts, setAmounts] = useState<Record<string, string>>({});
  const [problem, setProblem] = useState<string | null>(null);
  const bills = useMemo(() => open.data?.items ?? [], [open.data]);
  useEffect(() => {
    if (!open.data) return;
    setAmounts((current) => {
      const next = { ...current };
      for (const b of open.data.items) if (preselected.includes(b.id) && next[b.id] === undefined) next[b.id] = numberText(b.open);
      return next;
    });
  }, [open.data, preselected]);
  useEffect(() => {
    if (!bankId && banks.data?.length) setBankId(banks.data.find((b) => b.isActive)?.id ?? null);
  }, [bankId, banks.data]);

  const chosen = bills.filter((b) => amounts[b.id] !== undefined && amountOf(amounts[b.id]) > 0);
  const groups = useMemo(() => {
    const map = new Map<string, BillRow[]>();
    for (const b of chosen) map.set(`${b.supplierId}|${b.storeId}`, [...(map.get(`${b.supplierId}|${b.storeId}`) ?? []), b]);
    return [...map.values()];
  }, [chosen]);
  const quotes = useQueries({
    queries: groups.map((g) => ({
      queryKey: ['withholding', g[0].supplierId, g[0].storeId],
      queryFn: () => api.withholding(g[0].supplierId, g[0].storeId),
      staleTime: 60_000,
    })),
  });
  const preview = groups.map((g, i) => {
    const quote = quotes[i]?.data;
    const result = withholdingFor(
      g.map((b) => ({ amount: amountOf(amounts[b.id]), subtotal: b.subtotal, total: b.total, kind: b.kind })),
      quote?.rate ?? 0,
      quote?.threshold ?? 0,
    );
    return { supplier: g[0].supplier, store: g[0].storeCode, quote, ...result };
  });
  // What leaves the account: the bills less debit notes, less the tax kept back.
  const total = roundMoney(preview.reduce((sum, p) => sum + p.paid, 0));

  const pay = useMutation({
    mutationFn: () =>
      api.payRun({
        bankAccountId: bankId!,
        date,
        bills: chosen.map((b) => ({ billId: b.id, amount: amountOf(amounts[b.id]) })),
        withholdingAmount: null,
        exchangeRate: 1,
        reference: reference.trim() || null,
        notes: null,
      }),
    onSuccess: (result) => {
      toast.ok(`Payment run ${result.run.number}: ${formatCount(result.run.payments)} payments, ${money(result.run.total)} paid.`);
      for (const key of ['payments', 'bills', 'bills-due', 'finance-home', 'bank-accounts']) void queryClient.invalidateQueries({ queryKey: [key] });
      onClose();
    },
  });

  function submit() {
    if (!bankId) return setProblem('Choose the account to pay from.');
    if (chosen.length === 0) return setProblem('Choose the bills to pay.');
    const over = chosen.find((b) => amountOf(amounts[b.id]) > b.open);
    if (over) return setProblem(`Bill ${over.number} has ${money(over.open)} open.`);
    setProblem(null);
    pay.mutate();
  }

  return (
    <Dialog
      open
      width="xl"
      title="Pay bills"
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button type="submit" variant="sun" icon={HandCoins} shortcut="Enter" busy={pay.isPending}>
            {chosen.length ? `Pay ${money(total)}` : 'Pay'}
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
          <Field label="Pay from" htmlFor={ids.bank}>
            <BankSelect id={ids.bank} value={bankId} onChange={setBankId} accounts={banks.data} />
          </Field>
          <Field label="Date" htmlFor={ids.date}>
            <Input id={ids.date} type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </Field>
          <Field label="Cheque or transfer number" htmlFor={ids.ref}>
            <Input id={ids.ref} value={reference} maxLength={40} onChange={(e) => setReference(e.target.value)} />
          </Field>
        </div>
        {!open.data ? (
          <Skeleton className="h-40" />
        ) : bills.length === 0 ? (
          <EmptyState title="Nothing to pay">Every posted bill is paid.</EmptyState>
        ) : (
          <div className="grid-card">
            <div className="grid-scroll max-h-[45vh] overflow-y-auto">
              <table className="grid-table" aria-label="Open bills">
                <thead>
                  <tr>
                    <th scope="col">
                      <span className="sr-only">Pay</span>
                    </th>
                    <th scope="col">Bill</th>
                    <th scope="col">Supplier</th>
                    <th scope="col">Due</th>
                    <th scope="col" className="num">
                      Open
                    </th>
                    <th scope="col" className="num">
                      To pay
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {bills.map((b) => {
                    const picked = amounts[b.id] !== undefined;
                    return (
                      <tr key={b.id}>
                        <td>
                          <input
                            type="checkbox"
                            className="check"
                            aria-label={`Pay ${label(b.kind).toLowerCase()} ${b.number}`}
                            checked={picked}
                            onChange={(e) =>
                              setAmounts((a) => {
                                const next = { ...a };
                                if (e.target.checked) next[b.id] = numberText(b.open);
                                else delete next[b.id];
                                return next;
                              })
                            }
                          />
                        </td>
                        <td>
                          {label(b.kind)} {b.number}
                        </td>
                        <td>{b.supplier}</td>
                        <td>{formatDate(b.dueDate)}</td>
                        <td className="num">{money(b.open)}</td>
                        <td className="num w-[150px]">
                          {picked && <MoneyInput ariaLabel={`Amount for bill ${b.number}`} value={amounts[b.id]} onChange={(v) => setAmounts((a) => ({ ...a, [b.id]: v }))} />}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        )}
        {preview.length > 0 && (
          <ul className="space-y-1" aria-label="Payments to make">
            {preview.map((p) => (
              <li key={`${p.supplier}-${p.store}`}>
                <span className="font-semibold">{p.supplier}</span> ({p.store}): {money(p.paid)} paid
                {p.withholding ? `, ${money(p.withholding)} withheld at ${p.quote?.rate ?? 0}%${p.quote?.section ? ` (${p.quote.section})` : ''}` : ', nothing withheld'}
              </li>
            ))}
          </ul>
        )}
        <p className="text-note text-muted">The server works out the withholding tax again from the store’s rule set and each supplier’s details.</p>
        {(problem || pay.error) && (problem ? <p className="text-danger">{problem}</p> : <FormError error={pay.error} />)}
      </div>
    </Dialog>
  );
}

function PaymentView({ id, onClose }: { id: string; onClose(): void }) {
  const money = useMoney();
  const { can } = useSession();
  const toast = useToast();
  const queryClient = useQueryClient();
  const detail = useQuery({ queryKey: ['payment', id], queryFn: () => api.payment(id) });
  const [voiding, setVoiding] = useState(false);
  const voidPayment = useMutation({
    mutationFn: ({ on, reason }: { on: string; reason: string }) => api.voidPayment(id, on, reason),
    onSuccess: (p) => {
      toast.ok(`Payment ${p.number} is void; its bills are open again.`);
      setVoiding(false);
      for (const key of ['payments', 'bills', 'payment']) void queryClient.invalidateQueries({ queryKey: [key] });
    },
  });
  const p = detail.data;
  return (
    <Dialog
      open
      width="lg"
      title={p ? `Payment ${p.number}` : 'Payment'}
      onClose={onClose}
      footer={
        <>
          {p?.status === 'Posted' && can(P.financePayables) && (
            <Button variant="danger" icon={Ban} onClick={() => setVoiding(true)}>
              Void
            </Button>
          )}
          <Button onClick={onClose} shortcut="Esc">
            Close
          </Button>
        </>
      }
    >
      {!p ? (
        <Skeleton className="h-40" />
      ) : (
        <div className="space-y-3">
          <dl className="grid grid-cols-2 gap-3">
            <Detail label="Supplier">{p.supplier}</Detail>
            <Detail label="Date">{formatDate(p.date)}</Detail>
            <Detail label="From">{p.bankAccount}</Detail>
            <Detail label="Reference">{p.reference ?? ''}</Detail>
            <Detail label="Paid">{money(p.amount)}</Detail>
            <Detail label="Withheld">{`${money(p.withholdingAmount)} (${label(p.withholdingKind).toLowerCase()}, ${p.withholdingRate}%)`}</Detail>
            <Detail label="Status">
              <StatusChip status={p.status} />
            </Detail>
            <Detail label="Store">{p.storeCode}</Detail>
          </dl>
          <h3 className="section-title">Bills settled</h3>
          <ul>
            {p.allocations.map((a) => (
              <li key={a.billId}>
                {label(a.kind)} {a.billNumber}: {money(a.amount)}
              </li>
            ))}
          </ul>
        </div>
      )}
      {voiding && (
        <ReasonDialog
          title={`Void payment ${p?.number}?`}
          message="Its ledger entry is reversed and the bills it paid are open again."
          confirmLabel="Void"
          busy={voidPayment.isPending}
          error={voidPayment.error}
          onConfirm={(on, reason) => voidPayment.mutate({ on, reason })}
          onClose={() => setVoiding(false)}
        />
      )}
    </Dialog>
  );
}
