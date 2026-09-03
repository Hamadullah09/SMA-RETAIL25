'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { BrowseFormShell, FormSection } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi, type SalesLogFilters } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { cn, formatCurrency } from '@/lib/utils';
import type { SaleDetail, SalesLogRow } from '@/types/masters';
import { describeError } from '@/lib/errors';

/**
 * The itemized sales log (guide p.14–15, p.101) — and, because they are the same question asked from
 * two places, POS history.
 *
 * Voided sales stay in the list by default with their status showing. Hiding them would make the log
 * disagree with the ledger, and reconciling a drawer against a log that quietly drops rows is how a
 * shortage becomes unexplainable.
 */
export default function SalesLogPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  // An environment variable is a string; the station it names is a row. undefined when unset, so a
  // reprint button stays disabled rather than addressing station 0.
  const stationId = process.env.NEXT_PUBLIC_STATION_ID ? Number(process.env.NEXT_PUBLIC_STATION_ID) : undefined;

  const [from, setFrom] = useState(() => isoDate(-7));
  const [to, setTo] = useState(() => isoDate(0));
  const [includeVoided, setIncludeVoided] = useState(true);
  const [rows, setRows] = useState<SalesLogRow[]>([]);
  const [totals, setTotals] = useState({ count: 0, grandTotal: 0 });
  const [loading, setLoading] = useState(false);
  const [selected, setSelected] = useState<SaleDetail | null>(null);

  const filters = useMemo<SalesLogFilters>(() => ({ from, to, includeVoided, take: 500 }), [from, to, includeVoided]);

  const load = useCallback(async () => {
    if (!locationId) return;

    setLoading(true);

    try {
      const page = await mastersApi.sales.log(locationId, filters);
      setRows(page.rows);
      setTotals({ count: page.totalCount, grandTotal: page.grandTotal });
    } catch (error) {
      toast({ title: 'Could not load the sales log', description: describeError(error), variant: 'destructive' });
    } finally {
      setLoading(false);
    }
  }, [locationId, filters]);

  useEffect(() => {
    void load();
  }, [load]);

  const open = async (row: SalesLogRow) => {
    try {
      setSelected(await mastersApi.sales.get(row.id));
    } catch (error) {
      toast({ title: 'Could not open the sale', description: describeError(error), variant: 'destructive' });
    }
  };

  const columns = useMemo<DataGridColumn<SalesLogRow>[]>(
    () => [
      {
        key: 'number',
        header: 'No.',
        width: 90,
        numeric: true,
        render: (r) => r.transactionNumber,
        sortValue: (r) => r.transactionNumber,
      },
      {
        key: 'completed',
        header: 'Completed',
        width: 160,
        render: (r) => new Date(r.completedAt).toLocaleString(),
        sortValue: (r) => r.completedAt,
      },
      { key: 'station', header: 'Till', width: 60, render: (r) => r.stationCode },
      { key: 'staff', header: 'Staff', width: 140, render: (r) => r.staffName },
      { key: 'customer', header: 'Customer', width: 180, render: (r) => r.customerName ?? '—' },
      { key: 'lines', header: 'Lines', width: 60, numeric: true, render: (r) => r.lineCount },
      { key: 'subtotal', header: 'Subtotal', width: 100, numeric: true, render: (r) => formatCurrency(r.subtotal) },
      {
        key: 'discount',
        header: 'Discount',
        width: 100,
        numeric: true,
        render: (r) => (r.discountTotal === 0 ? '—' : formatCurrency(r.discountTotal)),
      },
      { key: 'tax', header: 'Tax', width: 100, numeric: true, render: (r) => formatCurrency(r.tax1Total + r.tax2Total) },
      {
        key: 'total',
        header: 'Total',
        width: 110,
        numeric: true,
        render: (r) => formatCurrency(r.grandTotal),
        sortValue: (r) => r.grandTotal,
      },
      {
        key: 'status',
        header: 'Status',
        width: 90,
        // A voided sale is not a footnote — it is the row someone is usually looking for.
        render: (r) => (
          <span className={r.status !== 'Completed' ? 'text-negative' : undefined}>{r.status}</span>
        ),
      },
    ],
    [],
  );

  if (!locationId) {
    return <p className="text-body text-ink-muted">No location is attached to this session.</p>;
  }

  return (
    <BrowseFormShell
      title="Sales log"
      toolbar={
        <a className="pos-button" href={mastersApi.sales.exportUrl(locationId, filters)} download>
          Export CSV
        </a>
      }
      filters={
        <>
          <label className="flex items-center gap-1.5">
            From
            <input
              type="date"
              className="pos-input"
              value={from}
              onChange={(event) => setFrom(event.target.value)}
            />
          </label>

          <label className="flex items-center gap-1.5">
            To
            <input
              type="date"
              className="pos-input"
              value={to}
              onChange={(event) => setTo(event.target.value)}
            />
          </label>

          <label className="flex items-center gap-1.5">
            <input type="checkbox" checked={includeVoided} onChange={(event) => setIncludeVoided(event.target.checked)} />
            Show voided
          </label>
        </>
      }
      grid={
        <DataGrid
          gridId="sales-log"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          onRowActivate={(row) => void open(row)}
          loading={loading}
          emptyMessage="No sales in this window."
        />
      }
      form={selected ? <SaleDetailPanel sale={selected} stationId={stationId} onClose={() => setSelected(null)} /> : null}
      status={
        <span className="flex items-center gap-4">
          <span>
            {totals.count} sale{totals.count === 1 ? '' : 's'}
          </span>
          <span>
            Total taken <span className="pos-amount">{formatCurrency(totals.grandTotal)}</span>
          </span>
          <span>Double-click a sale to see its lines and reprint it.</span>
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

function SaleDetailPanel({
  sale,
  stationId,
  onClose,
}: {
  sale: SaleDetail;
  stationId: number | undefined;
  onClose: () => void;
}) {
  const [busy, setBusy] = useState(false);

  const reprint = async () => {
    if (!stationId) {
      // Reprinting needs a till to print on. Saying so beats a request that fails server-side with
      // something the user cannot act on.
      toast({
        title: 'No till configured',
        description: 'Set NEXT_PUBLIC_STATION_ID on this machine to reprint from it.',
        variant: 'destructive',
      });
      return;
    }

    setBusy(true);

    try {
      await mastersApi.sales.reprint(sale.id, stationId);
      toast({ variant: 'success', title: 'Sent to the printer' });
    } catch (error) {
      toast({ title: 'Could not reprint', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">
          Sale {sale.transactionNumber}
          {sale.status !== 'Completed' ? (
            <span className="pos-badge ml-2 text-negative">{sale.status}</span>
          ) : null}
        </h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Close
        </button>
      </div>

      <FormSection
        title="Summary"
        actions={
          <button type="button" className="underline" disabled={busy} onClick={() => void reprint()}>
            Reprint
          </button>
        }
      >
        <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-label">
          <dt className="text-ink-muted">Completed</dt>
          <dd>{new Date(sale.completedAt).toLocaleString()}</dd>
          <dt className="text-ink-muted">Till</dt>
          <dd>{sale.stationCode}</dd>
          <dt className="text-ink-muted">Staff</dt>
          <dd>{sale.staffName}</dd>
          <dt className="text-ink-muted">Customer</dt>
          <dd>{sale.customerName ?? '—'}</dd>
        </dl>

        {sale.voidReason ? (
          <p className="text-label text-negative">Voided: {sale.voidReason}</p>
        ) : null}
      </FormSection>

      <FormSection title={`Lines (${sale.lines.length})`}>
        <table className="pos-table">
          <thead className="text-ink-muted">
            <tr>
              <th className="text-left">Item</th>
              <th className="text-right">Qty</th>
              <th className="text-right">Price</th>
              <th className="text-right">Net</th>
            </tr>
          </thead>
          <tbody>
            {sale.lines.map((line) => (
              <tr key={line.sequence} className={cn(line.extendedNet < 0 && 'text-negative')}>
                <td className="truncate">
                  <span className="pos-amount">{line.stockCode}</span> {line.name}
                </td>
                <td className="pos-amount text-right">{line.quantity}</td>
                <td className="pos-amount text-right">{formatCurrency(line.unitPrice)}</td>
                <td className="pos-amount text-right">{formatCurrency(line.extendedNet)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </FormSection>

      <FormSection title="Totals" hint="These are the figures stored on the sale, not recomputed — a reprint shows what was charged.">
        <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-label">
          <dt className="text-ink-muted">Subtotal</dt>
          <dd className="pos-amount text-right">{formatCurrency(sale.subtotal)}</dd>
          <dt className="text-ink-muted">Discount</dt>
          <dd className="pos-amount text-right">{formatCurrency(sale.discountTotal)}</dd>
          <dt className="text-ink-muted">{sale.tax1Name || 'Tax 1'}</dt>
          <dd className="pos-amount text-right">{formatCurrency(sale.tax1Total)}</dd>
          <dt className="text-ink-muted">{sale.tax2Name || 'Tax 2'}</dt>
          <dd className="pos-amount text-right">{formatCurrency(sale.tax2Total)}</dd>
          <dt className="font-medium">Total</dt>
          <dd className="pos-amount text-right font-medium">{formatCurrency(sale.grandTotal)}</dd>
        </dl>
      </FormSection>

      <FormSection title="Payment">
        <ul className="space-y-0.5 text-label">
          {sale.tenders.map((tender, index) => (
            <li key={`${tender.tenderName}-${index}`} className="flex justify-between">
              <span>
                {tender.tenderName}
                {tender.reference ? ` · ${tender.reference}` : ''}
              </span>
              <span className="pos-amount">{formatCurrency(tender.amount)}</span>
            </li>
          ))}
          {sale.changeGiven > 0 ? (
            <li className="flex justify-between text-ink-muted">
              <span>Change</span>
              <span className="pos-amount">{formatCurrency(sale.changeGiven)}</span>
            </li>
          ) : null}
        </ul>
      </FormSection>
    </div>
  );
}
