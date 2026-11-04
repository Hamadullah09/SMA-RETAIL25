'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CalendarPlus, ChevronLeft, ChevronRight, Pencil, Plus, Save, Trash2, X } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { EmployeeSelect, useHrRights } from '@/components/hr/common';
import { ConflictDialog } from '@/components/masters';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { addDays, isBusinessDate, startOfWeek, toBusinessDate } from '@/lib/dates';
import { checkNumber, numberText, parseNumber } from '@/lib/forms';
import { formatDate } from '@/lib/format';
import { daysText, WEEK_DAYS } from '@/lib/hr';
import { useStore } from '@/lib/store';
import type { RosterChangeInfo, RosterRowInfo, ShiftInfo } from '@/lib/types';

type Tab = 'shifts' | 'roster';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'shifts', label: 'Shifts' },
  { key: 'roster', label: 'Roster' },
];

export default function ShiftsPage() {
  const [tab, setTab] = useState<Tab>('shifts');
  return (
    <div>
      <PageHeader title="Shifts and rosters" />
      <Tabs label="Shifts and rosters" tabs={TABS} value={tab} onChange={setTab} className="mb-4" />
      {tab === 'shifts' ? <Shifts /> : <Roster />}
    </div>
  );
}

function useShifts() {
  const { storeId } = useStore();
  return useQuery({ queryKey: ['hr-shifts', storeId], queryFn: () => api.shifts({ storeId }) });
}

