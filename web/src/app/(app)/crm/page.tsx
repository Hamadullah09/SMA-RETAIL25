'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDown, ArrowUp, GripVertical, Pencil, Plus, Save, Settings2, Trash2, UserPlus } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState, type DragEvent } from 'react';
import {
  ActivityDialog,
  ConvertDialog,
  LeadDialog,
  LostReasonDialog,
  OpportunityDialog,
  OptionalRangeBar,
  OwnerSelect,
  rangeQuery,
  useActivityDone,
  useCrmRefresh,
  useMayTick,
  useStages,
  type OptionalRange,
} from '@/components/crm';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Input, Select } from '@/components/ui/field';
import { Chip, PageHeader, Pager, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { ACTIVITY_KINDS, DUE, LEAD_STATUS, moveCard, openTotals, outcomeTone, OUTCOMES, relatedTo } from '@/lib/crm';
import { cx } from '@/lib/cx';
import { formatCount, formatDate, formatMoney, formatPercent, formatShortDateTime, formatShortUtc } from '@/lib/format';
import { parseNumber } from '@/lib/forms';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { ActivityRow, BoardStage, LeadRow, OpportunityBoard, OpportunityRow, OpportunityStage, StageOutcome, StageRequest } from '@/lib/types';

type Tab = 'pipeline' | 'leads' | 'activities' | 'report';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'pipeline', label: 'Pipeline' },
  { key: 'leads', label: 'Leads' },
  { key: 'activities', label: 'Activities' },
  { key: 'report', label: 'Won and lost' },
];
const PAGE_SIZE = 50;

export default function CrmPage() {
  return (
    <Suspense>
      <Crm />
    </Suspense>
  );
}

function Crm() {
  const params = useSearchParams();
  const router = useRouter();
  const { can, canChangeShared } = useSession();
  const asked = params.get('tab') as Tab | null;
  const [tab, setTab] = useState<Tab>(asked && TABS.some((t) => t.key === asked) ? asked : 'pipeline');
  const [adding, setAdding] = useState<Tab | null>(null);
  const [stages, setStages] = useState(false);

  useEffect(() => {
    const t = params.get('tab') as Tab | null;
    if (t && TABS.some((x) => x.key === t)) setTab(t);
    if (params.get('new') === '1') {
      if (can(P.crmEdit)) setAdding(t && t !== 'report' ? t : 'pipeline');
      router.replace('/crm/');
    }
  }, [params, router, can]);

  return (
    <div>
      <PageHeader title="CRM">
        {can(P.crmEdit) && canChangeShared && (
          <Button icon={Settings2} onClick={() => setStages(true)}>
            Stages
          </Button>
        )}
      </PageHeader>
      <Tabs label="CRM" tabs={TABS} value={tab} onChange={setTab} className="mb-4" />
      {tab === 'pipeline' && <Pipeline />}
      {tab === 'leads' && <Leads />}
      {tab === 'activities' && <Activities />}
      {tab === 'report' && <WonLost />}
      {adding === 'pipeline' && <OpportunityDialog opportunity={null} onClose={() => setAdding(null)} />}
      {adding === 'leads' && <LeadDialog lead={null} onClose={() => setAdding(null)} />}
      {adding === 'activities' && <ActivityDialog activity={null} onClose={() => setAdding(null)} />}
      {stages && <StagesDialog onClose={() => setStages(false)} />}
    </div>
  );
}

// Pipeline ----------------------------------------------------------------------------------------------------------

