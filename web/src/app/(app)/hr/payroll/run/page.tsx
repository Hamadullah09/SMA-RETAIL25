'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, BadgeCheck, Banknote, Calculator, Download, LockOpen, Printer, Send, Trash2, Wallet } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useState } from 'react';
import { useHrRights } from '@/components/hr/common';
import { ConflictDialog } from '@/components/masters';
import { Button, LinkButton } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Field, Input } from '@/components/ui/field';
import { Chip, Detail, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { cx } from '@/lib/cx';
import { isBusinessDate, toBusinessDate } from '@/lib/dates';
import { textOrNull } from '@/lib/forms';
import { formatCount, formatDate, formatMoney, formatQuantity, formatShortUtc } from '@/lib/format';
import { RUN_STATUS, RUN_STEPS, runActions, saveBlob, type RunAction } from '@/lib/hr';
import { useHotkey } from '@/lib/hotkeys';
import { useStore } from '@/lib/store';
import type { PayslipRow, RunRow } from '@/lib/types';

export default function RunPage() {
  return (
    <Suspense>
      <RunLoader />
    </Suspense>
  );
}

function RunLoader() {
  const id = useSearchParams().get('id') ?? '';
  const query = useQuery({ queryKey: ['hr-run', id], queryFn: () => api.payrollRun(id), enabled: !!id });
  if (!id) {
    return (
      <div className="card">
        <EmptyState title="No payroll run chosen">Choose one on the Payroll page.</EmptyState>
      </div>
    );
  }
  if (query.error) {
    return (
      <div className="card">
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </div>
    );
  }
  if (!query.data) {
    return (
      <div className="space-y-4" aria-busy="true">
        <Skeleton className="h-11 w-72" />
        <Skeleton className="h-[50vh] w-full rounded-card" />
      </div>
    );
  }
  return <RunView run={query.data.run} payslips={query.data.payslips} />;
}

const LABELS: Record<RunAction, string> = {
  calculate: 'Calculate',
  approve: 'Approve',
  reopen: 'Reopen',
  post: 'Post to the ledger',
  pay: 'Record payment',
  delete: 'Delete',
};

type View = 'sheet' | 'cash';

function RunView({ run, payslips }: { run: RunRow; payslips: PayslipRow[] }) {
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const rights = useHrRights();
  const { currencySymbol: symbol } = useStore();
  const [view, setView] = useState<View>('sheet');
  const [paying, setPaying] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [conflict, setConflict] = useState(false);
  const money = (n: number) => formatMoney(n, symbol);
  const actions = runActions(run.status, { run: rights.payrollRun, approve: rights.payrollApprove });
  const step = RUN_STEPS.indexOf(run.status);
  useHotkey('Esc', () => router.push('/hr/payroll/'));

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['hr-run', run.id] });
    void queryClient.invalidateQueries({ queryKey: ['hr-runs'] });
  };
  const act = useMutation({
    mutationFn: (action: 'calculate' | 'approve' | 'reopen' | 'post') =>
      action === 'calculate'
        ? api.calculateRun(run.id, run.version)
        : action === 'approve'
          ? api.approveRun(run.id, run.version)
          : action === 'reopen'
            ? api.reopenRun(run.id, run.version)
            : api.postRun(run.id, run.version),
    onSuccess: (saved, action) => {
      toast.ok(
        action === 'calculate'
          ? `Calculated: ${formatCount(saved.employees)} payslips.`
          : action === 'approve'
            ? 'The run is approved.'
            : action === 'reopen'
              ? 'The run is open to change again.'
              : `Posted to the ledger${saved.journalNumber ? ` as entry #${saved.journalNumber}` : ''}.`,
      );
      refresh();
    },
    onError: (e: Error) => (e instanceof ApiError && e.isConflict ? setConflict(true) : toast.error(e.message)),
  });
  const remove = useMutation({
    mutationFn: () => api.deletePayrollRun(run.id),
    onSuccess: () => {
      toast.ok(`Run #${run.number} is deleted.`);
      void queryClient.invalidateQueries({ queryKey: ['hr-runs'] });
      router.push('/hr/payroll/');
    },
    onError: (e: Error) => {
      setDeleting(false);
      toast.error(e.message);
    },
  });

  async function bankList() {
    try {
      saveBlob(await api.payrollBankTransfers(run.id), `bank-transfers-PR${run.number}.csv`);
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'The list did not download.');
    }
  }

  const sum = (pick: (p: PayslipRow) => number) => money(payslips.reduce((s, p) => s + pick(p), 0));

  return (
    <div>
      <Toolbar>
        <LinkButton href="/hr/payroll/" icon={ArrowLeft} shortcut="Esc">
          Payroll
        </LinkButton>
        {actions.map((a) => {
          if (a === 'pay')
            return (
              <Button key={a} variant="sun" icon={Wallet} onClick={() => setPaying(true)}>
                {LABELS[a]}
              </Button>
            );
          if (a === 'delete')
            return (
              <Button key={a} icon={Trash2} onClick={() => setDeleting(true)}>
                {LABELS[a]}
              </Button>
            );
          const Icon = a === 'calculate' ? Calculator : a === 'approve' ? BadgeCheck : a === 'reopen' ? LockOpen : Send;
          const main = (a === 'calculate' && run.status === 'Draft') || a === 'approve' || a === 'post';
          return (
            <Button key={a} variant={main ? 'sun' : 'plain'} icon={Icon} busy={act.isPending && act.variables === a} disabled={act.isPending} onClick={() => act.mutate(a)}>
              {a === 'calculate' && run.status === 'Calculated' ? 'Calculate again' : LABELS[a]}
            </Button>
          );
        })}
      </Toolbar>

      <header className="mb-3 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="page-title">
            Payroll #{run.number}: {run.title}
          </h1>
          <p className="text-muted">
            {run.storeCode ? `Store ${run.storeCode}` : 'The whole company'} · {formatDate(run.periodStart)} to {formatDate(run.periodEnd)}
            {run.notes ? ` · ${run.notes}` : ''}
          </p>
        </div>
        <Chip tone={RUN_STATUS[run.status].tone}>{RUN_STATUS[run.status].label}</Chip>
      </header>

      <ol className="no-print mb-4 flex flex-wrap gap-2" aria-label="Steps">
        {RUN_STEPS.map((s, i) => (
          <li
            key={s}
            aria-current={i === step ? 'step' : undefined}
            className={cx('rounded-full px-4 py-1.5 font-semibold', i < step ? 'bg-ok-light text-ok-ink' : i === step ? 'bg-charcoal text-white' : 'bg-card text-muted')}
          >
            {i + 1}. {RUN_STATUS[s].label}
          </li>
        ))}
      </ol>

      {run.warnings.length > 0 && (
        <div className="no-print mb-4 rounded-card bg-sun-light px-5 py-3 text-ink" role="status">
          <p className="font-semibold">Left out of this run:</p>
          <ul className="list-disc pl-5">
            {run.warnings.map((w) => (
              <li key={w}>{w}</li>
            ))}
          </ul>
        </div>
      )}

      <div className="mb-4 grid gap-4 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <Card title="Totals">
          <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <Detail label="Payslips">{formatCount(run.employees)}</Detail>
            <Detail label="Gross">{money(run.gross)}</Detail>
            <Detail label="Deductions">{money(run.deductions)}</Detail>
            <Detail label="Net pay">{money(run.net)}</Detail>
            <Detail label="Income tax">{money(run.tax)}</Detail>
            <Detail label="Employer contributions">{money(run.employerContributions)}</Detail>
          </dl>
        </Card>
        <Card title="Who did what">
          <dl className="grid grid-cols-2 gap-3">
            <Detail label="Started by">{run.createdBy}</Detail>
            <Detail label="Calculated">{run.calculatedBy ? `${run.calculatedBy}, ${formatShortUtc(run.calculatedAtUtc)}` : ''}</Detail>
            <Detail label="Approved">{run.approvedBy ? `${run.approvedBy}, ${formatShortUtc(run.approvedAtUtc)}` : ''}</Detail>
            <Detail label="Posted">{run.postedBy ? `${run.postedBy}${run.journalNumber ? `, entry #${run.journalNumber}` : ''}` : ''}</Detail>
            <Detail label="Paid">{run.paidOn ? `${formatDate(run.paidOn)}${run.paidBy ? `, ${run.paidBy}` : ''}` : ''}</Detail>
          </dl>
        </Card>
      </div>

      <Toolbar>
        <Tabs label="Show" tabs={[{ key: 'sheet', label: 'Salary sheet' }, { key: 'cash', label: 'Cash sheet' }]} value={view} onChange={setView} />
        <Button icon={Printer} shortcut="Ctrl+P" disabled={payslips.length === 0} onClick={() => window.print()}>
          Print
        </Button>
        <Button icon={Download} disabled={payslips.length === 0} onClick={() => void bankList()}>
          Bank transfer list
        </Button>
      </Toolbar>

      {view === 'sheet' ? (
        <DataTable<PayslipRow>
          rows={payslips}
          rowKey={(p) => p.id}
          onRowClick={(p) => router.push(`/hr/payroll/payslip/?id=${encodeURIComponent(p.id)}`)}
          caption="Salary sheet"
          showTotals={payslips.length > 0}
          empty={<EmptyState title={run.status === 'Draft' ? 'Not calculated yet' : 'Nobody is paid in this run'}>{run.status === 'Draft' ? 'Calculate makes a payslip for every employee.' : 'Employees without a salary are left out.'}</EmptyState>}
          columns={[
            { key: 'who', header: 'Employee', cell: (p) => <span className="font-semibold">{p.code} {p.name}</span> },
            { key: 'days', header: 'Days paid', numeric: true, cell: (p) => formatQuantity(p.daysPresent + p.paidLeaveDays), hideBelow: 'lg' },
            { key: 'basic', header: 'Basic', numeric: true, cell: (p) => money(p.basic), total: sum((p) => p.basic), hideBelow: 'md' },
            { key: 'allow', header: 'Allowances', numeric: true, cell: (p) => money(p.allowances), total: sum((p) => p.allowances), hideBelow: 'lg' },
            { key: 'ot', header: 'Overtime', numeric: true, cell: (p) => money(p.overtime), total: sum((p) => p.overtime), hideBelow: 'xl' },
            { key: 'comm', header: 'Commission', numeric: true, cell: (p) => money(p.commission), total: sum((p) => p.commission), hideBelow: 'xl' },
            { key: 'gross', header: 'Gross', numeric: true, cell: (p) => money(p.gross), total: sum((p) => p.gross) },
            { key: 'att', header: 'Leave, lateness', numeric: true, cell: (p) => money(p.attendance), total: sum((p) => p.attendance), hideBelow: 'xl' },
            { key: 'tax', header: 'Income tax', numeric: true, cell: (p) => money(p.tax), total: sum((p) => p.tax), hideBelow: 'md' },
            { key: 'eobi', header: 'EOBI', numeric: true, cell: (p) => money(p.eobi), total: sum((p) => p.eobi), hideBelow: 'xl' },
            { key: 'loans', header: 'Loans', numeric: true, cell: (p) => money(p.loans), total: sum((p) => p.loans), hideBelow: 'lg' },
            { key: 'net', header: 'Net pay', numeric: true, cell: (p) => <span className="font-semibold">{money(p.net)}</span>, total: sum((p) => p.net) },
            { key: 'how', header: 'Paid by', cell: (p) => p.paymentMethod, hideBelow: 'lg' },
          ]}
        />
      ) : (
        <CashSheet run={run} />
      )}

      {paying && <PayDialog run={run} onClose={() => setPaying(false)} onPaid={refresh} onConflict={() => setConflict(true)} />}
      <ConfirmDialog
        open={deleting}
        title={`Delete payroll run #${run.number}?`}
        message="Its payslips go with it. A run is deleted only before it is approved."
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => remove.mutate()}
        onClose={() => setDeleting(false)}
      />
      <ConflictDialog
        open={conflict}
        what="payroll run"
        onReload={() => {
          setConflict(false);
          refresh();
        }}
        onClose={() => setConflict(false)}
      />
    </div>
  );
}

