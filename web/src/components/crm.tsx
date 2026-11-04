'use client';

/**
 * What the CRM, My tasks and Clients pages share: owner and store boxes, an optional date filter, a picker for a
 * client, lead or opportunity, and the editors of leads, opportunities and activities.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Save, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { ConflictDialog } from './masters';
import { Button } from './ui/button';
import { ConfirmDialog, Dialog } from './ui/dialog';
import { Checkbox, Field, Input, Select, TextArea } from './ui/field';
import { ErrorState, FormError, Skeleton } from './ui/states';
import { useToast } from './ui/toast';
import { api, ApiError } from '@/lib/api';
import { ACTIVITY_KINDS, checkWhatsApp, joinLocal, LEAD_STATUS, normalizeWhatsApp, splitLocal } from '@/lib/crm';
import { addDays, isBusinessDate, toBusinessDate } from '@/lib/dates';
import { checkEmail, checkNumber, numberText, parseNumber, textOrNull } from '@/lib/forms';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { staffName, useStaff } from '@/lib/staff';
import { useStore } from '@/lib/store';
import type { ActivityKind, ActivityRow, LeadRow, LeadStatus, OpportunityRow, OpportunityStage } from '@/lib/types';

/** The query keys the CRM lists live under, refreshed after every change. */
export const CRM_KEYS = ['board', 'leads', 'opportunities', 'activities', 'my-tasks', 'won-lost'] as const;

export function useCrmRefresh() {
  const queryClient = useQueryClient();
  return () => {
    for (const key of CRM_KEYS) void queryClient.invalidateQueries({ queryKey: [key] });
  };
}

export function useStages() {
  return useQuery({ queryKey: ['crm-stages'], queryFn: () => api.stages(), staleTime: 60_000 });
}

// Boxes -------------------------------------------------------------------------------------------------------------

/** The people a record can belong to: the staff list, and always the signed-in user. */
export function useOwners(storeId: string | null) {
  const { user } = useSession();
  const staff = useStaff(storeId);
  return useMemo(() => {
    const list = (staff.data ?? []).map((s) => ({ id: s.id, name: staffName(s) }));
    if (!list.some((o) => o.id === user.id)) list.unshift({ id: user.id, name: user.fullName?.trim() || user.userName });
    return list;
  }, [staff.data, user]);
}

export function OwnerSelect({
  id,
  value,
  onChange,
  storeId,
  anyLabel,
  current,
  className,
  disabled,
}: {
  id: string;
  value: string;
  onChange(value: string): void;
  storeId: string | null;
  /** With a first option for "anyone" (filters). */
  anyLabel?: string;
  /** The record's owner when the list may not have them. */
  current?: { id: string | null; name: string | null };
  className?: string;
  disabled?: boolean;
}) {
  const owners = useOwners(storeId);
  return (
    <Select id={id} className={className} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)}>
      {anyLabel !== undefined && <option value="">{anyLabel}</option>}
      {current?.id && !owners.some((o) => o.id === current.id) && <option value={current.id}>{current.name ?? 'The owner'}</option>}
      {owners.map((o) => (
        <option key={o.id} value={o.id}>
          {o.name}
        </option>
      ))}
    </Select>
  );
}

/** The store of a new record, for users who see several stores. */
export function StoreSelect({ id, value, onChange, disabled }: { id: string; value: string; onChange(value: string): void; disabled?: boolean }) {
  const { stores } = useStore();
  return (
    <Select id={id} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)}>
      {stores.map((s) => (
        <option key={s.id} value={s.id}>
          {s.code} · {s.name}
        </option>
      ))}
    </Select>
  );
}

export type RangeMode = 'any' | 'week' | 'month' | 'last30' | 'next30' | 'custom';

export interface OptionalRange {
  mode: RangeMode;
  from: string;
  to: string;
}

