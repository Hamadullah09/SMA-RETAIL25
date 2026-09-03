'use client';

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  ClipboardList,
  Download,
  History,
  Link2,
  MapPin,
  RotateCcw,
  Send,
  X,
  type LucideIcon,
} from 'lucide-react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { filterInputClass, isoDate } from '@/components/reports/report-shell';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { cn } from '@/lib/utils';
import type { ExternalMapRow, PreflightReport, SyncEntityName, SyncLogDetail, SyncLogRow } from '@/types/masters';
import { PageHeader as SharedPageHeader } from '@/components/shell/page-header';
import { describeError } from '@/lib/errors';
import { EmptyState } from '@/components/ui/states';

const PUSHABLE: { entity: SyncEntityName; label: string; needsDate?: boolean }[] = [
  { entity: 'Customers', label: 'Customers' },
  { entity: 'Items', label: 'Items' },
  { entity: 'Vendors', label: 'Suppliers' },
  { entity: 'Invoices', label: 'Open invoices' },
  { entity: 'PosRevenue', label: "A day's takings", needsDate: true },
];


/**
 * The accounting link (doc 09 §1).
 *
 * The legacy integration failed silently and its manual has a whole troubleshooting chapter because
 * of it (guide p.109–111). So this screen leads with what is not yet mapped, and every attempt keeps
 * its request and response where someone can read them.
 */
