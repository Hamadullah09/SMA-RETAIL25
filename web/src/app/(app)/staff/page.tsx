'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Save, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { ConflictDialog } from '@/components/masters';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Checkbox, Field, Input, Select } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { isBusinessDate } from '@/lib/dates';
import { formatCount, formatDate, formatHours, formatMoney, formatPercent, formatQuantity, formatShortDateTime } from '@/lib/format';
import { checkNumber, numberText, parseNumber } from '@/lib/forms';
import { useDebounced } from '@/lib/hooks';
import { COMMISSION_BASIS, COMMISSION_SCOPES, label } from '@/lib/labels';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { CommissionRow, CommissionRule, HoursRow, TimeClockEntry } from '@/lib/types';

type Tab = 'clock' | 'hours' | 'commission' | 'rules';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'clock', label: 'Time clock' },
  { key: 'hours', label: 'Hours' },
  { key: 'commission', label: 'Commission' },
  { key: 'rules', label: 'Commission rules' },
];

export default function StaffPage() {
  const [tab, setTab] = useState<Tab>('clock');
  const dates = useDateRange('week');
  return (
    <div>
      <PageHeader title="Staff" />
      <Tabs label="Staff" tabs={TABS} value={tab} onChange={setTab} className="mb-4" />
      {tab !== 'rules' && (
        <Toolbar>
          <DateRangeBar state={dates} compact />
        </Toolbar>
      )}
      {tab === 'clock' && <TimeClock from={dates.range.from} to={dates.range.to} />}
      {tab === 'hours' && <Hours from={dates.range.from} to={dates.range.to} />}
      {tab === 'commission' && <Commission from={dates.range.from} to={dates.range.to} />}
      {tab === 'rules' && <Rules />}
    </div>
  );
}

/** The staff to choose from: the users list for those who manage users, otherwise the names seen in the entries. */
function useStaffList(storeId: string | null) {
  const { can } = useSession();
  return useQuery({ queryKey: ['users', storeId], queryFn: () => api.users({ storeId }), enabled: can(P.setupUsers) });
}

