'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Check, Plus, Printer, Save, X } from 'lucide-react';
import { useState } from 'react';
import { EmployeeSelect, useHrRights } from '@/components/hr/common';
import { DecisionDialog } from '@/components/hr/decision-dialog';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Field, Input, Select } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, TableSkeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { cx } from '@/lib/cx';
import { addDays, isBusinessDate, toBusinessDate } from '@/lib/dates';
import { checkNumber, parseNumber } from '@/lib/forms';
import { formatCount, formatDate, formatHours, formatShortUtc, formatTime } from '@/lib/format';
import { APPROVAL, ATTENDANCE_CHOICES, ATTENDANCE_STATUS, formatDays, formatMinutes, markTone, monthKey, monthName, SHEET_MARKS } from '@/lib/hr';
import { useStore } from '@/lib/store';
import type { AttendanceRow, AttendanceStatus, CorrectionRow, HrApproval } from '@/lib/types';

type Tab = 'daily' | 'sheet' | 'corrections';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'daily', label: 'Daily' },
  { key: 'sheet', label: 'Monthly sheet' },
  { key: 'corrections', label: 'Corrections' },
];

export default function AttendancePage() {
  const [tab, setTab] = useState<Tab>('daily');
  return (
    <div>
      <PageHeader title="Attendance" />
      <Tabs label="Attendance" tabs={TABS} value={tab} onChange={setTab} className="no-print mb-4" />
      {tab === 'daily' && <Daily />}
      {tab === 'sheet' && <Sheet />}
      {tab === 'corrections' && <Corrections />}
    </div>
  );
}

function Daily() {
  const { storeId } = useStore();
  const dates = useDateRange('week');
  const [employeeId, setEmployeeId] = useState('');
  const query = useQuery({
    queryKey: ['hr-attendance', storeId, dates.range, employeeId],
    queryFn: () => api.attendance({ storeId, ...dates.range, employeeId }),
  });
  const rows = query.data ?? [];
  const count = (status: AttendanceStatus) => rows.filter((r) => r.status === status).length;
  return (
    <>
      <Toolbar>
        <DateRangeBar state={dates} compact />
        <label className="sr-only" htmlFor="att-employee">
          Employee
        </label>
        <EmployeeSelect id="att-employee" className="w-[260px]" value={employeeId} onChange={setEmployeeId} empty="Everyone" all />
      </Toolbar>
      <DataTable<AttendanceRow>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => `${r.employeeId}-${r.date}`}
        caption="Attendance by day"
        maxHeight="calc(100dvh - 330px)"
        empty={<EmptyState title="No working days here">Attendance is worked out from the time clock for the employees of the store.</EmptyState>}
        columns={[
          { key: 'date', header: 'Day', cell: (r) => formatDate(r.date) },
          { key: 'who', header: 'Employee', cell: (r) => <span className="font-semibold">{r.name}</span> },
          {
            key: 'status',
            header: 'Status',
            cell: (r) => (
              <span className="flex flex-wrap items-center gap-1.5">
                <Chip tone={ATTENDANCE_STATUS[r.status].tone}>{r.status === 'Leave' && r.leaveCode ? `${r.leaveCode}${r.leaveDays < 1 ? ' ½' : ''}` : ATTENDANCE_STATUS[r.status].label}</Chip>
                {r.status === 'Holiday' && r.holiday && <span className="text-note text-muted">{r.holiday}</span>}
                {r.corrected && <Chip tone="dark">Corrected</Chip>}
              </span>
            ),
          },
          { key: 'shift', header: 'Shift', cell: (r) => (r.shift ? `${r.shift} ${formatTime(r.shiftStart)}–${formatTime(r.shiftEnd)}` : ''), hideBelow: 'xl' },
          { key: 'in', header: 'In', cell: (r) => formatTime(r.firstIn), hideBelow: 'md' },
          { key: 'out', header: 'Out', cell: (r) => formatTime(r.lastOut), hideBelow: 'md' },
          { key: 'worked', header: 'Worked', numeric: true, cell: (r) => formatMinutes(r.workedMinutes), hideBelow: 'lg' },
          { key: 'late', header: 'Late', numeric: true, cell: (r) => <span className={cx(r.lateMinutes > 0 && 'font-semibold text-danger')}>{formatMinutes(r.lateMinutes)}</span> },
          { key: 'early', header: 'Left early', numeric: true, cell: (r) => formatMinutes(r.earlyMinutes), hideBelow: 'lg' },
          { key: 'over', header: 'Overtime', numeric: true, cell: (r) => formatMinutes(r.overtimeMinutes), hideBelow: 'sm' },
        ]}
      />
      {query.data && (
        <p className="mt-2 px-1 font-semibold text-ink">
          {formatCount(count('Present'))} present · {formatCount(rows.filter((r) => r.lateMinutes > 0).length)} late · {formatCount(count('Absent'))} absent ·{' '}
          {formatCount(count('Leave'))} on leave · {formatHours(rows.reduce((s, r) => s + r.overtimeMinutes, 0) / 60)} overtime hours
        </p>
      )}
    </>
  );
}

