'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeftRight, Landmark, Plus, Save, Scale } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useId, useState } from 'react';
import { amountOf, BankSelect, MoneyInput, StoreSelect, today, useAccounts, useBankAccounts, useMoney } from '@/components/finance';
import { Button } from '@/components/ui/button';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select } from '@/components/ui/field';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { label, postingAccounts } from '@/lib/finance';
import { formatDate, formatLocalDateTime } from '@/lib/format';
import { numberText } from '@/lib/forms';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { BankAccountKind, BankAccountRequest, BankAccountRow, BankTransferRow, DepositCandidate, DepositRow } from '@/lib/types';

type Tab = 'accounts' | 'transfers' | 'deposits';

export default function BankPage() {
  return (
    <Suspense>
      <Bank />
    </Suspense>
  );
}

/** Bank and cash accounts with their balances, money moved between them, and till cash taken to the bank. */
function Bank() {
  const params = useSearchParams();
  const router = useRouter();
  const { can } = useSession();
  const [tab, setTab] = useState<Tab>('accounts');
  const [transferring, setTransferring] = useState(false);
  useEffect(() => {
    if (params.get('transfer') === '1' && can(P.financeBank)) {
      setTab('transfers');
      setTransferring(true);
      router.replace('/finance/bank/');
    }
  }, [params, can, router]);
  return (
    <div>
      <PageHeader title="Bank and cash" />
      <Toolbar>
        <Tabs
          label="Bank and cash"
          tabs={[
            { key: 'accounts', label: 'Accounts' },
            { key: 'transfers', label: 'Transfers' },
            { key: 'deposits', label: 'Till cash to the bank' },
          ]}
          value={tab}
          onChange={setTab}
        />
      </Toolbar>
      {tab === 'accounts' ? <Accounts /> : tab === 'transfers' ? <Transfers transferring={transferring} setTransferring={setTransferring} /> : <Deposits />}
    </div>
  );
}