function Shifts() {
  const rights = useHrRights();
  const { stores } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const query = useShifts();
  const [selected, setSelected] = useState<ShiftInfo | null>(null);
  const [editing, setEditing] = useState<ShiftInfo | 'new' | null>(null);
  const [deleting, setDeleting] = useState<ShiftInfo | null>(null);
  const may = (s: ShiftInfo | null) => rights.edit && (s?.storeId ? true : rights.editShared);
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteShift(id),
    onSuccess: () => {
      toast.ok('The shift is deleted.');
      setDeleting(null);
      setSelected(null);
      void queryClient.invalidateQueries({ queryKey: ['hr-shifts'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  return (
    <>
      {rights.edit && (
        <Toolbar>
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            New shift
          </Button>
          <Button icon={Pencil} shortcut="Ctrl+O" disabled={!selected || !may(selected)} onClick={() => selected && setEditing(selected)}>
            Edit
          </Button>
          <Button icon={Trash2} shortcut="Del" disabled={!selected || !may(selected)} onClick={() => selected && setDeleting(selected)}>
            Delete
          </Button>
        </Toolbar>
      )}
      <DataTable<ShiftInfo>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(s) => s.id ?? s.name}
        selectedKey={selected?.id}
        onRowClick={(s) => {
          if (selected?.id === s.id && may(s)) setEditing(s);
          else setSelected(s);
        }}
        caption="Shifts"
        empty={<EmptyState title="No shifts yet">Without shifts, employees work the rule set&apos;s week and lateness is not measured.</EmptyState>}
        columns={[
          { key: 'name', header: 'Shift', cell: (s) => <span className="font-semibold">{s.name}</span> },
          { key: 'time', header: 'Hours', cell: (s) => `${s.startTime}–${s.endTime}` },
          { key: 'break', header: 'Break', numeric: true, cell: (s) => `${s.breakMinutes} min`, hideBelow: 'md' },
          { key: 'grace', header: 'Grace', numeric: true, cell: (s) => `${s.graceMinutes} min`, hideBelow: 'md' },
          { key: 'days', header: 'Days', cell: (s) => daysText(s.days) },
          { key: 'store', header: 'Store', cell: (s) => (s.storeId ? stores.find((x) => x.id === s.storeId)?.code ?? '' : 'Every store'), hideBelow: 'lg' },
          { key: 'active', header: 'Active', cell: (s) => (s.isActive ? <Chip tone="ok">Active</Chip> : <Chip>Off</Chip>), hideBelow: 'sm' },
        ]}
      />
      {editing && <ShiftDialog shift={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete the shift ${deleting?.name ?? ''}?`}
        message="A shift on the roster is kept; make it inactive instead."
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => deleting?.id && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  );
}

function ShiftDialog({ shift, onClose }: { shift: ShiftInfo | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const rights = useHrRights();
  const { stores, storeId } = useStore();
  const [name, setName] = useState(shift?.name ?? '');
  const [start, setStart] = useState(shift?.startTime ?? '09:00');
  const [end, setEnd] = useState(shift?.endTime ?? '17:00');
  const [breakText, setBreak] = useState(numberText(shift?.breakMinutes ?? 60));
  const [grace, setGrace] = useState(numberText(shift?.graceMinutes ?? 10));
  const [days, setDays] = useState<string[]>(shift?.days ?? WEEK_DAYS.slice(0, 6));
  const [store, setStore] = useState(shift ? shift.storeId ?? '' : rights.editShared ? '' : storeId ?? '');
  const [active, setActive] = useState(shift?.isActive ?? true);
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const save = useMutation({
    mutationFn: (body: ShiftInfo) => (shift?.id ? api.saveShift(shift.id, body) : api.addShift(body)),
    onSuccess: () => {
      toast.ok(`The shift ${name.trim()} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['hr-shifts'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  function submit() {
    const p = !name.trim()
      ? 'Give the shift a name.'
      : days.length === 0
        ? 'Choose the days the shift is worked.'
        : checkNumber(breakText, { label: 'The break', decimals: 0 }) ?? checkNumber(grace, { label: 'Grace', decimals: 0 }) ?? null;
    setProblem(p);
    if (p) return;
    save.mutate({
      id: shift?.id ?? null,
      version: shift?.version ?? null,
      name: name.trim(),
      startTime: start,
      endTime: end,
      breakMinutes: parseNumber(breakText) ?? 0,
      graceMinutes: parseNumber(grace) ?? 0,
      days: WEEK_DAYS.filter((d) => days.includes(d)),
      storeId: store || null,
      isActive: active,
    });
  }
  return (
    <Dialog
      open
      title={shift ? 'Change shift' : 'New shift'}
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
        <Field label="Name" htmlFor="shift-name">
          <Input id="shift-name" value={name} maxLength={60} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="Starts" htmlFor="shift-start">
          <Input id="shift-start" type="time" className="max-w-[160px]" value={start} onChange={(e) => setStart(e.target.value)} />
        </Field>
        <Field label="Ends" htmlFor="shift-end" hint="An end before the start is the next morning.">
          <Input id="shift-end" type="time" className="max-w-[160px]" value={end} onChange={(e) => setEnd(e.target.value)} />
        </Field>
        <Field label="Break (minutes)" htmlFor="shift-break">
          <Input id="shift-break" inputMode="numeric" className="max-w-[160px]" value={breakText} onChange={(e) => setBreak(e.target.value)} />
        </Field>
        <Field label="Grace (minutes)" htmlFor="shift-grace" hint="Late or leaving early only beyond this.">
          <Input id="shift-grace" inputMode="numeric" className="max-w-[160px]" value={grace} onChange={(e) => setGrace(e.target.value)} />
        </Field>
        <Field label="Days" htmlFor="shift-days">
          <div className="flex flex-wrap gap-x-4">
            {WEEK_DAYS.map((d) => (
              <Checkbox key={d} label={d.slice(0, 3)} checked={days.includes(d)} onChange={(on) => setDays((list) => (on ? [...list, d] : list.filter((x) => x !== d)))} />
            ))}
          </div>
        </Field>
        <Field label="Store" htmlFor="shift-store">
          <Select id="shift-store" value={store} onChange={(e) => setStore(e.target.value)}>
            {rights.editShared && <option value="">Every store</option>}
            {stores.map((s) => (
              <option key={s.id} value={s.id}>
                {s.code} · {s.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="" htmlFor="shift-active">
          <Checkbox label="Active" checked={active} onChange={setActive} />
        </Field>
      </div>
      <FormError error={problem ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
      <ConflictDialog
        open={conflict}
        what="shift"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['hr-shifts'] });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}

function Roster() {
  const rights = useHrRights();
  const { storeId } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [week, setWeek] = useState(() => startOfWeek(toBusinessDate(new Date())));
  const [picked, setPicked] = useState<Record<string, string>>({});
  const [changing, setChanging] = useState(false);
  const shifts = useShifts();
  const query = useQuery({ queryKey: ['hr-roster', storeId, week], queryFn: () => api.roster({ storeId, week }) });
  useEffect(() => setPicked({}), [week, storeId]);
  const shiftName = useMemo(() => {
    const map = new Map((shifts.data ?? []).map((s) => [s.id, s.name]));
    return (id: string | null) => (id ? map.get(id) ?? 'A shift' : 'Day off');
  }, [shifts.data]);
  const dirty = Object.keys(picked).length > 0;

  const save = useMutation({
    mutationFn: () => api.saveRoster({ weekStart: week, assignments: Object.entries(picked).map(([employeeId, shiftId]) => ({ employeeId, shiftId: shiftId || null })) }),
    onSuccess: () => {
      toast.ok('The roster is saved.');
      setPicked({});
      void queryClient.invalidateQueries({ queryKey: ['hr-roster'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  const removeChange = useMutation({
    mutationFn: (id: string) => api.deleteRosterChange(id),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['hr-roster'] }),
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <>
      <Toolbar>
        <Button icon={ChevronLeft} aria-label="The week before" onClick={() => setWeek(addDays(week, -7))} />
        <label className="sr-only" htmlFor="roster-week">
          Week
        </label>
        <Input id="roster-week" type="date" className="w-[190px]" value={week} onChange={(e) => isBusinessDate(e.target.value) && setWeek(startOfWeek(e.target.value))} />
        <Button icon={ChevronRight} aria-label="The week after" onClick={() => setWeek(addDays(week, 7))} />
        <span className="text-muted">
          {formatDate(week)} to {formatDate(addDays(week, 6))}
        </span>
        {rights.attendance && (
          <>
            <Button variant="sun" icon={Save} shortcut="Ctrl+S" disabled={!dirty} busy={save.isPending} onClick={() => save.mutate()}>
              Save the week
            </Button>
            <Button icon={CalendarPlus} onClick={() => setChanging(true)}>
              Change a day
            </Button>
          </>
        )}
      </Toolbar>
      <DataTable<RosterRowInfo>
        rows={query.data?.rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => r.employeeId}
        caption="Roster"
        empty={<EmptyState title="Nobody works this week" />}
        columns={[
          { key: 'who', header: 'Employee', cell: (r) => <span className="font-semibold">{r.code} {r.name}</span> },
          { key: 'usual', header: 'Usual shift', cell: (r) => (r.usualShiftId ? shiftName(r.usualShiftId) : 'Working week'), hideBelow: 'md' },
          {
            key: 'week',
            header: 'This week',
            cell: (r) => {
              const value = picked[r.employeeId] ?? r.weekShiftId ?? '';
              return (
                <Select
                  aria-label={`Shift of ${r.name} this week`}
                  className="min-w-[200px]"
                  value={value}
                  disabled={!rights.attendance}
                  onChange={(e) => setPicked((p) => ({ ...p, [r.employeeId]: e.target.value }))}
                >
                  <option value="">The usual shift</option>
                  {(shifts.data ?? []).filter((s) => s.isActive || s.id === value).map((s) => (
                    <option key={s.id ?? s.name} value={s.id ?? ''}>
                      {s.name} ({s.startTime}–{s.endTime})
                    </option>
                  ))}
                </Select>
              );
            },
          },
          {
            key: 'changes',
            header: 'Days changed',
            className: 'wrap',
            cell: (r) => (
              <span className="flex flex-wrap gap-1.5">
                {r.changes.map((c: RosterChangeInfo) => (
                  <Chip key={c.id ?? c.date} tone={c.shiftId ? 'plain' : 'warn'}>
                    {formatDate(c.date)}: {shiftName(c.shiftId)}
                    {rights.attendance && c.id && (
                      <button type="button" className="ml-1 rounded-full p-0.5 hover:bg-black/10" aria-label={`Undo the change of ${c.date}`} onClick={() => removeChange.mutate(c.id!)}>
                        <X className="h-3.5 w-3.5" />
                      </button>
                    )}
                  </Chip>
                ))}
              </span>
            ),
          },
        ]}
      />
      <p className="mt-2 px-1 text-ink">The week&apos;s shift wins over the usual one, and a changed day over both. Holidays come from HR settings.</p>
      {changing && <ChangeDayDialog week={week} onClose={() => setChanging(false)} />}
    </>
  );
}

function ChangeDayDialog({ week, onClose }: { week: string; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const shifts = useShifts();
  const [employeeId, setEmployeeId] = useState('');
  const [date, setDate] = useState(week);
  const [shiftId, setShiftId] = useState('');
  const [note, setNote] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () => api.addRosterChange({ id: null, employeeId, date, shiftId: shiftId || null, note: note.trim() || null }),
    onSuccess: () => {
      toast.ok('The day is changed.');
      void queryClient.invalidateQueries({ queryKey: ['hr-roster'] });
      onClose();
    },
  });
  return (
    <Dialog
      open
      title="Change a day"
      onClose={onClose}
      onSubmit={() => {
        const p = !employeeId ? 'Choose the employee.' : !isBusinessDate(date) ? 'Give the day.' : null;
        setProblem(p);
        if (!p) save.mutate();
      }}
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
        <Field label="Employee" htmlFor="change-employee">
          <EmployeeSelect id="change-employee" value={employeeId} onChange={setEmployeeId} />
        </Field>
        <Field label="Day" htmlFor="change-date">
          <Input id="change-date" type="date" className="max-w-[200px]" value={date} onChange={(e) => setDate(e.target.value)} />
        </Field>
        <Field label="Works" htmlFor="change-shift">
          <Select id="change-shift" value={shiftId} onChange={(e) => setShiftId(e.target.value)}>
            <option value="">A day off</option>
            {(shifts.data ?? []).filter((s) => s.isActive).map((s) => (
              <option key={s.id ?? s.name} value={s.id ?? ''}>
                {s.name} ({s.startTime}–{s.endTime})
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Note" htmlFor="change-note">
          <Input id="change-note" value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? save.error} />
    </Dialog>
  );
}
