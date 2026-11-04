'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Download, Eye, Pencil, Plus, RefreshCw, Save, Trash2 } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useMemo, useState } from 'react';
import { ConflictDialog } from '@/components/masters';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Field, Input, Select, TextArea } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { formatCount, formatDate, formatMoney, formatShortUtc } from '@/lib/format';
import { P } from '@/lib/nav';
import { describeRules, formToRules, MONTHS, rulesToForm, type RuleForm, type RuleKey } from '@/lib/segments';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { SegmentMember, SegmentResult, SegmentRow, SegmentRules } from '@/lib/types';

export default function SegmentsPage() {
  return (
    <Suspense>
      <Segments />
    </Suspense>
  );
}

/** A segment of the user's own store, or (with the right to change shared records) any segment. */
function useMayChange() {
  const { can, canChangeShared, user } = useSession();
  return (segment: { storeId: string | null } | null) =>
    can(P.campaigns) && (canChangeShared || (segment ? segment.storeId !== null && segment.storeId === user.storeId : !!user.storeId));
}

function useNames() {
  const { stores, currencySymbol: symbol } = useStore();
  const departments = useQuery({ queryKey: ['departments'], queryFn: () => api.departments(), staleTime: 5 * 60_000 });
  return useMemo(
    () => ({
      store: (id: string) => stores.find((s) => s.id === id)?.name,
      department: (id: string) => departments.data?.find((d) => d.id === id)?.name,
      symbol,
    }),
    [stores, departments.data, symbol],
  );
}

