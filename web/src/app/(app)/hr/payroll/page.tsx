'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ban, LogOut, Plus, Save } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useState } from 'react';
import { EmployeeSelect, employeeLabel, useHrRights } from '@/components/hr/common';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { Field, Input, Select } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { isBusinessDate, toBusinessDate } from '@/lib/dates';
import { checkNumber, parseNumber, textOrNull } from '@/lib/forms';
import { formatCount, formatDate, formatMoney } from '@/lib/format';
import { addMonths, LOAN_KINDS, LOAN_STATUS, monthKey, monthName, RUN_STATUS } from '@/lib/hr';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { LoanKind, LoanRow, RunRow } from '@/lib/types';

type Tab = 'runs' | 'loans';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'runs', label: 'Payroll runs' },
  { key: 'loans', label: 'Loans and advances' },
];

export default function PayrollPage() {
  return (
    <Suspense>
      <Payroll />
    </Suspense>
  );
}

function Payroll() {
  const params = useSearchParams();
  const [tab, setTab] = useState<Tab>('runs');
  return (
    <div>
      <PageHeader title="Payroll" />
      <Tabs label="Payroll" tabs={TABS} value={tab} onChange={setTab} className="mb-4" />
      {tab === 'runs' ? <Runs startNew={params.get('new') === '1'} /> : <Loans />}
    </div>
  );
}

function Runs({ startNew }: { startNew: boolean }) {
  const router = useRouter();
  const rights = useHrRights();
  const { storeId, currencySymbol: symbol } = useStore();
  const [year, setYear] = useState(String(new Date().getFullYear()));
  const [adding, setAdding] = useState(startNew && rights.payrollRun);
  const [settling, setSettling] = useState(false);
  const query = useQuery({ queryKey: ['hr-runs', storeId, year], queryFn: () => api.payrollRuns({ storeId, year: Number(year) || null }) });
  const rows = query.data ?? [];
  const money = (n: number) => formatMoney(n, symbol);
  return (
    <>
      <Toolbar>
        <label className="text-muted" htmlFor="runs-year">
          Year
        </label>
        <Input id="runs-year" inputMode="numeric" className="w-[110px]" value={year} maxLength={4} onChange={(e) => setYear(e.target.value)} />
        {rights.payrollRun && (
          <>
            <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setAdding(true)}>
              New payroll run
            </Button>
            <Button icon={LogOut} onClick={() => setSettling(true)}>
              Final settlement
            </Button>
          </>
        )}
      </Toolbar>
      <DataTable<RunRow>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => r.id}
        onRowClick={(r) => router.push(`/hr/payroll/run/?id=${encodeURIComponent(r.id)}`)}
        caption="Payroll runs"
        showTotals={rows.length > 0}
        empty={<EmptyState title="No payroll runs this year">{rights.payrollRun ? 'Start a month with New payroll run.' : 'Runs show here once someone starts them.'}</EmptyState>}
        columns={[
          { key: 'no', header: 'Run', cell: (r) => <span className="font-semibold">#{r.number}</span> },
          { key: 'title', header: 'For', cell: (r) => r.title },
          { key: 'store', header: 'Store', cell: (r) => r.storeCode ?? 'Company', hideBelow: 'md' },
          { key: 'status', header: 'Status', cell: (r) => <Chip tone={RUN_STATUS[r.status].tone}>{RUN_STATUS[r.status].label}</Chip> },
          { key: 'emp', header: 'Payslips', numeric: true, cell: (r) => formatCount(r.employees), total: formatCount(rows.reduce((s, r) => s + r.employees, 0)), hideBelow: 'sm' },
          { key: 'gross', header: 'Gross', numeric: true, cell: (r) => money(r.gross), total: money(rows.reduce((s, r) => s + r.gross, 0)), hideBelow: 'lg' },
          { key: 'tax', header: 'Income tax', numeric: true, cell: (r) => money(r.tax), total: money(rows.reduce((s, r) => s + r.tax, 0)), hideBelow: 'xl' },
          { key: 'net', header: 'Net pay', numeric: true, cell: (r) => <span className="font-semibold">{money(r.net)}</span>, total: money(rows.reduce((s, r) => s + r.net, 0)) },
          { key: 'warn', header: '', cell: (r) => (r.warnings.length > 0 ? <Chip tone="warn">{r.warnings.length} to check</Chip> : null), hideBelow: 'md' },
        ]}
      />
      {adding && <NewRunDialog onClose={() => setAdding(false)} />}
      {settling && <SettlementDialog onClose={() => setSettling(false)} />}
    </>
  );
}

