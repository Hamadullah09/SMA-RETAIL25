'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Copy, Info, Pencil, Plus, Save, Trash2, X } from 'lucide-react';
import { useState } from 'react';
import { useHrRights } from '@/components/hr/common';
import { ConflictDialog } from '@/components/masters';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select, TextArea } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { isBusinessDate } from '@/lib/dates';
import { numberText, parseNumber, textOrNull } from '@/lib/forms';
import { formatDate, formatMoney, formatPercent } from '@/lib/format';
import { DAILY_RATE_BASIS, formatDays, GENDERS, LATENESS_RULES, LEAVE_KINDS, SOCIAL_SECURITY_BASIS } from '@/lib/hr';
import type { HolidayInfo, HrListItem, HrStoreCountry, LeaveKind, LeaveTypeInfo, RuleSetInfo } from '@/lib/types';

/** A rate as people write it: 35%, 1.5%. */
const pct = (n: number | null) => formatPercent(n, undefined, n !== null && n % 1 !== 0 ? 2 : 0);

type Tab = 'departments' | 'designations' | 'leave' | 'holidays' | 'rules' | 'stores';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'departments', label: 'Departments' },
  { key: 'designations', label: 'Designations' },
  { key: 'leave', label: 'Leave types' },
  { key: 'holidays', label: 'Holidays' },
  { key: 'rules', label: 'Payroll rule sets' },
  { key: 'stores', label: 'Stores' },
];

/** Said on the rule set tab whatever the rule sets' own notes say. */
const BUDGET_NOTE =
  'Tax slabs, the minimum wage and the contribution rates change with every budget. Check each figure against the Finance Act and the provincial and EOBI notifications every year, and add the new tax year’s rule set from its first day.';

export default function HrSettingsPage() {
  const [tab, setTab] = useState<Tab>('departments');
  const rights = useHrRights();
  return (
    <div>
      <PageHeader title="HR settings" />
      <Tabs label="HR settings" tabs={TABS} value={tab} onChange={setTab} className="mb-4" />
      {!rights.editShared && rights.edit && tab !== 'stores' && (
        <p className="mb-3 px-1 text-ink">These lists are shared by every store; a super administrator changes them.</p>
      )}
      {tab === 'departments' && <NameList kind="departments" />}
      {tab === 'designations' && <NameList kind="designations" />}
      {tab === 'leave' && <LeaveTypes />}
      {tab === 'holidays' && <Holidays />}
      {tab === 'rules' && <RuleSets />}
      {tab === 'stores' && <Stores />}
    </div>
  );
}

function useSettings() {
  return useQuery({ queryKey: ['hr-settings'], queryFn: () => api.hrSettings(), staleTime: 60_000 });
}

// Departments and designations ---------------------------------------------------------------------------------------

