'use client';

import { useCallback, useEffect, useState } from 'react';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { formatCurrency } from '@/lib/utils';
import type { ArchiveRow, FiscalYear, FiscalYearCloseResult } from '@/types/masters';

const inputClass =
  'rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1 text-sm';

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
  const [previewFor, setPreviewFor] = useState<string | null>(null);
  const [history, setHistory] = useState<ArchiveRow[]>([]);
  const [historyYear, setHistoryYear] = useState<number | ''>('');
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (!locationId) return;

    try {
      setYears(await mastersApi.fiscalYears.list(locationId));
    } catch (error) {
      toast({ title: 'Could not load the years', description: describe(error), variant: 'destructive' });
    }
  }, [locationId]);

  const loadHistory = useCallback(async () => {
    if (!locationId) return;

    try {
      setHistory(await mastersApi.fiscalYears.history(locationId, historyYear === '' ? undefined : historyYear));
    } catch (error) {
      toast({ title: 'Could not load the history', description: describe(error), variant: 'destructive' });
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
      <div className="p-6">
        <h1 className="text-lg font-semibold">Year end</h1>
        <p className="mt-2 text-sm text-[rgb(var(--text-muted))]">
          You do not have permission to close a fiscal year.
        </p>
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
      toast({ title: 'Not opened', description: describe(error), variant: 'destructive' });
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
      toast({ title: 'Could not work that out', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const close = async (year: FiscalYear) => {
    const confirmed = window.confirm(
      `Close ${year.year}? Nothing is deleted — the year is rolled up and checkpointed, and it can be reopened.`,
    );

    if (!confirmed) return;

    setBusy(true);

    try {
      const result = await mastersApi.fiscalYears.close(year.id, false);
      setPreview(null);
      setPreviewFor(null);
      await load();
      await loadHistory();
      toast({ title: `${year.year} closed`, description: `${result.archiveRows} archive row(s) written.` });
    } catch (error) {
      toast({ title: 'Not closed', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const reopen = async (year: FiscalYear) => {
    const confirmed = window.confirm(
      `Reopen ${year.year}? The archive rows and checkpoints go with it. The sales they were derived from are untouched.`,
    );

    if (!confirmed) return;

    setBusy(true);

    try {
      await mastersApi.fiscalYears.reopen(year.id);
      await load();
      await loadHistory();
      toast({ title: `${year.year} reopened` });
    } catch (error) {
      toast({ title: 'Not reopened', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-4 p-6">
      <header>
        <h1 className="text-lg font-semibold">Year end</h1>
        <p className="text-sm text-[rgb(var(--text-muted))]">
          Closing a year rolls its trading up into the sales history and writes a stock checkpoint. Nothing is
          deleted, every previous year keeps its own figures, and a close can be undone.
        </p>
      </header>

      <section className="pos-panel">
        <div className="pos-panel-header">
          <span>Years</span>
        </div>
        <div className="space-y-3 p-3">
          <div className="flex flex-wrap items-end gap-2">
            <label className="flex flex-col gap-1 text-xs">
              Open a year
              <input
                type="number"
                className={`${inputClass} w-28`}
                value={newYear}
                onChange={(event) => setNewYear(Number(event.target.value) || new Date().getFullYear())}
              />
            </label>
            <button type="button" className="pos-button" disabled={busy} onClick={() => void open()}>
              Open
            </button>
          </div>

          <table className="w-full text-sm">
            <thead className="text-xs">
              <tr>
                <th className="py-1 text-left">Year</th>
                <th className="py-1 text-left">Period</th>
                <th className="py-1 text-left">Status</th>
                <th className="py-1 text-right">Archived</th>
                <th className="py-1 text-right">Net sales</th>
                <th className="py-1 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {years.map((year) => (
                <tr key={year.id} className="border-t border-[rgb(var(--border))]">
                  <td className="py-1 font-medium">{year.year}</td>
                  <td className="py-1">
                    {year.startsOn} to {year.endsOn}
                  </td>
                  {/* Words rather than a colour: "closed" is the fact, and it needs to read as one. */}
                  <td className="py-1">
                    {year.status === 'Closed'
                      ? `Closed${year.closedAt ? ` ${new Date(year.closedAt).toLocaleDateString()}` : ''}`
                      : 'Open'}
                  </td>
                  <td className="py-1 text-right">{year.status === 'Closed' ? year.archivedRows : '—'}</td>
                  <td className="py-1 text-right">
                    {year.status === 'Closed' ? formatCurrency(year.archivedNetSales) : '—'}
                  </td>
                  <td className="py-1 text-right">
                    {year.status === 'Open' ? (
                      <>
                        <button
                          type="button"
                          className="text-xs underline"
                          disabled={busy}
                          onClick={() => void dryRun(year)}
                        >
                          Dry run
                        </button>
                        <button
                          type="button"
                          className="ml-2 text-xs underline"
                          disabled={busy || previewFor !== year.id}
                          onClick={() => void close(year)}
                        >
                          Close
                        </button>
                      </>
                    ) : (
                      <button
                        type="button"
                        className="text-xs underline"
                        disabled={busy}
                        onClick={() => void reopen(year)}
                      >
                        Reopen
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {years.length === 0 ? (
            <p className="text-xs text-[rgb(var(--text-muted))]">
              No fiscal years yet. Open the year you want to close.
            </p>
          ) : (
            <p className="text-xs text-[rgb(var(--text-muted))]">
              Close stays disabled until a dry run has been read. Years close in order, and a year that has not
              finished cannot be closed at all.
            </p>
          )}
        </div>
      </section>

      {preview ? (
        <section className="pos-panel">
          <div className="pos-panel-header">
            <span>Dry run — {preview.year}</span>
            <span className="normal-case">nothing has been written</span>
          </div>
          <div className="space-y-2 p-3 text-sm">
            <div className="grid grid-cols-2 gap-x-6 gap-y-1 sm:grid-cols-4">
              <Figure label="Archive rows" value={String(preview.archiveRows)} />
              <Figure label="Items checkpointed" value={String(preview.productsCheckpointed)} />
              <Figure label="Transactions" value={String(preview.transactionsCovered)} />
              <Figure label="Net sales" value={formatCurrency(preview.netSales)} />
              <Figure label="Cost of goods" value={formatCurrency(preview.costOfGoodsSold)} />
              <Figure label="Gross margin" value={formatCurrency(preview.grossMargin)} />
            </div>

            {preview.warnings.length > 0 ? (
              <ul className="text-xs text-[rgb(var(--warning))]">
                {preview.warnings.map((warning) => (
                  <li key={warning}>⚠ {warning}</li>
                ))}
              </ul>
            ) : null}

            <p className="text-xs text-[rgb(var(--text-muted))]">
              These are the figures the real close will write. Voided and practice sales are already excluded.
            </p>
          </div>
        </section>
      ) : null}

      <section className="pos-panel">
        <div className="pos-panel-header">
          <span>Sales history</span>
        </div>
        <div className="space-y-2 p-3">
          <div className="flex flex-wrap items-end gap-2">
            <label className="flex flex-col gap-1 text-xs">
              Year
              <select
                className={inputClass}
                value={historyYear}
                onChange={(event) => setHistoryYear(event.target.value === '' ? '' : Number(event.target.value))}
              >
                <option value="">Every closed year</option>
                {years.filter((y) => y.status === 'Closed').map((y) => (
                  <option key={y.id} value={y.year}>
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
                Download
              </a>
            ) : null}
          </div>

          <div className="max-h-96 overflow-y-auto border border-[rgb(var(--border))]">
            <table className="w-full text-sm">
              <thead className="sticky top-0 bg-[rgb(var(--panel))] text-xs">
                <tr>
                  <th className="px-2 py-1 text-left">Year</th>
                  <th className="px-2 py-1 text-left">Month</th>
                  <th className="px-2 py-1 text-left">Code</th>
                  <th className="px-2 py-1 text-left">Description</th>
                  <th className="px-2 py-1 text-right">Sold</th>
                  <th className="px-2 py-1 text-right">Net</th>
                  <th className="px-2 py-1 text-right">Margin</th>
                </tr>
              </thead>
              <tbody>
                {history.map((row) => (
                  <tr key={`${row.year}-${row.month}-${row.stockCode}`} className="border-t border-[rgb(var(--border))]">
                    <td className="px-2 py-1">{row.year}</td>
                    <td className="px-2 py-1">{monthNames[row.month] ?? row.month}</td>
                    <td className="px-2 py-1">{row.stockCode}</td>
                    <td className="px-2 py-1">{row.name}</td>
                    <td className="px-2 py-1 text-right">{row.quantitySold}</td>
                    <td className="px-2 py-1 text-right">{formatCurrency(row.netSales)}</td>
                    <td className="px-2 py-1 text-right">{formatCurrency(row.grossMargin)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {history.length === 0 ? (
            <p className="text-xs text-[rgb(var(--text-muted))]">
              Nothing archived yet — the history fills up as years are closed.
            </p>
          ) : null}
        </div>
      </section>
    </div>
  );
}

function Figure({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-xs text-[rgb(var(--text-muted))]">{label}</p>
      <p className="pos-amount font-semibold">{value}</p>
    </div>
  );
}

function describe(error: unknown): string {
  return error instanceof PosApiError ? error.problem.detail : 'Something went wrong.';
}
