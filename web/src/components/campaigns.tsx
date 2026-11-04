'use client';

/**
 * What the Campaigns page and a campaign's page share: the WhatsApp status banner, the campaign editor, scheduling,
 * cancelling and the counts per status.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CircleAlert, MessageCircle, Save, Send } from 'lucide-react';
import { useState } from 'react';
import { StoreSelect } from './crm';
import { ConflictDialog } from './masters';
import { Button } from './ui/button';
import { ConfirmDialog, Dialog } from './ui/dialog';
import { Field, Input, Select } from './ui/field';
import { FormError, Skeleton } from './ui/states';
import { useToast } from './ui/toast';
import { api, ApiError } from '@/lib/api';
import { cx } from '@/lib/cx';
import { toBusinessDate } from '@/lib/dates';
import { formatCount } from '@/lib/format';
import { useStore } from '@/lib/store';
import type { CampaignRow, MessageCounts } from '@/lib/types';
import { MESSAGE_STATUS, MESSAGE_STATUSES } from '@/lib/whatsapp';

export function useWhatsAppStatus() {
  return useQuery({ queryKey: ['whatsapp-status'], queryFn: () => api.whatsAppStatus(), staleTime: 5 * 60_000 });
}

/** Says when nothing really leaves the server (no WhatsApp account set up), and how fast messages go when one is. */
export function WhatsAppBanner() {
  const status = useWhatsAppStatus();
  if (!status.data) return null;
  if (status.data.provider === 'recording') {
    return (
      <p className="mb-4 flex items-start gap-3 rounded-card bg-sun-light px-4 py-3 text-ink" role="status" data-testid="whatsapp-banner">
        <CircleAlert className="mt-0.5 h-5 w-5 shrink-0" aria-hidden="true" />
        <span>
          <strong>WhatsApp is not connected.</strong> Messages are recorded on the server as sent, and nothing reaches the clients&apos; phones. The server&apos;s
          WhatsApp settings connect it to WhatsApp Business.
        </span>
      </p>
    );
  }
  return (
    <p className="mb-4 flex items-start gap-3 rounded-card bg-ok-light px-4 py-3 text-ok-ink" role="status" data-testid="whatsapp-banner">
      <MessageCircle className="mt-0.5 h-5 w-5 shrink-0" aria-hidden="true" />
      <span>
        Sends through WhatsApp Business{status.data.phoneNumberIdHint ? ` (${status.data.phoneNumberIdHint})` : ''}, at most {formatCount(status.data.messagesPerMinute)} a
        minute.{!status.data.webhookReady && ' Delivered and read show once the webhook is set up.'}
      </span>
    </p>
  );
}

/** The counts per status as small figures. */
export function CountsRow({ counts, className }: { counts: MessageCounts; className?: string }) {
  return (
    <dl className={cx('grid grid-cols-3 gap-2 sm:grid-cols-6', className)}>
      {MESSAGE_STATUSES.map((s) => {
        const info = MESSAGE_STATUS[s];
        return (
          <div key={s} className="rounded-control bg-cream px-3 py-2">
            <dt className="text-note text-muted">{info.label}</dt>
            <dd className={cx('font-heading text-section tabular-nums', (s === 'Failed' || s === 'Error') && counts[info.key] > 0 && 'text-danger')} data-testid={`count-${s}`}>
              {formatCount(counts[info.key])}
            </dd>
          </div>
        );
      })}
    </dl>
  );
}

export function useCampaignRefresh() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['campaigns'] });
    void queryClient.invalidateQueries({ queryKey: ['campaign'] });
    void queryClient.invalidateQueries({ queryKey: ['campaign-messages'] });
  };
}

