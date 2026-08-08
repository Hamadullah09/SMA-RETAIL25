'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { BrowseFormShell, FormSection } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { formatCurrency , recordIdFrom} from '@/lib/utils';
import type { StockCount, StockCountRow, StockCountStatus } from '@/types/masters';

const filterClass =
  'pos-input';

const statusLabel: Record<StockCountStatus, string> = {
  InProgress: 'Counting',
  Posted: 'Posted',
  Cancelled: 'Cancelled',
};

/**
 * Stock counts (guide p.22).
 *
 * Counting and posting are separate steps on purpose: a count is gathered over hours by people with
 * clipboards, and nothing moves until someone has looked at the variances and decided they are real.
 */
export default function StockCountsPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canCount = auth.can('inventory.count');

  const [status, setStatus] = useState<StockCountStatus | ''>('');
  const [rows, setRows] = useState<StockCountRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [newDepartmentId, setNewDepartmentId] = useState<number | ''>('');

  const { data: departments = [] } = useQuery({
    queryKey: ['departments', locationId],
    queryFn: () => mastersApi.departments.list(locationId!),
    enabled: Boolean(locationId),
  });

  const load = useCallback(async () => {
    if (!locationId) return;
    setLoading(true);

    try {
      setRows(await mastersApi.stockCounts.browse(locationId, status || undefined));
    } catch (error) {
      toast({ title: 'Could not load counts', description: describe(error), variant: 'destructive' });
    } finally {
      setLoading(false);
    }
  }, [locationId, status]);

  useEffect(() => {
    void load();
  }, [load]);

  const columns = useMemo<DataGridColumn<StockCountRow>[]>(
    () => [
      { key: 'number', header: 'Count', width: 90, render: (r) => `SC-${r.countNumber}` },
      { key: 'status', header: 'Status', width: 100, render: (r) => statusLabel[r.status] },
      { key: 'department', header: 'Scope', width: 150, render: (r) => r.departmentName ?? 'Whole shop' },
      { key: 'lines', header: 'Counted', width: 80, numeric: true, render: (r) => r.lineCount },
      {
        key: 'variances',
        header: 'Variances',
        width: 90,
        numeric: true,
        // Labelled rather than coloured: "0 of 412" says more than a green number does.
        render: (r) => `${r.varianceCount}`,
        sortValue: (r) => r.varianceCount,
      },
      {
        key: 'value',
        header: 'Net value',
        width: 110,
        numeric: true,
        render: (r) => formatCurrency(r.netVarianceValue),
        sortValue: (r) => r.netVarianceValue,
      },
      {
        key: 'created',
        header: 'Started',
        width: 150,
        render: (r) => new Date(r.createdAt).toLocaleString(),
      },
    ],
    [],
  );

  const start = async () => {
    if (!locationId) return;

    try {
      const created = await mastersApi.stockCounts.start(locationId, newDepartmentId || undefined);
      setSelectedId(created.id);
      await load();
    } catch (error) {
      toast({ title: 'Could not start a count', description: describe(error), variant: 'destructive' });
    }
  };

  return (
    <BrowseFormShell
      title="Stock counts"
      toolbar={
        canCount ? (
          <>
            <select
              className={filterClass}
              value={newDepartmentId}
              onChange={(event) => setNewDepartmentId(recordIdFrom(event.target.value))}
            >
              <option value="">Whole shop</option>
              {departments.map((department) => (
                <option key={String(department.id)} value={department.id}>
                  {department.name}
                </option>
              ))}
            </select>
            <button type="button" className="pos-button-primary" onClick={() => void start()}>
              Start a count
            </button>
          </>
        ) : null
      }
      filters={
        <select
          className={filterClass}
          value={status}
          onChange={(event) => setStatus(event.target.value as StockCountStatus | '')}
        >
          <option value="">All states</option>
          <option value="InProgress">Counting</option>
          <option value="Posted">Posted</option>
          <option value="Cancelled">Cancelled</option>
        </select>
      }
      grid={
        <DataGrid
          gridId="stock-counts"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          onRowActivate={(row) => setSelectedId(row.id)}
          emptyMessage={loading ? 'Loading…' : 'No counts yet.'}
        />
      }
      form={
        selectedId ? (
          <CountPanel
            key={String(selectedId)}
            countId={selectedId}
            canCount={canCount}
            onClose={() => setSelectedId(null)}
            onChanged={() => void load()}
          />
        ) : null
      }
      status={
        <span className="flex items-center gap-3">
          <span>{rows.length} count(s)</span>
          <span>Double-click a row to open it.</span>
        </span>
      }
    />
  );
}