function Accounts() {
  const router = useRouter();
  const { can } = useSession();
  const money = useMoney();
  const banks = useBankAccounts();
  const [editing, setEditing] = useState<BankAccountRow | 'new' | null>(null);
  const [selected, setSelected] = useState<BankAccountRow | null>(null);
  return (
    <>
      <Toolbar>
        {can(P.financeBank) && (
          <>
            <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
              New account
            </Button>
            <Button icon={Landmark} disabled={!selected} onClick={() => selected && setEditing(selected)}>
              Change
            </Button>
          </>
        )}
        <Button icon={Scale} disabled={!selected} onClick={() => selected && router.push(`/finance/bank/reconcile/?id=${encodeURIComponent(selected.id)}`)}>
          Statements and reconciliation
        </Button>
      </Toolbar>
      <DataTable<BankAccountRow>
        caption="Bank and cash accounts"
        rows={banks.data}
        loading={banks.isLoading}
        error={banks.error}
        onRetry={() => void banks.refetch()}
        rowKey={(a) => a.id}
        selectedKey={selected?.id}
        onRowClick={(a) => (selected?.id === a.id ? router.push(`/finance/bank/reconcile/?id=${encodeURIComponent(a.id)}`) : setSelected(a))}
        empty={<EmptyState title="No bank accounts">Add the company’s bank accounts and cash boxes.</EmptyState>}
        columns={[
          { key: 'name', header: 'Account', cell: (a) => <span className="font-semibold">{a.name}</span> },
          { key: 'kind', header: 'Kind', cell: (a) => label(a.kind), hideBelow: 'sm' },
          { key: 'bank', header: 'Bank and number', cell: (a) => [a.bankName, a.accountNumber ?? a.iban].filter(Boolean).join(' · '), hideBelow: 'md' },
          { key: 'ledger', header: 'Ledger account', cell: (a) => a.ledgerCode, hideBelow: 'lg' },
          { key: 'store', header: 'Store', cell: (a) => a.storeCode ?? 'Company', hideBelow: 'md' },
          { key: 'active', header: 'Active', cell: (a) => (a.isActive ? '' : 'Inactive'), hideBelow: 'lg' },
          { key: 'balance', header: 'Balance in the books', numeric: true, cell: (a) => money(a.balance) },
        ]}
      />
      {editing && <AccountDialog account={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function AccountDialog({ account, onClose }: { account: BankAccountRow | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { defaultStoreId, canSwitch } = useStore();
  const accounts = useAccounts();
  const ids = { name: useId(), kind: useId(), bank: useId(), number: useId(), iban: useId(), currency: useId(), ledger: useId(), store: useId(), opening: useId(), on: useId() };
  const [form, setForm] = useState<BankAccountRequest>({
    name: account?.name ?? '',
    kind: account?.kind ?? 'Bank',
    bankName: account?.bankName ?? null,
    accountNumber: account?.accountNumber ?? null,
    iban: account?.iban ?? null,
    currency: account?.currency ?? null,
    ledgerAccountId: account?.ledgerAccountId ?? null,
    storeId: account ? account.storeId : canSwitch ? null : defaultStoreId,
    openingBalance: account?.openingBalance ?? 0,
    openingDate: account?.openingDate ?? null,
    isActive: account?.isActive ?? true,
    version: account?.version ?? null,
  });
  const [opening, setOpening] = useState(account ? numberText(account.openingBalance) : '0');
  const set = <K extends keyof BankAccountRequest>(key: K, value: BankAccountRequest[K]) => setForm((f) => ({ ...f, [key]: value }));
  const save = useMutation({
    mutationFn: (body: BankAccountRequest) => (account ? api.saveBankAccount(account.id, body) : api.addBankAccount(body)),
    onSuccess: (saved) => {
      toast.ok(`${saved.name} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['bank-accounts'] });
      void queryClient.invalidateQueries({ queryKey: ['finance-accounts'] });
      onClose();
    },
  });
  const assets = postingAccounts(accounts.data ?? [], ['Asset']);
  return (
    <Dialog
      open
      width="lg"
      title={account ? account.name : 'New bank or cash account'}
      onClose={onClose}
      onSubmit={() => save.mutate({ ...form, openingBalance: amountOf(opening), name: form.name.trim() })}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Save
          </Button>
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
        <Field label="Kind" htmlFor={ids.kind}>
          <Select id={ids.kind} value={form.kind} onChange={(e) => set('kind', e.target.value as BankAccountKind)}>
            <option value="Bank">Bank account</option>
            <option value="Cash">Cash box</option>
            <option value="Wallet">Wallet or card settlement</option>
          </Select>
        </Field>
        <Field label="Bank" htmlFor={ids.bank} hint="Statements of one bank are read the same way">
          <Input id={ids.bank} value={form.bankName ?? ''} maxLength={60} onChange={(e) => set('bankName', e.target.value || null)} />
        </Field>
        <Field label="Account number" htmlFor={ids.number}>
          <Input id={ids.number} value={form.accountNumber ?? ''} maxLength={40} onChange={(e) => set('accountNumber', e.target.value || null)} />
        </Field>
        <Field label="IBAN" htmlFor={ids.iban}>
          <Input id={ids.iban} value={form.iban ?? ''} maxLength={34} onChange={(e) => set('iban', e.target.value || null)} />
        </Field>
        <Field label="Currency" htmlFor={ids.currency} hint="Empty for the books’ own">
          <Input id={ids.currency} value={form.currency ?? ''} maxLength={3} onChange={(e) => set('currency', e.target.value.toUpperCase() || null)} />
        </Field>
        <Field label="Ledger account" htmlFor={ids.ledger}>
          <Select id={ids.ledger} value={form.ledgerAccountId ?? ''} onChange={(e) => set('ledgerAccountId', e.target.value || null)}>
            <option value="">Make one for it</option>
            {assets.map((a) => (
              <option key={a.id} value={a.id}>
                {a.code} {a.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Store" htmlFor={ids.store}>
          <StoreSelect id={ids.store} value={form.storeId} onChange={(v) => set('storeId', v)} allowCompany />
        </Field>
        <Field label="Opening balance" htmlFor={ids.opening}>
          <MoneyInput id={ids.opening} value={opening} onChange={setOpening} />
        </Field>
        <Field label="On" htmlFor={ids.on}>
          <Input id={ids.on} type="date" value={form.openingDate ?? ''} onChange={(e) => set('openingDate', e.target.value || null)} />
        </Field>
        <Checkbox label="Active" checked={form.isActive} onChange={(v) => set('isActive', v)} />
        {save.error ? (
          <div className="md:col-span-2">
            <FormError error={save.error} />
          </div>
        ) : null}
      </div>
    </Dialog>
  );
}

function Transfers({ transferring, setTransferring }: { transferring: boolean; setTransferring(open: boolean): void }) {
  const { storeId } = useStore();
  const { can } = useSession();
  const money = useMoney();
  const dates = useDateRange('month');
  const query = useQuery({ queryKey: ['bank-transfers', storeId, dates.range], queryFn: () => api.bankTransfers({ storeId, ...dates.range }) });
  return (
    <>
      <Toolbar>
        <DateRangeBar state={dates} compact />
        {can(P.financeBank) && (
          <Button variant="sun" icon={ArrowLeftRight} onClick={() => setTransferring(true)}>
            New transfer
          </Button>
        )}
      </Toolbar>
      <DataTable<BankTransferRow>
        caption="Transfers"
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(t) => t.id}
        empty={<EmptyState title="No transfers">No money moved between the accounts on these days.</EmptyState>}
        columns={[
          { key: 'number', header: 'Transfer', cell: (t) => <span className="font-semibold">{t.number}</span> },
          { key: 'date', header: 'Date', cell: (t) => formatDate(t.date) },
          { key: 'from', header: 'From', cell: (t) => t.fromAccount },
          { key: 'to', header: 'To', cell: (t) => t.toAccount },
          { key: 'ref', header: 'Reference', cell: (t) => t.reference ?? '', hideBelow: 'md' },
          { key: 'amount', header: 'Amount', numeric: true, cell: (t) => money(t.amount) },
        ]}
      />
      {transferring && <TransferDialog onClose={() => setTransferring(false)} />}
    </>
  );
}

function TransferDialog({ onClose }: { onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const banks = useBankAccounts();
  const ids = { from: useId(), to: useId(), date: useId(), amount: useId(), arrived: useId(), ref: useId() };
  const [fromId, setFromId] = useState<string | null>(null);
  const [toId, setToId] = useState<string | null>(null);
  const [date, setDate] = useState(today());
  const [amount, setAmount] = useState('');
  const [arrived, setArrived] = useState('');
  const [reference, setReference] = useState('');
  const from = banks.data?.find((b) => b.id === fromId);
  const to = banks.data?.find((b) => b.id === toId);
  const currencies = !!from && !!to && from.currency !== to.currency;
  const save = useMutation({
    mutationFn: () =>
      api.bankTransfer({ fromAccountId: fromId!, toAccountId: toId!, date, amount: amountOf(amount), toAmount: currencies ? amountOf(arrived) : null, reference: reference.trim() || null, memo: null }),
    onSuccess: (t) => {
      toast.ok(`Transfer ${t.number} is posted.`);
      void queryClient.invalidateQueries({ queryKey: ['bank-transfers'] });
      void queryClient.invalidateQueries({ queryKey: ['bank-accounts'] });
      onClose();
    },
  });
  return (
    <Dialog
      open
      title="Move money between accounts"
      onClose={onClose}
      onSubmit={() => fromId && toId && save.mutate()}
      footer={
        <>
          <Button type="submit" variant="sun" icon={ArrowLeftRight} shortcut="Enter" busy={save.isPending} disabled={!fromId || !toId || amountOf(amount) <= 0}>
            Transfer
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="From" htmlFor={ids.from}>
          <BankSelect id={ids.from} value={fromId} onChange={setFromId} accounts={banks.data} />
        </Field>
        <Field label="To" htmlFor={ids.to}>
          <BankSelect id={ids.to} value={toId} onChange={setToId} accounts={banks.data?.filter((b) => b.id !== fromId)} />
        </Field>
        <Field label="Date" htmlFor={ids.date}>
          <Input id={ids.date} type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        </Field>
        <Field label={currencies ? `Amount in ${from!.currency}` : 'Amount'} htmlFor={ids.amount}>
          <MoneyInput id={ids.amount} value={amount} onChange={setAmount} />
        </Field>
        {currencies && (
          <Field label={`What arrived in ${to!.currency}`} htmlFor={ids.arrived}>
            <MoneyInput id={ids.arrived} value={arrived} onChange={setArrived} />
          </Field>
        )}
        <Field label="Reference" htmlFor={ids.ref}>
          <Input id={ids.ref} value={reference} maxLength={40} onChange={(e) => setReference(e.target.value)} />
        </Field>
        {save.error ? <FormError error={save.error} /> : null}
      </div>
    </Dialog>
  );
}

function Deposits() {
  const { storeId } = useStore();
  const { can } = useSession();
  const money = useMoney();
  const dates = useDateRange('week');
  const [depositing, setDepositing] = useState<DepositCandidate | 'other' | null>(null);
  const candidates = useQuery({ queryKey: ['deposit-candidates', storeId, dates.range], queryFn: () => api.depositCandidates({ storeId, ...dates.range }) });
  const deposits = useQuery({ queryKey: ['deposits', storeId, dates.range], queryFn: () => api.deposits({ storeId, ...dates.range }) });
  return (
    <>
      <Toolbar>
        <DateRangeBar state={dates} compact />
        {can(P.financeBank) && (
          <Button icon={Plus} onClick={() => setDepositing('other')}>
            Other deposit
          </Button>
        )}
      </Toolbar>
      <h2 className="section-title mb-2">End of Day cash</h2>
      <DataTable<DepositCandidate>
        caption="End of Day cash"
        rows={candidates.data}
        loading={candidates.isLoading}
        error={candidates.error}
        onRetry={() => void candidates.refetch()}
        rowKey={(c) => c.drawerSessionId}
        onRowClick={(c) => !c.depositId && can(P.financeBank) && setDepositing(c)}
        empty={<EmptyState title="No End of Day">No till ran End of Day on these days.</EmptyState>}
        columns={[
          { key: 'when', header: 'End of Day', cell: (c) => formatLocalDateTime(c.closedAt) },
          { key: 'till', header: 'Till', cell: (c) => `${c.storeCode} ${c.stationNumber}` },
          { key: 'state', header: 'Deposited', cell: (c) => (c.depositId ? 'Yes' : 'Not yet') },
          { key: 'cash', header: 'Cash taken', numeric: true, cell: (c) => money(c.cash) },
        ]}
      />
      <h2 className="section-title mb-2 mt-4">Deposits</h2>
      <DataTable<DepositRow>
        caption="Deposits"
        rows={deposits.data}
        loading={deposits.isLoading}
        error={deposits.error}
        rowKey={(d) => d.id}
        empty={<EmptyState title="No deposits">Nothing was taken to the bank on these days.</EmptyState>}
        columns={[
          { key: 'number', header: 'Deposit', cell: (d) => <span className="font-semibold">{d.number}</span> },
          { key: 'date', header: 'Date', cell: (d) => formatDate(d.date) },
          { key: 'store', header: 'Store', cell: (d) => d.storeCode },
          { key: 'bank', header: 'To', cell: (d) => d.bankAccount },
          { key: 'ref', header: 'Slip', cell: (d) => d.reference ?? '', hideBelow: 'md' },
          { key: 'amount', header: 'Amount', numeric: true, cell: (d) => money(d.amount) },
        ]}
      />
      {depositing && <DepositDialog candidate={depositing === 'other' ? null : depositing} onClose={() => setDepositing(null)} />}
    </>
  );
}

function DepositDialog({ candidate, onClose }: { candidate: DepositCandidate | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { defaultStoreId } = useStore();
  const banks = useBankAccounts();
  const ids = { store: useId(), bank: useId(), date: useId(), amount: useId(), ref: useId() };
  const [storeId, setStoreId] = useState<string | null>(candidate?.storeId ?? defaultStoreId);
  const finance = useQuery({ queryKey: ['store-finance', storeId], queryFn: () => api.storeFinance(storeId!), enabled: !!storeId });
  const [bankId, setBankId] = useState<string | null>(null);
  const [date, setDate] = useState(today());
  const [amount, setAmount] = useState(candidate ? numberText(candidate.cash) : '');
  const [reference, setReference] = useState('');
  useEffect(() => {
    if (!bankId && finance.data?.depositBankAccountId) setBankId(finance.data.depositBankAccountId);
  }, [bankId, finance.data]);
  const save = useMutation({
    mutationFn: () => api.deposit({ storeId: storeId!, bankAccountId: bankId!, date, amount: amountOf(amount), drawerSessionId: candidate?.drawerSessionId ?? null, reference: reference.trim() || null }),
    onSuccess: (d) => {
      toast.ok(`Deposit ${d.number} is posted.`);
      for (const key of ['deposits', 'deposit-candidates', 'bank-accounts', 'finance-home']) void queryClient.invalidateQueries({ queryKey: [key] });
      onClose();
    },
  });
  return (
    <Dialog
      open
      title={candidate ? `Deposit the cash of till ${candidate.storeCode} ${candidate.stationNumber}` : 'Deposit till cash'}
      onClose={onClose}
      onSubmit={() => storeId && bankId && save.mutate()}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Landmark} shortcut="Enter" busy={save.isPending} disabled={!storeId || !bankId || amountOf(amount) <= 0}>
            Deposit
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        {!candidate && (
          <Field label="Store" htmlFor={ids.store}>
            <StoreSelect id={ids.store} value={storeId} onChange={setStoreId} />
          </Field>
        )}
        <Field label="Bank account" htmlFor={ids.bank}>
          <BankSelect id={ids.bank} value={bankId} onChange={setBankId} accounts={banks.data} />
        </Field>
        <Field label="Date" htmlFor={ids.date}>
          <Input id={ids.date} type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        </Field>
        <Field label="Amount" htmlFor={ids.amount} hint={candidate ? 'Less when a float stays in the drawer' : undefined}>
          <MoneyInput id={ids.amount} value={amount} onChange={setAmount} />
        </Field>
        <Field label="Deposit slip" htmlFor={ids.ref}>
          <Input id={ids.ref} value={reference} maxLength={40} onChange={(e) => setReference(e.target.value)} />
        </Field>
        {save.error ? <FormError error={save.error} /> : null}
      </div>
    </Dialog>
  );
}