export default function AccountingSyncPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;

  const [preflight, setPreflight] = useState<PreflightReport | null>(null);
  const [logRows, setLogRows] = useState<SyncLogRow[]>([]);
  const [maps, setMaps] = useState<ExternalMapRow[]>([]);
  const [selected, setSelected] = useState<SyncLogDetail | null>(null);
  const [businessDate, setBusinessDate] = useState(() => isoDate(-1));
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (!locationId) return;

    try {
      const [report, log, mappings] = await Promise.all([
        mastersApi.accounting.preflight(locationId),
        mastersApi.accounting.log({ take: 200 }),
        mastersApi.accounting.mappings(),
      ]);

      setPreflight(report);
      setLogRows(log.rows);
      setMaps(mappings);
    } catch (error) {
      toast({ title: 'Could not load the accounting link', description: describeError(error), variant: 'destructive' });
    }
  }, [locationId]);

  useEffect(() => {
    void load();
  }, [load]);

  const run = async (entity: SyncEntityName, needsDate?: boolean) => {
    if (!locationId) return;

    setBusy(true);

    try {
      const result = await mastersApi.accounting.run(entity, locationId, needsDate ? { businessDate } : {});

      toast(
        result.success
          ? { title: `${entity} posted`, description: `${result.recordCount} record(s) written.` }
          : { title: `${entity} did not post`, description: result.error ?? 'Unknown failure.', variant: 'destructive' },
      );

      await load();
    } catch (error) {
      toast({ title: 'The sync failed', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const openLog = async (row: SyncLogRow) => {
    try {
      setSelected(await mastersApi.accounting.logDetail(row.id));
    } catch (error) {
      toast({ title: 'Could not open the attempt', description: describeError(error), variant: 'destructive' });
    }
  };

  const logColumns: DataGridColumn<SyncLogRow>[] = [
    {
      key: 'when',
      header: 'When',
      width: 170,
      render: (r) => new Date(r.occurredAt).toLocaleString(),
      sortValue: (r) => r.occurredAt,
    },
    { key: 'entity', header: 'What', width: 130, render: (r) => r.entity },
    { key: 'direction', header: 'Way', width: 70, render: (r) => r.direction },
    {
      key: 'status',
      header: 'Result',
      width: 110,
      // Shape and word, not colour alone.
      render: (r) => (r.status === 'Success' ? '✓ Posted' : '✕ Failed'),
      sortValue: (r) => r.status,
    },
    { key: 'records', header: 'Records', width: 90, numeric: true, render: (r) => r.recordCount },
    { key: 'ms', header: 'Took', width: 90, numeric: true, render: (r) => `${r.durationMs} ms` },
    { key: 'error', header: 'Detail', width: 320, render: (r) => r.errorMessage ?? '—' },
  ];

  if (!locationId) {
    return (
      <div className="p-4 lg:p-6">
        <PageHeader title="Accounting" lede="The link that hands the shop's figures to the bookkeeper." />
        <section className="pos-panel mt-4">
          <EmptyState
            icon={Link2}
            title="No location is attached to this session"
            description="The accounting link posts one location's figures at a time. Sign in against a location, or ask an administrator to attach one to your account."
          />
        </section>
      </div>
    );
  }

  const outstanding = preflight ? preflight.checks.filter((check) => !check.satisfied).length : null;

  return (
    <div className="space-y-4 p-4 lg:p-6">
      <PageHeader
        title="Accounting"
        lede="Hands the shop's figures to the bookkeeper's system. Everything it sends is kept here with what came back, because the link it replaces failed silently."
      >
        <button type="button" className="pos-button" disabled={busy} onClick={() => void load()}>
          <RotateCcw className="h-5 w-5" aria-hidden />
          Refresh
        </button>
      </PageHeader>

      <Panel
        title="Before the first sync"
        icon={ClipboardList}
        action={
          preflight ? (
            outstanding === 0 ? (
              <span className="pos-badge text-positive-text">
                <CheckCircle2 className="h-4 w-4" aria-hidden />
                All ready
              </span>
            ) : (
              <span className="pos-badge text-warning-text">
                <AlertTriangle className="h-4 w-4" aria-hidden />
                {outstanding} need attention
              </span>
            )
          ) : (
            'Checking…'
          )
        }
      >
        <p className="border-b border-subtle px-4 py-2.5 text-body text-ink-muted">
          These are the four things the legacy link failed on silently. Nothing here blocks selling — an
          unmapped account only means the file needs a hand before it is imported.
        </p>

        {preflight ? (
          <ul>
            {preflight.checks.map((check) => (
              <li
                key={check.requirement}
                className="flex items-start gap-2.5 border-b border-subtle px-4 py-3 last:border-0"
              >
                {check.satisfied ? (
                  <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-positive" aria-hidden />
                ) : (
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-warning" aria-hidden />
                )}
                <div className="min-w-0 flex-1">
                  <p className="text-body font-medium text-ink">{check.requirement}</p>
                  <p className="mt-0.5 text-body text-ink-muted">{check.detail}</p>
                </div>
                <span
                  className={cn('pos-badge shrink-0', check.satisfied ? 'text-positive-text' : 'text-warning-text')}
                >
                  {check.satisfied ? 'Ready' : 'Not yet'}
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="px-4 py-8 text-center text-body text-ink-muted">Checking…</p>
        )}
      </Panel>

      <Panel title="Post now" icon={Send}>
        <p className="border-b border-subtle px-4 py-2.5 text-body text-ink-muted">
          Writes a file the bookkeeper can import. The day&apos;s takings also post on their own each night.
        </p>

        <div className="space-y-3 p-4">
          <label className="flex w-fit flex-col gap-1 text-label text-ink-muted">
            Business date
            <input
              type="date"
              className={filterInputClass}
              value={businessDate}
              onChange={(e) => setBusinessDate(e.target.value)}
            />
          </label>

          <ul className="divide-y divide-subtle rounded border border-subtle">
            {PUSHABLE.map((item) => (
              <li key={item.entity} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2">
                <span className="min-w-0 text-body text-ink">
                  {item.label}
                  {item.needsDate ? (
                    <span className="ml-1.5 text-label text-ink-muted">for {businessDate}</span>
                  ) : null}
                </span>

                <span className="flex shrink-0 items-center gap-2">
                  <a
                    className="pos-button"
                    title={`Download ${item.label} as CSV`}
                    href={mastersApi.accounting.exportUrl(
                      item.entity,
                      locationId,
                      item.needsDate ? { businessDate } : {},
                    )}
                    download
                  >
                    <Download className="h-5 w-5" aria-hidden />
                    CSV
                  </a>

                  <button
                    type="button"
                    className="pos-button"
                    disabled={busy}
                    onClick={() => void run(item.entity, item.needsDate)}
                  >
                    <Send className="h-5 w-5" aria-hidden />
                    Post
                  </button>
                </span>
              </li>
            ))}
          </ul>
        </div>
      </Panel>

      <Panel title="Mappings" icon={MapPin} action={`${maps.length} mapped`}>
        <p className="border-b border-subtle px-4 py-2.5 text-body text-ink-muted">
          What each local record is called on the other side. Mapping by identity rather than by name is
          why a rename stays a rename here.
        </p>

        {maps.length === 0 ? (
          <EmptyState
            icon={MapPin}
            title="Nothing mapped yet"
            description="A mapping is written the first time a record is posted successfully. Post customers or items above and they will appear here."
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="pos-table">
              <thead className="border-b border-subtle bg-panel-sunken">
                <tr>
                  <th scope="col" >Type</th>
                  <th scope="col" >Local</th>
                  <th scope="col" >Remote</th>
                </tr>
              </thead>
              <tbody>
                {maps.map((map) => (
                  <tr
                    key={String(map.id)}
                    className="border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover"
                  >
                    <td>{map.entityType}</td>
                    <td className={'pos-amount'}>{map.localKey ?? map.localId ?? '—'}</td>
                    <td className={'text-ink-muted'}>{map.remoteName ?? map.remoteId}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>

      <Panel title="Attempts" icon={History} action="Double-click an attempt to see what was sent">
        <p className="border-b border-subtle px-4 py-2.5 text-body text-ink-muted">
          Every push, with what was sent and what came back — the modern version of the legacy Last
          Request / Last Response.
        </p>

        <div className="h-80 p-4">
          <DataGrid
            gridId="sync-log"
            rows={logRows}
            columns={logColumns}
            rowKey={(row) => row.id}
            onRowActivate={(row) => void openLog(row)}
            emptyMessage="Nothing has been posted yet. Use “Post now” above and the attempt lands here, whether it worked or not."
          />
        </div>
      </Panel>

      {selected ? (
        <Panel
          title={`${selected.entity} — ${new Date(selected.occurredAt).toLocaleString()}`}
          icon={History}
          action={
            <button type="button" className="pos-button" onClick={() => setSelected(null)}>
              <X className="h-5 w-5" aria-hidden />
              Close
            </button>
          }
        >
          <div className="space-y-3 p-4">
            <div>
              <p className="mb-1 text-label font-medium text-ink-muted">Sent</p>
              <pre className="max-h-40 overflow-auto rounded-sm border border-subtle bg-panel-sunken p-2.5 text-caption leading-snug">
                {selected.requestPayload ?? '—'}
              </pre>
            </div>

            <div>
              <p className="mb-1 text-label font-medium text-ink-muted">Returned</p>
              <pre
                className={cn(
                  'max-h-64 overflow-auto rounded-sm border border-subtle bg-panel-sunken p-2.5 text-caption leading-snug',
                  selected.errorMessage && 'border-negative/40 text-negative',
                )}
              >
                {selected.errorMessage ?? selected.responsePayload ?? '—'}
              </pre>
            </div>
          </div>
        </Panel>
      ) : null}
    </div>
  );
}

/* ------------------------------------------------------------------ page furniture */

/**
 * Delegates to the shared header.
 *
 * This was copy-pasted verbatim into six admin screens, and had already drifted: year-end
 * aligned its actions to the bottom while the other five centred them. Kept behind the local
 * name so the call sites in this file do not change.
 */
function PageHeader({ title, lede, children }: { title: string; lede: string; children?: ReactNode }) {
  return <SharedPageHeader title={title} description={lede} actions={children} />;
}

function Panel({
  title,
  icon: Icon,
  action,
  children,
}: {
  title: string;
  icon?: LucideIcon;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="pos-panel overflow-hidden">
      <header className="pos-panel-header">
        <span className="pos-panel-title">
          {Icon ? <Icon /> : null}
          <span className="truncate">{title}</span>
        </span>
        {action ? <span className="pos-panel-header-action">{action}</span> : null}
      </header>
      {children}
    </section>
  );
}