/** The dates a filter sends: none for "any time". */
export function rangeQuery(range: OptionalRange, now = new Date()): { from?: string; to?: string } {
  const today = toBusinessDate(now);
  switch (range.mode) {
    case 'any':
      return {};
    case 'week': {
      const sinceMonday = (now.getDay() + 6) % 7;
      return { from: addDays(today, -sinceMonday), to: addDays(today, 6 - sinceMonday) };
    }
    case 'month': {
      const last = new Date(now.getFullYear(), now.getMonth() + 1, 0);
      return { from: today.slice(0, 8) + '01', to: toBusinessDate(last) };
    }
    case 'last30':
      return { from: addDays(today, -29), to: today };
    case 'next30':
      return { from: today, to: addDays(today, 29) };
    case 'custom':
      return isBusinessDate(range.from) && isBusinessDate(range.to) ? (range.from <= range.to ? { from: range.from, to: range.to } : { from: range.to, to: range.from }) : {};
  }
}

const RANGE_LABELS: Record<RangeMode, string> = {
  any: 'Any time',
  week: 'This week',
  month: 'This month',
  last30: 'Last 30 days',
  next30: 'Next 30 days',
  custom: 'Custom',
};

/** A date filter that may be left off. */
export function OptionalRangeBar({ id, label, value, onChange }: { id: string; label: string; value: OptionalRange; onChange(value: OptionalRange): void }) {
  return (
    <div className="flex flex-wrap items-center gap-2.5">
      <label htmlFor={id} className="shrink-0 text-muted">
        {label}
      </label>
      <Select id={id} className="w-[170px]" value={value.mode} onChange={(e) => onChange({ ...value, mode: e.target.value as RangeMode })}>
        {(Object.keys(RANGE_LABELS) as RangeMode[]).map((m) => (
          <option key={m} value={m}>
            {RANGE_LABELS[m]}
          </option>
        ))}
      </Select>
      {value.mode === 'custom' && (
        <>
          <Input type="date" aria-label={`${label} from`} className="w-[170px]" value={value.from} onChange={(e) => onChange({ ...value, from: e.target.value })} />
          <Input type="date" aria-label={`${label} to`} className="w-[170px]" value={value.to} onChange={(e) => onChange({ ...value, to: e.target.value })} />
        </>
      )}
    </div>
  );
}

// Picker ------------------------------------------------------------------------------------------------------------

export type RecordKind = 'client' | 'lead' | 'opportunity';

export interface Picked {
  id: string;
  name: string;
}

/** Finds a client, lead or opportunity by typing, then choosing from what was found. */
export function RecordPicker({ kind, value, onChange, id }: { kind: RecordKind; value: Picked | null; onChange(value: Picked | null): void; id: string }) {
  const [text, setText] = useState('');
  const search = useDebounced(text.trim());
  const found = useQuery({
    queryKey: ['pick', kind, search],
    enabled: search.length > 0,
    queryFn: async (): Promise<Picked[]> => {
      if (kind === 'client') return (await api.clients({ search, pageSize: 20 })).items.map((c) => ({ id: c.id, name: c.company && !c.name ? c.company : c.name }));
      if (kind === 'lead') return (await api.leads({ search, status: 'Open', pageSize: 20 })).items.map((l) => ({ id: l.id, name: l.company ? `${l.name} (${l.company})` : l.name }));
      return (await api.opportunities({ search, outcome: 'Open', pageSize: 20 })).items.map((o) => ({ id: o.id, name: o.title }));
    },
  });
  const options = [...(value ? [value] : []), ...(found.data ?? []).filter((f) => f.id !== value?.id)];
  const noun = kind === 'client' ? 'client' : kind === 'lead' ? 'lead' : 'opportunity';
  return (
    <div className="space-y-2">
      <Input id={id} value={text} placeholder={`Find a ${noun} by name`} onChange={(e) => setText(e.target.value)} />
      <Select aria-label={`The ${noun}`} value={value?.id ?? ''} onChange={(e) => onChange(options.find((o) => o.id === e.target.value) ?? null)}>
        <option value="">{found.isFetching ? 'Looking…' : search && found.data?.length === 0 ? 'Nothing found' : 'Choose…'}</option>
        {options.map((o) => (
          <option key={o.id} value={o.id}>
            {o.name}
          </option>
        ))}
      </Select>
    </div>
  );
}

// Lead editor -------------------------------------------------------------------------------------------------------

