'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ban, Play, Plus, Save, Send, Trash2 } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useId, useState } from 'react';
import {
  AccountSelect,
  amountOf,
  Attachments,
  BankSelect,
  MoneyInput,
  ReasonDialog,
  StatusChip,
  StoreSelect,
  today,
  useAccounts,
  useBankAccounts,
  useMoney,
  useSuppliers,
} from '@/components/finance';
import { Button } from '@/components/ui/button';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select } from '@/components/ui/field';
import { PageHeader, Pager, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { label } from '@/lib/finance';
import { formatCount, formatDate } from '@/lib/format';
import { numberText } from '@/lib/forms';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { ExpenseDetail, ExpensePaidBy, ExpenseRequest, ExpenseRow, RecurrenceFrequency, RecurringExpense } from '@/lib/types';

const PAGE_SIZE = 50;
type Tab = 'expenses' | 'recurring';

export default function ExpensesPage() {
  return (
    <Suspense>
      <Expenses />
    </Suspense>
  );
}

/** Expenses paid in cash, from a bank or owed, with their receipts; and those made again every month. */
function Expenses() {
  const params = useSearchParams();
  const router = useRouter();
  const { can } = useSession();
  const [tab, setTab] = useState<Tab>('expenses');
  const [editing, setEditing] = useState<string | 'new' | null>(null);
  useEffect(() => {
    if (params.get('new') === '1' && can(P.financePost)) {
      setEditing('new');
      router.replace('/finance/expenses/');
    }
  }, [params, can, router]);
  return (
    <div>
      <PageHeader title="Expenses" />
      <Toolbar>
        <Tabs
          label="Expenses"
          tabs={[
            { key: 'expenses', label: 'Expenses' },
            { key: 'recurring', label: 'Every month' },
          ]}
          value={tab}
          onChange={setTab}
        />
        {can(P.financePost) && tab === 'expenses' && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            New expense
          </Button>
        )}
      </Toolbar>
      {tab === 'expenses' ? <ExpenseList onOpen={setEditing} /> : <RecurringList />}
      {editing && <ExpenseDialog id={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function ExpenseList({ onOpen }: { onOpen(id: string): void }) {
  const { storeId } = useStore();
  const money = useMoney();
  const dates = useDateRange('month');
  const [status, setStatus] = useState('');
  const [text, setText] = useState('');
  const [page, setPage] = useState(1);
  const search = useDebounced(text.trim());
  useEffect(() => setPage(1), [storeId, status, search, dates.range]);
  const query = useQuery({
    queryKey: ['expenses', storeId, dates.range, status, search, page],
    queryFn: ({ signal }) => api.expenses({ storeId, ...dates.range, status, search, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  return (
    <>
      <Toolbar>
        <DateRangeBar state={dates} compact />
        <label className="sr-only" htmlFor="expense-status">
          Status
        </label>
        <Select id="expense-status" className="w-[160px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every status</option>
          <option value="Draft">Draft</option>
          <option value="Posted">Posted</option>
          <option value="Void">Void</option>
        </Select>
        <SearchBox value={text} onChange={setText} placeholder="Payee or description" />
      </Toolbar>
      <DataTable<ExpenseRow>
        caption="Expenses"
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(e) => e.id}
        onRowClick={(e) => onOpen(e.id)}
        empty={<EmptyState title="No expenses">Nothing was spent on these days.</EmptyState>}
        columns={[
          { key: 'date', header: 'Date', cell: (e) => formatDate(e.date) },
          { key: 'payee', header: 'Paid to', cell: (e) => <span className="font-semibold">{e.payee}</span> },
          { key: 'account', header: 'Account', cell: (e) => `${e.accountCode} ${e.accountName}`, hideBelow: 'md' },
          { key: 'how', header: 'Paid', cell: (e) => label(e.paidBy), hideBelow: 'sm' },
          { key: 'receipts', header: 'Receipts', cell: (e) => (e.attachments ? formatCount(e.attachments) : ''), hideBelow: 'lg' },
          { key: 'status', header: 'Status', cell: (e) => <StatusChip status={e.status} /> },
          { key: 'total', header: 'Total', numeric: true, cell: (e) => money(e.total) },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="expenses" />}
      />
    </>
  );
}

function ExpenseDialog({ id, onClose }: { id: string | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { can } = useSession();
  const { defaultStoreId } = useStore();
  const accounts = useAccounts();
  const banks = useBankAccounts();
  const suppliers = useSuppliers();
  const money = useMoney();
  const [savedId, setSavedId] = useState<string | null>(id);
  const detail = useQuery({ queryKey: ['expense', savedId], queryFn: () => api.expense(savedId!), enabled: !!savedId, staleTime: 0 });
  const e = detail.data?.expense;
  const editable = (!e || e.status === 'Draft') && can(P.financePost);
  const ids = { date: useId(), store: useId(), payee: useId(), account: useId(), description: useId(), amount: useId(), tax: useId(), how: useId(), bank: useId(), supplier: useId(), ref: useId() };
  const [form, setForm] = useState({
    date: today(),
    storeId: defaultStoreId,
    payee: '',
    accountId: null as string | null,
    description: '',
    amount: '',
    tax: '0',
    paidBy: 'Cash' as ExpensePaidBy,
    bankAccountId: null as string | null,
    supplierId: null as string | null,
    reference: '',
  });
  const [loaded, setLoaded] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [voiding, setVoiding] = useState(false);
  const [deleting, setDeleting] = useState(false);
  useEffect(() => {
    if (e && e.version !== loaded) {
      setForm({
        date: e.date,
        storeId: e.storeId,
        payee: e.payee,
        accountId: e.accountId,
        description: e.description ?? '',
        amount: numberText(e.amount),
        tax: numberText(e.taxAmount),
        paidBy: e.paidBy,
        bankAccountId: e.bankAccountId,
        supplierId: e.supplierId,
        reference: e.reference ?? '',
      });
      setLoaded(e.version);
    }
  }, [e, loaded]);
  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) => setForm((f) => ({ ...f, [key]: value }));
  const refresh = (saved: ExpenseDetail) => {
    queryClient.setQueryData(['expense', saved.expense.id], saved);
    void queryClient.invalidateQueries({ queryKey: ['expenses'] });
    void queryClient.invalidateQueries({ queryKey: ['finance-home'] });
  };
  const save = useMutation({
    mutationFn: (body: ExpenseRequest) => (savedId ? api.saveExpense(savedId, body) : api.addExpense(body)),
    onSuccess: (saved) => {
      refresh(saved);
      setSavedId(saved.expense.id);
      toast.ok(saved.expense.status === 'Posted' ? `Expense ${saved.expense.number} is posted.` : 'The expense is saved; attach its receipt, then post it.');
    },
  });
  const voidExpense = useMutation({
    mutationFn: ({ on, reason }: { on: string; reason: string }) => api.voidExpense(savedId!, on, reason),
    onSuccess: (saved) => {
      refresh(saved);
      setVoiding(false);
      toast.ok(`Expense ${saved.expense.number} is void.`);
    },
  });
  const remove = useMutation({
    mutationFn: () => api.deleteExpense(savedId!),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['expenses'] });
      toast.ok('The draft is deleted.');
      onClose();
    },
  });

  function submit(andPost: boolean) {
    if (!form.storeId) return setProblem('Choose the store.');
    if (!form.payee.trim()) return setProblem('Say who was paid.');
    if (!form.accountId) return setProblem('Choose the expense account.');
    if (amountOf(form.amount) <= 0) return setProblem('The amount must be more than zero.');
    if (form.paidBy === 'Bank' && !form.bankAccountId) return setProblem('Choose the bank account it was paid from.');
    if (form.paidBy === 'OnAccount' && !form.supplierId) return setProblem('An expense on account needs its supplier.');
    setProblem(null);
    save.mutate({
      date: form.date,
      storeId: form.storeId,
      payee: form.payee.trim(),
      supplierId: form.supplierId,
      accountId: form.accountId,
      description: form.description.trim() || null,
      amount: amountOf(form.amount),
      taxAmount: amountOf(form.tax),
      paidBy: form.paidBy,
      bankAccountId: form.paidBy === 'OnAccount' ? null : form.bankAccountId,
      reference: form.reference.trim() || null,
      post: andPost,
      version: e?.version ?? null,
    });
  }

  return (
    <Dialog
      open
      width="lg"
      title={e ? (e.number ? `Expense ${e.number}` : 'Draft expense') : 'New expense'}
      onClose={onClose}
      onSubmit={() => (editable ? submit(false) : onClose())}
      footer={
        <>
          {editable && (
            <>
              <Button type="submit" icon={Save} shortcut="Enter" busy={save.isPending}>
                Save
              </Button>
              <Button variant="sun" icon={Send} busy={save.isPending} onClick={() => submit(true)}>
                Save and post
              </Button>
            </>
          )}
          {e?.status === 'Draft' && can(P.financePost) && (
            <Button variant="danger" icon={Trash2} onClick={() => setDeleting(true)}>
              Delete
            </Button>
          )}
          {e?.status === 'Posted' && can(P.financePost) && (
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
      {savedId && !e ? (
        <Skeleton className="h-64" />
      ) : (
        <div className="space-y-4">
          {e && (
            <p>
              <StatusChip status={e.status} /> {e.status !== 'Draft' && `${label(e.paidBy)} · ${money(e.total)}`}
            </p>
          )}
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            <Field label="Date" htmlFor={ids.date}>
              <Input id={ids.date} type="date" value={form.date} readOnly={!editable} onChange={(ev) => set('date', ev.target.value)} />
            </Field>
            <Field label="Store" htmlFor={ids.store}>
              <StoreSelect id={ids.store} value={form.storeId} onChange={(v) => set('storeId', v)} disabled={!editable} />
            </Field>
            <Field label="Paid to" htmlFor={ids.payee}>
              <Input id={ids.payee} value={form.payee} maxLength={100} readOnly={!editable} onChange={(ev) => set('payee', ev.target.value)} />
            </Field>
            <Field label="Account" htmlFor={ids.account}>
              <AccountSelect id={ids.account} value={form.accountId} accounts={accounts.data} types={['Expense', 'Asset']} disabled={!editable} onChange={(v) => set('accountId', v)} />
            </Field>
            <Field label="Amount before tax" htmlFor={ids.amount}>
              <MoneyInput id={ids.amount} value={form.amount} disabled={!editable} onChange={(v) => set('amount', v)} />
            </Field>
            <Field label="Tax" htmlFor={ids.tax}>
              <MoneyInput id={ids.tax} value={form.tax} disabled={!editable} onChange={(v) => set('tax', v)} />
            </Field>
            <Field label="Paid" htmlFor={ids.how}>
              <Select id={ids.how} value={form.paidBy} disabled={!editable} onChange={(ev) => set('paidBy', ev.target.value as ExpensePaidBy)}>
                <option value="Cash">In cash</option>
                <option value="Bank">From a bank account</option>
                <option value="OnAccount">Not yet (on account: a bill is made)</option>
              </Select>
            </Field>
            {form.paidBy === 'OnAccount' ? (
              <Field label="Supplier" htmlFor={ids.supplier}>
                <Select id={ids.supplier} value={form.supplierId ?? ''} disabled={!editable} onChange={(ev) => set('supplierId', ev.target.value || null)}>
                  <option value="">Choose the supplier</option>
                  {(suppliers.data?.items ?? []).map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.company}
                    </option>
                  ))}
                </Select>
              </Field>
            ) : (
              <Field label={form.paidBy === 'Cash' ? 'Cash box' : 'Bank account'} htmlFor={ids.bank} hint={form.paidBy === 'Cash' ? 'None: the store’s cash in hand' : undefined}>
                <BankSelect id={ids.bank} value={form.bankAccountId} accounts={banks.data} allowEmpty={form.paidBy === 'Cash'} emptyLabel="The store’s cash in hand" onChange={(v) => set('bankAccountId', v)} />
              </Field>
            )}
            <Field label="Description" htmlFor={ids.description}>
              <Input id={ids.description} value={form.description} maxLength={200} readOnly={!editable} onChange={(ev) => set('description', ev.target.value)} />
            </Field>
            <Field label="Receipt number" htmlFor={ids.ref}>
              <Input id={ids.ref} value={form.reference} maxLength={40} readOnly={!editable} onChange={(ev) => set('reference', ev.target.value)} />
            </Field>
          </div>
          <p className="text-ink">Total {money(amountOf(form.amount) + amountOf(form.tax))}</p>
          {(problem || save.error) && (problem ? <p className="text-danger">{problem}</p> : <FormError error={save.error} />)}
          {e && <Attachments owner="expenses" ownerId={e.id} files={detail.data!.attachments} canChange={can(P.financePost)} queryKey={['expense', e.id]} />}
          {!e && <p className="text-note text-muted">Save the expense to attach its receipt.</p>}
        </div>
      )}
      {voiding && (
        <ReasonDialog
          title={`Void expense ${e?.number}?`}
          message="Its ledger entry (or its bill, when it was on account) is reversed."
          confirmLabel="Void"
          busy={voidExpense.isPending}
          error={voidExpense.error}
          onConfirm={(on, reason) => voidExpense.mutate({ on, reason })}
          onClose={() => setVoiding(false)}
        />
      )}
      <ConfirmDialog open={deleting} title="Delete this draft?" confirmLabel="Delete" danger busy={remove.isPending} onConfirm={() => remove.mutate()} onClose={() => setDeleting(false)} />
    </Dialog>
  );
}

function RecurringList() {
  const { storeId } = useStore();
  const { can } = useSession();
  const toast = useToast();
  const queryClient = useQueryClient();
  const money = useMoney();
  const [editing, setEditing] = useState<RecurringExpense | 'new' | null>(null);
  const query = useQuery({ queryKey: ['recurring-expenses', storeId], queryFn: () => api.recurringExpenses({ storeId }) });
  const run = useMutation({
    mutationFn: () => api.runRecurringExpenses({ storeId }),
    onSuccess: (r) => {
      toast.ok(r.made ? `${formatCount(r.made)} expenses were made.` : 'Nothing was due.');
      void queryClient.invalidateQueries({ queryKey: ['recurring-expenses'] });
      void queryClient.invalidateQueries({ queryKey: ['expenses'] });
    },
  });
  return (
    <>
      {can(P.financePost) && (
        <Toolbar>
          <Button variant="sun" icon={Plus} onClick={() => setEditing('new')}>
            New recurring expense
          </Button>
          <Button icon={Play} busy={run.isPending} onClick={() => run.mutate()}>
            Make what is due now
          </Button>
        </Toolbar>
      )}
      <DataTable<RecurringExpense>
        caption="Recurring expenses"
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => r.id ?? r.name}
        onRowClick={(r) => can(P.financePost) && setEditing(r)}
        empty={<EmptyState title="None yet">Rent and other costs made every month go here; the server makes them when they fall due.</EmptyState>}
        columns={[
          { key: 'name', header: 'Name', cell: (r) => <span className="font-semibold">{r.name}</span> },
          { key: 'payee', header: 'Paid to', cell: (r) => r.payee, hideBelow: 'sm' },
          { key: 'every', header: 'Every', cell: (r) => label(r.frequency) },
          { key: 'next', header: 'Next', cell: (r) => formatDate(r.nextDate) },
          { key: 'how', header: 'Made as', cell: (r) => (r.postAutomatically ? 'Posted' : 'Draft'), hideBelow: 'md' },
          { key: 'active', header: 'Active', cell: (r) => (r.isActive ? 'Yes' : 'No'), hideBelow: 'md' },
          { key: 'amount', header: 'Amount', numeric: true, cell: (r) => money(r.amount + r.taxAmount) },
        ]}
      />
      {editing && <RecurringDialog row={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function RecurringDialog({ row, onClose }: { row: RecurringExpense | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { defaultStoreId } = useStore();
  const accounts = useAccounts();
  const banks = useBankAccounts();
  const suppliers = useSuppliers();
  const ids = { name: useId(), store: useId(), payee: useId(), account: useId(), amount: useId(), tax: useId(), how: useId(), bank: useId(), supplier: useId(), every: useId(), next: useId(), end: useId() };
  const [form, setForm] = useState<RecurringExpense>(
    row ?? {
      id: null,
      version: null,
      name: '',
      storeId: defaultStoreId ?? '',
      payee: '',
      supplierId: null,
      accountId: '',
      amount: 0,
      taxAmount: 0,
      paidBy: 'Bank',
      bankAccountId: null,
      frequency: 'Monthly',
      nextDate: today(),
      endDate: null,
      postAutomatically: false,
      isActive: true,
      lastMadeOn: null,
    },
  );
  const [amount, setAmount] = useState(row ? numberText(row.amount) : '');
  const [tax, setTax] = useState(row ? numberText(row.taxAmount) : '0');
  const [deleting, setDeleting] = useState(false);
  const set = <K extends keyof RecurringExpense>(key: K, value: RecurringExpense[K]) => setForm((f) => ({ ...f, [key]: value }));
  const done = (message: string) => {
    toast.ok(message);
    void queryClient.invalidateQueries({ queryKey: ['recurring-expenses'] });
    onClose();
  };
  const save = useMutation({
    mutationFn: (body: RecurringExpense) => (row?.id ? api.saveRecurringExpense(row.id, body) : api.addRecurringExpense(body)),
    onSuccess: (saved) => done(`${saved.name} is saved.`),
  });
  const remove = useMutation({ mutationFn: () => api.deleteRecurringExpense(row!.id!), onSuccess: () => done('It is deleted.') });
  return (
    <Dialog
      open
      width="lg"
      title={row ? row.name : 'New recurring expense'}
      onClose={onClose}
      onSubmit={() => save.mutate({ ...form, amount: amountOf(amount), taxAmount: amountOf(tax) })}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Save
          </Button>
          {row && (
            <Button variant="danger" icon={Trash2} onClick={() => setDeleting(true)}>
              Delete
            </Button>
          )}
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <Field label="Name" htmlFor={ids.name}>
          <Input id={ids.name} value={form.name} maxLength={60} onChange={(e) => set('name', e.target.value)} />
        </Field>
        <Field label="Store" htmlFor={ids.store}>
          <StoreSelect id={ids.store} value={form.storeId || null} onChange={(v) => set('storeId', v ?? '')} />
        </Field>
        <Field label="Paid to" htmlFor={ids.payee}>
          <Input id={ids.payee} value={form.payee} maxLength={100} onChange={(e) => set('payee', e.target.value)} />
        </Field>
        <Field label="Account" htmlFor={ids.account}>
          <AccountSelect id={ids.account} value={form.accountId || null} accounts={accounts.data} types={['Expense']} onChange={(v) => set('accountId', v ?? '')} />
        </Field>
        <Field label="Amount before tax" htmlFor={ids.amount}>
          <MoneyInput id={ids.amount} value={amount} onChange={setAmount} />
        </Field>
        <Field label="Tax" htmlFor={ids.tax}>
          <MoneyInput id={ids.tax} value={tax} onChange={setTax} />
        </Field>
        <Field label="Paid" htmlFor={ids.how}>
          <Select id={ids.how} value={form.paidBy} onChange={(e) => set('paidBy', e.target.value as ExpensePaidBy)}>
            <option value="Cash">In cash</option>
            <option value="Bank">From a bank account</option>
            <option value="OnAccount">On account (a bill is made)</option>
          </Select>
        </Field>
        {form.paidBy === 'OnAccount' ? (
          <Field label="Supplier" htmlFor={ids.supplier}>
            <Select id={ids.supplier} value={form.supplierId ?? ''} onChange={(e) => set('supplierId', e.target.value || null)}>
              <option value="">Choose the supplier</option>
              {(suppliers.data?.items ?? []).map((s) => (
                <option key={s.id} value={s.id}>
                  {s.company}
                </option>
              ))}
            </Select>
          </Field>
        ) : (
          <Field label="Account it is paid from" htmlFor={ids.bank}>
            <BankSelect id={ids.bank} value={form.bankAccountId} accounts={banks.data} allowEmpty={form.paidBy === 'Cash'} emptyLabel="The store’s cash in hand" onChange={(v) => set('bankAccountId', v)} />
          </Field>
        )}
        <Field label="Every" htmlFor={ids.every}>
          <Select id={ids.every} value={form.frequency} onChange={(e) => set('frequency', e.target.value as RecurrenceFrequency)}>
            <option value="Weekly">Week</option>
            <option value="Monthly">Month</option>
            <option value="Quarterly">Quarter</option>
            <option value="Yearly">Year</option>
          </Select>
        </Field>
        <Field label="Next made on" htmlFor={ids.next}>
          <Input id={ids.next} type="date" value={form.nextDate} onChange={(e) => set('nextDate', e.target.value)} />
        </Field>
        <Field label="Ends on" htmlFor={ids.end} hint="Empty: it goes on">
          <Input id={ids.end} type="date" value={form.endDate ?? ''} onChange={(e) => set('endDate', e.target.value || null)} />
        </Field>
        <div className="space-y-1 md:col-span-2">
          <Checkbox label="Post each one at once" hint="Otherwise each waits as a draft for its receipt" checked={form.postAutomatically} onChange={(v) => set('postAutomatically', v)} />
          <Checkbox label="Active" checked={form.isActive} onChange={(v) => set('isActive', v)} />
        </div>
        {save.error ? (
          <div className="md:col-span-2">
            <FormError error={save.error} />
          </div>
        ) : null}
      </div>
      <ConfirmDialog open={deleting} title={`Delete ${row?.name}?`} message="The expenses it made stay." confirmLabel="Delete" danger busy={remove.isPending} onConfirm={() => remove.mutate()} onClose={() => setDeleting(false)} />
    </Dialog>
  );
}
