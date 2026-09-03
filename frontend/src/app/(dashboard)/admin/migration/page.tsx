'use client';

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  ClipboardCheck,
  DatabaseZap,
  FileSearch,
  FileUp,
  FlaskConical,
  Inbox,
  ListChecks,
  Lock,
  Scale,
  Trash2,
  Upload,
  XCircle,
  type LucideIcon,
} from 'lucide-react';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { cn, formatCurrency } from '@/lib/utils';
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
import { PageHeader as SharedPageHeader } from '@/components/shell/page-header';
import { describeError } from '@/lib/errors';
import { EmptyState } from '@/components/ui/states';
import { ConfirmDialog, useConfirm } from '@/components/ui/confirm-dialog';

const inputClass =
  'pos-input';

const thText = 'px-3 py-2 text-left text-label font-medium text-ink-muted';
const thNum = 'px-3 py-2 text-right text-label font-medium text-ink-muted';
const td = 'px-3 py-2 align-top';
const tdNum = 'px-3 py-2 text-right align-top tabular-nums';

/** What each stage means in the operator's words, not the enum's. */
const stageLabel: Record<MigrationStage, string> = {
  Staged: 'Read in',
  Validated: 'Checked',
  DryRun: 'Dry run done',
  Imported: 'Imported',
  Cancelled: 'Cancelled',
};

/** Each stage as a glyph and a tone, so the column is readable without colour vision. */
const stageStyle: Record<MigrationStage, { icon: LucideIcon; tone: string }> = {
  Staged: { icon: Inbox, tone: 'text-ink-muted' },
  Validated: { icon: ClipboardCheck, tone: 'text-accent-text' },
  DryRun: { icon: FlaskConical, tone: 'text-accent-text' },
  Imported: { icon: CheckCircle2, tone: 'text-positive' },
  Cancelled: { icon: XCircle, tone: 'text-ink-muted' },
};