function Segments() {
  const params = useSearchParams();
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const mayChange = useMayChange();
  const { currencySymbol: symbol } = useStore();
  const [selected, setSelected] = useState<SegmentRow | null>(null);
  const [editing, setEditing] = useState<SegmentRow | 'new' | null>(null);
  const [deleting, setDeleting] = useState<SegmentRow | null>(null);
  const [result, setResult] = useState<{ id: string; result: SegmentResult } | null>(null);
  const [downloading, setDownloading] = useState(false);
  const query = useQuery({ queryKey: ['segments'], queryFn: () => api.segments() });

  useEffect(() => {
    if (params.get('new') === '1') {
      if (mayChange(null)) setEditing('new');
      router.replace('/segments/');
    }
  }, [params, router, mayChange]);

  const evaluate = useMutation({
    mutationFn: (id: string) => api.evaluateSegment(id),
    onSuccess: (r, id) => {
      setResult({ id, result: r });
      toast.ok(`${formatCount(r.count)} clients are in the segment now.`);
      void queryClient.invalidateQueries({ queryKey: ['segments'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteSegment(id),
    onSuccess: () => {
      toast.ok(`${deleting?.name ?? 'The segment'} is deleted.`);
      setDeleting(null);
      setSelected(null);
      void queryClient.invalidateQueries({ queryKey: ['segments'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  async function downloadCsv(segment: SegmentRow) {
    setDownloading(true);
    try {
      const blob = await api.segmentCsv(segment.id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `segment-${segment.name.replace(/[^\w-]+/g, '-')}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : 'The CSV file did not download.');
    } finally {
      setDownloading(false);
    }
  }

  const shown = result && selected && result.id === selected.id ? result.result : null;

  return (
    <div>
      <PageHeader title="Segments" aside={query.data && `${formatCount(query.data.length)} segments`} />
      <Toolbar>
        {mayChange(null) && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            New segment
          </Button>
        )}
        <Button icon={Pencil} shortcut="Ctrl+O" disabled={!selected} onClick={() => selected && setEditing(selected)}>
          {selected && mayChange(selected) ? 'Edit' : 'Open'}
        </Button>
        <Button icon={RefreshCw} disabled={!selected} busy={evaluate.isPending} onClick={() => selected && evaluate.mutate(selected.id)}>
          Evaluate
        </Button>
        <Button icon={Download} disabled={!selected} busy={downloading} onClick={() => selected && void downloadCsv(selected)}>
          Export CSV
        </Button>
        {selected && mayChange(selected) && (
          <Button icon={Trash2} shortcut="Del" onClick={() => setDeleting(selected)}>
            Delete
          </Button>
        )}
      </Toolbar>
      <DataTable<SegmentRow>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(s) => s.id}
        selectedKey={selected?.id}
        onRowClick={(s) => (selected?.id === s.id ? setEditing(s) : setSelected(s))}
        caption="Segments"
        empty={<EmptyState title="No segments yet">A segment picks clients by what they bought, when, their birthday, points or consent.</EmptyState>}
        columns={[
          { key: 'name', header: 'Name', cell: (s) => <span className="font-semibold">{s.name}</span> },
          { key: 'rules', header: 'Who is in it', cell: (s) => s.summary, className: 'wrap', hideBelow: 'md' },
          { key: 'count', header: 'Clients', numeric: true, cell: (s) => (s.lastCount === null ? '—' : formatCount(s.lastCount)) },
          { key: 'when', header: 'Evaluated', cell: (s) => (s.lastEvaluatedAtUtc ? formatShortUtc(s.lastEvaluatedAtUtc) : 'Not yet'), hideBelow: 'lg' },
          { key: 'scope', header: 'For', cell: (s) => (s.storeId ? <Chip>One store</Chip> : <Chip tone="dark">Every store</Chip>), hideBelow: 'xl' },
        ]}
      />
      <p className="mt-2 px-1 text-ink">Choose a segment, then Evaluate to see who is in it now. Campaigns send to a segment&apos;s members who agreed to WhatsApp.</p>
      {shown && selected && (
        <Card className="mt-4" title={`${selected.name}: ${formatCount(shown.count)} clients`} aside={`Evaluated ${formatShortUtc(shown.evaluatedAtUtc)}`}>
          <Members members={shown.members} total={shown.count} symbol={symbol} />
        </Card>
      )}
      {editing && <SegmentDialog segment={editing === 'new' ? null : editing} readOnly={editing !== 'new' && !mayChange(editing)} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete the segment ${deleting?.name ?? ''}?`}
        message="Campaigns that used it keep their messages. A segment a campaign uses is not deleted."
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </div>
  );
}

function Members({ members, total, symbol }: { members: SegmentMember[]; total: number; symbol: string }) {
  return (
    <>
      <DataTable<SegmentMember>
        className="shadow-none"
        rows={members}
        rowKey={(m) => m.clientId}
        caption="Members"
        maxHeight="420px"
        empty={<p className="p-4 text-muted">No client meets these rules.</p>}
        columns={[
          { key: 'n', header: 'Number', cell: (m) => m.number ?? '', hideBelow: 'lg' },
          { key: 'name', header: 'Name', cell: (m) => <span className="font-semibold">{m.name}</span> },
          { key: 'wa', header: 'WhatsApp', cell: (m) => (m.whatsAppOptIn && m.whatsAppNumber ? <Chip tone="ok">{m.whatsAppNumber}</Chip> : m.whatsAppNumber ?? ''), hideBelow: 'md' },
          { key: 'spend', header: 'Spend', numeric: true, cell: (m) => formatMoney(m.spend, symbol) },
          { key: 'visits', header: 'Visits', numeric: true, cell: (m) => formatCount(m.visits), hideBelow: 'sm' },
          { key: 'last', header: 'Last bought', cell: (m) => formatDate(m.lastPurchase), hideBelow: 'md' },
          { key: 'pts', header: 'Points', numeric: true, cell: (m) => formatCount(m.points), hideBelow: 'xl' },
          { key: 'bal', header: 'Owing', numeric: true, cell: (m) => formatMoney(m.balance, symbol), hideBelow: 'xl' },
        ]}
      />
      {total > members.length && <p className="mt-2 text-muted">The first {formatCount(members.length)} are shown; Export CSV has all {formatCount(total)}.</p>}
    </>
  );
}

function SegmentDialog({ segment, readOnly, onClose }: { segment: SegmentRow | null; readOnly: boolean; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { canChangeShared, user } = useSession();
  const { stores, currencySymbol: symbol } = useStore();
  const names = useNames();
  const departments = useQuery({ queryKey: ['departments'], queryFn: () => api.departments(), staleTime: 5 * 60_000 });
  const [name, setName] = useState(segment?.name ?? '');
  const [description, setDescription] = useState(segment?.description ?? '');
  // Without the right to change shared records, a segment is for one's own store.
  const [form, setForm] = useState<RuleForm>(() => {
    const f = rulesToForm(segment?.rules);
    return !segment && !canChangeShared && user.storeId ? { ...f, storeId: user.storeId } : f;
  });
  const [problem, setProblem] = useState<string | null>(null);
  const [preview, setPreview] = useState<SegmentResult | null>(null);
  const [conflict, setConflict] = useState(false);
  const { rules, errors } = formToRules(form);
  const firstError = Object.values(errors)[0];

  const set = (key: RuleKey, value: string) => {
    setForm((f) => ({ ...f, [key]: value }));
    setPreview(null);
  };

  const save = useMutation({
    mutationFn: (body: { name: string; description: string | null; rules: SegmentRules }) =>
      segment ? api.saveSegment(segment.id, { ...body, version: segment.version }) : api.addSegment(body),
    onSuccess: (saved) => {
      toast.ok(`${saved.name} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['segments'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  const run = useMutation({ mutationFn: (r: SegmentRules) => api.previewSegment(r), onSuccess: setPreview });

  function submit() {
    if (readOnly) return onClose();
    const found = !name.trim() ? 'Give the segment a name.' : firstError ?? null;
    setProblem(found);
    if (!found) save.mutate({ name: name.trim(), description: description.trim() || null, rules });
  }

  const box = (key: RuleKey, label: string, extra: { type?: string; width?: string } = {}) => (
    <Field label={label} htmlFor={`seg-${key}`} error={errors[key]}>
      <Input
        id={`seg-${key}`}
        type={extra.type}
        inputMode={extra.type ? undefined : 'decimal'}
        className={extra.width ?? 'max-w-[180px]'}
        value={String(form[key])}
        readOnly={readOnly}
        invalid={!!errors[key]}
        onChange={(e) => set(key, e.target.value)}
      />
    </Field>
  );

  return (
    <Dialog
      open
      width="xl"
      title={segment ? segment.name : 'New segment'}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button className="mr-auto" icon={Eye} busy={run.isPending} disabled={!!firstError} onClick={() => run.mutate(rules)}>
            Preview
          </Button>
          {!readOnly && (
            <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
              Save
            </Button>
          )}
          <Button onClick={onClose} shortcut="Esc">
            {readOnly ? 'Close' : 'Cancel'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Name" htmlFor="seg-name">
          <Input id="seg-name" value={name} readOnly={readOnly} maxLength={80} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="Description" htmlFor="seg-description">
          <TextArea id="seg-description" rows={2} value={description} readOnly={readOnly} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <p className="rounded-control bg-sun-light px-3 py-2 font-semibold text-ink" data-testid="segment-summary">
          {describeRules(rules, names)}
        </p>
      </div>
      <div className="mt-4 grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
        <div className="space-y-3">
          <h3 className="font-heading text-section">What they bought</h3>
          <Field label="Counted over" htmlFor="seg-period">
            <Select id="seg-period" className="max-w-[220px]" value={form.period} disabled={readOnly} onChange={(e) => set('period', e.target.value)}>
              <option value="all">All time</option>
              <option value="days">The last days</option>
              <option value="dates">Dates</option>
            </Select>
          </Field>
          {form.period === 'days' && box('periodDays', 'Days')}
          {form.period === 'dates' && (
            <>
              {box('from', 'First day', { type: 'date', width: 'max-w-[200px]' })}
              {box('to', 'Last day', { type: 'date', width: 'max-w-[200px]' })}
            </>
          )}
          {box('minSpend', 'Spent at least')}
          {box('maxSpend', 'Spent at most')}
          {box('minVisits', 'Visits at least')}
          {box('maxVisits', 'Visits at most')}
          <Field label="Department" htmlFor="seg-departmentId">
            <Select id="seg-departmentId" value={form.departmentId} disabled={readOnly} onChange={(e) => set('departmentId', e.target.value)}>
              <option value="">Any</option>
              {(departments.data ?? []).map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="At the store" htmlFor="seg-storeId" hint={!canChangeShared ? 'Your segments are for your store.' : undefined}>
            <Select id="seg-storeId" value={form.storeId} disabled={readOnly || !canChangeShared} onChange={(e) => set('storeId', e.target.value)}>
              <option value="">Any store</option>
              {stores.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.code} · {s.name}
                </option>
              ))}
            </Select>
          </Field>
        </div>
        <div className="space-y-3">
          <h3 className="font-heading text-section">When and who</h3>
          {box('noPurchaseForDays', 'No purchase for (days)')}
          {box('lastPurchaseBefore', 'Last bought before', { type: 'date', width: 'max-w-[200px]' })}
          {box('lastPurchaseAfter', 'Last bought after', { type: 'date', width: 'max-w-[200px]' })}
          <Field label="Birthday in" htmlFor="seg-birthdayMonth">
            <Select id="seg-birthdayMonth" className="max-w-[220px]" value={form.birthdayMonth} disabled={readOnly} onChange={(e) => set('birthdayMonth', e.target.value)}>
              <option value="">Any month</option>
              {MONTHS.map((m, i) => (
                <option key={m} value={String(i + 1)}>
                  {m}
                </option>
              ))}
            </Select>
          </Field>
          {box('minPoints', 'Points at least')}
          {box('maxPoints', 'Points at most')}
          {box('minBalance', 'Owes at least')}
          <Field label="WhatsApp" htmlFor="seg-whatsAppConsent">
            <Select id="seg-whatsAppConsent" className="max-w-[260px]" value={form.whatsAppConsent} disabled={readOnly} onChange={(e) => set('whatsAppConsent', e.target.value)}>
              <option value="">Either</option>
              <option value="yes">Agreed to WhatsApp messages</option>
              <option value="no">Did not agree</option>
            </Select>
          </Field>
        </div>
      </div>
      <FormError error={problem ?? run.error ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
      {preview && (
        <div className="mt-4" data-testid="segment-preview">
          <h3 className="mb-2 font-heading text-section">
            {formatCount(preview.count)} {preview.count === 1 ? 'client' : 'clients'}
          </h3>
          <Members members={preview.members} total={preview.count} symbol={symbol} />
        </div>
      )}
      <ConflictDialog
        open={conflict}
        what="segment"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['segments'] });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}