function Pipeline() {
  const { can } = useSession();
  const { storeId, currencySymbol: symbol } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const refresh = useCrmRefresh();
  const edit = can(P.crmEdit);
  const [ownerId, setOwnerId] = useState('');
  const [range, setRange] = useState<OptionalRange>({ mode: 'any', from: '', to: '' });
  const [opening, setOpening] = useState<OpportunityRow | null>(null);
  const [adding, setAdding] = useState<string | null>(null);
  const [losing, setLosing] = useState<{ card: OpportunityRow; stageId: string } | null>(null);
  const [dragging, setDragging] = useState<string | null>(null);
  const [over, setOver] = useState<string | null>(null);
  const dates = rangeQuery(range);
  const key = ['board', { storeId, ownerId, ...dates }];
  const board = useQuery({ queryKey: key, queryFn: ({ signal }) => api.board({ storeId, ownerId, ...dates }, signal), placeholderData: keepPreviousData });

  const move = useMutation({
    mutationFn: (m: { card: OpportunityRow; stageId: string; lostReason?: string }) =>
      api.moveOpportunity(m.card.id, { stageId: m.stageId, version: m.card.version, lostReason: m.lostReason ?? null }),
    onMutate: (m) => {
      const before = queryClient.getQueryData<OpportunityBoard>(key);
      if (before) queryClient.setQueryData(key, moveCard(before, m.card.id, m.stageId, m.lostReason));
      return { before };
    },
    onSuccess: (saved) => {
      toast.ok(`${saved.title} is in ${saved.stage}.`);
      setLosing(null);
    },
    onError: (e: Error, _m, context) => {
      if (context?.before) queryClient.setQueryData(key, context.before);
      toast.error(e instanceof ApiError && e.isConflict ? 'Someone else changed this opportunity. The board is loaded again.' : e.message);
    },
    onSettled: () => refresh(),
  });

  function moveTo(card: OpportunityRow, stage: BoardStage) {
    if (card.stageId === stage.id) return;
    if (stage.outcome === 'Lost') setLosing({ card, stageId: stage.id });
    else move.mutate({ card, stageId: stage.id });
  }

  function drop(e: DragEvent, stage: BoardStage) {
    e.preventDefault();
    setOver(null);
    const id = e.dataTransfer.getData('text/plain') || dragging;
    setDragging(null);
    const card = board.data?.stages.flatMap((s) => s.opportunities).find((o) => o.id === id);
    if (card && edit) moveTo(card, stage);
  }

  const money = (n: number) => formatMoney(n, symbol);
  const totals = board.data ? openTotals(board.data) : null;

  return (
    <>
      <Toolbar>
        <label className="sr-only" htmlFor="board-owner">
          Owner
        </label>
        <OwnerSelect id="board-owner" className="w-[220px]" value={ownerId} onChange={setOwnerId} storeId={storeId} anyLabel="Everyone" />
        <OptionalRangeBar id="board-dates" label="Close date" value={range} onChange={setRange} />
        {edit && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setAdding('')}>
            New opportunity
          </Button>
        )}
      </Toolbar>
      {totals && (
        <p className="mb-3 px-1 text-ink" data-testid="pipeline-totals">
          Open: <strong>{formatCount(totals.count)}</strong> opportunities worth <strong className="tabular-nums">{money(totals.value)}</strong>, weighted{' '}
          <strong className="tabular-nums">{money(totals.weighted)}</strong>
        </p>
      )}
      {board.error ? (
        <div className="card">
          <ErrorState error={board.error} onRetry={() => void board.refetch()} />
        </div>
      ) : !board.data ? (
        <div className="flex gap-4">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-[360px] w-[280px] shrink-0 rounded-card" />
          ))}
        </div>
      ) : (
        <div className="relative -mx-1 overflow-x-auto px-1 pb-2" role="region" aria-label="Pipeline board">
          <div className="flex min-w-max gap-4">
            {board.data.stages.map((stage) => (
              <section
                key={stage.id}
                aria-label={`Stage ${stage.name}`}
                data-testid={`stage-${stage.name}`}
                className={cx('flex w-[280px] shrink-0 flex-col rounded-card bg-card shadow-card transition-shadow', over === stage.id && 'ring-2 ring-sun-dark')}
                onDragOver={(e) => {
                  if (!edit) return;
                  e.preventDefault();
                  setOver(stage.id);
                }}
                onDragLeave={() => setOver((o) => (o === stage.id ? null : o))}
                onDrop={(e) => drop(e, stage)}
              >
                <header className="border-b border-line px-4 pb-3 pt-4">
                  <div className="flex items-center justify-between gap-2">
                    <h2 className="truncate font-heading text-section">{stage.name}</h2>
                    <Chip tone={outcomeTone(stage.outcome)}>{formatCount(stage.count)}</Chip>
                  </div>
                  <p className="text-note text-muted">
                    <span className="tabular-nums" data-testid={`stage-value-${stage.name}`}>
                      {money(stage.value)}
                    </span>
                    {stage.outcome === 'Open' && <span className="tabular-nums"> · weighted {money(stage.weighted)}</span>}
                    {stage.outcome === 'Open' && ` · ${stage.probability}%`}
                  </p>
                </header>
                <ol className="flex min-h-[120px] flex-1 flex-col gap-2.5 p-3">
                  {stage.opportunities.map((o) => (
                    <li
                      key={o.id}
                      draggable={edit}
                      onDragStart={(e) => {
                        e.dataTransfer.setData('text/plain', o.id);
                        e.dataTransfer.effectAllowed = 'move';
                        setDragging(o.id);
                      }}
                      onDragEnd={() => setDragging(null)}
                      className={cx('rounded-control border border-line bg-cream p-3', dragging === o.id && 'opacity-50', edit && 'cursor-grab')}
                      data-testid={`card-${o.id}`}
                    >
                      <div className="flex items-start gap-2">
                        {edit && <GripVertical className="mt-1 h-4 w-4 shrink-0 text-muted" aria-hidden="true" />}
                        <button type="button" className="min-w-0 flex-1 text-left font-semibold hover:underline" onClick={() => setOpening(o)}>
                          {o.title}
                        </button>
                      </div>
                      <p className="truncate text-note text-muted">{o.clientName ?? (o.leadName ? `Lead ${o.leadName}` : '')}</p>
                      <div className="mt-1 flex flex-wrap items-center justify-between gap-x-3 text-note">
                        <span className="font-semibold tabular-nums">{money(o.expectedValue)}</span>
                        <span className="text-muted">{o.closedOn ? formatDate(o.closedOn) : o.expectedCloseOn ? `Close ${formatDate(o.expectedCloseOn)}` : ''}</span>
                      </div>
                      {o.ownerName && <p className="text-note text-muted">{o.ownerName}</p>}
                      {o.lostReason && <p className="text-note text-danger-ink">{o.lostReason}</p>}
                      {edit && (
                        <>
                          <label className="sr-only" htmlFor={`move-${o.id}`}>
                            Move {o.title} to
                          </label>
                          <Select
                            id={`move-${o.id}`}
                            className="mt-2 min-h-[40px] py-1"
                            value={o.stageId}
                            onChange={(e) => {
                              const target = board.data?.stages.find((s) => s.id === e.target.value);
                              if (target) moveTo(o, target);
                            }}
                          >
                            {board.data!.stages.map((s) => (
                              <option key={s.id} value={s.id}>
                                {s.id === o.stageId ? `In ${s.name}` : `Move to ${s.name}`}
                              </option>
                            ))}
                          </Select>
                        </>
                      )}
                    </li>
                  ))}
                  {stage.opportunities.length === 0 && <li className="px-1 py-6 text-center text-note text-muted">{edit ? 'Drop a card here' : 'Nothing here'}</li>}
                </ol>
                {edit && stage.outcome === 'Open' && (
                  <div className="px-3 pb-3">
                    <Button icon={Plus} className="w-full" onClick={() => setAdding(stage.id)}>
                      Add here
                    </Button>
                  </div>
                )}
              </section>
            ))}
          </div>
        </div>
      )}
      <p className="mt-2 px-1 text-ink">
        {edit ? 'Drag a card to another stage, or use its Move to box. A lost one asks for the reason.' : 'Your role can read the pipeline but not change it.'}
      </p>
      {opening && <OpportunityDialog opportunity={opening} onClose={() => setOpening(null)} />}
      {adding !== null && <OpportunityDialog opportunity={null} stageId={adding || undefined} onClose={() => setAdding(null)} />}
      {losing && (
        <LostReasonDialog
          title={losing.card.title}
          busy={move.isPending}
          onConfirm={(reason) => move.mutate({ card: losing.card, stageId: losing.stageId, lostReason: reason })}
          onClose={() => setLosing(null)}
        />
      )}
    </>
  );
}

