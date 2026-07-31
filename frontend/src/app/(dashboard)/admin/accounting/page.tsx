'use client';

import { useCallback, useEffect, useState } from 'react';
import { AlertTriangle, CheckCircle2, Download } from 'lucide-react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { FormSection } from '@/components/masters/browse-form';
import { filterInputClass, isoDate } from '@/components/reports/report-shell';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import type { ExternalMapRow, PreflightReport, SyncEntityName, SyncLogDetail, SyncLogRow } from '@/types/masters';

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
      toast({ title: 'Could not load the accounting link', description: describe(error), variant: 'destructive' });
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
      toast({ title: 'The sync failed', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const openLog = async (row: SyncLogRow) => {
    try {
      setSelected(await mastersApi.accounting.logDetail(row.id));
    } catch (error) {
      toast({ title: 'Could not open the attempt', description: describe(error), variant: 'destructive' });
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
    return <p className="text-sm text-[rgb(var(--text-muted))]">No location is attached to this session.</p>;
  }

  return (
    <div className="space-y-4">
      <h1 className="text-lg font-semibold">Accounting</h1>

      <FormSection
        title="Before the first sync"
        hint="These are the four things the legacy link failed on silently. Nothing here blocks selling — an unmapped account only means the file needs a hand before it is imported."
      >
        {preflight ? (
          <ul className="space-y-1.5">
            {preflight.checks.map((check) => (
              <li key={check.requirement} className="flex items-start gap-2 text-xs">
                {check.satisfied ? (
                  <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 shrink-0" style={{ color: 'rgb(var(--positive))' }} />
                ) : (
                  <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" style={{ color: 'rgb(var(--warning))' }} />
                )}
                <span>
                  <span className="font-medium">{check.requirement}</span>
                  <span className="block text-[rgb(var(--text-muted))]">{check.detail}</span>
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-xs text-[rgb(var(--text-muted))]">Checking…</p>
        )}
      </FormSection>

      <FormSection
        title="Post now"
        hint="Writes a file the bookkeeper can import. The day's takings also post on their own each night."
      >
        <label className="mb-2 flex items-center gap-1.5 text-sm">
          Business date
          <input
            type="date"
            className={filterInputClass}
            value={businessDate}
            onChange={(e) => setBusinessDate(e.target.value)}
          />
        </label>

        <div className="flex flex-wrap gap-2">
          {PUSHABLE.map((item) => (
            <span key={item.entity} className="flex items-center gap-1">
              <button
                type="button"
                className="pos-button"
                disabled={busy}
                onClick={() => void run(item.entity, item.needsDate)}
              >
                {item.label}
              </button>
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
                <Download className="h-3.5 w-3.5" />
              </a>
            </span>
          ))}
        </div>
      </FormSection>

      <FormSection
        title="Mappings"
        hint="What each local record is called on the other side. Mapping by identity rather than by name is why a rename stays a rename here."
      >
        {maps.length === 0 ? (
          <p className="text-xs text-[rgb(var(--text-muted))]">Nothing mapped yet.</p>
        ) : (
          <table className="w-full text-xs">
            <thead>
              <tr className="text-left text-[rgb(var(--text-muted))]">
                <th className="pb-1">Type</th>
                <th className="pb-1">Local</th>
                <th className="pb-1">Remote</th>
              </tr>
            </thead>
            <tbody>
              {maps.map((map) => (
                <tr key={map.id}>
                  <td className="py-0.5">{map.entityType}</td>
                  <td className="py-0.5">{map.localKey ?? map.localId ?? '—'}</td>
                  <td className="py-0.5">{map.remoteName ?? map.remoteId}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </FormSection>

      <FormSection
        title="Attempts"
        hint="Every push, with what was sent and what came back — the modern version of the legacy Last Request / Last Response."
      >
        <div className="h-72">
          <DataGrid
            gridId="sync-log"
            rows={logRows}
            columns={logColumns}
            rowKey={(row) => row.id}
            onRowActivate={(row) => void openLog(row)}
            emptyMessage="Nothing has been posted yet."
          />
        </div>
        <p className="mt-1 text-xs text-[rgb(var(--text-muted))]">Double-click an attempt to see what was sent.</p>
      </FormSection>

      {selected ? (
        <FormSection title={`${selected.entity} — ${new Date(selected.occurredAt).toLocaleString()}`}>
          <button type="button" className="pos-button mb-2" onClick={() => setSelected(null)}>
            Close
          </button>

          <p className="mb-1 text-xs font-medium">Sent</p>
          <pre className="mb-3 max-h-32 overflow-auto rounded-[var(--radius-dense)] border border-[rgb(var(--border))] p-2 text-[11px]">
            {selected.requestPayload ?? '—'}
          </pre>

          <p className="mb-1 text-xs font-medium">Returned</p>
          <pre className="max-h-64 overflow-auto rounded-[var(--radius-dense)] border border-[rgb(var(--border))] p-2 text-[11px]">
            {selected.errorMessage ?? selected.responsePayload ?? '—'}
          </pre>
        </FormSection>
      ) : null}
    </div>
  );
}

function describe(error: unknown): string {
  return error instanceof PosApiError ? error.problem.detail : 'Something went wrong.';
}