function StageBadge({ stage }: { stage: MigrationStage }) {
  const { icon: Icon, tone } = stageStyle[stage];

  return (
    <span className={cn('pos-badge', tone)}>
      <Icon className="h-4 w-4" aria-hidden />
      {stageLabel[stage]}
    </span>
  );
}

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
      toast({ title: 'Could not load the batches', description: describeError(error), variant: 'destructive' });
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
      <div className="p-4 lg:p-6">
        <PageHeader
          title="Bring data across"
          lede="Reads a file from the old system, checks it, rehearses the import, then writes it."
        />
        <section className="pos-panel mt-4">
          <EmptyState
            icon={Lock}
            title="You do not have permission to run a migration"
            description="Bringing legacy data across needs the migration.run permission. Ask an administrator to grant it on your role."
          />
        </section>
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
      toast({ title: 'Could not read that file', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);

      if (fileRef.current) {
        fileRef.current.value = '';
      }
    }
  };

  const selectedKind = kinds.find((k) => k.entity === entity);

  return (
    <div className="space-y-4 p-4 lg:p-6">
      <PageHeader
        title="Bring data across"
        lede="Read a file from the old system, check it, run it as a rehearsal, then import. Nothing is written until the import step, and the import will not start without a dry run that passed."
      >
        <button
          type="button"
          className="pos-button-primary"
          disabled={busy}
          onClick={() => fileRef.current?.click()}
        >
          <Upload className="h-5 w-5" aria-hidden />
          {busy ? 'Reading…' : 'Read a file'}
        </button>
      </PageHeader>

      <Panel title="Read a file" icon={FileUp} action="Step 1 of 4">
        <div className="space-y-3 p-4">
          <div className="flex flex-wrap items-end gap-3">
            <label className="flex flex-col gap-1 text-label text-ink-muted">
              What kind of file
              <select className={inputClass} value={entity} onChange={(event) => setEntity(event.target.value)}>
                {kinds.map((kind) => (
                  <option key={kind.entity} value={kind.entity}>
                    {kind.displayName}
                  </option>
                ))}
              </select>
            </label>

            <label className="flex flex-col gap-1 text-label text-ink-muted">
              File
              <input
                ref={fileRef}
                type="file"
                className={`${inputClass} py-1`}
                disabled={busy}
                onChange={(event) => {
                  const file = event.target.files?.[0];
                  if (file) void upload(file);
                }}
              />
            </label>
          </div>

          {selectedKind ? (
            <p className="rounded border border-subtle bg-panel-sunken p-3 text-body text-ink-muted">
              <span className="font-medium text-ink">Expected column order</span>{' '}
              ({selectedKind.guideReference}):{' '}
              <span className="pos-amount">{selectedKind.columns.join(', ')}</span>. These files have no
              header row, so the order is what identifies the columns.
            </p>
          ) : null}
        </div>
      </Panel>

      <Panel
        title="Files read in"
        icon={Inbox}
        action={batches.length > 0 ? 'Click a file to work on it' : undefined}
      >
        {batches.length === 0 ? (
          <EmptyState
            icon={Inbox}
            title="Nothing read in yet"
            description="Choose the kind of file and pick it above. Reading a file in writes nothing to the live system — it only stages the rows so they can be checked."
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full border-collapse text-body">
              <thead className="border-b border-subtle bg-panel-sunken">
                <tr>
                  <th scope="col" className={thText}>File</th>
                  <th scope="col" className={thText}>Kind</th>
                  <th scope="col" className={thText}>Where it is up to</th>
                  <th scope="col" className={thNum}>Rows</th>
                  <th scope="col" className={thNum}>Problems</th>
                  <th scope="col" className={thNum}>Imported</th>
                </tr>
              </thead>
              <tbody>
                {batches.map((batch) => {
                  const isSelected = selected?.id === batch.id;

                  return (
                    <tr
                      key={batch.id}
                      tabIndex={0}
                      aria-selected={isSelected}
                      onClick={() => setSelected(batch)}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter' || event.key === ' ') {
                          event.preventDefault();
                          setSelected(batch);
                        }
                      }}
                      className={cn(
                        'cursor-pointer border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover',
                        isSelected && 'bg-accent-soft font-medium',
                      )}
                    >
                      <td className={cn(td, 'text-ink')}>{batch.sourceFileName}</td>
                      <td className={cn(td, 'text-ink-muted')}>{batch.entity}</td>
                      <td className={td}>
                        <StageBadge stage={batch.stage} />
                      </td>
                      <td className={tdNum} data-numeric="">{batch.rowsStaged}</td>
                      <td className={tdNum} data-numeric="">
                        {batch.blockingErrors > 0 ? (
                          <span className="inline-flex items-center gap-1 font-medium text-negative">
                            <XCircle className="h-5 w-5" aria-hidden />
                            {batch.blockingErrors} to fix
                          </span>
                        ) : batch.warnings > 0 ? (
                          <span className="inline-flex items-center gap-1 text-warning">
                            <AlertTriangle className="h-5 w-5" aria-hidden />
                            {batch.warnings} noted
                          </span>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td className={tdNum} data-numeric="">
                        {batch.stage === 'Imported' ? batch.rowsImported : '—'}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Panel>

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
      toast({ title: 'Not done', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const confirmer = useConfirm();

  const askImport = () => {
    confirmer.ask(
      {
        subject: `${batch.rowsStaged} row${batch.rowsStaged === 1 ? '' : 's'} from ${batch.sourceFileName}`,
        consequence:
          'These are written into the live catalogue and the stock ledger. Existing items are '
          + 'updated in place, and the stock movements are real.',
        verb: 'Import into the live system',
      },
      doImport,
    );
  };

  const doImport = async () => {
    await run(() => mastersApi.migration.import(batch.id, totals), 'Imported');
  };

  const number = (value: number | null | undefined) => (value === null || value === undefined ? '' : String(value));

  return (
    <>
      <Panel title={batch.sourceFileName} icon={FileSearch} action={<StageBadge stage={batch.stage} />}>
        <div className="space-y-4 p-4">
          {analysis ? (
            <div className="space-y-1.5">
              <p className="text-body text-ink">
                Read as <span className="font-medium">{analysis.format}</span>, treated as{' '}
                <span className="font-medium">{analysis.detectedLayout}</span> ({analysis.guideReference}).
              </p>
              <p className="text-body text-ink-muted">
                {analysis.rowCount} row(s), {analysis.columnCount} column(s)
                {analysis.deletedRowCount > 0
                  ? `, ${analysis.deletedRowCount} of which the old system had already deleted and which will not be imported`
                  : ''}
                .
              </p>

              {analysis.notes.length > 0 ? (
                <ul className="space-y-1 rounded border border-subtle bg-panel-sunken p-3">
                  {analysis.notes.map((note) => (
                    <li key={note} className="flex items-start gap-2 text-body text-ink">
                      <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning" aria-hidden />
                      <span>{note}</span>
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
          ) : null}

          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              className="pos-button"
              disabled={busy || batch.stage === 'Imported' || batch.stage === 'Cancelled'}
              onClick={() => void run(() => mastersApi.migration.validate(batch.id), 'Checked')}
            >
              <ClipboardCheck className="h-5 w-5" aria-hidden />
              2 · Check it
            </button>

            <button
              type="button"
              className="pos-button"
              disabled={busy || batch.stage === 'Staged' || batch.stage === 'Imported' || batch.stage === 'Cancelled'}
              onClick={() => void run(() => mastersApi.migration.dryRun(batch.id, totals), 'Dry run done')}
            >
              <FlaskConical className="h-5 w-5" aria-hidden />
              3 · Dry run
            </button>

            <button
              type="button"
              className="pos-button-primary"
              disabled={busy || !batch.canImport}
              onClick={askImport}
              title="Writes these rows into the live catalogue and stock ledger"
            >
              <DatabaseZap className="h-5 w-5" aria-hidden />
              4 · Import for real
            </button>

            <span className="ml-auto">
              <button
                type="button"
                className="pos-button-danger"
                disabled={busy || batch.stage === 'Imported' || batch.stage === 'Cancelled'}
                onClick={() =>
                  confirmer.ask(
                    {
                      subject: batch.sourceFileName,
                      consequence:
                        'The staged rows for this file are thrown away. Anything already imported '
                        + 'from it is not affected.',
                      verb: 'Discard staged rows',
                    },
                    () => run(() => mastersApi.migration.cancel(batch.id), 'Cancelled'),
                  )
                }
                title="Throws away the staged rows for this file. Nothing that was already imported is affected."
              >
                <Trash2 className="h-5 w-5" aria-hidden />
                Discard
              </button>
            </span>
          </div>

          <div className="flex items-start gap-2.5 rounded border border-negative/30 bg-negative/5 p-3">
            <DatabaseZap className="mt-0.5 h-4 w-4 shrink-0 text-negative" aria-hidden />
            <div className="min-w-0">
              <p className="text-body font-semibold text-negative">Import writes to the live system</p>
              <p className="mt-0.5 max-w-[76ch] text-body text-ink-muted">
                It creates and updates records in the catalogue and appends to the stock ledger. Steps 1 to 3
                write nothing, so read the check and the dry run below before pressing it — and take a
                database backup first if this is a cutover.
              </p>
            </div>
          </div>

          {!batch.canImport && batch.stage !== 'Imported' ? (
            <p className="flex items-start gap-2 text-body text-ink-muted">
              <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning" aria-hidden />
              {batch.blockingErrors > 0
                ? `${batch.blockingErrors} problem(s) have to be fixed in the source file first.`
                : 'Import stays disabled until a dry run has been done for this file.'}
            </p>
          ) : null}
        </div>
      </Panel>

      {batch.stage !== 'Staged' && findings.length > 0 ? (
        <Panel
          title="What the check found"
          icon={ListChecks}
          action={
            <span className="flex items-center gap-2">
              <span className={cn('pos-badge', batch.blockingErrors > 0 ? 'text-negative' : 'text-ink-muted')}>
                <XCircle className="h-4 w-4" aria-hidden />
                {batch.blockingErrors} to fix
              </span>
              <span className={cn('pos-badge', batch.warnings > 0 ? 'text-warning' : 'text-ink-muted')}>
                <AlertTriangle className="h-4 w-4" aria-hidden />
                {batch.warnings} noted
              </span>
            </span>
          }
        >
          <div className="max-h-72 overflow-auto">
            <table className="w-full border-collapse text-body">
              <thead className="sticky top-0 z-10 border-b border-subtle bg-panel-sunken">
                <tr>
                  <th scope="col" className={thNum}>Row</th>
                  <th scope="col" className={thText}>Column</th>
                  <th scope="col" className={thText}>Severity</th>
                  <th scope="col" className={thText}>What is wrong</th>
                </tr>
              </thead>
              <tbody>
                {findings.slice(0, 300).map((finding, index) => (
                  <tr
                    key={`${finding.rowNumber}-${finding.code}-${index}`}
                    className="border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover"
                  >
                    <td className={tdNum} data-numeric="">{finding.rowNumber}</td>
                    <td className={cn(td, 'text-ink-muted')}>{finding.column ?? '—'}</td>
                    {/* The word and a glyph, not a colour — a blocking error has to read as one. */}
                    <td className={td}>
                      {finding.severity === 'Blocking' ? (
                        <span className="pos-badge text-negative">
                          <XCircle className="h-4 w-4" aria-hidden />
                          Must fix
                        </span>
                      ) : (
                        <span className="pos-badge text-warning">
                          <AlertTriangle className="h-4 w-4" aria-hidden />
                          Noted
                        </span>
                      )}
                    </td>
                    <td className={td}>{finding.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {findings.length > 300 ? (
            <p className="border-t border-subtle px-4 py-2.5 text-body text-ink-muted">
              Showing the first 300 of {findings.length}.
            </p>
          ) : null}
        </Panel>
      ) : null}

      {rows.length > 0 ? (
        <Panel title="Rows that need looking at" icon={FileSearch} action={`${rows.length} row(s)`}>
          <div className="max-h-72 overflow-auto">
            <table className="w-full border-collapse text-body">
              <thead className="sticky top-0 z-10 border-b border-subtle bg-panel-sunken">
                <tr>
                  <th scope="col" className={thNum}>Row</th>
                  <th scope="col" className={thText}>Key</th>
                  <th scope="col" className={thText}>Problems</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr
                    key={row.rowNumber}
                    className="border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover"
                  >
                    <td className={tdNum} data-numeric="">{row.rowNumber}</td>
                    <td className={cn(td, 'pos-amount')}>{row.legacyKey ?? '—'}</td>
                    <td className={cn(td, 'whitespace-pre-line')}>{row.problems}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>
      ) : null}

      <Panel title="What the old system said" icon={Scale}>
        <p className="border-b border-subtle px-4 py-2.5 text-body text-ink-muted">
          Type these off the old system&apos;s own reports before the dry run. Anything left blank is
          reported as imported-only rather than counted as agreeing.
        </p>

        <div className="flex flex-wrap gap-4 p-4">
          <label className="flex flex-col gap-1 text-label text-ink-muted">
            Item count
            <input
              type="number"
              className={`${inputClass} w-32 text-right`}
              value={number(totals.itemCount)}
              onChange={(e) => setTotals({ ...totals, itemCount: e.target.value === '' ? null : Number(e.target.value) })}
            />
          </label>
          <label className="flex flex-col gap-1 text-label text-ink-muted">
            Inventory value at cost
            <input
              type="number"
              step="0.01"
              className={`${inputClass} w-40 text-right`}
              value={number(totals.inventoryValue)}
              onChange={(e) => setTotals({ ...totals, inventoryValue: e.target.value === '' ? null : Number(e.target.value) })}
            />
          </label>
          <label className="flex flex-col gap-1 text-label text-ink-muted">
            Clients
            <input
              type="number"
              className={`${inputClass} w-28 text-right`}
              value={number(totals.customerCount)}
              onChange={(e) => setTotals({ ...totals, customerCount: e.target.value === '' ? null : Number(e.target.value) })}
            />
          </label>
          <label className="flex flex-col gap-1 text-label text-ink-muted">
            Suppliers
            <input
              type="number"
              className={`${inputClass} w-28 text-right`}
              value={number(totals.supplierCount)}
              onChange={(e) => setTotals({ ...totals, supplierCount: e.target.value === '' ? null : Number(e.target.value) })}
            />
          </label>
        </div>
      </Panel>

      {reconciliation ? (
        <Panel
          title={batch.stage === 'Imported' ? 'What was imported' : 'What the import would do'}
          icon={Scale}
          action={`${reconciliation.rowsWouldImport} of ${reconciliation.rowsConsidered} row(s)`}
        >
          <div className="overflow-x-auto">
            <table className="w-full border-collapse text-body">
              <thead className="border-b border-subtle bg-panel-sunken">
                <tr>
                  <th scope="col" className={thText}>Measure</th>
                  <th scope="col" className={thNum}>Here</th>
                  <th scope="col" className={thNum}>Old system</th>
                  <th scope="col" className={thNum}>Difference</th>
                  <th scope="col" className={thText}>Agrees</th>
                </tr>
              </thead>
              <tbody>
                {reconciliation.lines.map((line) => (
                  <tr
                    key={line.measure}
                    className="border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover"
                  >
                    <td className={td}>{line.measure}</td>
                    <td className={tdNum} data-numeric="">
                      {line.measure.includes('value') || line.measure.includes('balance')
                        ? formatCurrency(line.imported)
                        : line.imported}
                    </td>
                    <td className={tdNum} data-numeric="">{line.legacyReported ?? '—'}</td>
                    <td className={tdNum} data-numeric="">{line.variance ?? '—'}</td>
                    {/* Words and a glyph, because "agrees" and "does not" is the whole answer. */}
                    <td className={td}>
                      {line.legacyReported === null ? (
                        <span className="text-ink-faint">Nothing to compare</span>
                      ) : line.matches ? (
                        <span className="pos-badge text-positive">
                          <CheckCircle2 className="h-4 w-4" aria-hidden />
                          Agrees
                        </span>
                      ) : (
                        <span className="pos-badge text-negative">
                          <XCircle className="h-4 w-4" aria-hidden />
                          Does not agree
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {reconciliation.warnings.length > 0 ? (
            <ul className="space-y-1 border-t border-subtle p-4">
              {reconciliation.warnings.map((warning) => (
                <li key={warning} className="flex items-start gap-2 text-body text-ink">
                  <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning" aria-hidden />
                  <span>{warning}</span>
                </li>
              ))}
            </ul>
          ) : null}
        </Panel>
      ) : null}

      <ConfirmDialog
        request={confirmer.request}
        open={confirmer.open}
        onOpenChange={confirmer.setOpen}
        onConfirm={confirmer.confirm}
        busy={confirmer.busy}
      />
    </>
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