function Sheet() {
  const { storeId, store } = useStore();
  const [month, setMonth] = useState(monthKey());
  const query = useQuery({ queryKey: ['hr-sheet', storeId, month], queryFn: () => api.attendanceSheet({ storeId, month }) });
  const sheet = query.data;
  return (
    <>
      <Toolbar>
        <label className="text-muted" htmlFor="sheet-month">
          Month
        </label>
        <Input id="sheet-month" type="month" className="w-[200px]" value={month} onChange={(e) => e.target.value && setMonth(e.target.value)} />
        <Button variant="sun" icon={Printer} shortcut="Ctrl+P" disabled={!sheet} onClick={() => window.print()}>
          Print
        </Button>
      </Toolbar>
      <h2 className="mb-2 font-heading text-section">
        Attendance sheet, {monthName(month)}
        {store ? ` · ${store.code} ${store.name}` : ''}
      </h2>
      {query.error ? (
        <div className="card">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      ) : !sheet ? (
        <div className="grid-card">
          <TableSkeleton rows={6} columns={8} />
        </div>
      ) : sheet.rows.length === 0 ? (
        <div className="card">
          <EmptyState title="Nobody worked this month" />
        </div>
      ) : (
        <div className="grid-card">
          <div className="grid-scroll print:max-h-none">
            <table className="grid-table sheet-table" aria-label="Attendance sheet">
              <thead>
                <tr>
                  <th scope="col">Employee</th>
                  {sheet.days.map((d) => (
                    <th key={d} scope="col" className="num px-1 text-center" title={formatDate(d)}>
                      {Number(d.slice(8))}
                    </th>
                  ))}
                  <th scope="col" className="num">P</th>
                  <th scope="col" className="num">Late</th>
                  <th scope="col" className="num">A</th>
                  <th scope="col" className="num">Leave</th>
                  <th scope="col" className="num">OT h</th>
                </tr>
              </thead>
              <tbody>
                {sheet.rows.map((r) => (
                  <tr key={r.employeeId}>
                    <td className="whitespace-nowrap font-semibold">
                      {r.code} {r.name}
                    </td>
                    {r.marks.map((m, i) => (
                      <td key={i} className={cx('px-1 text-center text-note', markTone(m) === 'danger' && 'font-semibold text-danger', markTone(m) === 'warn' && 'font-semibold')}>
                        {m}
                      </td>
                    ))}
                    <td className="num">{formatCount(r.present)}</td>
                    <td className="num">{formatCount(r.late)}</td>
                    <td className="num">{formatCount(r.absent)}</td>
                    <td className="num">{formatDays(r.leave)}</td>
                    <td className="num">{formatHours(r.overtimeHours)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      <p className="mt-2 px-1 text-note text-muted">{SHEET_MARKS.map((m) => `${m.mark} ${m.meaning}`).join(' · ')}</p>
    </>
  );
}

function Corrections() {
  const rights = useHrRights();
  const { storeId } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<HrApproval | ''>('Pending');
  const [asking, setAsking] = useState(false);
  const [deciding, setDeciding] = useState<{ row: CorrectionRow; approve: boolean } | null>(null);
  const query = useQuery({ queryKey: ['hr-corrections', storeId, status], queryFn: () => api.corrections({ storeId, status }) });
  return (
    <>
      <Toolbar>
        <label className="sr-only" htmlFor="corr-status">
          Status
        </label>
        <Select id="corr-status" className="w-[200px]" value={status} onChange={(e) => setStatus(e.target.value as HrApproval | '')}>
          <option value="Pending">Waiting</option>
          <option value="Approved">Approved</option>
          <option value="Rejected">Rejected</option>
          <option value="">All</option>
        </Select>
        {rights.attendance && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setAsking(true)}>
            Ask for a correction
          </Button>
        )}
      </Toolbar>
      <DataTable<CorrectionRow>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(c) => c.id}
        caption="Attendance corrections"
        empty={<EmptyState title="No corrections">A forgotten punch or a day worked elsewhere is corrected here, and someone else approves it.</EmptyState>}
        columns={[
          { key: 'date', header: 'Day', cell: (c) => formatDate(c.date) },
          { key: 'who', header: 'Employee', cell: (c) => <span className="font-semibold">{c.name}</span> },
          { key: 'to', header: 'Should be', cell: (c) => ATTENDANCE_STATUS[c.status].label },
          {
            key: 'mins',
            header: 'Minutes',
            cell: (c) => [c.workedMinutes && `worked ${formatMinutes(c.workedMinutes)}`, c.lateMinutes && `late ${formatMinutes(c.lateMinutes)}`, c.overtimeMinutes && `overtime ${formatMinutes(c.overtimeMinutes)}`].filter(Boolean).join(', '),
            hideBelow: 'lg',
          },
          { key: 'why', header: 'Reason', cell: (c) => c.reason, className: 'wrap', hideBelow: 'md' },
          { key: 'by', header: 'Asked by', cell: (c) => `${c.requestedBy ?? ''} ${formatShortUtc(c.requestedAtUtc)}`, hideBelow: 'xl' },
          { key: 'state', header: 'Status', cell: (c) => <Chip tone={APPROVAL[c.approval].tone}>{APPROVAL[c.approval].label}</Chip> },
          {
            key: 'act',
            header: '',
            cell: (c) =>
              rights.attendance && c.approval === 'Pending' ? (
                <span className="flex justify-end gap-2">
                  <Button icon={Check} className="min-h-0 py-1.5" onClick={() => setDeciding({ row: c, approve: true })}>
                    Approve
                  </Button>
                  <Button icon={X} className="min-h-0 py-1.5" onClick={() => setDeciding({ row: c, approve: false })}>
                    Reject
                  </Button>
                </span>
              ) : (
                <span className="text-note text-muted">{c.decidedBy ?? ''}</span>
              ),
          },
        ]}
      />
      {asking && <CorrectionDialog onClose={() => setAsking(false)} />}
      {deciding && (
        <DecisionDialog
          title={`${deciding.approve ? 'Approve' : 'Reject'} the correction of ${deciding.row.name}, ${formatDate(deciding.row.date)}?`}
          approve={deciding.approve}
          onClose={() => setDeciding(null)}
          onDecide={async (note) => {
            await (deciding.approve ? api.approveCorrection(deciding.row.id, note) : api.rejectCorrection(deciding.row.id, note));
            toast.ok(deciding.approve ? 'The correction is approved.' : 'The correction is rejected.');
            setDeciding(null);
            void queryClient.invalidateQueries({ queryKey: ['hr-corrections'] });
            void queryClient.invalidateQueries({ queryKey: ['hr-attendance'] });
            void queryClient.invalidateQueries({ queryKey: ['hr-sheet'] });
          }}
        />
      )}
    </>
  );
}

function CorrectionDialog({ onClose }: { onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [employeeId, setEmployeeId] = useState('');
  const [date, setDate] = useState(addDays(toBusinessDate(new Date()), -1));
  const [status, setStatus] = useState<AttendanceStatus>('Present');
  const [worked, setWorked] = useState('');
  const [late, setLate] = useState('');
  const [early, setEarly] = useState('');
  const [overtime, setOvertime] = useState('');
  const [reason, setReason] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () =>
      api.requestCorrection({
        employeeId,
        date,
        status,
        workedMinutes: parseNumber(worked) ?? 0,
        lateMinutes: parseNumber(late) ?? 0,
        earlyMinutes: parseNumber(early) ?? 0,
        overtimeMinutes: parseNumber(overtime) ?? 0,
        reason: reason.trim(),
      }),
    onSuccess: () => {
      toast.ok('The correction waits for approval.');
      void queryClient.invalidateQueries({ queryKey: ['hr-corrections'] });
      onClose();
    },
  });
  function submit() {
    const p = !employeeId
      ? 'Choose the employee.'
      : !isBusinessDate(date)
        ? 'Give the day.'
        : !reason.trim()
          ? 'Say why the day is corrected.'
          : [worked, late, early, overtime].map((m) => checkNumber(m, { label: 'Minutes', decimals: 0 })).find(Boolean) ?? null;
    setProblem(p);
    if (!p) save.mutate();
  }
  const minutes = (id: string, label: string, value: string, set: (v: string) => void) => (
    <Field label={label} htmlFor={id}>
      <Input id={id} inputMode="numeric" className="max-w-[140px]" value={value} onChange={(e) => set(e.target.value)} />
    </Field>
  );
  return (
    <Dialog
      open
      title="Ask for a correction"
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
        <Field label="Employee" htmlFor="corr-employee">
          <EmployeeSelect id="corr-employee" value={employeeId} onChange={setEmployeeId} all />
        </Field>
        <Field label="Day" htmlFor="corr-date">
          <Input id="corr-date" type="date" className="max-w-[200px]" value={date} onChange={(e) => setDate(e.target.value)} />
        </Field>
        <Field label="Should be" htmlFor="corr-to">
          <Select id="corr-to" value={status} onChange={(e) => setStatus(e.target.value as AttendanceStatus)}>
            {ATTENDANCE_CHOICES.map((s) => (
              <option key={s} value={s}>
                {ATTENDANCE_STATUS[s].label}
              </option>
            ))}
          </Select>
        </Field>
        {minutes('corr-worked', 'Minutes worked', worked, setWorked)}
        {status === 'Present' && minutes('corr-late', 'Minutes late', late, setLate)}
        {status === 'Present' && minutes('corr-early', 'Minutes left early', early, setEarly)}
        {minutes('corr-over', 'Overtime minutes', overtime, setOvertime)}
        <Field label="Reason" htmlFor="corr-reason">
          <Input id="corr-reason" value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? save.error} />
    </Dialog>
  );
}
