'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { formatCurrency } from '@/lib/utils';
import type {
  AnalysisReport,
  LegacyControlTotals,
  LegacySourceKind,
  MigrationBatch,
  MigrationStage,
  ReconciliationReport,
  StagingRow,
  ValidationFinding,
} from '@/types/masters';

const inputClass =
  'rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1 text-sm';

/** What each stage means in the operator's words, not the enum's. */
const stageLabel: Record<MigrationStage, string> = {
  Staged: 'Read in',
  Validated: 'Checked',
  DryRun: 'Dry run done',
  Imported: 'Imported',
  Cancelled: 'Cancelled',
};

/**
 * The legacy migration screen (doc 09 §3).
 *
 * The pipeline is deliberately visible rather than a single "import" button: read in → check →
 * dry run → import, with a report at every step. A cutover is the one operation where the operator
 * has to be able to see what is about to happen before it does.
 */
export default function MigrationPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canRun = auth.can('migration.run');

  const [kinds, setKinds] = useState<LegacySourceKind[]>([]);
  const [entity, setEntity] = useState('Inventory');
  const [batches, setBatches] = useState<MigrationBatch[]>([]);
  const [selected, setSelected] = useState<MigrationBatch | null>(null);
  const [busy, setBusy] = useState(false);

  const fileRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    if (!locationId) return;

    try {
      setBatches(await mastersApi.migration.batches(locationId));
    } catch (error) {
      toast({ title: 'Could not load the batches', description: describe(error), variant: 'destructive' });
    }
  }, [locationId]);

  useEffect(() => {
    void mastersApi.migration.kinds().then(setKinds).catch(() => setKinds([]));
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  if (!canRun) {
    return (
      <div className="p-6">
        <h1 className="text-lg font-semibold">Bring data across</h1>
        <p className="mt-2 text-sm text-[rgb(var(--text-muted))]">
          You do not have permission to run a migration.
        </p>
      </div>
    );
  }

  const upload = async (file: File) => {
    if (!locationId) return;
    setBusy(true);

    try {
      // Base64 because a DBF is binary and a text field would mangle it.
      const base64 = await toBase64(file);
      const batch = await mastersApi.migration.stage(locationId, file.name, entity, base64);

      setSelected(batch);
      await load();
      toast({ title: `${batch.rowsStaged} row(s) read in` });
    } catch (error) {
      toast({ title: 'Could not read that file', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);

      if (fileRef.current) {
        fileRef.current.value = '';
      }
    }
  };

  const selectedKind = kinds.find((k) => k.entity === entity);

  return (
    <div className="space-y-4 p-6">
      <header>
        <h1 className="text-lg font-semibold">Bring data across</h1>
        <p className="text-sm text-[rgb(var(--text-muted))]">
          Read a file from the old system, check it, run it as a rehearsal, then import. Nothing is written
          until the import step, and the import will not start without a dry run that passed.
        </p>
      </header>

      <section className="pos-panel">
        <div className="pos-panel-header">
          <span>Read a file</span>
        </div>
        <div className="space-y-2 p-3">
          <div className="flex flex-wrap items-end gap-3">
            <label className="flex flex-col gap-1 text-xs">
              What kind of file
              <select className={inputClass} value={entity} onChange={(event) => setEntity(event.target.value)}>
                {kinds.map((kind) => (
                  <option key={kind.entity} value={kind.entity}>
                    {kind.displayName}
                  </option>
                ))}
              </select>
            </label>

            <label className="flex flex-col gap-1 text-xs">
              File
              <input
                ref={fileRef}
                type="file"
                className={inputClass}
                disabled={busy}
                onChange={(event) => {
                  const file = event.target.files?.[0];
                  if (file) void upload(file);
                }}
              />
            </label>
          </div>

          {selectedKind ? (
            <p className="text-xs text-[rgb(var(--text-muted))]">
              Expected column order ({selectedKind.guideReference}): {selectedKind.columns.join(', ')}. These files
              have no header row, so the order is what identifies the columns.
            </p>
          ) : null}
        </div>
      </section>

      <section className="pos-panel">
        <div className="pos-panel-header">
          <span>Files read in</span>
        </div>
        <div className="p-3">
          <table className="w-full text-sm">
            <thead className="text-xs">
              <tr>
                <th className="py-1 text-left">File</th>
                <th className="py-1 text-left">Kind</th>
                <th className="py-1 text-left">Where it is up to</th>
                <th className="py-1 text-right">Rows</th>
                <th className="py-1 text-right">Problems</th>
                <th className="py-1 text-right">Imported</th>
              </tr>
            </thead>
            <tbody>
              {batches.map((batch) => (
                <tr
                  key={batch.id}
                  className={`cursor-pointer border-t border-[rgb(var(--border))] ${
                    selected?.id === batch.id ? 'font-medium' : ''
                  }`}
                  onClick={() => setSelected(batch)}
                >
                  <td className="py-1">{batch.sourceFileName}</td>
                  <td className="py-1">{batch.entity}</td>
                  <td className="py-1">{stageLabel[batch.stage]}</td>
                  <td className="py-1 text-right">{batch.rowsStaged}</td>
                  <td className="py-1 text-right">
                    {batch.blockingErrors > 0 ? `${batch.blockingErrors} to fix` : batch.warnings > 0 ? `${batch.warnings} noted` : '—'}
                  </td>
                  <td className="py-1 text-right">{batch.stage === 'Imported' ? batch.rowsImported : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {batches.length === 0 ? (
            <p className="text-xs text-[rgb(var(--text-muted))]">Nothing read in yet.</p>
          ) : (
            <p className="mt-2 text-xs text-[rgb(var(--text-muted))]">Click a file to work on it.</p>
          )}
        </div>
      </section>

      {selected ? (
        <BatchPanel
          key={selected.id}
          batch={selected}
          onChanged={async (updated) => {
            setSelected(updated);
            await load();
          }}
        />
      ) : null}
    </div>
  );
}

function describe(error: unknown): string {
  return error instanceof PosApiError ? error.problem.detail : 'Something went wrong.';
}

/** Reads a file as base64 without the data-URL prefix. */
function toBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(',')[1] ?? '');
    reader.onerror = () => reject(new Error('The file could not be read.'));
    reader.readAsDataURL(file);
  });
}

function BatchPanel({
  batch,
  onChanged,
}: {
  batch: MigrationBatch;
  onChanged: (updated: MigrationBatch) => Promise<void>;
}) {
  const [analysis, setAnalysis] = useState<AnalysisReport | null>(null);
  const [findings, setFindings] = useState<ValidationFinding[]>([]);
  const [rows, setRows] = useState<StagingRow[]>([]);
  const [reconciliation, setReconciliation] = useState<ReconciliationReport | null>(null);
  const [totals, setTotals] = useState<LegacyControlTotals>({});
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void mastersApi.migration.analysis(batch.id).then(setAnalysis).catch(() => setAnalysis(null));
  }, [batch.id]);

  useEffect(() => {
    if (batch.stage === 'Staged') return;

    void mastersApi.migration.validation(batch.id).then(setFindings).catch(() => setFindings([]));
    void mastersApi.migration.rows(batch.id).then(setRows).catch(() => setRows([]));
  }, [batch.id, batch.stage, batch.validatedAt]);

  useEffect(() => {
    if (batch.stage !== 'DryRun' && batch.stage !== 'Imported') {
      setReconciliation(null);
      return;
    }

    void mastersApi.migration.reconciliation(batch.id).then(setReconciliation).catch(() => setReconciliation(null));
  }, [batch.id, batch.stage, batch.dryRunAt, batch.importedAt]);

  const run = async (action: () => Promise<unknown>, success: string) => {
    setBusy(true);

    try {
      await action();
      await onChanged(await mastersApi.migration.batch(batch.id));
      toast({ title: success });
    } catch (error) {
      toast({ title: 'Not done', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const doImport = async () => {
    const confirmed = window.confirm(
      `Import ${batch.rowsStaged} row(s) from ${batch.sourceFileName} into the live system? `
      + 'This writes to the catalogue and the stock ledger.',
    );

    if (!confirmed) return;

    await run(() => mastersApi.migration.import(batch.id, totals), 'Imported');
  };

  const number = (value: number | null | undefined) => (value === null || value === undefined ? '' : String(value));

  return (
    <>
      <section className="pos-panel">
        <div className="pos-panel-header">
          <span>{batch.sourceFileName}</span>
          <span className="normal-case">{stageLabel[batch.stage]}</span>
        </div>
        <div className="space-y-3 p-3">
          {analysis ? (
            <div className="space-y-1 text-sm">
              <p>
                Read as <span className="font-medium">{analysis.format}</span>, treated as{' '}
                <span className="font-medium">{analysis.detectedLayout}</span> ({analysis.guideReference}).
              </p>
              <p className="text-xs text-[rgb(var(--text-muted))]">
                {analysis.rowCount} row(s), {analysis.columnCount} column(s)
                {analysis.deletedRowCount > 0
                  ? `, ${analysis.deletedRowCount} of which the old system had already deleted and which will not be imported`
                  : ''}
                .
              </p>

              {analysis.notes.length > 0 ? (
                <ul className="text-xs text-[rgb(var(--warning))]">
                  {analysis.notes.map((note) => (
                    <li key={note}>⚠ {note}</li>
                  ))}
                </ul>
              ) : null}
            </div>
          ) : null}

          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              className="pos-button"
              disabled={busy || batch.stage === 'Imported' || batch.stage === 'Cancelled'}
              onClick={() => void run(() => mastersApi.migration.validate(batch.id), 'Checked')}
            >
              Check it
            </button>

            <button
              type="button"
              className="pos-button"
              disabled={busy || batch.stage === 'Staged' || batch.stage === 'Imported' || batch.stage === 'Cancelled'}
              onClick={() => void run(() => mastersApi.migration.dryRun(batch.id, totals), 'Dry run done')}
            >
              Dry run
            </button>

            <button
              type="button"
              className="pos-button-primary"
              disabled={busy || !batch.canImport}
              onClick={() => void doImport()}
            >
              Import
            </button>

            <button
              type="button"
              className="pos-button text-[rgb(var(--negative))]"
              disabled={busy || batch.stage === 'Imported' || batch.stage === 'Cancelled'}
              onClick={() => void run(() => mastersApi.migration.cancel(batch.id), 'Cancelled')}
            >
              Discard
            </button>
          </div>

          {!batch.canImport && batch.stage !== 'Imported' ? (
            <p className="text-xs text-[rgb(var(--text-muted))]">
              {batch.blockingErrors > 0
                ? `${batch.blockingErrors} problem(s) have to be fixed in the source file first.`
                : 'Import stays disabled until a dry run has been done for this file.'}
            </p>
          ) : null}
        </div>
      </section>

      {batch.stage !== 'Staged' && findings.length > 0 ? (
        <section className="pos-panel">
          <div className="pos-panel-header">
            <span>What the check found</span>
            <span className="normal-case">
              {batch.blockingErrors} to fix · {batch.warnings} noted
            </span>
          </div>
          <div className="max-h-72 overflow-y-auto p-3">
            <table className="w-full text-sm">
              <thead className="sticky top-0 bg-[rgb(var(--panel))] text-xs">
                <tr>
                  <th className="py-1 text-left">Row</th>
                  <th className="py-1 text-left">Column</th>
                  <th className="py-1 text-left">Severity</th>
                  <th className="py-1 text-left">What is wrong</th>
                </tr>
              </thead>
              <tbody>
                {findings.slice(0, 300).map((finding, index) => (
                  <tr key={`${finding.rowNumber}-${finding.code}-${index}`} className="border-t border-[rgb(var(--border))]">
                    <td className="py-1">{finding.rowNumber}</td>
                    <td className="py-1">{finding.column ?? '—'}</td>
                    {/* The word, not a colour — a blocking error has to read as one. */}
                    <td className="py-1 font-medium">
                      {finding.severity === 'Blocking' ? 'Must fix' : 'Noted'}
                    </td>
                    <td className="py-1">{finding.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            {findings.length > 300 ? (
              <p className="mt-2 text-xs text-[rgb(var(--text-muted))]">
                Showing the first 300 of {findings.length}.
              </p>
            ) : null}
          </div>
        </section>
      ) : null}

      {rows.length > 0 ? (
        <section className="pos-panel">
          <div className="pos-panel-header">
            <span>Rows that need looking at</span>
          </div>
          <div className="max-h-72 overflow-y-auto p-3">
            <table className="w-full text-sm">
              <thead className="sticky top-0 bg-[rgb(var(--panel))] text-xs">
                <tr>
                  <th className="py-1 text-left">Row</th>
                  <th className="py-1 text-left">Key</th>
                  <th className="py-1 text-left">Problems</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.rowNumber} className="border-t border-[rgb(var(--border))]">
                    <td className="py-1">{row.rowNumber}</td>
                    <td className="py-1">{row.legacyKey ?? '—'}</td>
                    <td className="py-1 whitespace-pre-line">{row.problems}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ) : null}

      <section className="pos-panel">
        <div className="pos-panel-header">
          <span>What the old system said</span>
        </div>
        <div className="space-y-2 p-3">
          <p className="text-xs text-[rgb(var(--text-muted))]">
            Type these off the old system&apos;s own reports before the dry run. Anything left blank is reported
            as imported-only rather than counted as agreeing.
          </p>
          <div className="flex flex-wrap gap-3">
            <label className="flex flex-col gap-1 text-xs">
              Item count
              <input
                type="number"
                className={`${inputClass} w-32`}
                value={number(totals.itemCount)}
                onChange={(e) => setTotals({ ...totals, itemCount: e.target.value === '' ? null : Number(e.target.value) })}
              />
            </label>
            <label className="flex flex-col gap-1 text-xs">
              Inventory value at cost
              <input
                type="number"
                step="0.01"
                className={`${inputClass} w-40`}
                value={number(totals.inventoryValue)}
                onChange={(e) => setTotals({ ...totals, inventoryValue: e.target.value === '' ? null : Number(e.target.value) })}
              />
            </label>
            <label className="flex flex-col gap-1 text-xs">
              Clients
              <input
                type="number"
                className={`${inputClass} w-28`}
                value={number(totals.customerCount)}
                onChange={(e) => setTotals({ ...totals, customerCount: e.target.value === '' ? null : Number(e.target.value) })}
              />
            </label>
            <label className="flex flex-col gap-1 text-xs">
              Suppliers
              <input
                type="number"
                className={`${inputClass} w-28`}
                value={number(totals.supplierCount)}
                onChange={(e) => setTotals({ ...totals, supplierCount: e.target.value === '' ? null : Number(e.target.value) })}
              />
            </label>
          </div>
        </div>
      </section>

      {reconciliation ? (
        <section className="pos-panel">
          <div className="pos-panel-header">
            <span>{batch.stage === 'Imported' ? 'What was imported' : 'What the import would do'}</span>
            <span className="normal-case">
              {reconciliation.rowsWouldImport} of {reconciliation.rowsConsidered} row(s)
            </span>
          </div>
          <div className="space-y-2 p-3">
            <table className="w-full text-sm">
              <thead className="text-xs">
                <tr>
                  <th className="py-1 text-left">Measure</th>
                  <th className="py-1 text-right">Here</th>
                  <th className="py-1 text-right">Old system</th>
                  <th className="py-1 text-right">Difference</th>
                  <th className="py-1 text-left">Agrees</th>
                </tr>
              </thead>
              <tbody>
                {reconciliation.lines.map((line) => (
                  <tr key={line.measure} className="border-t border-[rgb(var(--border))]">
                    <td className="py-1">{line.measure}</td>
                    <td className="py-1 text-right">
                      {line.measure.includes('value') || line.measure.includes('balance')
                        ? formatCurrency(line.imported)
                        : line.imported}
                    </td>
                    <td className="py-1 text-right">{line.legacyReported ?? '—'}</td>
                    <td className="py-1 text-right">{line.variance ?? '—'}</td>
                    {/* Words, because "agrees" and "does not" is the whole answer someone needs. */}
                    <td className="py-1">
                      {line.legacyReported === null ? 'nothing to compare' : line.matches ? 'yes' : 'NO'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {reconciliation.warnings.length > 0 ? (
              <ul className="text-xs text-[rgb(var(--warning))]">
                {reconciliation.warnings.map((warning) => (
                  <li key={warning}>⚠ {warning}</li>
                ))}
              </ul>
            ) : null}
          </div>
        </section>
      ) : null}
    </>
  );
}