function NewRunDialog({ onClose }: { onClose(): void }) {
  const router = useRouter();
  const { stores, storeId } = useStore();
  const { allStores } = useSession();
  const [month, setMonth] = useState(addMonths(monthKey(), 0));
  const [store, setStore] = useState(storeId ?? (allStores ? '' : stores[0]?.id ?? ''));
  const [notes, setNotes] = useState('');
  const save = useMutation({
    mutationFn: () => {
      const [year, m] = month.split('-').map(Number);
      return api.addPayrollRun({ year, month: m, storeId: store || null, notes: textOrNull(notes) });
    },
    onSuccess: (run) => router.push(`/hr/payroll/run/?id=${encodeURIComponent(run.id)}`),
  });
  return (
    <Dialog
      open
      title="New payroll run"
      onClose={onClose}
      onSubmit={() => save.mutate()}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Start
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Month" htmlFor="run-month" hint={monthName(month)}>
          <Input id="run-month" type="month" className="max-w-[200px]" value={month} onChange={(e) => e.target.value && setMonth(e.target.value)} />
        </Field>
        <Field label="Who" htmlFor="run-store" hint="A month has one run for each store, or one for the whole company.">
          <Select id="run-store" value={store} onChange={(e) => setStore(e.target.value)}>
            {allStores && <option value="">The whole company</option>}
            {stores.map((s) => (
              <option key={s.id} value={s.id}>
                {s.code} · {s.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Notes" htmlFor="run-notes">
          <Input id="run-notes" value={notes} maxLength={400} onChange={(e) => setNotes(e.target.value)} />
        </Field>
      </div>
      <FormError error={save.error} />
    </Dialog>
  );
}

function SettlementDialog({ onClose }: { onClose(): void }) {
  const router = useRouter();
  const { storeId } = useStore();
  const [employeeId, setEmployeeId] = useState('');
  const [notes, setNotes] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const leavers = useQuery({
    queryKey: ['hr-leavers', storeId],
    queryFn: () => api.employees({ storeId, status: 'all', pageSize: 500 }),
    select: (page) => page.items.filter((e) => e.leavingDate),
  });
  const save = useMutation({
    mutationFn: () => api.addSettlement({ employeeId, notes: textOrNull(notes) }),
    onSuccess: (run) => router.push(`/hr/payroll/run/?id=${encodeURIComponent(run.id)}`),
  });
  return (
    <Dialog
      open
      title="Final settlement"
      onClose={onClose}
      onSubmit={() => {
        const p = employeeId ? null : 'Choose the employee who leaves.';
        setProblem(p);
        if (!p) save.mutate();
      }}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Start
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <p className="mb-3 text-ink">The last pay of someone who leaves: the days of their last month, leave paid out, gratuity and the loans they still owe.</p>
      <div className="space-y-3">
        <Field label="Employee" htmlFor="settle-employee" hint="Those with a leaving date; give it on their record first.">
          <Select id="settle-employee" value={employeeId} onChange={(e) => setEmployeeId(e.target.value)}>
            <option value="">{leavers.isLoading ? 'Loading…' : 'Choose…'}</option>
            {(leavers.data ?? []).map((e) => (
              <option key={e.id} value={e.id}>
                {employeeLabel(e)}, leaves {formatDate(e.leavingDate)}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Notes" htmlFor="settle-notes">
          <Input id="settle-notes" value={notes} maxLength={400} onChange={(e) => setNotes(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? save.error} />
    </Dialog>
  );
}

function Loans() {
  const rights = useHrRights();
  const { storeId, currencySymbol: symbol } = useStore();
  const [status, setStatus] = useState('Active');
  const [adding, setAdding] = useState(false);
  const [cancelling, setCancelling] = useState<LoanRow | null>(null);
  const query = useQuery({ queryKey: ['hr-loans', storeId, status], queryFn: () => api.loans({ storeId, status }) });
  const rows = query.data ?? [];
  const money = (n: number) => formatMoney(n, symbol);
  return (
    <>
      <Toolbar>
        <label className="sr-only" htmlFor="loan-status">
          Status
        </label>
        <Select id="loan-status" className="w-[200px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="Active">Being recovered</option>
          <option value="Settled">Recovered</option>
          <option value="Cancelled">Cancelled</option>
          <option value="">All</option>
        </Select>
        {rights.payrollRun && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setAdding(true)}>
            New loan
          </Button>
        )}
      </Toolbar>
      <DataTable<LoanRow>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(l) => l.id}
        caption="Loans and advances"
        showTotals={rows.length > 0}
        empty={<EmptyState title="No loans">Loans and salary advances are taken back from pay in monthly instalments.</EmptyState>}
        columns={[
          { key: 'no', header: 'Loan', cell: (l) => <span className="font-semibold">#{l.number}</span> },
          { key: 'who', header: 'Employee', cell: (l) => l.name },
          { key: 'kind', header: 'Kind', cell: (l) => LOAN_KINDS[l.kind] ?? l.kind, hideBelow: 'md' },
          { key: 'paid', header: 'Paid', cell: (l) => formatDate(l.issuedOn), hideBelow: 'lg' },
          { key: 'amount', header: 'Amount', numeric: true, cell: (l) => money(l.amount), total: money(rows.reduce((s, l) => s + l.amount, 0)) },
          { key: 'inst', header: 'A month', numeric: true, cell: (l) => money(l.instalment), hideBelow: 'md' },
          { key: 'first', header: 'From', cell: (l) => monthName(l.firstMonth.slice(0, 7)), hideBelow: 'xl' },
          { key: 'rec', header: 'Recovered', numeric: true, cell: (l) => money(l.recovered), total: money(rows.reduce((s, l) => s + l.recovered, 0)), hideBelow: 'sm' },
          { key: 'out', header: 'Owed', numeric: true, cell: (l) => <span className="font-semibold">{money(l.outstanding)}</span>, total: money(rows.reduce((s, l) => s + l.outstanding, 0)) },
          { key: 'status', header: 'Status', cell: (l) => <Chip tone={LOAN_STATUS[l.status].tone}>{LOAN_STATUS[l.status].label}</Chip>, hideBelow: 'sm' },
          {
            key: 'act',
            header: '',
            cell: (l) =>
              rights.payrollRun && l.status === 'Active' && l.recovered === 0 ? (
                <Button icon={Ban} className="min-h-0 py-1.5" onClick={() => setCancelling(l)}>
                  Cancel
                </Button>
              ) : null,
          },
        ]}
      />
      {adding && <LoanDialog onClose={() => setAdding(false)} />}
      {cancelling && <CancelLoanDialog loan={cancelling} onClose={() => setCancelling(null)} />}
    </>
  );
}

function LoanDialog({ onClose }: { onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const today = toBusinessDate(new Date());
  const [employeeId, setEmployeeId] = useState('');
  const [kind, setKind] = useState<LoanKind>('Loan');
  const [issuedOn, setIssuedOn] = useState(today);
  const [amount, setAmount] = useState('');
  const [instalment, setInstalment] = useState('');
  const [firstMonth, setFirstMonth] = useState(addMonths(monthKey(), 1));
  const [paidFrom, setPaidFrom] = useState('Cash');
  const [account, setAccount] = useState('');
  const [reason, setReason] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () =>
      api.issueLoan({
        employeeId,
        kind,
        issuedOn,
        amount: parseNumber(amount) ?? 0,
        instalment: parseNumber(instalment) ?? 0,
        firstMonth: `${firstMonth}-01`,
        reason: textOrNull(reason),
        paidFrom: paidFrom === 'account' ? textOrNull(account) : paidFrom,
      }),
    onSuccess: (loan) => {
      toast.ok(`Loan #${loan.number} is paid and posted.`);
      void queryClient.invalidateQueries({ queryKey: ['hr-loans'] });
      onClose();
    },
  });
  function submit() {
    const p = !employeeId
      ? 'Choose the employee.'
      : !isBusinessDate(issuedOn)
        ? 'Give the day it is paid.'
        : checkNumber(amount, { required: true, label: 'The amount' }) ??
          checkNumber(instalment, { required: true, label: 'The instalment' }) ??
          ((parseNumber(instalment) ?? 0) > (parseNumber(amount) ?? 0) ? 'The instalment is at most the amount.' : paidFrom === 'account' && !account.trim() ? 'Give the account it is paid from.' : null);
    setProblem(p ?? null);
    if (!p) save.mutate();
  }
  return (
    <Dialog
      open
      title="New loan or advance"
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Pay it
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Employee" htmlFor="loan-employee">
          <EmployeeSelect id="loan-employee" value={employeeId} onChange={setEmployeeId} />
        </Field>
        <Field label="Kind" htmlFor="loan-kind">
          <Select id="loan-kind" value={kind} onChange={(e) => setKind(e.target.value as LoanKind)}>
            {Object.entries(LOAN_KINDS).map(([k, v]) => (
              <option key={k} value={k}>
                {v}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Paid on" htmlFor="loan-date">
          <Input id="loan-date" type="date" className="max-w-[200px]" value={issuedOn} onChange={(e) => setIssuedOn(e.target.value)} />
        </Field>
        <Field label="Amount" htmlFor="loan-amount">
          <Input id="loan-amount" inputMode="decimal" className="max-w-[200px]" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </Field>
        <Field label="Taken back a month" htmlFor="loan-instalment">
          <Input id="loan-instalment" inputMode="decimal" className="max-w-[200px]" value={instalment} onChange={(e) => setInstalment(e.target.value)} />
        </Field>
        <Field label="From the pay of" htmlFor="loan-first">
          <Input id="loan-first" type="month" className="max-w-[200px]" value={firstMonth} onChange={(e) => e.target.value && setFirstMonth(e.target.value)} />
        </Field>
        <Field label="Paid from" htmlFor="loan-from" hint="Posted to the ledger: advances to employees against this account.">
          <Select id="loan-from" value={paidFrom} onChange={(e) => setPaidFrom(e.target.value)}>
            <option value="Cash">Cash</option>
            <option value="Bank">Bank</option>
            <option value="account">Another account…</option>
          </Select>
        </Field>
        {paidFrom === 'account' && (
          <Field label="Account code" htmlFor="loan-account">
            <Input id="loan-account" className="max-w-[200px]" value={account} maxLength={20} onChange={(e) => setAccount(e.target.value)} />
          </Field>
        )}
        <Field label="Reason" htmlFor="loan-reason">
          <Input id="loan-reason" value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? save.error} />
    </Dialog>
  );
}

function CancelLoanDialog({ loan, onClose }: { loan: LoanRow; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [reason, setReason] = useState('');
  const save = useMutation({
    mutationFn: () => api.cancelLoan(loan.id, reason.trim()),
    onSuccess: () => {
      toast.ok(`Loan #${loan.number} is cancelled and its payment reversed.`);
      void queryClient.invalidateQueries({ queryKey: ['hr-loans'] });
      onClose();
    },
  });
  return (
    <Dialog
      open
      width="sm"
      title={`Cancel loan #${loan.number}?`}
      onClose={onClose}
      onSubmit={() => reason.trim() && save.mutate()}
      footer={
        <>
          <Button type="submit" variant="danger" icon={Ban} shortcut="Enter" busy={save.isPending} disabled={!reason.trim()}>
            Cancel the loan
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Keep it
          </Button>
        </>
      }
    >
      <Field label="Why" htmlFor="cancel-reason" stacked>
        <Input id="cancel-reason" value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} />
      </Field>
      <FormError error={save.error} />
    </Dialog>
  );
}