function CashSheet({ run }: { run: RunRow }) {
  const { currencySymbol: symbol } = useStore();
  const query = useQuery({ queryKey: ['hr-cash-sheet', run.id, run.version], queryFn: () => api.cashSheet(run.id) });
  const rows = query.data?.rows;
  return (
    <DataTable
      rows={rows}
      loading={query.isLoading}
      error={query.error}
      onRetry={() => void query.refetch()}
      rowKey={(r) => r.code}
      caption="Cash sheet"
      showTotals={(rows?.length ?? 0) > 0}
      empty={<EmptyState title="Nobody is paid in cash" />}
      columns={[
        { key: 'code', header: 'Code', cell: (r) => r.code },
        { key: 'name', header: 'Employee', cell: (r) => <span className="font-semibold">{r.name}</span> },
        { key: 'job', header: 'Designation', cell: (r) => r.designation ?? '', hideBelow: 'md' },
        { key: 'net', header: 'Net pay', numeric: true, cell: (r) => formatMoney(r.net, symbol), total: formatMoney(query.data?.total ?? 0, symbol) },
        { key: 'sign', header: 'Received (signature)', cell: () => <span className="inline-block min-w-[180px] border-b border-line">&nbsp;</span>, className: 'min-w-[200px]' },
      ]}
    />
  );
}

