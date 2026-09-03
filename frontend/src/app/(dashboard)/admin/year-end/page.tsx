'use client';

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import {
  AlertTriangle,
  CalendarDays,
  CalendarPlus,
  Download,
  FlaskConical,
  Lock,
  LockOpen,
  ShieldAlert,
  Table2,
  Undo2,
  type LucideIcon,
} from 'lucide-react';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { cn, formatCurrency } from '@/lib/utils';
import type { ArchiveRow, FiscalYear, FiscalYearCloseResult } from '@/types/masters';
import { PageHeader as SharedPageHeader } from '@/components/shell/page-header';
import { describeError } from '@/lib/errors';
import { EmptyState } from '@/components/ui/states';
import { ConfirmDialog, useConfirm } from '@/components/ui/confirm-dialog';

const inputClass =
  'pos-input';

const thText = 'px-3 py-2 text-left text-label font-medium text-ink-muted';
const thNum = 'px-3 py-2 text-right text-label font-medium text-ink-muted';
const td = 'px-3 py-2 align-middle';
const tdNum = 'px-3 py-2 text-right align-middle tabular-nums';

const monthNames = [
  '', 'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

/**
 * The year-end close (guide p.29).
 *
 * The legacy close cleared histories and rolled this year's monthly figures into last year's. This
 * one destroys nothing — it rolls the year up into an archive and writes a checkpoint — which is
 * what makes the dry run meaningful and reopening safe.
 */
export default function YearEndPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canClose = auth.can('inventory.year_end');

  const [years, setYears] = useState<FiscalYear[]>([]);
  const [newYear, setNewYear] = useState(() => new Date().getFullYear() - 1);
  const [preview, setPreview] = useState<FiscalYearCloseResult | null>(null);
  const [previewFor, setPreviewFor] = useState<number | null>(null);
  const [history, setHistory] = useState<ArchiveRow[]>([]);
  const [historyYear, setHistoryYear] = useState<number | ''>('');
  const [busy, setBusy] = useState(false);
  const confirmer = useConfirm();

  const load = useCallback(async () => {
    if (!locationId) return;

    try {
      setYears(await mastersApi.fiscalYears.list(locationId));
    } catch (error) {
      toast({ title: 'Could not load the years', description: describeError(error), variant: 'destructive' });
    }
  }, [locationId]);

  const loadHistory = useCallback(async () => {
    if (!locationId) return;

    try {
      setHistory(await mastersApi.fiscalYears.history(locationId, historyYear === '' ? undefined : historyYear));
    } catch (error) {
      toast({ title: 'Could not load the history', description: describeError(error), variant: 'destructive' });
    }
  }, [locationId, historyYear]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    void loadHistory();
  }, [loadHistory]);

  if (!canClose) {
    return (
      <div className="p-4 lg:p-6">
        <PageHeader
          title="Year end"
          lede="Rolls a trading year up into the sales history and writes a stock checkpoint."
        />
        <section className="pos-panel mt-4">
          <EmptyState
            icon={Lock}
            title="You do not have permission to close a fiscal year"
            description="Closing and reopening a year needs the inventory.year_end permission. Ask an administrator to grant it on your role."
          />
        </section>
      </div>
    );
  }

  const open = async () => {
    if (!locationId) return;
    setBusy(true);

    try {
      await mastersApi.fiscalYears.open(locationId, newYear);
      await load();
      toast({ title: `${newYear} opened` });
    } catch (error) {
      toast({ title: 'Not opened', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const dryRun = async (year: FiscalYear) => {
    setBusy(true);
    setPreview(null);

    try {
      setPreview(await mastersApi.fiscalYears.close(year.id, true));
      setPreviewFor(year.id);
    } catch (error) {
      setPreviewFor(null);
      toast({ title: 'Could not work that out', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const askClose = (year: FiscalYear) => {
    confirmer.ask(
      {
        subject: `Financial year ${year.year}`,
        consequence:
          'The year is rolled up and checkpointed. Nothing is deleted, and it can be reopened '
          + 'afterwards if something was posted late.',
        verb: 'Close year',
        tone: 'caution',
      },
      () => close(year),
    );
  };

  const close = async (year: FiscalYear) => {
    setBusy(true);

    try {
      const result = await mastersApi.fiscalYears.close(year.id, false);
      setPreview(null);
      setPreviewFor(null);
      await load();
      await loadHistory();
      toast({ title: `${year.year} closed`, description: `${result.archiveRows} archive row(s) written.` });
    } catch (error) {
      toast({ title: 'Not closed', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  /**
   * The second place that makes you type the year.
   *
   * Reopening discards every archive row and checkpoint the close produced. It is recoverable —
   * closing again rebuilds them — but not cheaply, and it is one click away from a button labelled
   * with a year that looks much like the year next to it.
   */
  const askReopen = (year: FiscalYear) => {
    confirmer.ask(
      {
        subject: `Financial year ${year.year}`,
        consequence:
          'The archive rows and checkpoints written when this year was closed are discarded. The '
          + 'sales they were derived from are untouched, and closing again rebuilds them.',
        verb: 'Reopen year',
        typeToConfirm: String(year.year),
      },
      () => reopen(year),
    );
  };

  const reopen = async (year: FiscalYear) => {
    setBusy(true);

    try {
      await mastersApi.fiscalYears.reopen(year.id);
      await load();
      await loadHistory();
      toast({ title: `${year.year} reopened` });
    } catch (error) {
      toast({ title: 'Not reopened', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-4 p-4 lg:p-6">
      <PageHeader
        title="Year end"
        lede="Closing a year rolls its trading up into the sales history and writes a stock checkpoint. Nothing is deleted, every previous year keeps its own figures, and a close can be undone."
      >
        <label className="flex items-center gap-1.5 text-label text-ink-muted">
          <span className="sr-only sm:not-sr-only">Year</span>
          <input
            type="number"
            aria-label="Year to open"
            className={`${inputClass} w-24`}
            value={newYear}
            onChange={(event) => setNewYear(Number(event.target.value) || new Date().getFullYear())}
          />
        </label>
        <button type="button" className="pos-button-primary" disabled={busy} onClick={() => void open()}>
          <CalendarPlus className="h-5 w-5" aria-hidden />
          Open year
        </button>
      </PageHeader>

      <Panel
        title="Fiscal years"
        icon={CalendarDays}
        action={`${years.filter((y) => y.status === 'Open').length} open`}
      >
        {years.length === 0 ? (
          <EmptyState
            icon={CalendarDays}
            title="No fiscal years yet"
            description="Type the year you want to close in the box at the top of the page and press “Open year”. A year has to exist before it can be closed."
          />
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full border-collapse text-body">
                <thead className="border-b border-subtle bg-panel-sunken">
                  <tr>
                    <th scope="col" className={thText}>Year</th>
                    <th scope="col" className={thText}>Period</th>
                    <th scope="col" className={thText}>Status</th>
                    <th scope="col" className={thNum}>Archived</th>
                    <th scope="col" className={thNum}>Net sales</th>
                    <th scope="col" className={thNum}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {years.map((year) => {
                    const closed = year.status === 'Closed';

                    return (
                      <tr
                        key={String(year.id)}
                        className="border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover"
                      >
                        <td className={cn(td, 'font-medium tabular-nums text-ink')}>{year.year}</td>
                        <td className={cn(td, 'tabular-nums text-ink-muted')}>
                          {year.startsOn} to {year.endsOn}
                        </td>
                        {/* Words and a glyph rather than a colour: "closed" is the fact, and it needs
                            to read as one for anyone who cannot separate the two hues. */}
                        <td className={td}>
                          <span className={cn('pos-badge', closed ? 'text-ink-muted' : 'text-positive')}>
                            {closed ? <Lock className="h-4 w-4" aria-hidden /> : <LockOpen className="h-4 w-4" aria-hidden />}
                            {closed
                              ? `Closed${year.closedAt ? ` ${new Date(year.closedAt).toLocaleDateString()}` : ''}`
                              : 'Open'}
                          </span>
                        </td>
                        <td className={tdNum} data-numeric="">{closed ? year.archivedRows : '—'}</td>
                        <td className={tdNum} data-numeric="">
                          {closed ? formatCurrency(year.archivedNetSales) : '—'}
                        </td>
                        <td className={cn(td, 'text-right')}>
                          {year.status === 'Open' ? (
                            <span className="inline-flex items-center gap-2">
                              <button
                                type="button"
                                className="pos-button"
                                disabled={busy}
                                onClick={() => void dryRun(year)}
                                title="Work out what a close would write, without writing anything"
                              >
                                <FlaskConical className="h-5 w-5" aria-hidden />
                                Dry run
                              </button>
                              <button
                                type="button"
                                className="pos-button-danger"
                                disabled={busy || previewFor !== year.id}
                                onClick={() => askClose(year)}
                                title={
                                  previewFor === year.id
                                    ? `Close ${year.year} for good — it stops accepting trading and is rolled up`
                                    : 'Read a dry run first'
                                }
                              >
                                <Lock className="h-5 w-5" aria-hidden />
                                Close year
                              </button>
                            </span>
                          ) : (
                            <button
                              type="button"
                              className="pos-button-danger"
                              disabled={busy}
                              onClick={() => askReopen(year)}
                              title={`Reopen ${year.year} and discard its archive rows and checkpoints`}
                            >
                              <Undo2 className="h-5 w-5" aria-hidden />
                              Reopen
                            </button>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            <div className="flex items-start gap-2.5 border-t border-subtle bg-negative/5 px-4 py-3">
              <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-negative" aria-hidden />
              <div className="min-w-0">
                <p className="text-body font-semibold text-negative">What these two buttons do</p>
                <p className="mt-0.5 max-w-[76ch] text-body text-ink-muted">
                  <span className="font-medium text-ink">Close year</span> stops the year accepting any
                  more trading, rolls it up into the sales history and writes a stock checkpoint. It stays
                  disabled until a dry run has been read.{' '}
                  <span className="font-medium text-ink">Reopen</span> throws away that year&apos;s archive
                  rows and checkpoints — the sales they were derived from are untouched, but every report
                  reading the archive changes until it is closed again.
                </p>
                <p className="mt-1 text-body text-ink-muted">
                  Years close in order, and a year that has not finished cannot be closed at all.
                </p>
              </div>
            </div>
          </>
        )}
      </Panel>

      {preview ? (
        <Panel
          title={`Dry run — ${preview.year}`}
          icon={FlaskConical}
          action={
            <span className="pos-badge text-accent-text">
              <FlaskConical className="h-4 w-4" aria-hidden />
              Nothing has been written
            </span>
          }
        >
          <div className="space-y-3 p-4">
            <div className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-3 lg:grid-cols-6">
              <Figure label="Archive rows" value={String(preview.archiveRows)} />
              <Figure label="Items checkpointed" value={String(preview.productsCheckpointed)} />
              <Figure label="Transactions" value={String(preview.transactionsCovered)} />
              <Figure label="Net sales" value={formatCurrency(preview.netSales)} />
              <Figure label="Cost of goods" value={formatCurrency(preview.costOfGoodsSold)} />
              <Figure label="Gross margin" value={formatCurrency(preview.grossMargin)} />
            </div>

            {preview.warnings.length > 0 ? (
              <ul className="space-y-1 rounded border border-subtle bg-panel-sunken p-3">
                {preview.warnings.map((warning) => (
                  <li key={warning} className="flex items-start gap-2 text-body text-ink">
                    <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning" aria-hidden />
                    <span>{warning}</span>
                  </li>
                ))}
              </ul>
            ) : null}

            <p className="text-body text-ink-muted">
              These are the figures the real close will write. Voided and practice sales are already excluded.
            </p>
          </div>
        </Panel>
      ) : null}

      <Panel title="Sales history" icon={Table2} action={`${history.length} row${history.length === 1 ? '' : 's'}`}>
        <div className="flex flex-wrap items-end gap-3 border-b border-subtle px-4 py-3">
          <label className="flex flex-col gap-1 text-label text-ink-muted">
            Year
            <select
              className={inputClass}
              value={historyYear}
              onChange={(event) => setHistoryYear(event.target.value === '' ? '' : Number(event.target.value))}
            >
              <option value="">Every closed year</option>
              {years.filter((y) => y.status === 'Closed').map((y) => (
                <option key={String(y.id)} value={y.year}>
                  {y.year}
                </option>
              ))}
            </select>
          </label>

          {locationId ? (
            <a
              className="pos-button"
              href={mastersApi.fiscalYears.historyExportUrl(locationId, historyYear === '' ? undefined : historyYear)}
              target="_blank"
              rel="noopener noreferrer"
            >
              <Download className="h-5 w-5" aria-hidden />
              Download CSV
            </a>
          ) : null}
        </div>

        {history.length === 0 ? (
          <EmptyState
            icon={Table2}
            title="Nothing archived yet"
            description="The history fills up as years are closed. Run a dry run on an open year above to see what its first rows would look like."
          />
        ) : (
          <div className="max-h-96 overflow-auto">
            <table className="w-full border-collapse text-body">
              <thead className="sticky top-0 z-10 border-b border-subtle bg-panel-sunken">
                <tr>
                  <th scope="col" className={thText}>Year</th>
                  <th scope="col" className={thText}>Month</th>
                  <th scope="col" className={thText}>Code</th>
                  <th scope="col" className={thText}>Description</th>
                  <th scope="col" className={thNum}>Sold</th>
                  <th scope="col" className={thNum}>Net</th>
                  <th scope="col" className={thNum}>Margin</th>
                </tr>
              </thead>
              <tbody>
                {history.map((row) => (
                  <tr
                    key={`${row.year}-${row.month}-${row.stockCode}`}
                    className="border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover"
                  >
                    <td className={cn(td, 'tabular-nums')}>{row.year}</td>
                    <td className={cn(td, 'text-ink-muted')}>{monthNames[row.month] ?? row.month}</td>
                    <td className={cn(td, 'pos-amount')}>{row.stockCode}</td>
                    <td className={td}>{row.name}</td>
                    <td className={tdNum} data-numeric="">{row.quantitySold}</td>
                    <td className={tdNum} data-numeric="">{formatCurrency(row.netSales)}</td>
                    <td className={tdNum} data-numeric="">{formatCurrency(row.grossMargin)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>

      <ConfirmDialog
        request={confirmer.request}
        open={confirmer.open}
        onOpenChange={confirmer.setOpen}
        onConfirm={confirmer.confirm}
        busy={confirmer.busy}
      />
    </div>
  );
}

function Figure({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <p className="text-label text-ink-muted">{label}</p>
      <p className="pos-amount mt-0.5 text-body-lg font-semibold text-ink">{value}</p>

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