const SOURCES = ['Walk-in', 'Phone', 'WhatsApp', 'Website', 'Facebook', 'Instagram', 'Referral', 'Event'];

export function LeadDialog({ lead, onClose }: { lead: LeadRow | null; onClose(): void }) {
  const { can, user } = useSession();
  const { defaultStoreId, stores } = useStore();
  const toast = useToast();
  const refresh = useCrmRefresh();
  const edit = can(P.crmEdit);
  const [form, setForm] = useState({
    name: lead?.name ?? '',
    company: lead?.company ?? '',
    phone: lead?.phone ?? '',
    whatsAppNumber: lead?.whatsAppNumber ?? '',
    email: lead?.email ?? '',
    source: lead?.source ?? '',
    ownerId: lead?.ownerId ?? user.id,
    storeId: lead?.storeId ?? defaultStoreId ?? '',
    status: (lead?.status ?? 'Open') as LeadStatus,
    notes: lead?.notes ?? '',
  });
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const set = (key: keyof typeof form, value: string) => setForm((f) => ({ ...f, [key]: value }));

  const save = useMutation({
    mutationFn: () => {
      const body = {
        name: form.name.trim(),
        company: textOrNull(form.company),
        phone: textOrNull(form.phone),
        whatsAppNumber: normalizeWhatsApp(form.whatsAppNumber),
        email: textOrNull(form.email),
        source: textOrNull(form.source),
        ownerId: form.ownerId || null,
        storeId: form.storeId || null,
        notes: textOrNull(form.notes),
        status: form.status,
        version: lead?.version ?? null,
      };
      return lead ? api.saveLead(lead.id, body) : api.addLead(body);
    },
    onSuccess: (saved) => {
      toast.ok(`${saved.name} is saved.`);
      refresh();
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });

  function submit() {
    if (!edit) return onClose();
    const found = !form.name.trim() ? 'Type the name.' : checkWhatsApp(form.whatsAppNumber) ?? checkEmail(form.email) ?? null;
    setProblem(found);
    if (!found) save.mutate();
  }

  const box = (key: keyof typeof form, label: string, extra: { type?: string; hint?: string } = {}) => (
    <Field label={label} htmlFor={`lead-${key}`} hint={extra.hint}>
      <Input id={`lead-${key}`} type={extra.type} value={form[key]} readOnly={!edit} onChange={(e) => set(key, e.target.value)} />
    </Field>
  );

  return (
    <Dialog
      open
      width="lg"
      title={lead ? lead.name : 'New lead'}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          {edit && (
            <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
              Save
            </Button>
          )}
          <Button onClick={onClose} shortcut="Esc">
            {edit ? 'Cancel' : 'Close'}
          </Button>
        </>
      }
    >
      <div className="grid grid-cols-1 gap-x-8 gap-y-3 md:grid-cols-2">
        <div className="space-y-3">
          {box('name', 'Name')}
          {box('company', 'Company')}
          {box('phone', 'Phone', { type: 'tel' })}
          {box('whatsAppNumber', 'WhatsApp', { type: 'tel', hint: 'With the country code, like +923001234567.' })}
          {box('email', 'E-mail', { type: 'email' })}
        </div>
        <div className="space-y-3">
          <Field label="Source" htmlFor="lead-source">
            <Input id="lead-source" list="lead-sources" value={form.source} readOnly={!edit} onChange={(e) => set('source', e.target.value)} />
            <datalist id="lead-sources">
              {SOURCES.map((s) => (
                <option key={s} value={s} />
              ))}
            </datalist>
          </Field>
          <Field label="Owner" htmlFor="lead-owner">
            <OwnerSelect id="lead-owner" value={form.ownerId} onChange={(v) => set('ownerId', v)} storeId={form.storeId || null} current={{ id: lead?.ownerId ?? null, name: lead?.ownerName ?? null }} disabled={!edit} />
          </Field>
          {stores.length > 1 && (
            <Field label="Store" htmlFor="lead-store">
              <StoreSelect id="lead-store" value={form.storeId} onChange={(v) => set('storeId', v)} disabled={!edit || !!lead} />
            </Field>
          )}
          {lead && lead.status !== 'Converted' && (
            <Field label="Status" htmlFor="lead-status">
              <Select id="lead-status" value={form.status} disabled={!edit} onChange={(e) => set('status', e.target.value)}>
                <option value="Open">{LEAD_STATUS.Open.label}</option>
                <option value="Dropped">{LEAD_STATUS.Dropped.label}</option>
              </Select>
            </Field>
          )}
          <Field label="Notes" htmlFor="lead-notes">
            <TextArea id="lead-notes" value={form.notes} readOnly={!edit} onChange={(e) => set('notes', e.target.value)} />
          </Field>
        </div>
      </div>
      <FormError error={problem ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
      <ConflictDialog
        open={conflict}
        what="lead"
        onReload={() => {
          setConflict(false);
          refresh();
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}

/** Makes a lead a client: a new one made from the lead, or one that is there already. */
export function ConvertDialog({ lead, onClose }: { lead: LeadRow; onClose(): void }) {
  const toast = useToast();
  const refresh = useCrmRefresh();
  const queryClient = useQueryClient();
  const [mode, setMode] = useState<'new' | 'existing'>('new');
  const [client, setClient] = useState<Picked | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const convert = useMutation({
    mutationFn: () => api.convertLead(lead.id, { clientId: mode === 'existing' ? client?.id ?? null : null, version: lead.version }),
    onSuccess: (result) => {
      toast.ok(`${lead.name} is now client ${result.clientNumber ?? ''}.`.replace(' .', '.'));
      refresh();
      void queryClient.invalidateQueries({ queryKey: ['clients'] });
      onClose();
    },
  });
  return (
    <Dialog
      open
      title={`Make ${lead.name} a client`}
      onClose={onClose}
      onSubmit={() => {
        const found = mode === 'existing' && !client ? 'Find and choose the client.' : null;
        setProblem(found);
        if (!found) convert.mutate();
      }}
      footer={
        <>
          <Button type="submit" variant="sun" shortcut="Enter" busy={convert.isPending}>
            Make a client
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <fieldset className="space-y-1">
          <legend className="sr-only">Which client</legend>
          <label className="flex items-center gap-3">
            <input type="radio" name="convert-mode" className="check" checked={mode === 'new'} onChange={() => setMode('new')} />A new client with the lead&apos;s name, phone and e-mail
          </label>
          <label className="flex items-center gap-3">
            <input type="radio" name="convert-mode" className="check" checked={mode === 'existing'} onChange={() => setMode('existing')} />A client who is there already
          </label>
        </fieldset>
        {mode === 'existing' && (
          <Field label="Client" htmlFor="convert-client">
            <RecordPicker kind="client" id="convert-client" value={client} onChange={setClient} />
          </Field>
        )}
        <p className="text-muted">The lead&apos;s opportunities and activities move to the client.</p>
      </div>
      <FormError error={problem ?? convert.error} />
    </Dialog>
  );
}

// Opportunity editor ------------------------------------------------------------------------------------------------

export function OpportunityDialog({
  opportunity,
  stageId,
  about,
  onClose,
}: {
  opportunity: OpportunityRow | null;
  /** A new one's stage (the column it was added from). */
  stageId?: string;
  /** A new one's client or lead. */
  about?: { kind: 'client' | 'lead'; picked: Picked };
  onClose(): void;
}) {
  const { can, user } = useSession();
  const { defaultStoreId, stores } = useStore();
  const toast = useToast();
  const refresh = useCrmRefresh();
  const stages = useStages();
  const edit = can(P.crmEdit);
  const [title, setTitle] = useState(opportunity?.title ?? '');
  const initialKind: 'client' | 'lead' = opportunity?.leadId && !opportunity.clientId ? 'lead' : about?.kind ?? 'client';
  const initialPick: Picked | null = opportunity
    ? opportunity.clientId
      ? { id: opportunity.clientId, name: opportunity.clientName ?? 'The client' }
      : opportunity.leadId
        ? { id: opportunity.leadId, name: opportunity.leadName ?? 'The lead' }
        : null
    : about?.picked ?? null;
  const [kind, setKind] = useState<'client' | 'lead'>(initialKind);
  const [picked, setPicked] = useState<Picked | null>(initialPick);
  const [stage, setStage] = useState(opportunity?.stageId ?? stageId ?? '');
  const [value, setValue] = useState(numberText(opportunity?.expectedValue ?? 0));
  const [probability, setProbability] = useState(opportunity ? numberText(opportunity.probability) : '');
  const [closeOn, setCloseOn] = useState(opportunity?.expectedCloseOn ?? '');
  const [ownerId, setOwnerId] = useState(opportunity?.ownerId ?? user.id);
  const [storeId, setStoreId] = useState(opportunity?.storeId ?? defaultStoreId ?? '');
  const [lostReason, setLostReason] = useState(opportunity?.lostReason ?? '');
  const [notes, setNotes] = useState(opportunity?.notes ?? '');
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const list = useMemo(() => stages.data ?? [], [stages.data]);
  const chosen: OpportunityStage | undefined = list.find((s) => s.id === stage);
  useEffect(() => {
    if (!stage && list.length) setStage((list.find((s) => s.outcome === 'Open') ?? list[0]).id);
  }, [stage, list]);

  const save = useMutation({
    mutationFn: () => {
      // Unchanged, it keeps both links: a converted lead's opportunity has its client and its lead.
      const same = !!opportunity && kind === initialKind && picked?.id === initialPick?.id;
      const body = {
        title: title.trim(),
        clientId: same ? opportunity.clientId : kind === 'client' ? picked?.id ?? null : null,
        leadId: same ? opportunity.leadId : kind === 'lead' ? picked?.id ?? null : null,
        stageId: stage || null,
        expectedValue: parseNumber(value) ?? 0,
        probability: probability.trim() ? parseNumber(probability) : null,
        expectedCloseOn: isBusinessDate(closeOn) ? closeOn : null,
        ownerId: ownerId || null,
        storeId: storeId || null,
        lostReason: chosen?.outcome === 'Lost' ? textOrNull(lostReason) : null,
        notes: textOrNull(notes),
        version: opportunity?.version ?? null,
      };
      return opportunity ? api.saveOpportunity(opportunity.id, body) : api.addOpportunity(body);
    },
    onSuccess: (saved) => {
      toast.ok(`${saved.title} is saved.`);
      refresh();
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  const remove = useMutation({
    mutationFn: () => api.deleteOpportunity(opportunity!.id),
    onSuccess: () => {
      toast.ok(`${opportunity?.title} is deleted.`);
      refresh();
      onClose();
    },
    onError: (e: Error) => toast.error(e.message),
  });

  function submit() {
    if (!edit) return onClose();
    const found = !title.trim()
      ? 'Give it a title.'
      : !picked
        ? `Choose the ${kind}.`
        : checkNumber(value, { label: 'The expected value' }) ??
          (probability.trim() ? checkNumber(probability, { label: 'The probability', decimals: 0 }) : undefined) ??
          ((parseNumber(probability) ?? 0) > 100 ? 'The probability is at most 100.' : undefined) ??
          (chosen?.outcome === 'Lost' && !lostReason.trim() ? 'Say why it was lost.' : null);
    setProblem(found);
    if (!found) save.mutate();
  }

  return (
    <Dialog
      open
      width="lg"
      title={opportunity ? opportunity.title : 'New opportunity'}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          {edit && opportunity && (
            <Button icon={Trash2} variant="danger" onClick={() => setDeleting(true)} className="mr-auto">
              Delete
            </Button>
          )}
          {edit && (
            <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
              Save
            </Button>
          )}
          <Button onClick={onClose} shortcut="Esc">
            {edit ? 'Cancel' : 'Close'}
          </Button>
        </>
      }
    >
      {stages.error ? (
        <ErrorState error={stages.error} onRetry={() => void stages.refetch()} />
      ) : !stages.data ? (
        <Skeleton className="h-40" />
      ) : (
        <div className="grid grid-cols-1 gap-x-8 gap-y-3 md:grid-cols-2">
          <div className="space-y-3">
            <Field label="Title" htmlFor="opp-title">
              <Input id="opp-title" value={title} readOnly={!edit} maxLength={120} onChange={(e) => setTitle(e.target.value)} />
            </Field>
            <Field label="For" htmlFor="opp-kind">
              <Select
                id="opp-kind"
                value={kind}
                disabled={!edit}
                onChange={(e) => {
                  setKind(e.target.value as 'client' | 'lead');
                  setPicked(null);
                }}
              >
                <option value="client">A client</option>
                <option value="lead">A lead</option>
              </Select>
            </Field>
            {edit ? (
              <Field label={kind === 'client' ? 'Client' : 'Lead'} htmlFor="opp-about">
                <RecordPicker kind={kind} id="opp-about" value={picked} onChange={setPicked} />
              </Field>
            ) : (
              <Field label={kind === 'client' ? 'Client' : 'Lead'}>
                <p className="pt-2.5 font-semibold">{picked?.name ?? '—'}</p>
              </Field>
            )}
            <Field label="Expected value" htmlFor="opp-value">
              <Input id="opp-value" inputMode="decimal" className="max-w-[200px]" value={value} readOnly={!edit} onChange={(e) => setValue(e.target.value)} />
            </Field>
            <Field label="Expected close" htmlFor="opp-close">
              <Input id="opp-close" type="date" className="max-w-[200px]" value={closeOn} readOnly={!edit} onChange={(e) => setCloseOn(e.target.value)} />
            </Field>
          </div>
          <div className="space-y-3">
            <Field label="Stage" htmlFor="opp-stage">
              <Select id="opp-stage" value={stage} disabled={!edit} onChange={(e) => setStage(e.target.value)}>
                {list.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Probability %" htmlFor="opp-probability" hint={chosen ? `Empty: the stage's ${chosen.probability}%.` : undefined}>
              <Input id="opp-probability" inputMode="numeric" className="max-w-[120px]" value={probability} readOnly={!edit} onChange={(e) => setProbability(e.target.value)} />
            </Field>
            {chosen?.outcome === 'Lost' && (
              <Field label="Why it was lost" htmlFor="opp-lost">
                <Input id="opp-lost" value={lostReason} readOnly={!edit} onChange={(e) => setLostReason(e.target.value)} />
              </Field>
            )}
            <Field label="Owner" htmlFor="opp-owner">
              <OwnerSelect id="opp-owner" value={ownerId} onChange={setOwnerId} storeId={storeId || null} current={{ id: opportunity?.ownerId ?? null, name: opportunity?.ownerName ?? null }} disabled={!edit} />
            </Field>
            {stores.length > 1 && (
              <Field label="Store" htmlFor="opp-store">
                <StoreSelect id="opp-store" value={storeId} onChange={setStoreId} disabled={!edit || !!opportunity} />
              </Field>
            )}
            <Field label="Notes" htmlFor="opp-notes">
              <TextArea id="opp-notes" value={notes} readOnly={!edit} onChange={(e) => setNotes(e.target.value)} />
            </Field>
          </div>
        </div>
      )}
      <FormError error={problem ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
      <ConflictDialog
        open={conflict}
        what="opportunity"
        onReload={() => {
          setConflict(false);
          refresh();
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
      <ConfirmDialog
        open={deleting}
        title={`Delete ${opportunity?.title ?? ''}?`}
        message="Its activities go with it."
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => remove.mutate()}
        onClose={() => setDeleting(false)}
      />
    </Dialog>
  );
}

/** The reason a card dropped on a Lost column asks for. */
export function LostReasonDialog({ title, onConfirm, onClose, busy }: { title: string; onConfirm(reason: string): void; onClose(): void; busy?: boolean }) {
  const [reason, setReason] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  return (
    <Dialog
      open
      width="sm"
      title={`Why was ${title} lost?`}
      onClose={onClose}
      onSubmit={() => {
        if (!reason.trim()) return setProblem('Type the reason.');
        onConfirm(reason.trim());
      }}
      footer={
        <>
          <Button type="submit" variant="sun" shortcut="Enter" busy={busy}>
            Mark lost
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <Field label="Reason" htmlFor="lost-reason" stacked>
        <Input id="lost-reason" list="lost-reasons" value={reason} onChange={(e) => setReason(e.target.value)} />
        <datalist id="lost-reasons">
          {['Price', 'Went to a competitor', 'No budget', 'No answer', 'Not needed any more'].map((r) => (
            <option key={r} value={r} />
          ))}
        </datalist>
      </Field>
      <FormError error={problem} />
    </Dialog>
  );
}

// Activity editor ---------------------------------------------------------------------------------------------------

export function ActivityDialog({
  activity,
  about,
  onClose,
}: {
  activity: ActivityRow | null;
  /** A new one's client, lead or opportunity. */
  about?: { kind: RecordKind; picked: Picked };
  onClose(): void;
}) {
  const { can, user } = useSession();
  const { defaultStoreId, stores } = useStore();
  const toast = useToast();
  const refresh = useCrmRefresh();
  const edit = can(P.crmEdit);
  const initialKind: RecordKind = activity ? (activity.opportunityId ? 'opportunity' : activity.leadId && !activity.clientId ? 'lead' : 'client') : about?.kind ?? 'client';
  const initialPick: Picked | null = activity
    ? activity.opportunityId
      ? { id: activity.opportunityId, name: activity.opportunityTitle ?? 'The opportunity' }
      : activity.clientId
        ? { id: activity.clientId, name: activity.clientName ?? 'The client' }
        : activity.leadId
          ? { id: activity.leadId, name: activity.leadName ?? 'The lead' }
          : null
    : about?.picked ?? null;
  const due = splitLocal(activity?.dueAt);
  const [kind, setKind] = useState<ActivityKind>(activity?.kind ?? 'Task');
  const [subject, setSubject] = useState(activity?.subject ?? '');
  const [aboutKind, setAboutKind] = useState<RecordKind>(initialKind);
  const [picked, setPicked] = useState<Picked | null>(initialPick);
  const [date, setDate] = useState(due.date || (activity ? '' : toBusinessDate(new Date())));
  const [time, setTime] = useState(due.time);
  const [ownerId, setOwnerId] = useState(activity?.ownerId ?? user.id);
  const [storeId, setStoreId] = useState(activity?.storeId ?? defaultStoreId ?? '');
  const [isDone, setIsDone] = useState(activity?.isDone ?? false);
  const [notes, setNotes] = useState(activity?.notes ?? '');
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const save = useMutation({
    mutationFn: () => {
      // Unchanged, it keeps every link it had (a converted lead's activity has its client and its lead).
      const same = !!activity && aboutKind === initialKind && picked?.id === initialPick?.id;
      const body = {
        kind,
        subject: subject.trim(),
        notes: textOrNull(notes),
        clientId: same ? activity.clientId : aboutKind === 'client' ? picked?.id ?? null : null,
        leadId: same ? activity.leadId : aboutKind === 'lead' ? picked?.id ?? null : null,
        opportunityId: same ? activity.opportunityId : aboutKind === 'opportunity' ? picked?.id ?? null : null,
        dueAt: joinLocal(date, time),
        ownerId: ownerId || null,
        storeId: storeId || null,
        isDone,
        version: activity?.version ?? null,
      };
      return activity ? api.saveActivity(activity.id, body) : api.addActivity(body);
    },
    onSuccess: (saved) => {
      toast.ok(`${saved.subject} is saved.`);
      refresh();
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  const remove = useMutation({
    mutationFn: () => api.deleteActivity(activity!.id),
    onSuccess: () => {
      toast.ok(`${activity?.subject} is deleted.`);
      refresh();
      onClose();
    },
    onError: (e: Error) => toast.error(e.message),
  });

  function submit() {
    if (!edit) return onClose();
    const found = !subject.trim() ? 'Say what it is about.' : !picked ? `Choose the ${aboutKind}.` : null;
    setProblem(found);
    if (!found) save.mutate();
  }

  return (
    <Dialog
      open
      width="lg"
      title={activity ? activity.subject : 'New activity'}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          {edit && activity && (
            <Button icon={Trash2} variant="danger" onClick={() => setDeleting(true)} className="mr-auto">
              Delete
            </Button>
          )}
          {edit && (
            <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
              Save
            </Button>
          )}
          <Button onClick={onClose} shortcut="Esc">
            {edit ? 'Cancel' : 'Close'}
          </Button>
        </>
      }
    >
      <div className="grid grid-cols-1 gap-x-8 gap-y-3 md:grid-cols-2">
        <div className="space-y-3">
          <Field label="Kind" htmlFor="act-kind">
            <Select id="act-kind" value={kind} disabled={!edit} onChange={(e) => setKind(e.target.value as ActivityKind)}>
              {ACTIVITY_KINDS.map((k) => (
                <option key={k.key} value={k.key}>
                  {k.label}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="Subject" htmlFor="act-subject">
            <Input id="act-subject" value={subject} readOnly={!edit} maxLength={160} onChange={(e) => setSubject(e.target.value)} />
          </Field>
          <Field label="About" htmlFor="act-about-kind">
            <Select
              id="act-about-kind"
              value={aboutKind}
              disabled={!edit}
              onChange={(e) => {
                setAboutKind(e.target.value as RecordKind);
                setPicked(null);
              }}
            >
              <option value="client">A client</option>
              <option value="lead">A lead</option>
              <option value="opportunity">An opportunity</option>
            </Select>
          </Field>
          {edit ? (
            <Field label="" htmlFor="act-about">
              <RecordPicker kind={aboutKind} id="act-about" value={picked} onChange={setPicked} />
            </Field>
          ) : (
            <Field label="">
              <p className="pt-2.5 font-semibold">{picked?.name ?? '—'}</p>
            </Field>
          )}
        </div>
        <div className="space-y-3">
          <Field label="Due" htmlFor="act-date" hint="Leave the date empty for no due date.">
            <div className="flex flex-wrap gap-2">
              <Input id="act-date" type="date" className="w-[180px]" value={date} readOnly={!edit} onChange={(e) => setDate(e.target.value)} />
              <Input aria-label="Due time" type="time" className="w-[140px]" value={time} readOnly={!edit} onChange={(e) => setTime(e.target.value)} />
            </div>
          </Field>
          <Field label="Owner" htmlFor="act-owner">
            <OwnerSelect id="act-owner" value={ownerId} onChange={setOwnerId} storeId={storeId || null} current={{ id: activity?.ownerId ?? null, name: activity?.ownerName ?? null }} disabled={!edit} />
          </Field>
          {stores.length > 1 && (
            <Field label="Store" htmlFor="act-store">
              <StoreSelect id="act-store" value={storeId} onChange={setStoreId} disabled={!edit || !!activity} />
            </Field>
          )}
          <Field label="" htmlFor="act-done">
            <Checkbox label="Done" checked={isDone} disabled={!edit} onChange={setIsDone} />
          </Field>
          <Field label="Notes" htmlFor="act-notes">
            <TextArea id="act-notes" value={notes} readOnly={!edit} onChange={(e) => setNotes(e.target.value)} />
          </Field>
        </div>
      </div>
      <FormError error={problem ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
      <ConflictDialog
        open={conflict}
        what="activity"
        onReload={() => {
          setConflict(false);
          refresh();
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
      <ConfirmDialog
        open={deleting}
        title={`Delete ${activity?.subject ?? ''}?`}
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => remove.mutate()}
        onClose={() => setDeleting(false)}
      />
    </Dialog>
  );
}

/** Ticks an activity done or open again; owners may with crm.view, others' need crm.edit. */
export function useActivityDone() {
  const toast = useToast();
  const refresh = useCrmRefresh();
  return useMutation({
    mutationFn: (a: ActivityRow) => api.setActivityDone(a.id, { done: !a.isDone, version: a.version }),
    onSuccess: (saved) => {
      toast.ok(saved.isDone ? `${saved.subject} is done.` : `${saved.subject} is open again.`);
      refresh();
    },
    onError: (e: Error) => toast.error(e.message),
  });
}

/** Whether the signed-in user may tick this activity. */
export function useMayTick() {
  const { can, user } = useSession();
  return (a: ActivityRow) => can(P.crmEdit) || (can(P.crmView) && a.ownerId === user.id);
}
