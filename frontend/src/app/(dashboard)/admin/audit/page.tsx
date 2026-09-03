'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  FilePenLine,
  FilePlus2,
  KeyRound,
  LogIn,
  RotateCcw,
  ShieldAlert,
  ShieldCheck,
  Trash2,
  X,
  type LucideIcon,
} from 'lucide-react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { BrowseFormShell, FormSection } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi, type AuditFilters } from '@/lib/masters-api';
import { LOG_CATEGORIES, logCategory } from '@/lib/log-categories';
import { PosApiError } from '@/lib/pos-api';
import { cn } from '@/lib/utils';
import type { AuditAction, AuditLogRow } from '@/types/masters';
import { describeError } from '@/lib/errors';

const ACTIONS: Array<{ value: AuditAction | ''; label: string }> = [
  { value: '', label: 'All actions' },
  { value: 'Created', label: 'Created' },
  { value: 'Updated', label: 'Updated' },
  { value: 'Deleted', label: 'Deleted' },
  { value: 'SignedIn', label: 'Signed in' },
  { value: 'SignInFailed', label: 'Sign-in failed' },
  { value: 'PermissionDenied', label: 'Permission denied' },
  { value: 'StepUpGranted', label: 'Step-up granted' },
  { value: 'StepUpDenied', label: 'Step-up denied' },
];

/**
 * Each action as a glyph, a word and — last — a tone.
 *
 * A refusal and an ordinary edit used to differ only by hue in this column, which is exactly the
 * distinction an investigator is scanning for and exactly the one a colour-blind reader loses.
 */
const ACTION_STYLE: Record<AuditAction, { icon: LucideIcon; tone: string; label: string }> = {
  Created: { icon: FilePlus2, tone: 'text-ink-muted', label: 'Created' },
  Updated: { icon: FilePenLine, tone: 'text-ink-muted', label: 'Updated' },
  Deleted: { icon: Trash2, tone: 'text-negative-text', label: 'Deleted' },
  SignedIn: { icon: LogIn, tone: 'text-ink-muted', label: 'Signed in' },
  SignInFailed: { icon: ShieldAlert, tone: 'text-warning-text', label: 'Sign-in failed' },
  PermissionDenied: { icon: ShieldAlert, tone: 'text-warning-text', label: 'Permission denied' },
  StepUpGranted: { icon: ShieldCheck, tone: 'text-positive-text', label: 'Step-up granted' },
  StepUpDenied: { icon: KeyRound, tone: 'text-warning-text', label: 'Step-up denied' },
};

/**
 * The audit log (doc 07 §Audit).
 *
 * Read-only by construction — an audit log a user can edit is not an audit log. The row detail shows
 * the before/after values side by side, because "who changed this" is only half the question an
 * investigation asks; the other half is "from what, to what".
 */