function TimeClock({ from, to }: { from: string; to: string }) {
  const { storeId } = useStore();
  const [userId, setUserId] = useState('');
  const users = useStaffList(storeId);
  const query = useQuery({ queryKey: ['time-clock', storeId, from, to, userId], queryFn: () => api.timeClock({ storeId, from, to, userId }) });
  const rows = query.data;
  const finished = (rows ?? []).filter((e) => e.outAt && e.hours !== null);
  const total = finished.reduce((s, e) => s + (e.hours ?? 0), 0);
  const people = useMemo(() => {
    if (users.data) return users.data.map((u) => ({ id: u.id, name: u.fullName || u.userName }));
    const seen = new Map<string, string>();
    for (const e of rows ?? []) seen.set(e.userId, e.userName);
    return [...seen].map(([id, name]) => ({ id, name }));
  }, [users.data, rows]);

  return (
    <>
      <Toolbar>
        <label className="sr-only" htmlFor="clock-user">
          Staff
        </label>
        <Select id="clock-user" className="w-[220px]" value={userId} onChange={(e) => setUserId(e.target.value)}>
          <option value="">Everyone</option>
          {people.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </Select>
        <span className="text-ink">Staff punch in and out at the tills; supervisors correct entries there, with a reason.</span>
      </Toolbar>
      <DataTable<TimeClockEntry>
        rows={rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(e) => e.id}
        caption="Time clock entries"
        empty={<EmptyState title="Nobody punched in between these days" />}
        columns={[
          { key: 'who', header: 'Staff', cell: (e) => <span className="font-semibold">{e.userName}</span> },
          { key: 'till', header: 'Till', cell: (e) => (e.storeCode ? `${e.storeCode}${e.stationNumber ? ` · ${String(e.stationNumber).padStart(3, '0')}` : ''}` : ''), hideBelow: 'lg' },
          { key: 'in', header: 'In', cell: (e) => formatShortDateTime(e.inAt) },
          { key: 'out', header: 'Out', cell: (e) => (e.outAt ? formatShortDateTime(e.outAt) : <Chip tone="warn">Still in</Chip>) },
          { key: 'hours', header: 'Hours', numeric: true, cell: (e) => (e.outAt && e.hours !== null ? formatHours(e.hours) : '') },
          { key: 'by', header: 'Changed by', cell: (e) => e.editedBy ?? '', hideBelow: 'md' },
          { key: 'why', header: 'Reason', cell: (e) => e.editReason ?? '', hideBelow: 'xl', className: 'wrap' },
        ]}
      />
      {rows && (
        <p className="mt-2 px-1 font-semibold text-ink">
          {formatCount(rows.length)} entries · {formatHours(total)} hours in finished shifts between these days
        </p>
      )}
    </>
  );
}

function Hours({ from, to }: { from: string; to: string }) {
  const { storeId } = useStore();
  const query = useQuery({ queryKey: ['hours', storeId, from, to], queryFn: () => api.hours({ storeId, from, to }) });
  const rows = query.data ?? [];
  return (
    <DataTable<HoursRow>
      rows={query.data}
      loading={query.isLoading}
      error={query.error}
      onRetry={() => void query.refetch()}
      rowKey={(h) => h.userId}
      caption="Hours worked"
      showTotals={rows.length > 0}
      empty={<EmptyState title="No hours between these days" />}
      columns={[
        { key: 'who', header: 'Staff', cell: (h) => <span className="font-semibold">{h.userName}</span> },
        { key: 'days', header: 'Days', numeric: true, cell: (h) => formatCount(h.days), total: formatCount(rows.reduce((s, h) => s + h.days, 0)) },
        { key: 'hours', header: 'Hours', numeric: true, cell: (h) => formatHours(h.hours), total: formatHours(rows.reduce((s, h) => s + h.hours, 0)) },
      ]}
    />
  );
}

function Commission({ from, to }: { from: string; to: string }) {
  const { storeId, currencySymbol: symbol } = useStore();
  const query = useQuery({ queryKey: ['commission', storeId, from, to], queryFn: () => api.commission({ storeId, from, to }) });
  const rows = query.data ?? [];
  const sum = (pick: (c: CommissionRow) => number) => rows.reduce((s, c) => s + pick(c), 0);
  const money = (n: number) => formatMoney(n, symbol);
  return (
    <DataTable<CommissionRow>
      rows={query.data}
      loading={query.isLoading}
      error={query.error}
      onRetry={() => void query.refetch()}
      rowKey={(c) => c.staff}
      caption="Commission"
      showTotals={rows.length > 0}
      empty={<EmptyState title="No commission between these days" />}
      columns={[
        { key: 'who', header: 'Staff', cell: (c) => <span className="font-semibold">{c.staff}</span> },
        { key: 'sales', header: 'Sales', numeric: true, cell: (c) => formatCount(c.sales), total: formatCount(sum((c) => c.sales)), hideBelow: 'md' },
        { key: 'units', header: 'Units', numeric: true, cell: (c) => formatQuantity(c.units), total: formatQuantity(sum((c) => c.units)), hideBelow: 'lg' },
        { key: 'net', header: 'Net sales', numeric: true, cell: (c) => money(c.netSales), total: money(sum((c) => c.netSales)), hideBelow: 'sm' },
        { key: 'rules', header: 'From rules', numeric: true, cell: (c) => money(c.fromRules), total: money(sum((c) => c.fromRules)), hideBelow: 'xl' },
        { key: 'items', header: 'From items', numeric: true, cell: (c) => money(c.fromItems), total: money(sum((c) => c.fromItems)), hideBelow: 'xl' },
        { key: 'comm', header: 'Commission', numeric: true, cell: (c) => <span className="font-semibold">{money(c.commission)}</span>, total: money(sum((c) => c.commission)) },
      ]}
    />
  );
}

function rate(rule: CommissionRule, symbol: string) {
  return rule.basis === 'AmountPerUnit' ? `${formatMoney(rule.percent, symbol)} a unit` : formatPercent(rule.percent, undefined, rule.percent % 1 === 0 ? 0 : 2);
}

function Rules() {
  const { can } = useSession();
  const symbol = useStore().currencySymbol;
  const toast = useToast();
  const queryClient = useQueryClient();
  const edit = can(P.timeClockEdit);
  const [selected, setSelected] = useState<CommissionRule | null>(null);
  const [editing, setEditing] = useState<CommissionRule | 'new' | null>(null);
  const [deleting, setDeleting] = useState<CommissionRule | null>(null);
  const query = useQuery({ queryKey: ['commission-rules'], queryFn: () => api.commissionRules() });
  const departments = useQuery({ queryKey: ['departments'], queryFn: () => api.departments(), staleTime: 5 * 60_000 });
  const categories = useQuery({ queryKey: ['categories', ''], queryFn: () => api.categories(null), staleTime: 5 * 60_000 });

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteCommissionRule(id),
    onSuccess: () => {
      toast.ok('The rule is deleted.');
      setDeleting(null);
      setSelected(null);
      void queryClient.invalidateQueries({ queryKey: ['commission-rules'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const appliesTo = (r: CommissionRule) => {
    if (r.scope === 'Department') return `Department ${departments.data?.find((d) => d.id === r.departmentId)?.name ?? ''}`.trim();
    if (r.scope === 'Category') return `Category ${categories.data?.find((c) => c.id === r.categoryId)?.name ?? ''}`.trim();
    return label(COMMISSION_SCOPES, r.scope);
  };

  return (
    <>
      {edit && (
        <Toolbar>
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            New rule
          </Button>
          <Button icon={Pencil} shortcut="Ctrl+O" disabled={!selected} onClick={() => selected && setEditing(selected)}>
            Edit
          </Button>
          <Button icon={Trash2} shortcut="Del" disabled={!selected} onClick={() => selected && setDeleting(selected)}>
            Delete
          </Button>
        </Toolbar>
      )}
      <DataTable<CommissionRule>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => r.id ?? r.name}
        selectedKey={selected?.id}
        onRowClick={(r) => {
          if (edit && selected?.id === r.id) setEditing(r);
          else setSelected(r);
        }}
        caption="Commission rules"
        empty={<EmptyState title="No commission rules">Without rules, each item&apos;s commission percent applies.</EmptyState>}
        columns={[
          { key: 'name', header: 'Name', cell: (r) => <span className="font-semibold">{r.name}</span> },
          { key: 'for', header: 'For', cell: (r) => r.userName ?? 'Everyone' },
          { key: 'to', header: 'Applies to', cell: (r) => r.target || appliesTo(r), hideBelow: 'md' },
          { key: 'how', header: 'Commission', cell: (r) => label(COMMISSION_BASIS, r.basis ?? 'PercentOfNetSales'), hideBelow: 'xl' },
          { key: 'rate', header: 'Rate', numeric: true, cell: (r) => rate(r, symbol) },
          { key: 'from', header: 'First day', cell: (r) => (r.startsOn ? formatDate(r.startsOn) : ''), hideBelow: 'lg' },
          { key: 'to2', header: 'Last day', cell: (r) => (r.endsOn ? formatDate(r.endsOn) : ''), hideBelow: 'lg' },
          { key: 'active', header: 'Active', cell: (r) => (r.isActive ? <Chip tone="ok">Active</Chip> : <Chip>Off</Chip>), hideBelow: 'sm' },
        ]}
      />
      <p className="mt-2 px-1 text-ink">
        One item&apos;s rule wins over a category&apos;s, a category&apos;s over a department&apos;s, and a salesperson&apos;s own rule over everyone&apos;s. {edit ? '' : 'Your role can read the rules but not change them.'}
      </p>
      {editing && <RuleDialog rule={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete the rule ${deleting?.name ?? ''}?`}
        message="Sales already made keep the commission they had."
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

function RuleDialog({ rule, onClose }: { rule: CommissionRule | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { storeId } = useStore();
  const users = useStaffList(storeId);
  const departments = useQuery({ queryKey: ['departments'], queryFn: () => api.departments(), staleTime: 5 * 60_000 });
  const categories = useQuery({ queryKey: ['categories', ''], queryFn: () => api.categories(null), staleTime: 5 * 60_000 });
  const [name, setName] = useState(rule?.name ?? '');
  const [userId, setUserId] = useState(rule?.userId ?? '');
  const [scope, setScope] = useState(rule?.scope ?? 'AllItems');
  const [departmentId, setDepartmentId] = useState(rule?.departmentId ?? '');
  const [categoryId, setCategoryId] = useState(rule?.categoryId ?? '');
  const [itemId, setItemId] = useState(rule?.itemId ?? '');
  const [itemText, setItemText] = useState('');
  const [basis, setBasis] = useState(rule?.basis ?? 'PercentOfNetSales');
  const [rateText, setRateText] = useState(numberText(rule?.percent ?? 0));
  const [startsOn, setStartsOn] = useState(rule?.startsOn ?? '');
  const [endsOn, setEndsOn] = useState(rule?.endsOn ?? '');
  const [isActive, setIsActive] = useState(rule?.isActive ?? true);
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const itemSearch = useDebounced(itemText.trim());
  const items = useQuery({
    queryKey: ['items', { search: itemSearch, pick: true }],
    queryFn: () => api.items({ search: itemSearch, pageSize: 20 }),
    enabled: scope === 'Item' && itemSearch.length > 0,
  });

  useEffect(() => {
    if (scope !== 'Department') setDepartmentId('');
    if (scope !== 'Category') setCategoryId('');
    if (scope !== 'Item') setItemId('');
  }, [scope]);

  const save = useMutation({
    mutationFn: (body: CommissionRule) => (rule?.id ? api.saveCommissionRule(rule.id, body) : api.addCommissionRule(body)),
    onSuccess: () => {
      toast.ok(`The rule ${name.trim()} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['commission-rules'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });

  function submit() {
    const rateProblem = checkNumber(rateText, { required: true, label: 'The rate', decimals: 4 });
    const problemText = !name.trim()
      ? 'Give the rule a name.'
      : scope === 'Department' && !departmentId
        ? 'Choose the department.'
        : scope === 'Category' && !categoryId
          ? 'Choose the category.'
          : scope === 'Item' && !itemId
            ? 'Find and choose the item.'
            : rateProblem ?? (startsOn && endsOn && startsOn > endsOn ? 'The last day is before the first day.' : null);
    setProblem(problemText);
    if (problemText) return;
    const user = users.data?.find((u) => u.id === userId);
    save.mutate({
      id: rule?.id ?? null,
      version: rule?.version ?? null,
      name: name.trim(),
      userId: userId || null,
      userName: userId ? user?.userName ?? rule?.userName ?? null : null,
      scope,
      departmentId: departmentId || null,
      categoryId: categoryId || null,
      itemId: itemId || null,
      target: rule?.target ?? null,
      percent: parseNumber(rateText) ?? 0,
      basis,
      startsOn: isBusinessDate(startsOn) ? startsOn : null,
      endsOn: isBusinessDate(endsOn) ? endsOn : null,
      isActive,
    });
  }

  return (
    <Dialog
      open
      title={rule ? 'Change commission rule' : 'New commission rule'}
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
        <Field label="Name" htmlFor="rule-name">
          <Input id="rule-name" value={name} maxLength={60} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="For" htmlFor="rule-user">
          <Select id="rule-user" value={userId} onChange={(e) => setUserId(e.target.value)}>
            <option value="">Everyone</option>
            {rule?.userId && !users.data && <option value={rule.userId}>{rule.userName}</option>}
            {(users.data ?? []).map((u) => (
              <option key={u.id} value={u.id}>
                {u.fullName || u.userName}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Applies to" htmlFor="rule-scope">
          <Select id="rule-scope" value={scope} onChange={(e) => setScope(e.target.value)}>
            {Object.entries(COMMISSION_SCOPES).map(([key, text]) => (
              <option key={key} value={key}>
                {text}
              </option>
            ))}
          </Select>
        </Field>
        {scope === 'Department' && (
          <Field label="Department" htmlFor="rule-dept">
            <Select id="rule-dept" value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
              <option value="">Choose…</option>
              {(departments.data ?? []).map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </Select>
          </Field>
        )}
        {scope === 'Category' && (
          <Field label="Category" htmlFor="rule-cat">
            <Select id="rule-cat" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
              <option value="">Choose…</option>
              {(categories.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
          </Field>
        )}
        {scope === 'Item' && (
          <Field label="Item" htmlFor="rule-item-search" hint={rule?.itemId && !itemId ? undefined : 'Type a stock code or description, then choose it.'}>
            <div className="space-y-2">
              <Input id="rule-item-search" value={itemText} placeholder="Stock code or description" onChange={(e) => setItemText(e.target.value)} />
              <Select aria-label="Item" value={itemId} onChange={(e) => setItemId(e.target.value)}>
                <option value="">{items.isFetching ? 'Looking…' : 'Choose…'}</option>
                {rule?.itemId && <option value={rule.itemId}>The rule&apos;s item</option>}
                {(items.data?.items ?? []).map((i) => (
                  <option key={i.id} value={i.id}>
                    {i.plu} · {i.name}
                  </option>
                ))}
              </Select>
            </div>
          </Field>
        )}
        <Field label="Commission" htmlFor="rule-basis">
          <Select id="rule-basis" value={basis} onChange={(e) => setBasis(e.target.value)}>
            {Object.entries(COMMISSION_BASIS).map(([key, text]) => (
              <option key={key} value={key}>
                {text}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Rate" htmlFor="rule-rate" hint={basis === 'AmountPerUnit' ? 'Money for every unit sold.' : 'Percent of the line after its discount.'}>
          <Input id="rule-rate" inputMode="decimal" className="max-w-[160px]" value={rateText} onChange={(e) => setRateText(e.target.value)} />
        </Field>
        <Field label="First day" htmlFor="rule-from">
          <Input id="rule-from" type="date" className="max-w-[200px]" value={startsOn} onChange={(e) => setStartsOn(e.target.value)} />
        </Field>
        <Field label="Last day" htmlFor="rule-to">
          <Input id="rule-to" type="date" className="max-w-[200px]" value={endsOn} onChange={(e) => setEndsOn(e.target.value)} />
        </Field>
        <Field label="" htmlFor="rule-active">
          <Checkbox label="Active" checked={isActive} onChange={setIsActive} />
        </Field>
      </div>
      <FormError error={problem ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
      <ConflictDialog
        open={conflict}
        what="rule"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['commission-rules'] });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}
