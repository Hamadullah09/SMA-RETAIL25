'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ban, Check, Plus, Save, SlidersHorizontal, X } from 'lucide-react';
import { useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { EmployeeSelect, useHrRights } from '@/components/hr/common';
import { DecisionDialog } from '@/components/hr/decision-dialog';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select } from '@/components/ui/field';
import { Chip, PageHeader, Pager, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { isBusinessDate, toBusinessDate } from '@/lib/dates';
import { checkNumber, parseNumber } from '@/lib/forms';
import { formatDate } from '@/lib/format';
import { formatDays, LEAVE_STATUS } from '@/lib/hr';
import { useStore } from '@/lib/store';
import type { LeaveBalanceRow, LeaveRequestRow, LeaveRequestStatus } from '@/lib/types';

type Tab = 'requests' | 'balances' | 'approvals';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'requests', label: 'Requests' },
  { key: 'balances', label: 'Balances' },
  { key: 'approvals', label: 'Approvals' },
];
const PAGE_SIZE = 50;

export default function LeavePage() {
  return (
    <Suspense>
      <Leave />
    </Suspense>
  );
}

function Leave() {
  const params = useSearchParams();
  const [tab, setTab] = useState<Tab>('requests');
  const [asking, setAsking] = useState(params.get('new') === '1');
  return (
    <div>
      <PageHeader title="Leave" />
      <Tabs label="Leave" tabs={TABS} value={tab} onChange={setTab} className="mb-4" />
      {tab === 'requests' && <Requests onNew={() => setAsking(true)} />}
      {tab === 'balances' && <Balances />}
      {tab === 'approvals' && <Requests pendingOnly onNew={() => setAsking(true)} />}
      {asking && <RequestDialog onClose={() => setAsking(false)} />}
    </div>
  );
}

