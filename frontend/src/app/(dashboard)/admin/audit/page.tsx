'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { BrowseFormShell, FormSection } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi, type AuditFilters } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { cn } from '@/lib/utils';
import type { AuditAction, AuditLogRow } from '@/types/masters';

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
      action: action || undefined,
      take: 200,
    }),
    [from, to, entityType, action],
  );

  const load = useCallback(async () => {
    if (!canRead) return;

    setLoading(true);

    try {
      const page = await mastersApi.audit.list(filters);
      setRows(page.rows);
      setTotalCount(page.totalCount);
    } catch (error) {
      toast({ title: 'Could not load the audit log', description: describe(error), variant: 'destructive' });
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
        render: (r) => new Date(r.occurredAt).toLocaleString(),
        sortValue: (r) => r.occurredAt,
      },
      {
        key: 'action',
        header: 'Action',
        width: 130,
        render: (r) => (
          <span className={cn(isRefusal(r.action) && 'text-[rgb(var(--warning))]', r.action === 'Deleted' && 'text-[rgb(var(--negative))]')}>
            {r.action}
          </span>
        ),
      },
      { key: 'entity', header: 'Record', width: 170, render: (r) => r.entityType, sortValue: (r) => r.entityType },
      { key: 'actor', header: 'Who', width: 150, render: (r) => r.actorName ?? '—' },
      { key: 'operation', header: 'Operation', width: 180, render: (r) => r.operation ?? '—' },
      { key: 'approver', header: 'Approved by', width: 140, render: (r) => r.approverName ?? '—' },
      { key: 'ip', header: 'From', width: 120, render: (r) => r.ipAddress ?? '—' },
    ],
    [],
  );

  if (!canRead) {
    return <p className="text-sm text-[rgb(var(--text-muted))]">Your account cannot read the audit log.</p>;
  }

  return (
    <BrowseFormShell
      title="Audit log"
      filters={
        <>
          <label className="flex items-center gap-1.5">
            From
            <input
              type="date"
              className="rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1"
              value={from}
              onChange={(event) => setFrom(event.target.value)}
            />
          </label>

          <label className="flex items-center gap-1.5">
            To
            <input
              type="date"
              className="rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1"
              value={to}
              onChange={(event) => setTo(event.target.value)}
            />
          </label>

          <input
            className="w-48 rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1"
            placeholder="Record type, e.g. Product"
            value={entityType}
            onChange={(event) => setEntityType(event.target.value)}
          />

          <select
            className="rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1"
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
          emptyMessage={loading ? 'Loading…' : 'Nothing recorded in this window.'}
        />
      }
      form={selected ? <AuditDetailPanel row={selected} trail={trail} onClose={() => setSelected(null)} /> : null}
      status={
        <span>
          {rows.length} of {totalCount} entries · double-click one to see what changed
        </span>
      }
    />
  );
}

function isoDate(offsetDays: number): string {
  const date = new Date();
  date.setDate(date.getDate() + offsetDays);
  return date.toISOString().slice(0, 10);
}

function describe(error: unknown): string {
  return error instanceof PosApiError ? error.problem.detail : 'Something went wrong.';
}

function isRefusal(action: AuditAction): boolean {
  return action === 'PermissionDenied' || action === 'SignInFailed' || action === 'StepUpDenied';
}

function AuditDetailPanel({ row, trail, onClose }: { row: AuditLogRow; trail: AuditLogRow[]; onClose: () => void }) {
  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-sm font-semibold">
          {row.action} — {row.entityType}
        </h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Close
        </button>
      </div>

      <FormSection title="Entry">
        <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-xs">
          <dt className="text-[rgb(var(--text-muted))]">When</dt>
          <dd>{new Date(row.occurredAt).toLocaleString()}</dd>
          <dt className="text-[rgb(var(--text-muted))]">Who</dt>
          <dd>{row.actorName ?? 'Unknown'}</dd>
          <dt className="text-[rgb(var(--text-muted))]">Record</dt>
          <dd>
            {row.entityType} {row.entityId ? <span className="pos-amount">{row.entityId.slice(0, 8)}…</span> : null}
          </dd>
          <dt className="text-[rgb(var(--text-muted))]">Operation</dt>
          <dd>{row.operation ?? '—'}</dd>
          {row.approverName ? (
            <>
              <dt className="text-[rgb(var(--text-muted))]">Approved by</dt>
              <dd>{row.approverName}</dd>
            </>
          ) : null}
          {row.reason ? (
            <>
              <dt className="text-[rgb(var(--text-muted))]">Reason</dt>
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
          <ol className="space-y-0.5 text-xs">
            {trail.map((entry) => (
              <li key={entry.id} className={cn(entry.id === row.id && 'font-medium')}>
                {entry.action} {entry.entityType}
                {entry.operation ? ` — ${entry.operation}` : ''}
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
      <p className="mb-0.5 text-xs text-[rgb(var(--text-muted))]">{label}</p>
      <pre className="max-h-48 overflow-auto rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--surface))] p-2 text-[11px] leading-snug">
        {pretty}
      </pre>
    </div>
  );
}