export default function AuditPage() {
  const auth = useAuth();
  const canRead = auth.can('audit.read');

  const [from, setFrom] = useState(() => isoDate(-7));
  const [to, setTo] = useState(() => isoDate(0));
  const [entityType, setEntityType] = useState('');

  // The category the tabs select. A category is several record types — a sale is a transaction, its
  // lines, its tenders and a drawer movement — so this cannot be one entity name.
  const [category, setCategory] = useState('all');
  const [action, setAction] = useState<AuditAction | ''>('');
  const [rows, setRows] = useState<AuditLogRow[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(false);
  const [selected, setSelected] = useState<AuditLogRow | null>(null);
  const [trail, setTrail] = useState<AuditLogRow[]>([]);

  const filters = useMemo<AuditFilters>(
    () => ({
      // The date inputs give local days; the API takes instants. Sending the day edges keeps "from
      // Monday" meaning the whole of Monday.
      from: `${from}T00:00:00Z`,
      to: `${to}T23:59:59Z`,
      entityType: entityType || undefined,

      // Only when no explicit record type is typed: a name the investigator entered by hand is a
      // more specific request than the category it happens to belong to.
      entityTypes: entityType ? undefined : (logCategory(category).entityTypes.length > 0
        ? logCategory(category).entityTypes
        : undefined),
      action: action || undefined,
      take: 200,
    }),
    [from, to, entityType, action, category],
  );

  const load = useCallback(async () => {
    if (!canRead) return;

    setLoading(true);

    try {
      const page = await mastersApi.audit.list(filters);
      setRows(page.rows);
      setTotalCount(page.totalCount);
    } catch (error) {
      toast({ title: 'Could not load the audit log', description: describeError(error), variant: 'destructive' });
    } finally {
      setLoading(false);
    }
  }, [canRead, filters]);

  useEffect(() => {
    void load();
  }, [load]);

  const open = async (row: AuditLogRow) => {
    setSelected(row);
    setTrail([]);

    if (row.correlationId) {
      try {
        // Everything that one request did — the void and the approval that authorised it are one
        // story, and showing only the row that was clicked would tell half of it.
        setTrail(await mastersApi.audit.forRequest(row.correlationId));
      } catch {
        // The detail panel still works without the trail.
      }
    }
  };

  const columns = useMemo<DataGridColumn<AuditLogRow>[]>(
    () => [
      {
        key: 'occurred',
        header: 'When',
        width: 160,
        render: (r) => <span className="tabular-nums">{new Date(r.occurredAt).toLocaleString()}</span>,
        sortValue: (r) => r.occurredAt,
      },
      {
        key: 'action',
        header: 'Action',
        width: 160,
        render: (r) => <ActionBadge action={r.action} />,
        sortValue: (r) => r.action,
      },
      { key: 'entity', header: 'Record', width: 170, render: (r) => r.entityType, sortValue: (r) => r.entityType },
      { key: 'actor', header: 'Who', width: 150, render: (r) => r.actorName ?? '—' },
      { key: 'operation', header: 'Operation', width: 180, render: (r) => r.operation ?? '—' },
      { key: 'approver', header: 'Approved by', width: 140, render: (r) => r.approverName ?? '—' },
      {
        key: 'ip',
        header: 'From',
        width: 120,
        render: (r) => <span className="text-ink-muted">{r.ipAddress ?? '—'}</span>,
      },
    ],
    [],
  );

  if (!canRead) {
    return (
      <div className="p-4 lg:p-6">
        <h1>Audit log</h1>
        <p className="mt-1 max-w-[68ch] text-body text-ink-muted">
          Every change, sign-in and refusal, kept where nobody can edit it.
        </p>
        <section className="pos-panel mt-4">
          <div className="flex flex-col items-center gap-1.5 px-4 py-12 text-center">
            <ShieldAlert className="mb-1 h-6 w-6 text-ink-faint" aria-hidden />
            <p className="text-body-lg font-medium text-ink">Your account cannot read the audit log</p>
            <p className="max-w-[52ch] text-body text-ink-muted">
              Reading it needs the audit.read permission. Ask an administrator to grant it on your role.
            </p>
          </div>
        </section>
      </div>
    );
  }

  return (
    <BrowseFormShell
      title="Audit log"
      description="Every change, sign-in and refusal, kept where nobody can edit it. Double-click an entry to see what it changed and what else the same request did."
      toolbar={
        <button type="button" className="pos-button" disabled={loading} onClick={() => void load()}>
          <RotateCcw className="h-5 w-5" aria-hidden />
          {loading ? 'Loading…' : 'Refresh'}
        </button>
      }
      filters={
        <>
          {/*
            The categories, first, because choosing what you are looking at comes before narrowing
            when. Each is several record types — see log-categories.ts — which is what the old
            single free-text box could not express.
          */}
          <div className="flex flex-wrap items-center gap-1" role="tablist" aria-label="Log category">
            {LOG_CATEGORIES.map((entry) => (
              <button
                key={entry.id}
                type="button"
                role="tab"
                aria-selected={category === entry.id}
                title={entry.hint}
                onClick={() => setCategory(entry.id)}
                className={cn(
                  'rounded-sm px-2 py-1 text-label transition-colors',
                  category === entry.id
                    ? 'bg-panel-sunken font-semibold text-ink'
                    : 'text-ink-muted hover:bg-panel-hover hover:text-ink',
                )}
              >
                {entry.label}
              </button>
            ))}
          </div>

          <span className="h-4 w-px bg-subtle" aria-hidden />

          <label className="flex items-center gap-1.5 text-ink-muted">
            From
            <input
              type="date"
              className="pos-input"
              value={from}
              onChange={(event) => setFrom(event.target.value)}
            />
          </label>

          <label className="flex items-center gap-1.5 text-ink-muted">
            To
            <input
              type="date"
              className="pos-input"
              value={to}
              onChange={(event) => setTo(event.target.value)}
            />
          </label>

          {/*
            Kept, and demoted. The free-text box is the only way to ask about one exact table, which
            an investigator following a specific record does want — but it was the *only* control,
            so the ordinary question ("what happened with sales today") required knowing the schema
            and typing it correctly. Typing here overrides the category, because a name somebody
            entered by hand is the more specific request.
          */}
          <input
            className="w-56 pos-input"
            aria-label="Exact record type"
            placeholder={category === 'all' ? 'Exact record type, e.g. Product' : `Overrides ${logCategory(category).label}`}
            value={entityType}
            onChange={(event) => setEntityType(event.target.value)}
          />

          <select
            className="pos-input"
            aria-label="Action"
            value={action}
            onChange={(event) => setAction(event.target.value as AuditAction | '')}
          >
            {ACTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </>
      }
      grid={
        <DataGrid
          gridId="audit-log"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          onRowActivate={(row) => void open(row)}
          emptyMessage={
            loading
              ? 'Loading…'
              : 'Nothing recorded in this window. Widen the dates above, or clear the record type and action filters.'
          }
        />
      }
      form={selected ? <AuditDetailPanel row={selected} trail={trail} onClose={() => setSelected(null)} /> : null}
      status={
        <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
          <span className="tabular-nums">
            {rows.length} of {totalCount} entries
          </span>
          <span aria-hidden>·</span>
          <span>Double-click one to see what changed</span>
        </span>
      }
    />
  );
}

function ActionBadge({ action }: { action: AuditAction }) {
  const style = ACTION_STYLE[action] ?? { icon: FilePenLine, tone: 'text-ink-muted', label: action };
  const Icon = style.icon;

  return (
    <span className={cn('pos-badge', style.tone)}>
      <Icon className="h-4 w-4 shrink-0" aria-hidden />
      <span className="truncate">{style.label}</span>
    </span>
  );
}

function isoDate(offsetDays: number): string {
  const date = new Date();
  date.setDate(date.getDate() + offsetDays);
  return date.toISOString().slice(0, 10);
}

function AuditDetailPanel({ row, trail, onClose }: { row: AuditLogRow; trail: AuditLogRow[]; onClose: () => void }) {
  return (
    <div>
      <div className="mb-2 flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-body-lg font-semibold text-ink">{row.entityType}</h2>
          <div className="mt-1">
            <ActionBadge action={row.action} />
          </div>
        </div>
        <button type="button" className="pos-button shrink-0" onClick={onClose}>
          <X className="h-5 w-5" aria-hidden />
          Close
        </button>
      </div>

      <FormSection title="Entry">
        <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1.5 text-body">
          <dt className="text-ink-muted">When</dt>
          <dd className="tabular-nums">{new Date(row.occurredAt).toLocaleString()}</dd>
          <dt className="text-ink-muted">Who</dt>
          <dd>{row.actorName ?? 'Unknown'}</dd>
          <dt className="text-ink-muted">Record</dt>
          <dd>
            {row.entityType} {row.entityId ? <span className="pos-amount">{row.entityId}</span> : null}
          </dd>
          <dt className="text-ink-muted">Operation</dt>
          <dd>{row.operation ?? '—'}</dd>
          <dt className="text-ink-muted">From</dt>
          <dd className="tabular-nums">{row.ipAddress ?? '—'}</dd>
          {row.approverName ? (
            <>
              <dt className="text-ink-muted">Approved by</dt>
              <dd>{row.approverName}</dd>
            </>
          ) : null}
          {row.reason ? (
            <>
              <dt className="text-ink-muted">Reason</dt>
              <dd>{row.reason}</dd>
            </>
          ) : null}
        </dl>
      </FormSection>

      {row.beforeJson || row.afterJson ? (
        <FormSection title="What changed" hint="Values as they were stored, before and after this entry.">
          <JsonDiff label="Before" json={row.beforeJson} />
          <JsonDiff label="After" json={row.afterJson} />
        </FormSection>
      ) : null}

      {trail.length > 1 ? (
        <FormSection
          title={`The whole request (${trail.length} entries)`}
          hint="Everything the same request did — a void and the approval that authorised it are one story."
        >
          <ol className="divide-y divide-subtle rounded border border-subtle">
            {trail.map((entry) => (
              <li
                key={String(entry.id)}
                className={cn(
                  'flex flex-wrap items-center gap-2 px-2.5 py-1.5 text-body',
                  entry.id === row.id && 'bg-accent-soft font-medium',
                )}
              >
                <ActionBadge action={entry.action} />
                <span className="min-w-0 truncate">
                  {entry.entityType}
                  {entry.operation ? ` — ${entry.operation}` : ''}
                </span>
                {entry.id === row.id ? (
                  <span className="ml-auto text-caption text-ink-muted">this entry</span>
                ) : null}
              </li>
            ))}
          </ol>
        </FormSection>
      ) : null}
    </div>
  );
}

function JsonDiff({ label, json }: { label: string; json: string | null }) {
  if (!json) return null;

  let pretty = json;

  try {
    pretty = JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    // Show it raw rather than not at all.
  }

  return (
    <div>
      <p className="mb-1 text-label font-medium text-ink-muted">{label}</p>
      <pre className="max-h-48 overflow-auto rounded-sm border border-subtle bg-panel-sunken p-2.5 text-caption leading-snug">
        {pretty}
      </pre>
    </div>
  );
}
