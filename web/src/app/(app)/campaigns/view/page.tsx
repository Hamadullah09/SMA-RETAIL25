'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ArrowLeft, Ban, Send } from 'lucide-react';
import { useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { CancelCampaignDialog, CountsRow, ScheduleDialog, WhatsAppBanner } from '@/components/campaigns';
import { Button, LinkButton } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Select } from '@/components/ui/field';
import { Chip, Detail, PageHeader, Pager, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { api } from '@/lib/api';
import { formatCount, formatMoney, formatPercent, formatShortUtc, formatUtc } from '@/lib/format';
import { useStore } from '@/lib/store';
import type { WhatsAppMessageRow } from '@/lib/types';
import { CAMPAIGN_STATUS, campaignActions, deliveredPercent, MESSAGE_STATUS, MESSAGE_STATUSES } from '@/lib/whatsapp';

const PAGE_SIZE = 50;
const DAYS = [1, 3, 7, 14, 30, 90];

export default function CampaignPage() {
  return (
    <Suspense>
      <Campaign />
    </Suspense>
  );
}

function Campaign() {
  const id = useSearchParams().get('id') ?? '';
  const symbol = useStore().currencySymbol;
  const [days, setDays] = useState(7);
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const [scheduling, setScheduling] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  useEffect(() => setPage(1), [status]);
  const campaign = useQuery({
    queryKey: ['campaign', id],
    queryFn: () => api.campaign(id),
    enabled: !!id,
    refetchInterval: (q) => (q.state.data?.status === 'Sending' || q.state.data?.status === 'Scheduled' ? 15_000 : false),
  });
  const results = useQuery({ queryKey: ['campaign', id, 'results', days], queryFn: () => api.campaignResults(id, days), enabled: !!id, placeholderData: keepPreviousData });
  const messages = useQuery({
    queryKey: ['campaign-messages', id, status, page],
    queryFn: ({ signal }) => api.campaignMessages(id, { status, page, pageSize: PAGE_SIZE }, signal),
    enabled: !!id,
    placeholderData: keepPreviousData,
  });

  if (!id) {
    return (
      <div className="card">
        <EmptyState title="No campaign chosen">Choose one on the Campaigns page.</EmptyState>
      </div>
    );
  }
  if (campaign.error) {
    return (
      <div className="card">
        <ErrorState error={campaign.error} onRetry={() => void campaign.refetch()} />
      </div>
    );
  }
  const c = campaign.data;
  const actions = c ? campaignActions(c.status, c.counts) : null;
  const r = results.data;

  return (
    <div className="space-y-4">
      <PageHeader title={c?.name ?? 'Campaign'} aside={c && <Chip tone={CAMPAIGN_STATUS[c.status]?.tone ?? 'plain'}>{CAMPAIGN_STATUS[c.status]?.label ?? c.status}</Chip>} />
      <WhatsAppBanner />
      <Toolbar>
        <LinkButton icon={ArrowLeft} href="/campaigns/">
          Campaigns
        </LinkButton>
        {actions?.schedule && (
          <Button variant="sun" icon={Send} onClick={() => setScheduling(true)}>
            Send
          </Button>
        )}
        {actions?.cancel && (
          <Button icon={Ban} onClick={() => setCancelling(true)}>
            Cancel sending
          </Button>
        )}
      </Toolbar>
      {!c ? (
        <Skeleton className="h-40 rounded-card" />
      ) : (
        <Card title="The campaign">
          <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <Detail label="Segment">{c.segment}</Detail>
            <Detail label="Template">{c.template}</Detail>
            <Detail label="Store">{c.storeCode}</Detail>
            <Detail label="Recipients">{formatCount(c.recipients)}</Detail>
            <Detail label="Sends from">{c.scheduledAtUtc ? formatUtc(c.scheduledAtUtc) : c.status === 'Draft' ? 'Not scheduled' : ''}</Detail>
            <Detail label="Started">{formatUtc(c.startedAtUtc)}</Detail>
            <Detail label="Finished">{formatUtc(c.finishedAtUtc)}</Detail>
            <Detail label="Made by">{c.createdBy ? `${c.createdBy}, ${formatShortUtc(c.createdAtUtc)}` : formatShortUtc(c.createdAtUtc)}</Detail>
          </dl>
          <CountsRow counts={c.counts} className="mt-4" />
          <p className="mt-2 text-muted">{formatPercent(deliveredPercent(c.counts))} of the messages sent reached the phone.</p>
        </Card>
      )}
      <Card
        title="Results"
        aside={
          <span className="flex items-center gap-2">
            <label htmlFor="results-days">Sales within</label>
            <Select id="results-days" className="w-[130px]" value={String(days)} onChange={(e) => setDays(Number(e.target.value))}>
              {DAYS.map((d) => (
                <option key={d} value={d}>
                  {d} {d === 1 ? 'day' : 'days'}
                </option>
              ))}
            </Select>
          </span>
        }
      >
        {!r ? (
          results.error ? (
            <ErrorState error={results.error} onRetry={() => void results.refetch()} />
          ) : (
            <Skeleton className="h-20" />
          )
        ) : (
          <dl className="grid grid-cols-2 gap-4 lg:grid-cols-4">
            <Figure label="Recipients" value={formatCount(r.recipients)} />
            <Figure label="Bought since" value={formatCount(r.buyers)} note={r.recipients ? `${formatPercent((r.buyers / r.recipients) * 100)} of the recipients` : undefined} />
            <Figure label="Sales" value={formatCount(r.sales)} />
            <Figure label="Their sales" value={formatMoney(r.salesTotal, symbol)} note={`Within ${r.days} days of the start`} />
          </dl>
        )}
      </Card>
      <Toolbar>
        <label htmlFor="message-status" className="text-muted">
          Messages
        </label>
        <Select id="message-status" className="w-[170px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every status</option>
          {MESSAGE_STATUSES.map((s) => (
            <option key={s} value={s}>
              {MESSAGE_STATUS[s].label}
            </option>
          ))}
        </Select>
      </Toolbar>
      <DataTable<WhatsAppMessageRow>
        rows={messages.data?.items}
        loading={messages.isLoading}
        error={messages.error}
        onRetry={() => void messages.refetch()}
        rowKey={(m) => m.id}
        caption="Messages"
        empty={<EmptyState title={c?.status === 'Draft' ? 'No messages until it is sent' : 'No messages with this status'} />}
        columns={[
          { key: 'client', header: 'Client', cell: (m) => <span className="font-semibold">{m.clientName}</span> },
          { key: 'to', header: 'To', cell: (m) => m.toNumber, hideBelow: 'sm' },
          { key: 'status', header: 'Status', cell: (m) => <Chip tone={MESSAGE_STATUS[m.status]?.tone ?? 'plain'}>{MESSAGE_STATUS[m.status]?.label ?? m.status}</Chip> },
          { key: 'sent', header: 'Sent', cell: (m) => formatShortUtc(m.sentAtUtc), hideBelow: 'md' },
          { key: 'read', header: 'Read', cell: (m) => formatShortUtc(m.readAtUtc ?? m.deliveredAtUtc), hideBelow: 'lg' },
          { key: 'error', header: 'Problem', cell: (m) => m.error ?? '', className: 'wrap', hideBelow: 'xl' },
        ]}
        footer={messages.data && <Pager page={page} pageSize={PAGE_SIZE} total={messages.data.total} onPage={setPage} noun="messages" />}
      />
      {scheduling && c && <ScheduleDialog campaign={c} onClose={() => setScheduling(false)} />}
      {cancelling && c && <CancelCampaignDialog campaign={c} onClose={() => setCancelling(false)} />}
    </div>
  );
}

function Figure({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="rounded-control bg-cream px-4 py-3">
      <dt className="text-note text-muted">{label}</dt>
      <dd className="font-heading text-value tabular-nums" data-testid={`result-${label}`}>
        {value}
      </dd>
      {note && <dd className="text-note text-muted">{note}</dd>}
    </div>
  );
}
