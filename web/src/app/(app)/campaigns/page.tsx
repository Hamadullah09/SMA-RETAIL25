'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ban, Eye, Pencil, Plus, Save, Send, Trash2 } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useRef, useState } from 'react';
import { CampaignDialog, CancelCampaignDialog, ScheduleDialog, useCampaignRefresh, WhatsAppBanner } from '@/components/campaigns';
import { ConflictDialog } from '@/components/masters';
import { Button, LinkButton } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select, TextArea } from '@/components/ui/field';
import { Chip, PageHeader, Pager, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { formatCount, formatShortUtc } from '@/lib/format';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { CampaignRow, TemplateCategory, WhatsAppTemplate } from '@/lib/types';
import { CAMPAIGN_STATUS, campaignActions, checkTemplate, LANGUAGES, placeholdersOf, PLACEHOLDERS, previewBody } from '@/lib/whatsapp';

type Tab = 'campaigns' | 'templates';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'campaigns', label: 'Campaigns' },
  { key: 'templates', label: 'Templates' },
];
const PAGE_SIZE = 50;

export default function CampaignsPage() {
  return (
    <Suspense>
      <Campaigns />
    </Suspense>
  );
}

function Campaigns() {
  const params = useSearchParams();
  const router = useRouter();
  const [tab, setTab] = useState<Tab>(params.get('tab') === 'templates' ? 'templates' : 'campaigns');
  const [adding, setAdding] = useState(false);
  useEffect(() => {
    if (params.get('tab') === 'templates') setTab('templates');
    if (params.get('new') === '1') {
      setTab('campaigns');
      setAdding(true);
      router.replace('/campaigns/');
    }
  }, [params, router]);
  return (
    <div>
      <PageHeader title="Campaigns" />
      <WhatsAppBanner />
      <Tabs label="Campaigns" tabs={TABS} value={tab} onChange={setTab} className="mb-4" />
      {tab === 'campaigns' ? <CampaignList onAdd={() => setAdding(true)} /> : <Templates />}
      {adding && <CampaignDialog campaign={null} onClose={() => setAdding(false)} />}
    </div>
  );
}