function describe(error: unknown): string {
  return error instanceof PosApiError ? error.problem.detail : 'Something went wrong.';
}

function CountPanel({
  countId,
  canCount,
  onClose,
  onChanged,
}: {
  countId: number;
  canCount: boolean;
  onClose: () => void;
  onChanged: () => void;
}) {
  const [count, setCount] = useState<StockCount | null>(null);
  const [varianceOnly, setVarianceOnly] = useState(true);
  const [csv, setCsv] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [skipped, setSkipped] = useState<string[]>([]);

  const refresh = useCallback(
    async (only: boolean) => {
      try {
        setCount(await mastersApi.stockCounts.get(countId, only));
      } catch (error) {
        toast({ title: 'Could not open the count', description: describe(error), variant: 'destructive' });
      }
    },
    [countId],
  );

  useEffect(() => {
    void refresh(varianceOnly);
  }, [refresh, varianceOnly]);

  const importCsv = async () => {
    if (!csv.trim()) return;

    setBusy(true);

    try {
      const result = await mastersApi.stockCounts.importCsv(countId, csv);
      setSkipped(result.skipped);
      setCsv('');
      await refresh(varianceOnly);
      onChanged();

      toast({
        title: `${result.imported} imported, ${result.updated} updated`,
        description: result.skipped.length > 0 ? `${result.skipped.length} row(s) did not go in.` : undefined,
      });
    } catch (error) {
      toast({ title: 'Nothing imported', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const run = async (action: () => Promise<StockCount>, success: string) => {
    setBusy(true);

    try {
      setCount(await action());
      onChanged();
      toast({ title: success });
    } catch (error) {
      toast({ title: 'Not done', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  if (!count) {
    return <p className="px-1 text-label text-ink-muted">Loading…</p>;
  }

  const isOpen = count.status === 'InProgress';

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">
          SC-{count.countNumber} — {count.departmentName ?? 'whole shop'}
        </h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Close
        </button>
      </div>

      <FormSection title="Summary">
        <p className="text-body">
          <span className="font-semibold">{statusLabel[count.status]}</span>
          {count.postedAt ? ` · posted ${new Date(count.postedAt).toLocaleString()}` : ''}
        </p>
        <p className="text-body">
          {count.varianceCount} variance(s) out of {count.lineCount} counted —{' '}
          <span className="pos-amount font-semibold">{formatCurrency(count.netVarianceValue)}</span> net.
        </p>
        <p className="text-label text-ink-muted">
          A variance is valued at the item’s average cost at the moment it was counted, so it stays comparable
          even if the item is repriced afterwards.
        </p>
      </FormSection>

      {isOpen && canCount ? (
        <FormSection
          title="Import a count sheet"
          hint="One row per item: code, quantity, and an optional note. A header row is fine."
        >
          <textarea
            className={`${filterClass} h-28 w-full font-mono text-label`}
            placeholder={'StockCode,CountedQty,Notes\nA-1,12\nB-2,0,shelf empty'}
            value={csv}
            onChange={(event) => setCsv(event.target.value)}
          />
          <button type="button" className="pos-button" disabled={busy || !csv.trim()} onClick={() => void importCsv()}>
            Import
          </button>

          {skipped.length > 0 ? (
            <div className="border border-subtle p-2">
              <p className="text-label font-medium">{skipped.length} row(s) did not go in</p>
              <ul className="mt-1 max-h-32 overflow-y-auto text-label text-ink-muted">
                {skipped.map((line) => (
                  <li key={line}>{line}</li>
                ))}
              </ul>
            </div>
          ) : null}
        </FormSection>
      ) : null}

      <FormSection
        title="Lines"
        actions={
          <label className="flex items-center gap-1.5 text-label">
            <input
              type="checkbox"
              checked={varianceOnly}
              onChange={(event) => setVarianceOnly(event.target.checked)}
            />
            Variances only
          </label>
        }
      >
        <table className="w-full text-body">
          <thead className="text-label">
            <tr>
              <th className="py-1 text-left">Code</th>
              <th className="py-1 text-left">Description</th>
              <th className="py-1 text-right">Counted</th>
              <th className="py-1 text-right">System</th>
              <th className="py-1 text-right">Variance</th>
              <th className="py-1 text-right">Value</th>
              {isOpen && canCount ? <th /> : null}
            </tr>
          </thead>
          <tbody>
            {count.lines.map((line) => (
              <tr key={String(line.id)} className="border-t border-subtle">
                <td className="py-1">{line.stockCode}</td>
                <td className="py-1">
                  {line.productName}
                  {line.notes ? (
                    <span className="block text-label text-ink-muted">{line.notes}</span>
                  ) : null}
                </td>
                <td className="py-1 text-right">{line.countedQty}</td>
                <td className="py-1 text-right">{line.systemQtyAtCount}</td>
                {/* Signed rather than coloured — a minus sign reads the same to everyone. */}
                <td className="py-1 text-right font-medium">
                  {line.variance > 0 ? `+${line.variance}` : line.variance}
                </td>
                <td className="py-1 text-right">{formatCurrency(line.varianceValue)}</td>
                {isOpen && canCount ? (
                  <td className="py-1 text-right">
                    <button
                      type="button"
                      className="text-label underline"
                      disabled={busy}
                      onClick={() =>
                        void run(() => mastersApi.stockCounts.removeLine(count.id, line.id), 'Line removed')
                      }
                    >
                      Remove
                    </button>
                  </td>
                ) : null}
              </tr>
            ))}
          </tbody>
        </table>

        {count.lines.length === 0 ? (
          <p className="text-label text-ink-muted">
            {varianceOnly && count.lineCount > 0
              ? `Every one of the ${count.lineCount} counted items agrees with the system.`
              : 'Nothing counted yet.'}
          </p>
        ) : null}
      </FormSection>

      <div className="mb-6 flex flex-wrap gap-2">
        <a
          className="pos-button"
          href={mastersApi.stockCounts.exportUrl(count.id, varianceOnly)}
          target="_blank"
          rel="noopener noreferrer"
        >
          Download the sheet
        </a>

        {isOpen && canCount ? (
          <>
            <input
              className={`${filterClass} w-56`}
              placeholder="Reason (optional)"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
            />
            <button
              type="button"
              className="pos-button-primary"
              disabled={busy || count.lineCount === 0}
              onClick={() => void run(() => mastersApi.stockCounts.post(count.id, reason || undefined), 'Posted')}
            >
              Post — this moves stock
            </button>
            <button
              type="button"
              className="pos-button text-negative"
              disabled={busy}
              onClick={() => void run(() => mastersApi.stockCounts.cancel(count.id), 'Cancelled')}
            >
              Cancel the count
            </button>
          </>
        ) : null}
      </div>

      {isOpen ? (
        <p className="px-1 text-label text-ink-muted">
          Posting sets each item’s on-hand to the counted figure and writes a variance entry to the stock ledger.
          Nothing has moved yet.
        </p>
      ) : null}
    </div>
  );
}