// Leads -------------------------------------------------------------------------------------------------------------

function Leads() {
  const { can } = useSession();
  const { storeId } = useStore();
  const toast = useToast();
  const refresh = useCrmRefresh();
  const edit = can(P.crmEdit);
  const mayConvert = edit && can(P.clientsEdit);
  const [text, setText] = useState('');
  const [status, setStatus] = useState('Open');
  const [ownerId, setOwnerId] = useState('');
  const [range, setRange] = useState<OptionalRange>({ mode: 'any', from: '', to: '' });
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<LeadRow | null>(null);
  const [editing, setEditing] = useState<LeadRow | 'new' | null>(null);
  const [converting, setConverting] = useState<LeadRow | null>(null);
  const [deleting, setDeleting] = useState<LeadRow | null>(null);
  const search = useDebounced(text.trim());
  const dates = rangeQuery(range);
  const filters = { storeId, search, status, ownerId, ...dates };
  useEffect(() => setPage(1), [storeId, search, status, ownerId, dates.from, dates.to]);
  const query = useQuery({
    queryKey: ['leads', filters, page],
    queryFn: ({ signal }) => api.leads({ ...filters, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteLead(id),
    onSuccess: () => {
      toast.ok(`${deleting?.name ?? 'The lead'} is deleted.`);
      setDeleting(null);
      setSelected(null);
      refresh();
    },
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <>
      <Toolbar>
        <SearchBox value={text} onChange={setText} placeholder="Name, company, phone or e-mail" />
        <label className="sr-only" htmlFor="lead-status-filter">
          Status
        </label>
        <Select id="lead-status-filter" className="w-[170px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every status</option>
          <option value="Open">Open</option>
          <option value="Converted">Clients</option>
          <option value="Dropped">Dropped</option>
        </Select>
        <label className="sr-only" htmlFor="lead-owner-filter">
          Owner
        </label>
        <OwnerSelect id="lead-owner-filter" className="w-[200px]" value={ownerId} onChange={setOwnerId} storeId={storeId} anyLabel="Everyone" />
        <OptionalRangeBar id="lead-dates" label="Added" value={range} onChange={setRange} />
      </Toolbar>
      <Toolbar>
        {edit && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            New lead
          </Button>
        )}
        <Button icon={Pencil} shortcut="Ctrl+O" disabled={!selected} onClick={() => selected && setEditing(selected)}>
          {edit ? 'Edit' : 'Open'}
        </Button>
        {mayConvert && (
          <Button icon={UserPlus} disabled={!selected || selected.status === 'Converted'} onClick={() => selected && setConverting(selected)}>
            Make a client
          </Button>
        )}
        {edit && (
          <Button icon={Trash2} shortcut="Del" disabled={!selected} onClick={() => selected && setDeleting(selected)}>
            Delete
          </Button>
        )}
      </Toolbar>
      <DataTable<LeadRow>
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(l) => l.id}
        selectedKey={selected?.id}
        onRowClick={(l) => (selected?.id === l.id ? setEditing(l) : setSelected(l))}
        caption="Leads"
        empty={<EmptyState title="No leads found">{search ? 'Nothing matches the search.' : edit ? 'New lead adds one.' : null}</EmptyState>}
        columns={[
          { key: 'name', header: 'Name', cell: (l) => <span className="font-semibold">{l.name}</span> },
          { key: 'company', header: 'Company', cell: (l) => l.company ?? '', hideBelow: 'md' },
          { key: 'phone', header: 'Phone', cell: (l) => l.phone ?? l.whatsAppNumber ?? '', hideBelow: 'sm' },
          { key: 'source', header: 'Source', cell: (l) => l.source ?? '', hideBelow: 'xl' },
          { key: 'owner', header: 'Owner', cell: (l) => l.ownerName ?? '', hideBelow: 'lg' },
          { key: 'store', header: 'Store', cell: (l) => l.storeCode, hideBelow: 'xl' },
          { key: 'added', header: 'Added', cell: (l) => formatShortUtc(l.createdAtUtc), hideBelow: 'lg' },
          {
            key: 'status',
            header: 'Status',
            cell: (l) => <Chip tone={LEAD_STATUS[l.status]?.tone ?? 'plain'}>{l.status === 'Converted' && l.clientName ? `Client ${l.clientName}` : LEAD_STATUS[l.status]?.label ?? l.status}</Chip>,
          },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="leads" />}
      />
      {editing && <LeadDialog lead={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {converting && <ConvertDialog lead={converting} onClose={() => setConverting(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete the lead ${deleting?.name ?? ''}?`}
        message="Its activities go with it. A lead with opportunities is not deleted."
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

// Activities --------------------------------------------------------------------------------------------------------

function Activities() {
  const { can } = useSession();
  const { storeId } = useStore();
  const edit = can(P.crmEdit);
  const done = useActivityDone();
  const mayTick = useMayTick();
  const [ownerId, setOwnerId] = useState('');
  const [kind, setKind] = useState('');
  const [state, setState] = useState<'open' | 'done' | ''>('open');
  const [range, setRange] = useState<OptionalRange>({ mode: 'any', from: '', to: '' });
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<ActivityRow | 'new' | null>(null);
  const dates = rangeQuery(range);
  const filters = { storeId, ownerId, kind, done: state === '' ? undefined : state === 'done', ...dates };
  useEffect(() => setPage(1), [storeId, ownerId, kind, state, dates.from, dates.to]);
  const query = useQuery({
    queryKey: ['activities', filters, page],
    queryFn: ({ signal }) => api.activities({ ...filters, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <Toolbar>
        <label className="sr-only" htmlFor="act-owner-filter">
          Owner
        </label>
        <OwnerSelect id="act-owner-filter" className="w-[200px]" value={ownerId} onChange={setOwnerId} storeId={storeId} anyLabel="Everyone" />
        <label className="sr-only" htmlFor="act-kind-filter">
          Kind
        </label>
        <Select id="act-kind-filter" className="w-[160px]" value={kind} onChange={(e) => setKind(e.target.value)}>
          <option value="">Every kind</option>
          {ACTIVITY_KINDS.map((k) => (
            <option key={k.key} value={k.key}>
              {k.label}
            </option>
          ))}
        </Select>
        <label className="sr-only" htmlFor="act-state-filter">
          Done or open
        </label>
        <Select id="act-state-filter" className="w-[150px]" value={state} onChange={(e) => setState(e.target.value as 'open' | 'done' | '')}>
          <option value="open">Open</option>
          <option value="done">Done</option>
          <option value="">Open and done</option>
        </Select>
        <OptionalRangeBar id="act-dates" label="Due" value={range} onChange={setRange} />
        {edit && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            New activity
          </Button>
        )}
      </Toolbar>
      <DataTable<ActivityRow>
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(a) => a.id}
        onRowClick={(a) => setEditing(a)}
        caption="Activities"
        empty={<EmptyState title="No activities found" />}
        columns={[
          {
            key: 'done',
            header: <span className="sr-only">Done</span>,
            cell: (a) => (
              <input
                type="checkbox"
                className="check"
                aria-label={`${a.subject} done`}
                checked={a.isDone}
                disabled={!mayTick(a) || done.isPending}
                onClick={(e) => e.stopPropagation()}
                onChange={() => done.mutate(a)}
              />
            ),
          },
          { key: 'kind', header: 'Kind', cell: (a) => a.kind, hideBelow: 'sm' },
          { key: 'subject', header: 'Subject', cell: (a) => <span className={cx('font-semibold', a.isDone && 'line-through decoration-muted')}>{a.subject}</span>, className: 'wrap' },
          { key: 'about', header: 'About', cell: (a) => relatedTo(a), hideBelow: 'lg', className: 'wrap' },
          { key: 'due', header: 'Due', cell: (a) => (a.dueAt ? formatShortDateTime(a.dueAt) : ''), hideBelow: 'md' },
          { key: 'state', header: 'When', cell: (a) => <Chip tone={DUE[a.due]?.tone ?? 'plain'}>{DUE[a.due]?.label ?? a.due}</Chip> },
          { key: 'owner', header: 'Owner', cell: (a) => a.ownerName ?? '', hideBelow: 'xl' },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="activities" />}
      />
      {editing && <ActivityDialog activity={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

// Won and lost ------------------------------------------------------------------------------------------------------

function WonLost() {
  const { storeId, currencySymbol: symbol } = useStore();
  const dates = useDateRange('month');
  const [ownerId, setOwnerId] = useState('');
  const query = useQuery({
    queryKey: ['won-lost', storeId, ownerId, dates.range.from, dates.range.to],
    queryFn: () => api.wonLost({ storeId, ownerId, from: dates.range.from, to: dates.range.to }),
    placeholderData: keepPreviousData,
  });
  const r = query.data;
  const money = (n: number) => formatMoney(n, symbol);

  return (
    <div className="space-y-4">
      <Toolbar>
        <DateRangeBar state={dates} compact />
        <label className="sr-only" htmlFor="report-owner">
          Owner
        </label>
        <OwnerSelect id="report-owner" className="w-[200px]" value={ownerId} onChange={setOwnerId} storeId={storeId} anyLabel="Everyone" />
      </Toolbar>
      {query.error && !r ? (
        <div className="card">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      ) : (
        <>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <Figure label="Won" value={r && money(r.won.value)} note={r && `${formatCount(r.won.count)} opportunities`} tone="ok" />
            <Figure label="Lost" value={r && money(r.lost.value)} note={r && `${formatCount(r.lost.count)} opportunities`} tone="danger" />
            <Figure label="Win rate" value={r && formatPercent(r.winRate)} note={r && `${formatCount(r.won.count + r.lost.count)} closed`} dark />
          </div>
          <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
            <Card title="By owner">
              <DataTable
                className="shadow-none"
                rows={r?.byOwner}
                loading={!r}
                rowKey={(o, i) => (o.ownerId ?? 'none') + i}
                empty={<p className="p-4 text-muted">Nothing closed in these days.</p>}
                columns={[
                  { key: 'who', header: 'Owner', cell: (o) => o.ownerName ?? 'Nobody' },
                  { key: 'won', header: 'Won', numeric: true, cell: (o) => `${formatCount(o.won)} · ${money(o.wonValue)}` },
                  { key: 'lost', header: 'Lost', numeric: true, cell: (o) => `${formatCount(o.lost)} · ${money(o.lostValue)}` },
                ]}
              />
            </Card>
            <Card title="Why they were lost">
              <DataTable
                className="shadow-none"
                rows={r?.lostReasons}
                loading={!r}
                rowKey={(x) => x.reason}
                empty={<p className="p-4 text-muted">Nothing lost in these days.</p>}
                columns={[
                  { key: 'reason', header: 'Reason', cell: (x) => x.reason, className: 'wrap' },
                  { key: 'n', header: 'Times', numeric: true, cell: (x) => formatCount(x.count) },
                  { key: 'value', header: 'Value', numeric: true, cell: (x) => money(x.value) },
                ]}
              />
            </Card>
          </div>
          <DataTable<OpportunityRow>
            rows={r?.closed}
            loading={!r}
            rowKey={(o) => o.id}
            caption="Closed opportunities"
            empty={<EmptyState title="Nothing won or lost in these days" />}
            columns={[
              { key: 'title', header: 'Opportunity', cell: (o) => <span className="font-semibold">{o.title}</span>, className: 'wrap' },
              { key: 'who', header: 'Client or lead', cell: (o) => o.clientName ?? o.leadName ?? '', hideBelow: 'md' },
              { key: 'outcome', header: 'Result', cell: (o) => <Chip tone={outcomeTone(o.outcome)}>{o.outcome}</Chip> },
              { key: 'on', header: 'Closed', cell: (o) => formatDate(o.closedOn), hideBelow: 'sm' },
              { key: 'owner', header: 'Owner', cell: (o) => o.ownerName ?? '', hideBelow: 'lg' },
              { key: 'reason', header: 'Reason', cell: (o) => o.lostReason ?? '', hideBelow: 'xl', className: 'wrap' },
              { key: 'value', header: 'Value', numeric: true, cell: (o) => money(o.expectedValue) },
            ]}
          />
        </>
      )}
    </div>
  );
}

function Figure({ label, value, note, tone, dark }: { label: string; value: string | undefined; note?: string; tone?: 'ok' | 'danger'; dark?: boolean }) {
  return (
    <section className={cx('rounded-card p-5 shadow-card', dark ? 'bg-charcoal text-white' : 'bg-card')}>
      <h2 className="font-heading text-section">{label}</h2>
      {value === undefined ? (
        <Skeleton className="my-2 h-10 w-2/3" />
      ) : (
        <p className={cx('font-heading text-value tabular-nums', tone === 'ok' && 'text-ok', tone === 'danger' && 'text-danger')} data-testid={`figure-${label}`}>
          {value}
        </p>
      )}
      {note && <p className={cx('text-note', dark ? 'text-cream' : 'text-muted')}>{note}</p>}
    </section>
  );
}

// Stages ------------------------------------------------------------------------------------------------------------

interface StageForm {
  id: string | null;
  name: string;
  probability: string;
  outcome: StageOutcome;
}

function StagesDialog({ onClose }: { onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const refresh = useCrmRefresh();
  const stages = useStages();
  const [rows, setRows] = useState<StageForm[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useEffect(() => {
    if (stages.data && !rows) setRows(stages.data.map((s: OpportunityStage) => ({ id: s.id, name: s.name, probability: String(s.probability), outcome: s.outcome })));
  }, [stages.data, rows]);

  const save = useMutation({
    mutationFn: (list: StageRequest[]) => api.saveStages(list),
    onSuccess: () => {
      toast.ok('The stages are saved.');
      void queryClient.invalidateQueries({ queryKey: ['crm-stages'] });
      refresh();
      onClose();
    },
  });

  function change(i: number, patch: Partial<StageForm>) {
    setRows((list) => list!.map((r, j) => (j === i ? { ...r, ...patch } : r)));
  }
  function swap(i: number, j: number) {
    setRows((list) => {
      const next = [...list!];
      [next[i], next[j]] = [next[j], next[i]];
      return next;
    });
  }

  function submit() {
    const list = rows ?? [];
    const found = list.some((r) => !r.name.trim())
      ? 'Every stage has a name.'
      : list.some((r) => {
            const p = parseNumber(r.probability);
            return p === null || p < 0 || p > 100;
          })
        ? 'A probability is 0 to 100.'
        : !(['Open', 'Won', 'Lost'] as StageOutcome[]).every((o) => list.some((r) => r.outcome === o))
          ? 'Keep at least one open, one won and one lost stage.'
          : null;
    setProblem(found);
    if (found) return;
    save.mutate(list.map((r) => ({ id: r.id, name: r.name.trim(), probability: parseNumber(r.probability) ?? 0, outcome: r.outcome })));
  }

  return (
    <Dialog
      open
      width="lg"
      title="Pipeline stages"
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button className="mr-auto" icon={Plus} onClick={() => setRows((list) => [...(list ?? []), { id: null, name: '', probability: '50', outcome: 'Open' }])}>
            Add a stage
          </Button>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Save
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      {stages.error ? (
        <ErrorState error={stages.error} onRetry={() => void stages.refetch()} />
      ) : !rows ? (
        <Skeleton className="h-40" />
      ) : (
        <ol className="space-y-2">
          {rows.map((r, i) => (
            <li key={r.id ?? `new-${i}`} className="flex flex-wrap items-center gap-2 rounded-control bg-cream p-2">
              <Input aria-label={`Stage ${i + 1} name`} className="min-w-[160px] flex-1" value={r.name} onChange={(e) => change(i, { name: e.target.value })} />
              <Input aria-label={`Stage ${i + 1} probability`} inputMode="numeric" className="w-[90px]" value={r.probability} onChange={(e) => change(i, { probability: e.target.value })} />
              <span className="text-muted">%</span>
              <Select aria-label={`Stage ${i + 1} result`} className="w-[120px]" value={r.outcome} onChange={(e) => change(i, { outcome: e.target.value as StageOutcome })}>
                {OUTCOMES.map((o) => (
                  <option key={o.key} value={o.key}>
                    {o.label}
                  </option>
                ))}
              </Select>
              <button type="button" className="btn btn-icon h-11 min-h-0 w-11" aria-label={`Move ${r.name || 'the stage'} up`} disabled={i === 0} onClick={() => swap(i, i - 1)}>
                <ArrowUp className="h-5 w-5" />
              </button>
              <button type="button" className="btn btn-icon h-11 min-h-0 w-11" aria-label={`Move ${r.name || 'the stage'} down`} disabled={i === rows.length - 1} onClick={() => swap(i, i + 1)}>
                <ArrowDown className="h-5 w-5" />
              </button>
              <button type="button" className="btn btn-icon h-11 min-h-0 w-11" aria-label={`Remove ${r.name || 'the stage'}`} onClick={() => setRows((list) => list!.filter((_, j) => j !== i))}>
                <Trash2 className="h-5 w-5" />
              </button>
            </li>
          ))}
        </ol>
      )}
      <p className="mt-3 text-muted">The board shows the stages in this order. A stage that still holds opportunities cannot be removed.</p>
      <FormError error={problem ?? save.error} />
    </Dialog>
  );
}