export function CampaignDialog({ campaign, onClose }: { campaign: CampaignRow | null; onClose(): void }) {
  const toast = useToast();
  const refresh = useCampaignRefresh();
  const { defaultStoreId, stores } = useStore();
  const segments = useQuery({ queryKey: ['segments'], queryFn: () => api.segments() });
  const templates = useQuery({ queryKey: ['templates'], queryFn: () => api.templates() });
  const [name, setName] = useState(campaign?.name ?? '');
  const [segmentId, setSegmentId] = useState(campaign?.segmentId ?? '');
  const [templateId, setTemplateId] = useState(campaign?.templateId ?? '');
  const [storeId, setStoreId] = useState(campaign?.storeId ?? defaultStoreId ?? '');
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const save = useMutation({
    mutationFn: () => {
      const body = { name: name.trim(), segmentId, templateId, storeId: storeId || null, version: campaign?.version ?? null };
      return campaign ? api.saveCampaign(campaign.id, body) : api.addCampaign(body);
    },
    onSuccess: (saved) => {
      toast.ok(`${saved.name} is saved as a draft.`);
      refresh();
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  const segment = segments.data?.find((s) => s.id === segmentId);
  const template = templates.data?.find((t) => t.id === templateId);

  return (
    <Dialog
      open
      title={campaign ? campaign.name : 'New campaign'}
      onClose={onClose}
      onSubmit={() => {
        const found = !name.trim() ? 'Give the campaign a name.' : !segmentId ? 'Choose who gets it: a segment.' : !templateId ? 'Choose the message: a template.' : null;
        setProblem(found);
        if (!found) save.mutate();
      }}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Save draft
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      {!segments.data || !templates.data ? (
        segments.error || templates.error ? (
          <FormError error={segments.error ?? templates.error} />
        ) : (
          <Skeleton className="h-40" />
        )
      ) : (
        <div className="space-y-3">
          <Field label="Name" htmlFor="campaign-name">
            <Input id="campaign-name" value={name} maxLength={80} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field label="Segment" htmlFor="campaign-segment" hint={segment ? `${segment.summary}${segment.lastEvaluatedAtUtc !== null ? ` · ${formatCount(segment.lastCount)} clients when last evaluated` : ''}` : undefined}>
            <Select id="campaign-segment" value={segmentId} onChange={(e) => setSegmentId(e.target.value)}>
              <option value="">Choose…</option>
              {segments.data.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="Template" htmlFor="campaign-template" hint={template ? template.body : 'Only templates that are on are offered.'}>
            <Select id="campaign-template" value={templateId} onChange={(e) => setTemplateId(e.target.value)}>
              <option value="">Choose…</option>
              {templates.data
                .filter((t) => t.isActive || t.id === templateId)
                .map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name} ({t.language})
                  </option>
                ))}
            </Select>
          </Field>
          {stores.length > 1 && (
            <Field label="Sent by" htmlFor="campaign-store" hint="The store named in {Store}, whose campaign it is.">
              <StoreSelect id="campaign-store" value={storeId} onChange={setStoreId} />
            </Field>
          )}
          <p className="text-muted">Only the segment&apos;s clients who agreed to WhatsApp messages and have a number get it.</p>
        </div>
      )}
      <FormError error={problem ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
      <ConflictDialog
        open={conflict}
        what="campaign"
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

/** Now, or at a later date and time (the browser's), sent to the server in UTC. */
export function ScheduleDialog({ campaign, onClose }: { campaign: CampaignRow; onClose(): void }) {
  const toast = useToast();
  const refresh = useCampaignRefresh();
  const [when, setWhen] = useState<'now' | 'later'>('now');
  const [date, setDate] = useState(toBusinessDate(new Date()));
  const [time, setTime] = useState('10:00');
  const [problem, setProblem] = useState<string | null>(null);
  const schedule = useMutation({
    mutationFn: (sendAtUtc: string | null) => api.scheduleCampaign(campaign.id, { sendAtUtc, version: campaign.version }),
    onSuccess: (saved) => {
      toast.ok(`${saved.name}: ${formatCount(saved.recipients)} messages queued.`);
      refresh();
      onClose();
    },
  });
  function submit() {
    if (when === 'now') return schedule.mutate(null);
    const at = new Date(`${date}T${time || '00:00'}:00`);
    if (Number.isNaN(at.getTime())) return setProblem('Choose the date and time.');
    if (at.getTime() < Date.now() - 60_000) return setProblem('That time has passed. Choose a later one, or Send now.');
    setProblem(null);
    schedule.mutate(at.toISOString().replace(/\.\d{3}Z$/, 'Z'));
  }
  return (
    <Dialog
      open
      title={`Send ${campaign.name}`}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Send} shortcut="Enter" busy={schedule.isPending}>
            {when === 'now' ? 'Send now' : 'Schedule'}
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <p>
          To <strong>{campaign.segment}</strong> with the template <strong>{campaign.template}</strong>. The recipients are worked out now: the segment&apos;s clients who
          agreed to WhatsApp messages and have a number.
        </p>
        <fieldset className="space-y-1">
          <legend className="sr-only">When</legend>
          <label className="flex items-center gap-3">
            <input type="radio" name="send-when" className="check" checked={when === 'now'} onChange={() => setWhen('now')} />
            Now
          </label>
          <label className="flex items-center gap-3">
            <input type="radio" name="send-when" className="check" checked={when === 'later'} onChange={() => setWhen('later')} />
            Later
          </label>
        </fieldset>
        {when === 'later' && (
          <div className="flex flex-wrap gap-2">
            <Input aria-label="Send on" type="date" className="w-[180px]" value={date} onChange={(e) => setDate(e.target.value)} />
            <Input aria-label="Send at" type="time" className="w-[140px]" value={time} onChange={(e) => setTime(e.target.value)} />
          </div>
        )}
        <p className="text-muted">The server sends them a few at a time; it can take a while for a large segment.</p>
      </div>
      <FormError error={problem ?? schedule.error} />
    </Dialog>
  );
}

export function CancelCampaignDialog({ campaign, onClose }: { campaign: CampaignRow; onClose(): void }) {
  const toast = useToast();
  const refresh = useCampaignRefresh();
  const cancel = useMutation({
    mutationFn: () => api.cancelCampaign(campaign.id, { version: campaign.version }),
    onSuccess: (saved) => {
      toast.ok(`${saved.name} is cancelled.`);
      refresh();
      onClose();
    },
    onError: (e: Error) => toast.error(e.message),
  });
  return (
    <ConfirmDialog
      open
      title={`Cancel ${campaign.name}?`}
      message={`The ${formatCount(campaign.counts.queued)} messages still queued are never sent. Those already sent stay sent.`}
      confirmLabel="Cancel the campaign"
      cancelLabel="Keep sending"
      danger
      busy={cancel.isPending}
      onConfirm={() => cancel.mutate()}
      onClose={onClose}
    />
  );
}