function PayDialog({ run, onClose, onPaid, onConflict }: { run: RunRow; onClose(): void; onPaid(): void; onConflict(): void }) {
  const toast = useToast();
  const [paidOn, setPaidOn] = useState(toBusinessDate(new Date()));
  const [bank, setBank] = useState('');
  const [cash, setCash] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const pay = useMutation({
    mutationFn: () => api.payPayrollRun(run.id, { version: run.version, paidOn, bankAccount: textOrNull(bank), cashAccount: textOrNull(cash) }),
    onSuccess: (saved) => {
      toast.ok(`Paid${saved.paymentJournalNumber ? `, entry #${saved.paymentJournalNumber}` : ''}.`);
      onPaid();
      onClose();
    },
    onError: (e) => {
      if (e instanceof ApiError && e.isConflict) {
        onClose();
        onConflict();
      }
    },
  });
  return (
    <Dialog
      open
      title={`Record the payment of run #${run.number}`}
      onClose={onClose}
      onSubmit={() => {
        const p = isBusinessDate(paidOn) ? null : 'Give the day it was paid.';
        setProblem(p);
        if (!p) pay.mutate();
      }}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Banknote} shortcut="Enter" busy={pay.isPending}>
            Record payment
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <p className="mb-3 text-ink">Posts the net pay out of salaries payable: transfers from the bank account, cash from the cash account.</p>
      <div className="space-y-3">
        <Field label="Paid on" htmlFor="pay-date">
          <Input id="pay-date" type="date" className="max-w-[200px]" value={paidOn} onChange={(e) => setPaidOn(e.target.value)} />
        </Field>
        <Field label="Bank account" htmlFor="pay-bank" hint="Empty: the account with the role Bank.">
          <Input id="pay-bank" className="max-w-[200px]" value={bank} maxLength={20} onChange={(e) => setBank(e.target.value)} />
        </Field>
        <Field label="Cash account" htmlFor="pay-cash" hint="Empty: the account with the role Cash.">
          <Input id="pay-cash" className="max-w-[200px]" value={cash} maxLength={20} onChange={(e) => setCash(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? (pay.error && !(pay.error instanceof ApiError && pay.error.isConflict) ? pay.error : null)} />
    </Dialog>
  );
}