function Requests({ pendingOnly, onNew }: { pendingOnly?: boolean; onNew(): void }) {
  const rights = useHrRights();
  const { storeId } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<LeaveRequestStatus | ''>('');
  const [employeeId, setEmployeeId] = useState('');
  const [page, setPage] = useState(1);
  const [deciding, setDeciding] = useState<{ row: LeaveRequestRow; approve: boolean } | null>(null);
  const [cancelling, setCancelling] = useState<LeaveRequestRow | null>(null);
  const shown = pendingOnly ? 'Pending' : status;
  useEffect(() => setPage(1), [shown, employeeId, storeId]);
  const query = useQuery({
    queryKey: ['hr-leave', { storeId, status: shown, employeeId, page }],
    queryFn: ({ signal }) => api.leaveRequests({ storeId, status: shown, employeeId, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  const rows = pendingOnly ? query.data?.items.filter((r) => r.mayDecide) : query.data?.items;
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['hr-leave'] });
    void queryClient.invalidateQueries({ queryKey: ['hr-balances'] });
    void queryClient.invalidateQueries({ queryKey: ['hr-attendance'] });
  };
  const cancel = useMutation({
    mutationFn: (id: string) => api.cancelLeave(id),
    onSuccess: () => {
      toast.ok('The leave is cancelled.');
      setCancelling(null);
      refresh();
    },
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <>
      <Toolbar>
        {!pendingOnly && (
          <>
            <label className="sr-only" htmlFor="leave-status">
              Status
            </label>
            <Select id="leave-status" className="w-[180px]" value={status} onChange={(e) => setStatus(e.target.value as LeaveRequestStatus | '')}>
              <option value="">Every status</option>
              {Object.entries(LEAVE_STATUS).map(([k, v]) => (
                <option key={k} value={k}>
                  {v.label}
                </option>
              ))}
            </Select>
          </>
        )}
        <label className="sr-only" htmlFor={pendingOnly ? 'approve-employee' : 'leave-employee'}>
          Employee
        </label>
        <EmployeeSelect id={pendingOnly ? 'approve-employee' : 'leave-employee'} className="w-[260px]" value={employeeId} onChange={setEmployeeId} empty="Everyone" all />
        <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={onNew}>
          New leave request
        </Button>
      </Toolbar>
      <DataTable<LeaveRequestRow>
        rows={rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => r.id}
        caption={pendingOnly ? 'Leave waiting for you' : 'Leave requests'}
        empty={<EmptyState title={pendingOnly ? 'Nothing waits for your approval' : 'No leave requests'} />}
        columns={[
          { key: 'who', header: 'Employee', cell: (r) => <span className="font-semibold">{r.name}</span> },
          { key: 'type', header: 'Leave', cell: (r) => r.leaveType },
          { key: 'from', header: 'From', cell: (r) => formatDate(r.fromDate) + (r.firstDayHalf ? ' (½)' : '') },
          { key: 'to', header: 'To', cell: (r) => formatDate(r.toDate) + (r.lastDayHalf ? ' (½)' : ''), hideBelow: 'sm' },
          { key: 'days', header: 'Days', numeric: true, cell: (r) => formatDays(r.days) },
          { key: 'why', header: 'Reason', cell: (r) => r.reason ?? '', hideBelow: 'xl', className: 'wrap' },
          { key: 'status', header: 'Status', cell: (r) => <Chip tone={LEAVE_STATUS[r.status].tone}>{LEAVE_STATUS[r.status].label}</Chip> },
          { key: 'by', header: 'Decided by', cell: (r) => r.decidedBy ?? '', hideBelow: 'lg' },
          {
            key: 'act',
            header: '',
            cell: (r) => (
              <span className="flex justify-end gap-2">
                {r.mayDecide && r.status === 'Pending' && (
                  <>
                    <Button icon={Check} className="min-h-0 py-1.5" onClick={() => setDeciding({ row: r, approve: true })}>
                      Approve
                    </Button>
                    <Button icon={X} className="min-h-0 py-1.5" onClick={() => setDeciding({ row: r, approve: false })}>
                      Reject
                    </Button>
                  </>
                )}
                {(rights.leave || r.mayDecide) && (r.status === 'Pending' || r.status === 'Approved') && (
                  <Button icon={Ban} className="min-h-0 py-1.5" onClick={() => setCancelling(r)}>
                    Cancel
                  </Button>
                )}
              </span>
            ),
          },
        ]}
        footer={!pendingOnly && query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="requests" />}
      />
      <p className="mt-2 px-1 text-ink">The employee&apos;s manager, or someone who approves leave, decides. Approved leave shows in attendance; unpaid leave is taken off pay.</p>
      {deciding && (
        <DecisionDialog
          title={`${deciding.approve ? 'Approve' : 'Reject'} ${deciding.row.name}'s ${deciding.row.leaveType.toLowerCase()}?`}
          approve={deciding.approve}
          onClose={() => setDeciding(null)}
          onDecide={async (note) => {
            await (deciding.approve ? api.approveLeave(deciding.row.id, note) : api.rejectLeave(deciding.row.id, note));
            toast.ok(deciding.approve ? 'The leave is approved.' : 'The leave is rejected.');
            setDeciding(null);
            refresh();
          }}
        />
      )}
      <ConfirmDialog
        open={!!cancelling}
        title={`Cancel ${cancelling?.name ?? ''}'s leave?`}
        message={cancelling ? `${cancelling.leaveType}, ${formatDate(cancelling.fromDate)} to ${formatDate(cancelling.toDate)}.` : undefined}
        confirmLabel="Cancel the leave"
        cancelLabel="Keep it"
        danger
        busy={cancel.isPending}
        onConfirm={() => cancelling && cancel.mutate(cancelling.id)}
        onClose={() => setCancelling(null)}
      />
    </>
  );
}

function RequestDialog({ onClose }: { onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const today = toBusinessDate(new Date());
  const [employeeId, setEmployeeId] = useState('');
  const [leaveTypeId, setLeaveTypeId] = useState('');
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const [firstHalf, setFirstHalf] = useState(false);
  const [lastHalf, setLastHalf] = useState(false);
  const [reason, setReason] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const types = useQuery({ queryKey: ['hr-leave-types', ''], queryFn: () => api.leaveTypes(), staleTime: 5 * 60_000 });
  const countries = new Set((types.data ?? []).map((t) => t.country));
  const save = useMutation({
    mutationFn: () => api.requestLeave({ employeeId, leaveTypeId, fromDate: from, toDate: to, firstDayHalf: firstHalf, lastDayHalf: lastHalf && to !== from, reason: reason.trim() || null }),
    onSuccess: (r) => {
      toast.ok(`${r.name}'s leave waits for approval (${formatDays(r.days)} days).`);
      void queryClient.invalidateQueries({ queryKey: ['hr-leave'] });
      void queryClient.invalidateQueries({ queryKey: ['hr-balances'] });
      onClose();
    },
  });
  function submit() {
    const p = !employeeId
      ? 'Choose the employee.'
      : !leaveTypeId
        ? 'Choose the leave.'
        : !isBusinessDate(from) || !isBusinessDate(to)
          ? 'Give the first and last day.'
          : to < from
            ? 'The leave ends before it starts.'
            : null;
    setProblem(p);
    if (!p) save.mutate();
  }
  return (
    <Dialog
      open
      title="New leave request"
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Ask
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Employee" htmlFor="req-employee">
          <EmployeeSelect id="req-employee" value={employeeId} onChange={setEmployeeId} />
        </Field>
        <Field label="Leave" htmlFor="req-type">
          <Select id="req-type" value={leaveTypeId} onChange={(e) => setLeaveTypeId(e.target.value)}>
            <option value="">Choose…</option>
            {(types.data ?? []).filter((t) => t.isActive).map((t) => (
              <option key={t.id ?? t.code} value={t.id ?? ''}>
                {t.name}
                {countries.size > 1 ? ` (${t.country})` : ''}
                {t.isPaid ? '' : ', unpaid'}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="From" htmlFor="req-from">
          <Input id="req-from" type="date" className="max-w-[200px]" value={from} onChange={(e) => {
            setFrom(e.target.value);
            if (e.target.value > to) setTo(e.target.value);
          }} />
        </Field>
        <Field label="To" htmlFor="req-to">
          <Input id="req-to" type="date" className="max-w-[200px]" value={to} onChange={(e) => setTo(e.target.value)} />
        </Field>
        <Field label="" htmlFor="req-half">
          <div>
            <Checkbox label={from === to ? 'Half a day' : 'The first day is a half day (afternoon)'} checked={firstHalf} onChange={setFirstHalf} />
            {from !== to && <Checkbox label="The last day is a half day (morning)" checked={lastHalf} onChange={setLastHalf} />}
          </div>
        </Field>
        <Field label="Reason" htmlFor="req-reason">
          <Input id="req-reason" value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? save.error} />
    </Dialog>
  );
}

function Balances() {
  const rights = useHrRights();
  const { storeId } = useStore();
  const [year, setYear] = useState(String(new Date().getFullYear()));
  const [employeeId, setEmployeeId] = useState('');
  const [adjusting, setAdjusting] = useState(false);
  const y = Number(year);
  const query = useQuery({
    queryKey: ['hr-balances', y, employeeId, storeId],
    queryFn: () => api.leaveBalances({ year: y, employeeId, storeId }),
    enabled: y >= 2000 && y <= 2100,
  });
  return (
    <>
      <Toolbar>
        <label className="text-muted" htmlFor="bal-year">
          Year
        </label>
        <Input id="bal-year" inputMode="numeric" className="w-[110px]" value={year} maxLength={4} onChange={(e) => setYear(e.target.value)} />
        <label className="sr-only" htmlFor="bal-employee">
          Employee
        </label>
        <EmployeeSelect id="bal-employee" className="w-[260px]" value={employeeId} onChange={setEmployeeId} empty="Everyone" all />
        {rights.leave && (
          <Button icon={SlidersHorizontal} onClick={() => setAdjusting(true)}>
            Adjust a balance
          </Button>
        )}
      </Toolbar>
      <DataTable<LeaveBalanceRow>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(b) => `${b.employeeId}-${b.leaveTypeId}`}
        caption="Leave balances"
        maxHeight="calc(100dvh - 320px)"
        empty={<EmptyState title="No balances for this year" />}
        columns={[
          { key: 'who', header: 'Employee', cell: (b) => <span className="font-semibold">{b.name}</span> },
          { key: 'type', header: 'Leave', cell: (b) => b.leaveType },
          { key: 'ent', header: 'Entitled', numeric: true, cell: (b) => (b.limited ? formatDays(b.entitled) : 'No limit') },
          { key: 'earn', header: 'Earned so far', numeric: true, cell: (b) => (b.limited ? formatDays(b.earned) : ''), hideBelow: 'md' },
          { key: 'in', header: 'Carried in', numeric: true, cell: (b) => (b.limited ? formatDays(b.carriedIn) : ''), hideBelow: 'lg' },
          { key: 'adj', header: 'Adjusted', numeric: true, cell: (b) => (b.adjusted ? formatDays(b.adjusted) : ''), hideBelow: 'xl' },
          { key: 'taken', header: 'Taken', numeric: true, cell: (b) => formatDays(b.taken) },
          { key: 'pend', header: 'Waiting', numeric: true, cell: (b) => (b.pending ? formatDays(b.pending) : ''), hideBelow: 'md' },
          { key: 'left', header: 'Left', numeric: true, cell: (b) => <span className="font-semibold">{b.available === null ? '' : formatDays(b.available)}</span> },
        ]}
      />
      {adjusting && <AdjustDialog year={y} onClose={() => setAdjusting(false)} />}
    </>
  );
}

function AdjustDialog({ year, onClose }: { year: number; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [employeeId, setEmployeeId] = useState('');
  const [leaveTypeId, setLeaveTypeId] = useState('');
  const [yearText, setYearText] = useState(String(year));
  const [days, setDays] = useState('');
  const [reason, setReason] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const types = useQuery({ queryKey: ['hr-leave-types', ''], queryFn: () => api.leaveTypes(), staleTime: 5 * 60_000 });
  const save = useMutation({
    mutationFn: () => api.adjustLeave({ employeeId, leaveTypeId, year: Number(yearText), days: parseNumber(days) ?? 0, reason: reason.trim() }),
    onSuccess: () => {
      toast.ok('The balance is changed.');
      void queryClient.invalidateQueries({ queryKey: ['hr-balances'] });
      onClose();
    },
  });
  function submit() {
    const p = !employeeId
      ? 'Choose the employee.'
      : !leaveTypeId
        ? 'Choose the leave.'
        : checkNumber(days, { required: true, min: null, label: 'Days' }) ?? (parseNumber(days) === 0 ? 'Days is not zero.' : !reason.trim() ? 'Say why the balance changes.' : null);
    setProblem(p ?? null);
    if (!p) save.mutate();
  }
  return (
    <Dialog
      open
      title="Adjust a leave balance"
      onClose={onClose}
      onSubmit={submit}
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
      <div className="space-y-3">
        <Field label="Employee" htmlFor="adj-employee">
          <EmployeeSelect id="adj-employee" value={employeeId} onChange={setEmployeeId} all />
        </Field>
        <Field label="Leave" htmlFor="adj-type">
          <Select id="adj-type" value={leaveTypeId} onChange={(e) => setLeaveTypeId(e.target.value)}>
            <option value="">Choose…</option>
            {(types.data ?? []).map((t) => (
              <option key={t.id ?? t.code} value={t.id ?? ''}>
                {t.name} ({t.country})
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Year" htmlFor="adj-year">
          <Input id="adj-year" inputMode="numeric" className="max-w-[120px]" value={yearText} maxLength={4} onChange={(e) => setYearText(e.target.value)} />
        </Field>
        <Field label="Days" htmlFor="adj-days" hint="Negative takes days away (an opening balance used elsewhere).">
          <Input id="adj-days" inputMode="decimal" className="max-w-[140px]" value={days} onChange={(e) => setDays(e.target.value)} />
        </Field>
        <Field label="Reason" htmlFor="adj-reason">
          <Input id="adj-reason" value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? save.error} />
    </Dialog>
  );
}