function NameList({ kind }: { kind: 'departments' | 'designations' }) {
  const rights = useHrRights();
  const toast = useToast();
  const queryClient = useQueryClient();
  const queryKey = kind === 'departments' ? ['hr-departments'] : ['hr-designations'];
  const one = kind === 'departments' ? 'department' : 'designation';
  const query = useQuery({ queryKey, queryFn: () => (kind === 'departments' ? api.hrDepartments() : api.designations()) });
  const [editing, setEditing] = useState<HrListItem | 'new' | null>(null);
  const [deleting, setDeleting] = useState<HrListItem | null>(null);
  const remove = useMutation({
    mutationFn: (id: string) => (kind === 'departments' ? api.deleteHrDepartment(id) : api.deleteDesignation(id)),
    onSuccess: () => {
      toast.ok(`The ${one} is deleted.`);
      setDeleting(null);
      void queryClient.invalidateQueries({ queryKey });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  return (
    <>
      {rights.editShared && (
        <Toolbar>
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            {kind === 'departments' ? 'New department' : 'New designation'}
          </Button>
        </Toolbar>
      )}
      <DataTable<HrListItem>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(d) => d.id ?? d.name}
        onRowClick={rights.editShared ? (d) => setEditing(d) : undefined}
        caption={kind === 'departments' ? 'HR departments' : 'Designations'}
        empty={<EmptyState title={kind === 'departments' ? 'No departments yet' : 'No designations yet'}>{kind === 'departments' ? 'Sales floor, Warehouse, Accounts…' : 'Cashier, Store manager…'}</EmptyState>}
        columns={[
          { key: 'name', header: 'Name', cell: (d) => <span className="font-semibold">{d.name}</span> },
          { key: 'count', header: 'Employees', numeric: true, cell: (d) => String(d.employees ?? 0) },
          { key: 'active', header: 'Active', cell: (d) => (d.isActive ? <Chip tone="ok">Active</Chip> : <Chip>Off</Chip>) },
          {
            key: 'act',
            header: '',
            cell: (d) =>
              rights.editShared && d.id ? (
                <button
                  type="button"
                  className="btn h-10 min-h-0 px-3"
                  aria-label={`Delete ${d.name}`}
                  onClick={(e) => {
                    e.stopPropagation();
                    setDeleting(d);
                  }}
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              ) : null,
          },
        ]}
      />
      {editing && <NameDialog kind={kind} item={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete ${deleting?.name ?? ''}?`}
        message={`A ${one} employees are in is kept; make it inactive instead.`}
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

function NameDialog({ kind, item, onClose }: { kind: 'departments' | 'designations'; item: HrListItem | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [name, setName] = useState(item?.name ?? '');
  const [active, setActive] = useState(item?.isActive ?? true);
  const [conflict, setConflict] = useState(false);
  const queryKey = kind === 'departments' ? ['hr-departments'] : ['hr-designations'];
  const save = useMutation({
    mutationFn: () => {
      const body: HrListItem = { id: item?.id ?? null, version: item?.version ?? null, name: name.trim(), isActive: active };
      if (kind === 'departments') return item?.id ? api.saveHrDepartment(item.id, body) : api.addHrDepartment(body);
      return item?.id ? api.saveDesignation(item.id, body) : api.addDesignation(body);
    },
    onSuccess: () => {
      toast.ok(`${name.trim()} is saved.`);
      void queryClient.invalidateQueries({ queryKey });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  const what = kind === 'departments' ? 'department' : 'designation';
  return (
    <Dialog
      open
      width="sm"
      title={item ? `Change ${what}` : `New ${what}`}
      onClose={onClose}
      onSubmit={() => name.trim() && save.mutate()}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending} disabled={!name.trim()}>
            Save
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Name" htmlFor="list-name" stacked>
          <Input id="list-name" value={name} maxLength={60} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Checkbox label="Active" checked={active} onChange={setActive} />
      </div>
      <FormError error={save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null} />
      <ConflictDialog
        open={conflict}
        what={what}
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}

// Leave types --------------------------------------------------------------------------------------------------------

function CountryPicker({ id, value, onChange, all }: { id: string; value: string; onChange(value: string): void; all?: boolean }) {
  const settings = useSettings();
  const countries = settings.data?.countries ?? ['PK'];
  return (
    <Select id={id} className="w-[150px]" value={value} onChange={(e) => onChange(e.target.value)}>
      {all && <option value="">Every country</option>}
      {countries.map((c) => (
        <option key={c} value={c}>
          {c}
        </option>
      ))}
    </Select>
  );
}

function LeaveTypes() {
  const rights = useHrRights();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [country, setCountry] = useState('');
  const query = useQuery({ queryKey: ['hr-leave-types', country], queryFn: () => api.leaveTypes(country || null) });
  const [editing, setEditing] = useState<LeaveTypeInfo | 'new' | null>(null);
  const [deleting, setDeleting] = useState<LeaveTypeInfo | null>(null);
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteLeaveType(id),
    onSuccess: () => {
      toast.ok('The leave type is deleted.');
      setDeleting(null);
      void queryClient.invalidateQueries({ queryKey: ['hr-leave-types'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  return (
    <>
      <Toolbar>
        <label className="sr-only" htmlFor="lt-country">
          Country
        </label>
        <CountryPicker id="lt-country" value={country} onChange={setCountry} all />
        {rights.editShared && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            New leave type
          </Button>
        )}
      </Toolbar>
      <DataTable<LeaveTypeInfo>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(t) => t.id ?? `${t.country}-${t.code}`}
        onRowClick={rights.editShared ? (t) => setEditing(t) : undefined}
        caption="Leave types"
        empty={<EmptyState title="No leave types" />}
        columns={[
          { key: 'country', header: 'Country', cell: (t) => t.country, hideBelow: 'md' },
          { key: 'code', header: 'Code', cell: (t) => <span className="font-semibold">{t.code}</span> },
          { key: 'name', header: 'Leave', cell: (t) => t.name },
          { key: 'days', header: 'Days a year', numeric: true, cell: (t) => (t.daysPerYear > 0 ? formatDays(t.daysPerYear) : 'No limit') },
          { key: 'paid', header: 'Paid', cell: (t) => (t.isPaid ? 'Paid' : 'Unpaid') },
          { key: 'earn', header: 'Earned monthly', cell: (t) => (t.accruesMonthly ? 'Yes' : ''), hideBelow: 'lg' },
          { key: 'carry', header: 'Carried over', numeric: true, cell: (t) => (t.carryOverMaxDays > 0 ? formatDays(t.carryOverMaxDays) : ''), hideBelow: 'lg' },
          { key: 'cash', header: 'Paid out on leaving', cell: (t) => (t.encashable ? 'Yes' : ''), hideBelow: 'xl' },
          { key: 'for', header: 'For', cell: (t) => (t.onlyFor ? GENDERS[t.onlyFor] : 'Everyone'), hideBelow: 'xl' },
          { key: 'active', header: 'Active', cell: (t) => (t.isActive ? <Chip tone="ok">Active</Chip> : <Chip>Off</Chip>), hideBelow: 'sm' },
          {
            key: 'act',
            header: '',
            cell: (t) =>
              rights.editShared && t.id ? (
                <button
                  type="button"
                  className="btn h-10 min-h-0 px-3"
                  aria-label={`Delete ${t.name}`}
                  onClick={(e) => {
                    e.stopPropagation();
                    setDeleting(t);
                  }}
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              ) : null,
          },
        ]}
      />
      {editing && <LeaveTypeDialog type={editing === 'new' ? null : editing} country={country} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete ${deleting?.name ?? ''}?`}
        message="Leave that was taken keeps its type; make it inactive instead."
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

function LeaveTypeDialog({ type, country, onClose }: { type: LeaveTypeInfo | null; country: string; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const settings = useSettings();
  const [form, setForm] = useState({
    country: type?.country ?? (country || settings.data?.companyCountry || 'PK'),
    code: type?.code ?? '',
    name: type?.name ?? '',
    kind: (type?.kind ?? 'Other') as LeaveKind,
    days: numberText(type?.daysPerYear ?? 0),
    isPaid: type?.isPaid ?? true,
    accrues: type?.accruesMonthly ?? false,
    carry: numberText(type?.carryOverMaxDays ?? 0),
    encashable: type?.encashable ?? false,
    calendar: type?.countsCalendarDays ?? false,
    onlyFor: type?.onlyFor ?? '',
    service: numberText(type?.minimumServiceMonths ?? 0),
    active: type?.isActive ?? true,
    order: numberText(type?.sortOrder ?? 0),
  });
  const [conflict, setConflict] = useState(false);
  const set = <K extends keyof typeof form>(k: K, v: (typeof form)[K]) => setForm((f) => ({ ...f, [k]: v }));
  const save = useMutation({
    mutationFn: () => {
      const body: LeaveTypeInfo = {
        id: type?.id ?? null,
        version: type?.version ?? null,
        country: form.country.trim().toUpperCase(),
        code: form.code.trim().toUpperCase(),
        name: form.name.trim(),
        kind: form.kind,
        daysPerYear: parseNumber(form.days) ?? 0,
        isPaid: form.isPaid,
        accruesMonthly: form.accrues,
        carryOverMaxDays: parseNumber(form.carry) ?? 0,
        encashable: form.encashable,
        countsCalendarDays: form.calendar,
        onlyFor: (form.onlyFor || null) as LeaveTypeInfo['onlyFor'],
        minimumServiceMonths: parseNumber(form.service) ?? 0,
        isActive: form.active,
        sortOrder: parseNumber(form.order) ?? 0,
      };
      return type?.id ? api.saveLeaveType(type.id, body) : api.addLeaveType(body);
    },
    onSuccess: () => {
      toast.ok(`${form.name.trim()} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['hr-leave-types'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  const box = (key: 'days' | 'carry' | 'service' | 'order', label: string, hint?: string) => (
    <Field label={label} htmlFor={`lt-${key}`} hint={hint}>
      <Input id={`lt-${key}`} inputMode="decimal" className="max-w-[140px]" value={form[key]} onChange={(e) => set(key, e.target.value)} />
    </Field>
  );
  return (
    <Dialog
      open
      title={type ? 'Change leave type' : 'New leave type'}
      onClose={onClose}
      onSubmit={() => save.mutate()}
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
        <Field label="Country" htmlFor="lt-country-edit">
          <Input id="lt-country-edit" className="max-w-[100px]" maxLength={2} value={form.country} onChange={(e) => set('country', e.target.value)} />
        </Field>
        <Field label="Code" htmlFor="lt-code">
          <Input id="lt-code" className="max-w-[140px]" maxLength={10} value={form.code} onChange={(e) => set('code', e.target.value)} />
        </Field>
        <Field label="Name" htmlFor="lt-name">
          <Input id="lt-name" maxLength={60} value={form.name} onChange={(e) => set('name', e.target.value)} />
        </Field>
        <Field label="Kind" htmlFor="lt-kind">
          <Select id="lt-kind" value={form.kind} onChange={(e) => set('kind', e.target.value as LeaveKind)}>
            {Object.entries(LEAVE_KINDS).map(([k, v]) => (
              <option key={k} value={k}>
                {v}
              </option>
            ))}
          </Select>
        </Field>
        {box('days', 'Days a year', '0 for leave without a limit.')}
        {box('carry', 'Carried into the next year', 'At most this many days left over.')}
        {box('service', 'Months of service first')}
        <Field label="For" htmlFor="lt-for">
          <Select id="lt-for" value={form.onlyFor} onChange={(e) => set('onlyFor', e.target.value)}>
            <option value="">Everyone</option>
            {Object.entries(GENDERS).map(([k, v]) => (
              <option key={k} value={k}>
                {v} employees
              </option>
            ))}
          </Select>
        </Field>
        <Field label="" htmlFor="lt-flags">
          <div>
            <Checkbox label="Paid" checked={form.isPaid} onChange={(v) => set('isPaid', v)} />
            <Checkbox label="Earned a twelfth each month" checked={form.accrues} onChange={(v) => set('accrues', v)} />
            <Checkbox label="What is left is paid out on leaving" checked={form.encashable} onChange={(v) => set('encashable', v)} />
            <Checkbox label="Counts every day, not only working days" checked={form.calendar} onChange={(v) => set('calendar', v)} />
            <Checkbox label="Active" checked={form.active} onChange={(v) => set('active', v)} />
          </div>
        </Field>
        {box('order', 'Order in lists')}
      </div>
      <FormError error={save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null} />
      <ConflictDialog
        open={conflict}
        what="leave type"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['hr-leave-types'] });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}

// Holidays -----------------------------------------------------------------------------------------------------------

function Holidays() {
  const rights = useHrRights();
  const toast = useToast();
  const queryClient = useQueryClient();
  const settings = useSettings();
  const [country, setCountry] = useState('');
  const [year, setYear] = useState(String(new Date().getFullYear()));
  const shown = country || settings.data?.companyCountry || 'PK';
  const query = useQuery({ queryKey: ['hr-holidays', shown, year], queryFn: () => api.holidays({ country: shown, year: Number(year) || null }) });
  const [editing, setEditing] = useState<HolidayInfo | 'new' | null>(null);
  const [deleting, setDeleting] = useState<HolidayInfo | null>(null);
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteHoliday(id),
    onSuccess: () => {
      toast.ok('The holiday is deleted.');
      setDeleting(null);
      void queryClient.invalidateQueries({ queryKey: ['hr-holidays'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  return (
    <>
      <Toolbar>
        <label className="sr-only" htmlFor="hol-country">
          Country
        </label>
        <CountryPicker id="hol-country" value={shown} onChange={setCountry} />
        <label className="text-muted" htmlFor="hol-year">
          Year
        </label>
        <Input id="hol-year" inputMode="numeric" className="w-[110px]" value={year} maxLength={4} onChange={(e) => setYear(e.target.value)} />
        {rights.editShared && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            New holiday
          </Button>
        )}
      </Toolbar>
      <DataTable<HolidayInfo>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(h) => h.id ?? `${h.country}-${h.date}`}
        onRowClick={rights.editShared ? (h) => setEditing(h) : undefined}
        caption="Public holidays"
        empty={<EmptyState title="No holidays this year">Add the year&apos;s gazetted holidays; nobody is absent on them.</EmptyState>}
        columns={[
          { key: 'date', header: 'Day', cell: (h) => <span className="font-semibold">{formatDate(h.date)}</span> },
          { key: 'name', header: 'Holiday', cell: (h) => h.name },
          { key: 'country', header: 'Country', cell: (h) => h.country, hideBelow: 'md' },
          {
            key: 'act',
            header: '',
            cell: (h) =>
              rights.editShared && h.id ? (
                <button
                  type="button"
                  className="btn h-10 min-h-0 px-3"
                  aria-label={`Delete ${h.name}`}
                  onClick={(e) => {
                    e.stopPropagation();
                    setDeleting(h);
                  }}
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              ) : null,
          },
        ]}
      />
      <p className="mt-2 px-1 text-ink">The Islamic holidays follow the moon: check their dates against the government&apos;s notification each year.</p>
      {editing && <HolidayDialog holiday={editing === 'new' ? null : editing} country={shown} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete ${deleting?.name ?? ''}?`}
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

function HolidayDialog({ holiday, country, onClose }: { holiday: HolidayInfo | null; country: string; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [countryText, setCountry] = useState(holiday?.country ?? country);
  const [date, setDate] = useState(holiday?.date ?? '');
  const [name, setName] = useState(holiday?.name ?? '');
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const save = useMutation({
    mutationFn: () => {
      const body: HolidayInfo = { id: holiday?.id ?? null, version: holiday?.version ?? null, country: countryText.trim().toUpperCase(), date, name: name.trim() };
      return holiday?.id ? api.saveHoliday(holiday.id, body) : api.addHoliday(body);
    },
    onSuccess: () => {
      toast.ok(`${name.trim()} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['hr-holidays'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  return (
    <Dialog
      open
      width="sm"
      title={holiday ? 'Change holiday' : 'New holiday'}
      onClose={onClose}
      onSubmit={() => {
        const p = !isBusinessDate(date) ? 'Give the day.' : !name.trim() ? 'Give the holiday a name.' : null;
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
        <Field label="Country" htmlFor="hol-country-edit" stacked>
          <Input id="hol-country-edit" className="max-w-[100px]" maxLength={2} value={countryText} onChange={(e) => setCountry(e.target.value)} />
        </Field>
        <Field label="Day" htmlFor="hol-date" stacked>
          <Input id="hol-date" type="date" className="max-w-[200px]" value={date} onChange={(e) => setDate(e.target.value)} />
        </Field>
        <Field label="Name" htmlFor="hol-name" stacked>
          <Input id="hol-name" maxLength={80} value={name} onChange={(e) => setName(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
      <ConflictDialog
        open={conflict}
        what="holiday"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['hr-holidays'] });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}

// Payroll rule sets --------------------------------------------------------------------------------------------------

function RuleSets() {
  const rights = useHrRights();
  const toast = useToast();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['hr-rule-sets', 'all'], queryFn: () => api.ruleSets() });
  const [selected, setSelected] = useState<RuleSetInfo | null>(null);
  const [editing, setEditing] = useState<{ set: RuleSetInfo; copy: boolean } | null>(null);
  const [deleting, setDeleting] = useState<RuleSetInfo | null>(null);
  const shown = selected ?? query.data?.[0] ?? null;
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteRuleSet(id),
    onSuccess: () => {
      toast.ok('The rule set is deleted.');
      setDeleting(null);
      setSelected(null);
      void queryClient.invalidateQueries({ queryKey: ['hr-rule-sets'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  return (
    <>
      <div className="mb-4 flex items-start gap-3 rounded-card bg-sun-light px-5 py-4 text-ink" role="note" data-testid="budget-note">
        <Info className="mt-0.5 h-5 w-5 shrink-0" aria-hidden="true" />
        <p className="font-semibold">{BUDGET_NOTE}</p>
      </div>
      {rights.editShared && (
        <Toolbar>
          <Button variant="sun" icon={Copy} disabled={!shown} onClick={() => shown && setEditing({ set: nextYear(shown), copy: true })}>
            Start the next tax year
          </Button>
          <Button icon={Pencil} disabled={!shown} onClick={() => shown && setEditing({ set: shown, copy: false })}>
            Edit
          </Button>
          <Button icon={Trash2} disabled={!shown} onClick={() => shown && setDeleting(shown)}>
            Delete
          </Button>
        </Toolbar>
      )}
      <DataTable<RuleSetInfo>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => r.id ?? r.name}
        selectedKey={shown?.id}
        onRowClick={(r) => {
          if (shown?.id === r.id && rights.editShared) setEditing({ set: r, copy: false });
          else setSelected(r);
        }}
        caption="Payroll rule sets"
        empty={<EmptyState title="No payroll rules yet" />}
        columns={[
          { key: 'country', header: 'Country', cell: (r) => r.country },
          { key: 'name', header: 'Rule set', cell: (r) => <span className="font-semibold">{r.name}</span> },
          { key: 'from', header: 'From', cell: (r) => formatDate(r.effectiveFrom) },
          { key: 'to', header: 'To', cell: (r) => (r.effectiveTo ? formatDate(r.effectiveTo) : ''), hideBelow: 'md' },
          { key: 'wage', header: 'Minimum wage', numeric: true, cell: (r) => formatMoney(r.minimumWage), hideBelow: 'lg' },
          { key: 'slabs', header: 'Tax slabs', numeric: true, cell: (r) => String(r.taxSlabs.length), hideBelow: 'lg' },
        ]}
      />
      {shown && <RuleSetSummary set={shown} />}
      {editing && <RuleSetDialog set={editing.set} copy={editing.copy} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete ${deleting?.name ?? ''}?`}
        message="A rule set approved payroll used is kept; give it an end date instead."
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

/** A copy of a rule set for the year after it, to change what the new budget changed. */
function nextYear(set: RuleSetInfo): RuleSetInfo {
  const [y, m, d] = set.effectiveFrom.split('-').map(Number);
  const from = `${y + 1}-${String(m).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
  return { ...set, id: null, version: null, effectiveFrom: from, effectiveTo: null, name: `${set.country}, tax year from ${from}`, notes: set.notes };
}

function RuleSetSummary({ set }: { set: RuleSetInfo }) {
  return (
    <div className="mt-4 grid gap-4 lg:grid-cols-2">
      <section className="card p-5">
        <h2 className="mb-2 font-heading text-section">{set.name}</h2>
        {set.notes && <p className="mb-3 rounded-control bg-cream px-3 py-2 text-ink">{set.notes}</p>}
        <dl className="grid grid-cols-2 gap-x-4 gap-y-2">
          <dt className="text-muted">Minimum wage</dt>
          <dd className="font-semibold">{formatMoney(set.minimumWage)}</dd>
          <dt className="text-muted">EOBI</dt>
          <dd className="font-semibold">
            {pct(set.eobiEmployerRate)} employer, {pct(set.eobiEmployeeRate)} employee
          </dd>
          <dt className="text-muted">Social security</dt>
          <dd className="font-semibold">
            {pct(set.socialSecurityRate)} {set.socialSecurityProvince ?? ''}
            {set.socialSecurityWageCeiling > 0 ? `, up to ${formatMoney(set.socialSecurityWageCeiling)} a month` : ''}
          </dd>
          <dt className="text-muted">Working week</dt>
          <dd className="font-semibold">
            {set.workingDaysPerWeek} days, {set.hoursPerWeek} hours
          </dd>
          <dt className="text-muted">Overtime</dt>
          <dd className="font-semibold">× {set.overtimeMultiplier}, from {set.overtimeMinimumMinutes} minutes</dd>
          <dt className="text-muted">Lateness</dt>
          <dd className="font-semibold">{set.latenessRule === 'DayPerLates' ? `A day's pay for every ${set.latesPerDay} late arrivals` : LATENESS_RULES[set.latenessRule]}</dd>
          <dt className="text-muted">Gratuity</dt>
          <dd className="font-semibold">{set.gratuityEnabled ? `${set.gratuityDaysPerYear} days a year after ${set.gratuityMinimumMonths} months` : 'None'}</dd>
          <dt className="text-muted">CNIC and IBAN</dt>
          <dd className="font-semibold">
            {set.cnicExample ?? ''} {set.ibanExample ? `· ${set.ibanExample}` : ''}
          </dd>
        </dl>
      </section>
      <section className="card p-5">
        <h2 className="mb-2 font-heading text-section">Income tax on a year&apos;s taxable income</h2>
        <table className="grid-table">
          <thead>
            <tr>
              <th scope="col" className="num">Above</th>
              <th scope="col" className="num">Up to</th>
              <th scope="col" className="num">Tax</th>
              <th scope="col" className="num">Plus</th>
            </tr>
          </thead>
          <tbody>
            {set.taxSlabs.map((s) => (
              <tr key={s.incomeFrom}>
                <td className="num">{formatMoney(s.incomeFrom)}</td>
                <td className="num">{s.incomeTo === null ? '' : formatMoney(s.incomeTo)}</td>
                <td className="num">{formatMoney(s.fixedTax)}</td>
                <td className="num">{pct(s.rate)} of the excess</td>
              </tr>
            ))}
          </tbody>
        </table>
        {set.surchargeThreshold > 0 && (
          <p className="mt-2 text-muted">
            Above {formatMoney(set.surchargeThreshold)}, a surcharge of {pct(set.surchargeRate)} of the tax.
          </p>
        )}
        <h3 className="mb-1 mt-4 font-semibold">Allowances</h3>
        <ul className="space-y-1">
          {set.allowances.map((a) => (
            <li key={a.code}>
              <span className="font-semibold">{a.name}</span> ({a.code}):{' '}
              {a.isTaxable ? 'taxable' : a.exemptPercentOfBasic !== null ? `exempt up to ${pct(a.exemptPercentOfBasic)} of basic` : 'exempt'}
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}

interface SlabForm {
  from: string;
  to: string;
  fixed: string;
  rate: string;
}
interface AllowanceForm {
  code: string;
  name: string;
  taxable: boolean;
  exempt: string;
}

function RuleSetDialog({ set, copy, onClose }: { set: RuleSetInfo; copy: boolean; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [s, setS] = useState(() => ({
    country: set.country,
    name: set.name,
    effectiveFrom: set.effectiveFrom,
    effectiveTo: set.effectiveTo ?? '',
    taxYearStartMonth: numberText(set.taxYearStartMonth),
    minimumWage: numberText(set.minimumWage),
    eobiEmployerRate: numberText(set.eobiEmployerRate),
    eobiEmployeeRate: numberText(set.eobiEmployeeRate),
    socialSecurityProvince: set.socialSecurityProvince ?? '',
    socialSecurityRate: numberText(set.socialSecurityRate),
    socialSecurityWageCeiling: numberText(set.socialSecurityWageCeiling),
    socialSecurityBasis: set.socialSecurityBasis,
    workingDaysPerWeek: numberText(set.workingDaysPerWeek),
    hoursPerWeek: numberText(set.hoursPerWeek),
    overtimeMultiplier: numberText(set.overtimeMultiplier),
    overtimeMinimumMinutes: numberText(set.overtimeMinimumMinutes),
    dailyRateBasis: set.dailyRateBasis,
    latenessRule: set.latenessRule,
    latesPerDay: numberText(set.latesPerDay),
    deductAbsences: set.deductAbsences,
    surchargeThreshold: numberText(set.surchargeThreshold),
    surchargeRate: numberText(set.surchargeRate),
    gratuityEnabled: set.gratuityEnabled,
    gratuityDaysPerYear: numberText(set.gratuityDaysPerYear),
    gratuityMinimumMonths: numberText(set.gratuityMinimumMonths),
    gratuityPartYearMonths: numberText(set.gratuityPartYearMonths),
    gratuityOnBasic: set.gratuityOnBasic,
    cnicPattern: set.cnicPattern ?? '',
    cnicExample: set.cnicExample ?? '',
    ibanPattern: set.ibanPattern ?? '',
    ibanExample: set.ibanExample ?? '',
    notes: set.notes ?? '',
  }));
  const [slabs, setSlabs] = useState<SlabForm[]>(() =>
    set.taxSlabs.map((x) => ({ from: numberText(x.incomeFrom), to: x.incomeTo === null ? '' : numberText(x.incomeTo), fixed: numberText(x.fixedTax), rate: numberText(x.rate) })),
  );
  const [allowances, setAllowances] = useState<AllowanceForm[]>(() =>
    set.allowances.map((a) => ({ code: a.code, name: a.name, taxable: a.isTaxable, exempt: a.exemptPercentOfBasic === null ? '' : numberText(a.exemptPercentOfBasic) })),
  );
  const [conflict, setConflict] = useState(false);
  const up = <K extends keyof typeof s>(k: K, v: (typeof s)[K]) => setS((f) => ({ ...f, [k]: v }));
  const num = (text: string) => parseNumber(text) ?? 0;
  const save = useMutation({
    mutationFn: () => {
      const body: RuleSetInfo = {
        id: copy ? null : set.id,
        version: copy ? null : set.version,
        country: s.country.trim().toUpperCase(),
        name: s.name.trim(),
        effectiveFrom: s.effectiveFrom,
        effectiveTo: isBusinessDate(s.effectiveTo) ? s.effectiveTo : null,
        taxYearStartMonth: num(s.taxYearStartMonth),
        minimumWage: num(s.minimumWage),
        eobiEmployerRate: num(s.eobiEmployerRate),
        eobiEmployeeRate: num(s.eobiEmployeeRate),
        socialSecurityProvince: textOrNull(s.socialSecurityProvince),
        socialSecurityRate: num(s.socialSecurityRate),
        socialSecurityWageCeiling: num(s.socialSecurityWageCeiling),
        socialSecurityBasis: s.socialSecurityBasis,
        workingDaysPerWeek: num(s.workingDaysPerWeek),
        hoursPerWeek: num(s.hoursPerWeek),
        overtimeMultiplier: num(s.overtimeMultiplier),
        overtimeMinimumMinutes: num(s.overtimeMinimumMinutes),
        dailyRateBasis: s.dailyRateBasis,
        latenessRule: s.latenessRule,
        latesPerDay: num(s.latesPerDay),
        deductAbsences: s.deductAbsences,
        surchargeThreshold: num(s.surchargeThreshold),
        surchargeRate: num(s.surchargeRate),
        gratuityEnabled: s.gratuityEnabled,
        gratuityDaysPerYear: num(s.gratuityDaysPerYear),
        gratuityMinimumMonths: num(s.gratuityMinimumMonths),
        gratuityPartYearMonths: num(s.gratuityPartYearMonths),
        gratuityOnBasic: s.gratuityOnBasic,
        cnicPattern: textOrNull(s.cnicPattern),
        cnicExample: textOrNull(s.cnicExample),
        ibanPattern: textOrNull(s.ibanPattern),
        ibanExample: textOrNull(s.ibanExample),
        notes: textOrNull(s.notes),
        taxSlabs: slabs.map((x) => ({ incomeFrom: num(x.from), incomeTo: x.to.trim() ? num(x.to) : null, fixedTax: num(x.fixed), rate: num(x.rate) })),
        allowances: allowances.map((a, i) => ({ code: a.code.trim().toUpperCase(), name: a.name.trim(), isTaxable: a.taxable, exemptPercentOfBasic: a.taxable || !a.exempt.trim() ? null : num(a.exempt), sortOrder: i + 1 })),
      };
      return body.id ? api.saveRuleSet(body.id, body) : api.addRuleSet(body);
    },
    onSuccess: (saved) => {
      toast.ok(`${saved.name} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['hr-rule-sets'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  const box = (key: keyof typeof s, label: string, hint?: string, className = 'max-w-[180px]') => (
    <Field label={label} htmlFor={`rs-${key}`} hint={hint}>
      <Input id={`rs-${key}`} className={className} value={String(s[key])} onChange={(e) => up(key, e.target.value as never)} />
    </Field>
  );
  return (
    <Dialog
      open
      width="xl"
      title={copy ? 'The next tax year' : 'Change payroll rules'}
      onClose={onClose}
      onSubmit={() => save.mutate()}
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
      <p className="mb-3 rounded-control bg-sun-light px-3 py-2 font-semibold text-ink">{BUDGET_NOTE}</p>
      <div className="grid gap-6 lg:grid-cols-2">
        <div className="space-y-3">
          {box('country', 'Country', 'Two letters, such as PK.', 'max-w-[100px]')}
          {box('name', 'Name', undefined, '')}
          <Field label="From" htmlFor="rs-from">
            <Input id="rs-from" type="date" className="max-w-[200px]" value={s.effectiveFrom} onChange={(e) => up('effectiveFrom', e.target.value)} />
          </Field>
          <Field label="To" htmlFor="rs-to" hint="Empty until a later rule set takes over.">
            <Input id="rs-to" type="date" className="max-w-[200px]" value={s.effectiveTo} onChange={(e) => up('effectiveTo', e.target.value)} />
          </Field>
          {box('taxYearStartMonth', 'Tax year starts in month', '7 is July.')}
          {box('minimumWage', 'Minimum wage a month')}
          {box('eobiEmployerRate', 'EOBI employer %', 'Of the minimum wage.')}
          {box('eobiEmployeeRate', 'EOBI employee %', 'Of the minimum wage.')}
          {box('socialSecurityProvince', 'Social security of', 'Punjab (PESSI)…', '')}
          {box('socialSecurityRate', 'Social security %')}
          {box('socialSecurityWageCeiling', 'Covered up to a wage of', '0 covers everyone.')}
          <Field label="Worked out on" htmlFor="rs-ssbasis">
            <Select id="rs-ssbasis" value={s.socialSecurityBasis} onChange={(e) => up('socialSecurityBasis', e.target.value as RuleSetInfo['socialSecurityBasis'])}>
              {Object.entries(SOCIAL_SECURITY_BASIS).map(([k, v]) => (
                <option key={k} value={k}>
                  {v}
                </option>
              ))}
            </Select>
          </Field>
          {box('cnicPattern', 'CNIC format', 'A regular expression.', '')}
          {box('cnicExample', 'CNIC example', undefined, '')}
          {box('ibanPattern', 'IBAN format', 'A regular expression; the check digits are checked too.', '')}
          {box('ibanExample', 'IBAN example', undefined, '')}
        </div>
        <div className="space-y-3">
          {box('workingDaysPerWeek', 'Working days a week')}
          {box('hoursPerWeek', 'Hours a week')}
          {box('overtimeMultiplier', 'Overtime pay ×', '2 is double the hourly rate.')}
          {box('overtimeMinimumMinutes', 'Overtime counts from (minutes)')}
          <Field label="A day's pay is" htmlFor="rs-daily">
            <Select id="rs-daily" value={s.dailyRateBasis} onChange={(e) => up('dailyRateBasis', e.target.value as RuleSetInfo['dailyRateBasis'])}>
              {Object.entries(DAILY_RATE_BASIS).map(([k, v]) => (
                <option key={k} value={k}>
                  The month over {v.toLowerCase()}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="Lateness" htmlFor="rs-late">
            <Select id="rs-late" value={s.latenessRule} onChange={(e) => up('latenessRule', e.target.value as RuleSetInfo['latenessRule'])}>
              {Object.entries(LATENESS_RULES).map(([k, v]) => (
                <option key={k} value={k}>
                  {v}
                </option>
              ))}
            </Select>
          </Field>
          {s.latenessRule === 'DayPerLates' && box('latesPerDay', 'Late arrivals for a day')}
          <Field label="" htmlFor="rs-absent">
            <Checkbox label="Days absent without leave are not paid" checked={s.deductAbsences} onChange={(v) => up('deductAbsences', v)} />
          </Field>
          {box('surchargeThreshold', 'Surcharge above a year of', '0 for none.')}
          {box('surchargeRate', 'Surcharge % of the tax')}
          <Field label="" htmlFor="rs-gratuity">
            <Checkbox label="Gratuity when someone leaves" checked={s.gratuityEnabled} onChange={(v) => up('gratuityEnabled', v)} />
          </Field>
          {s.gratuityEnabled && (
            <>
              {box('gratuityDaysPerYear', "Days' wages a year")}
              {box('gratuityMinimumMonths', 'After months of service')}
              {box('gratuityPartYearMonths', 'A part year over months counts')}
              <Field label="" htmlFor="rs-gbasic">
                <Checkbox label="On the basic pay only" checked={s.gratuityOnBasic} onChange={(v) => up('gratuityOnBasic', v)} />
              </Field>
            </>
          )}
          <Field label="Notes" htmlFor="rs-notes" hint="Where the figures come from and when to check them.">
            <TextArea id="rs-notes" rows={4} maxLength={2000} value={s.notes} onChange={(e) => up('notes', e.target.value)} />
          </Field>
        </div>
      </div>

      <h3 className="mb-2 mt-5 font-heading text-section">Income tax slabs (a year)</h3>
      <div className="space-y-2">
        {slabs.map((x, i) => (
          <div key={i} className="grid grid-cols-2 gap-2 sm:grid-cols-[1fr_1fr_1fr_120px_auto] sm:items-center">
            <Input aria-label="Above" placeholder="Above" inputMode="decimal" value={x.from} onChange={(e) => setSlabs((l) => l.map((y, j) => (j === i ? { ...y, from: e.target.value } : y)))} />
            <Input aria-label="Up to" placeholder="Up to (empty: no limit)" inputMode="decimal" value={x.to} onChange={(e) => setSlabs((l) => l.map((y, j) => (j === i ? { ...y, to: e.target.value } : y)))} />
            <Input aria-label="Fixed tax" placeholder="Fixed tax" inputMode="decimal" value={x.fixed} onChange={(e) => setSlabs((l) => l.map((y, j) => (j === i ? { ...y, fixed: e.target.value } : y)))} />
            <Input aria-label="Rate %" placeholder="Rate %" inputMode="decimal" value={x.rate} onChange={(e) => setSlabs((l) => l.map((y, j) => (j === i ? { ...y, rate: e.target.value } : y)))} />
            <button type="button" className="btn h-10 min-h-0 px-3" aria-label="Remove the slab" onClick={() => setSlabs((l) => l.filter((_, j) => j !== i))}>
              <X className="h-4 w-4" />
            </button>
          </div>
        ))}
        <Button icon={Plus} onClick={() => setSlabs((l) => [...l, { from: l.length ? l[l.length - 1].to : '0', to: '', fixed: '', rate: '' }])}>
          Add a slab
        </Button>
      </div>

      <h3 className="mb-2 mt-5 font-heading text-section">Allowances and their tax</h3>
      <div className="space-y-2">
        {allowances.map((a, i) => (
          <div key={i} className="grid grid-cols-2 gap-2 sm:grid-cols-[140px_1fr_150px_160px_auto] sm:items-center">
            <Input aria-label="Code" placeholder="Code" maxLength={20} value={a.code} onChange={(e) => setAllowances((l) => l.map((y, j) => (j === i ? { ...y, code: e.target.value } : y)))} />
            <Input aria-label="Name" placeholder="Name" maxLength={60} value={a.name} onChange={(e) => setAllowances((l) => l.map((y, j) => (j === i ? { ...y, name: e.target.value } : y)))} />
            <Checkbox label="Taxable" checked={a.taxable} onChange={(v) => setAllowances((l) => l.map((y, j) => (j === i ? { ...y, taxable: v } : y)))} />
            <Input
              aria-label="Exempt up to % of basic"
              placeholder="Exempt up to % of basic"
              inputMode="decimal"
              disabled={a.taxable}
              value={a.exempt}
              onChange={(e) => setAllowances((l) => l.map((y, j) => (j === i ? { ...y, exempt: e.target.value } : y)))}
            />
            <button type="button" className="btn h-10 min-h-0 px-3" aria-label="Remove the allowance" onClick={() => setAllowances((l) => l.filter((_, j) => j !== i))}>
              <X className="h-4 w-4" />
            </button>
          </div>
        ))}
        <Button icon={Plus} onClick={() => setAllowances((l) => [...l, { code: '', name: '', taxable: true, exempt: '' }])}>
          Add an allowance
        </Button>
      </div>
      <FormError error={save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null} />
      <ConflictDialog
        open={conflict}
        what="rule set"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['hr-rule-sets'] });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}

// Stores' countries --------------------------------------------------------------------------------------------------

function Stores() {
  const rights = useHrRights();
  const settings = useSettings();
  const [editing, setEditing] = useState<HrStoreCountry | null>(null);
  return (
    <>
      <p className="mb-3 px-1 text-ink">
        Each store uses the rule sets, leave types and holidays of its country. Without one of its own, a store follows the company&apos;s ({settings.data?.companyCountry ?? '…'}).
      </p>
      <DataTable<HrStoreCountry>
        rows={settings.data?.stores}
        loading={settings.isLoading}
        error={settings.error}
        onRetry={() => void settings.refetch()}
        rowKey={(s) => s.storeId}
        onRowClick={rights.edit ? (s) => setEditing(s) : undefined}
        caption="Stores and their countries"
        empty={<EmptyState title="No stores" />}
        columns={[
          { key: 'store', header: 'Store', cell: (s) => <span className="font-semibold">{s.code} · {s.name}</span> },
          { key: 'country', header: 'Country', cell: (s) => s.country },
          { key: 'own', header: '', cell: (s) => (s.own ? <Chip tone="dark">Its own</Chip> : <Chip>The company&apos;s</Chip>) },
        ]}
      />
      {editing && <StoreCountryDialog store={editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function StoreCountryDialog({ store, onClose }: { store: HrStoreCountry; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const settings = useSettings();
  const [country, setCountry] = useState(store.own ? store.country : '');
  const save = useMutation({
    mutationFn: () => api.setStoreCountry(store.storeId, country.trim() ? country.trim().toUpperCase() : null),
    onSuccess: (next) => {
      queryClient.setQueryData(['hr-settings'], next);
      toast.ok(`${store.code} is saved.`);
      onClose();
    },
  });
  return (
    <Dialog
      open
      width="sm"
      title={`Country of ${store.code}`}
      onClose={onClose}
      onSubmit={() => save.mutate()}
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
      <Field label="Country" htmlFor="store-country" stacked hint={`Empty follows the company's (${settings.data?.companyCountry ?? ''}).`}>
        <Input id="store-country" className="max-w-[100px]" maxLength={2} value={country} onChange={(e) => setCountry(e.target.value)} />
      </Field>
      <FormError error={save.error} />
    </Dialog>
  );
}