function CampaignList({ onAdd }: { onAdd(): void }) {
  const router = useRouter();
  const toast = useToast();
  const refresh = useCampaignRefresh();
  const { storeId } = useStore();
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<CampaignRow | null>(null);
  const [editing, setEditing] = useState<CampaignRow | null>(null);
  const [scheduling, setScheduling] = useState<CampaignRow | null>(null);
  const [cancelling, setCancelling] = useState<CampaignRow | null>(null);
  const [deleting, setDeleting] = useState<CampaignRow | null>(null);
  useEffect(() => setPage(1), [status, storeId]);
  const query = useQuery({
    queryKey: ['campaigns', { storeId, status }, page],
    queryFn: ({ signal }) => api.campaigns({ storeId, status, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
    refetchInterval: (q) => (q.state.data?.items.some((c) => c.status === 'Sending' || c.status === 'Scheduled') ? 15_000 : false),
  });
  // The selected row follows the list (its counts and version change as messages go).
  const current = selected ? query.data?.items.find((c) => c.id === selected.id) ?? selected : null;
  const can = current ? campaignActions(current.status, current.counts) : null;
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteCampaign(id),
    onSuccess: () => {
      toast.ok(`${deleting?.name ?? 'The campaign'} is deleted.`);
      setDeleting(null);
      setSelected(null);
      refresh();
    },
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <>
      <Toolbar>
        <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={onAdd}>
          New campaign
        </Button>
        <Button icon={Pencil} shortcut="Ctrl+O" disabled={!can?.edit} onClick={() => current && setEditing(current)}>
          Edit
        </Button>
        <Button icon={Send} disabled={!can?.schedule} onClick={() => current && setScheduling(current)}>
          Send
        </Button>
        <Button icon={Ban} disabled={!can?.cancel} onClick={() => current && setCancelling(current)}>
          Cancel sending
        </Button>
        <Button icon={Trash2} shortcut="Del" disabled={!can?.remove} onClick={() => current && setDeleting(current)}>
          Delete
        </Button>
        {current && (
          <LinkButton icon={Eye} href={`/campaigns/view/?id=${encodeURIComponent(current.id)}`}>
            Results
          </LinkButton>
        )}
        <label className="sr-only" htmlFor="campaign-status">
          Status
        </label>
        <Select id="campaign-status" className="w-[170px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every status</option>
          {Object.entries(CAMPAIGN_STATUS).map(([key, info]) => (
            <option key={key} value={key}>
              {info.label}
            </option>
          ))}
        </Select>
      </Toolbar>
      <DataTable<CampaignRow>
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(c) => c.id}
        selectedKey={current?.id}
        onRowClick={(c) => (current?.id === c.id ? router.push(`/campaigns/view/?id=${encodeURIComponent(c.id)}`) : setSelected(c))}
        caption="Campaigns"
        empty={<EmptyState title="No campaigns yet">A campaign sends a template to a segment&apos;s clients who agreed to WhatsApp messages.</EmptyState>}
        columns={[
          { key: 'name', header: 'Name', cell: (c) => <span className="font-semibold">{c.name}</span> },
          { key: 'segment', header: 'Segment', cell: (c) => c.segment, hideBelow: 'md' },
          { key: 'template', header: 'Template', cell: (c) => c.template, hideBelow: 'xl' },
          { key: 'status', header: 'Status', cell: (c) => <Chip tone={CAMPAIGN_STATUS[c.status]?.tone ?? 'plain'}>{CAMPAIGN_STATUS[c.status]?.label ?? c.status}</Chip> },
          { key: 'when', header: 'Sends', cell: (c) => formatShortUtc(c.startedAtUtc ?? c.scheduledAtUtc), hideBelow: 'lg' },
          { key: 'to', header: 'Recipients', numeric: true, cell: (c) => formatCount(c.recipients), hideBelow: 'sm' },
          { key: 'read', header: 'Delivered · read', numeric: true, cell: (c) => `${formatCount(c.counts.delivered + c.counts.read)} · ${formatCount(c.counts.read)}`, hideBelow: 'md' },
          { key: 'bad', header: 'Failed', numeric: true, cell: (c) => formatCount(c.counts.failed + c.counts.error), hideBelow: 'lg' },
          { key: 'store', header: 'Store', cell: (c) => c.storeCode, hideBelow: 'xl' },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="campaigns" />}
      />
      <p className="mt-2 px-1 text-ink">Choose a campaign, then Send (a draft) or Results. Choosing it again opens its results.</p>
      {editing && <CampaignDialog campaign={editing} onClose={() => setEditing(null)} />}
      {scheduling && <ScheduleDialog campaign={scheduling} onClose={() => setScheduling(null)} />}
      {cancelling && <CancelCampaignDialog campaign={cancelling} onClose={() => setCancelling(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete the campaign ${deleting?.name ?? ''}?`}
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  );
}

// Templates ---------------------------------------------------------------------------------------------------------

function Templates() {
  const { canChangeShared } = useSession();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [selected, setSelected] = useState<WhatsAppTemplate | null>(null);
  const [editing, setEditing] = useState<WhatsAppTemplate | 'new' | null>(null);
  const [deleting, setDeleting] = useState<WhatsAppTemplate | null>(null);
  const query = useQuery({ queryKey: ['templates'], queryFn: () => api.templates() });
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteTemplate(id),
    onSuccess: () => {
      toast.ok(`${deleting?.name ?? 'The template'} is deleted.`);
      setDeleting(null);
      setSelected(null);
      void queryClient.invalidateQueries({ queryKey: ['templates'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <>
      <Toolbar>
        {canChangeShared && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            New template
          </Button>
        )}
        <Button icon={Pencil} shortcut="Ctrl+O" disabled={!selected} onClick={() => selected && setEditing(selected)}>
          {canChangeShared ? 'Edit' : 'Open'}
        </Button>
        {canChangeShared && (
          <Button icon={Trash2} shortcut="Del" disabled={!selected} onClick={() => selected && setDeleting(selected)}>
            Delete
          </Button>
        )}
      </Toolbar>
      <DataTable<WhatsAppTemplate>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(t) => t.id}
        selectedKey={selected?.id}
        onRowClick={(t) => (selected?.id === t.id ? setEditing(t) : setSelected(t))}
        caption="WhatsApp templates"
        empty={<EmptyState title="No templates yet">Add each template exactly as WhatsApp Business approved it.</EmptyState>}
        columns={[
          { key: 'name', header: 'Name', cell: (t) => <span className="font-mono font-semibold">{t.name}</span> },
          { key: 'lang', header: 'Language', cell: (t) => t.language },
          { key: 'cat', header: 'Kind', cell: (t) => t.category, hideBelow: 'md' },
          { key: 'body', header: 'Message', cell: (t) => t.body, className: 'wrap', hideBelow: 'lg' },
          { key: 'ph', header: 'Placeholders', cell: (t) => t.placeholders.map((p) => `{${p}}`).join(' '), hideBelow: 'xl' },
          { key: 'on', header: 'On', cell: (t) => (t.isActive ? <Chip tone="ok">On</Chip> : <Chip>Off</Chip>) },
        ]}
      />
      <p className="mt-2 px-1 text-ink">
        A template is sent by its name and language, with the placeholders as its parameters in order.
        {!canChangeShared && ' Templates are shared by every store: a super administrator changes them.'}
      </p>
      {editing && <TemplateDialog template={editing === 'new' ? null : editing} readOnly={!canChangeShared} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete the template ${deleting?.name ?? ''}?`}
        message="A template that messages were sent with is not deleted: switch it off instead."
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  );
}

function TemplateDialog({ template, readOnly, onClose }: { template: WhatsAppTemplate | null; readOnly: boolean; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const body = useRef<HTMLTextAreaElement>(null);
  const [form, setForm] = useState({ name: template?.name ?? '', language: template?.language ?? 'en', body: template?.body ?? '' });
  const [category, setCategory] = useState<TemplateCategory>(template?.category ?? 'Marketing');
  const [isActive, setIsActive] = useState(template?.isActive ?? true);
  const [showErrors, setShowErrors] = useState(false);
  const [conflict, setConflict] = useState(false);
  const errors = checkTemplate(form);
  const save = useMutation({
    mutationFn: () => {
      const request = { name: form.name.trim(), language: form.language.trim(), category, body: form.body.trim(), isActive, version: template?.version ?? null };
      return template ? api.saveTemplate(template.id, request) : api.addTemplate(request);
    },
    onSuccess: (saved) => {
      toast.ok(`${saved.name} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['templates'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });

  function insert(key: string) {
    const box = body.current;
    const text = `{${key}}`;
    if (!box) return setForm((f) => ({ ...f, body: f.body + text }));
    const start = box.selectionStart ?? form.body.length;
    const end = box.selectionEnd ?? start;
    const next = form.body.slice(0, start) + text + form.body.slice(end);
    setForm((f) => ({ ...f, body: next }));
    requestAnimationFrame(() => {
      box.focus();
      box.setSelectionRange(start + text.length, start + text.length);
    });
  }

  return (
    <Dialog
      open
      width="lg"
      title={template ? template.name : 'New template'}
      onClose={onClose}
      onSubmit={() => {
        if (readOnly) return onClose();
        setShowErrors(true);
        if (Object.keys(errors).length === 0) save.mutate();
      }}
      footer={
        <>
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
      <div className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-[minmax(0,1fr)_300px]">
        <div className="space-y-3">
          <Field label="Name at WhatsApp" htmlFor="template-name" error={showErrors ? errors.name : undefined} hint="As approved, like spring_sale.">
            <Input id="template-name" className="font-mono" value={form.name} readOnly={readOnly} invalid={showErrors && !!errors.name} onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))} />
          </Field>
          <Field label="Language" htmlFor="template-language" error={showErrors ? errors.language : undefined}>
            <Input id="template-language" list="template-languages" className="max-w-[180px]" value={form.language} readOnly={readOnly} onChange={(e) => setForm((f) => ({ ...f, language: e.target.value }))} />
            <datalist id="template-languages">
              {LANGUAGES.map((l) => (
                <option key={l.code} value={l.code}>
                  {l.label}
                </option>
              ))}
            </datalist>
          </Field>
          <Field label="Kind" htmlFor="template-category">
            <Select id="template-category" className="max-w-[220px]" value={category} disabled={readOnly} onChange={(e) => setCategory(e.target.value as TemplateCategory)}>
              <option value="Marketing">Marketing</option>
              <option value="Utility">Utility</option>
            </Select>
          </Field>
          <Field label="Message" htmlFor="template-body" error={showErrors ? errors.body : undefined}>
            <TextArea id="template-body" ref={body} rows={5} value={form.body} readOnly={readOnly} onChange={(e) => setForm((f) => ({ ...f, body: e.target.value }))} />
          </Field>
          {!readOnly && (
            <Field label="Placeholders">
              <div className="flex flex-wrap gap-2">
                {PLACEHOLDERS.map((p) => (
                  <button key={p.key} type="button" className="chip chip-plain hover:bg-sun-light" title={p.meaning} onClick={() => insert(p.key)}>
                    {`{${p.key}}`}
                  </button>
                ))}
              </div>
            </Field>
          )}
          <Field label="" htmlFor="template-on">
            <Checkbox label="On: campaigns and single messages may use it" checked={isActive} disabled={readOnly} onChange={setIsActive} />
          </Field>
        </div>
        <aside aria-label="Preview" className="rounded-card bg-cream p-4">
          <p className="mb-2 text-note text-muted">How it reads, with sample values</p>
          <p className="whitespace-pre-wrap rounded-card bg-ok-light px-4 py-3 text-ink shadow-card" data-testid="template-preview">
            {previewBody(form.body) || 'The message shows here.'}
          </p>
          <p className="mt-3 text-note text-muted">
            Parameters in order: {placeholdersOf(form.body).map((p, i) => `${i + 1}. {${p}}`).join(', ') || 'none'}.
          </p>
        </aside>
      </div>
      <FormError error={save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null} />
      <ConflictDialog
        open={conflict}
        what="template"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['templates'] });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}
