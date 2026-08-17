'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { Button } from '@/components/ui/button';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { posApi, PosApiError } from '@/lib/pos-api';
import { printPdf } from '@/lib/print';
import { formatCurrency } from '@/lib/utils';
import type { SaleDetail, SaleDetailLine, SalesLogRow, TenderSettings } from '@/types/masters';

/**
 * Previous sales, and what can be done to one.
 *
 * A completed sale is never edited here. Everything on this screen either reads it — open it,
 * reprint it — or writes a *new* transaction against it. That is the only safe meaning of "change
 * the previous sale": the original stands, the correction stands beside it, and the two together
 * are what the books show.
 */
export default function PreviousSalesPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canRefund = auth.can('pos.return');
  const canReprint = auth.can('pos.reprint');

  const today = new Date().toISOString().slice(0, 10);

  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const [term, setTerm] = useState('');
  const [rows, setRows] = useState<SalesLogRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [selected, setSelected] = useState<SaleDetail | null>(null);
  const [tenders, setTenders] = useState<TenderSettings[]>([]);
  const [busy, setBusy] = useState(false);

  // What the cashier has ticked to give back, keyed by sale line.
  const [returning, setReturning] = useState<Record<number, number>>({});
  const [refundTenderId, setRefundTenderId] = useState<number | null>(null);

  const load = useCallback(async () => {
    if (!locationId) return;
    setLoading(true);

    try {
      const page = await mastersApi.sales.log(locationId, { from, to, take: 200 });
      setRows(page.rows);
    } catch {
      toast({ title: 'Could not load sales', description: 'Please try again.', variant: 'destructive' });
    } finally {
      setLoading(false);
    }
  }, [locationId, from, to]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (!locationId) return;

    void mastersApi.settings
      .get(locationId)
      .then((snapshot) => setTenders(snapshot.tenders))
      .catch(() => setTenders([]));
  }, [locationId]);

  /**
   * Filtered in the browser, over the page already fetched.
   *
   * A cashier looking for a sale knows the receipt number, the customer, or roughly what it came
   * to — so all three are matched, rather than making them pick which one they are searching by.
   */
  const visible = useMemo(() => {
    const needle = term.trim().toLowerCase();
    if (!needle) return rows;

    return rows.filter((row) =>
      String(row.transactionNumber).includes(needle)
      || (row.customerName ?? '').toLowerCase().includes(needle)
      || row.staffName.toLowerCase().includes(needle)
      || row.stationCode.toLowerCase().includes(needle)
      || row.grandTotal.toFixed(2).includes(needle),
    );
  }, [rows, term]);

  async function open(id: number) {
    setBusy(true);
    try {
      const detail = await mastersApi.sales.get(id);
      setSelected(detail);
      setReturning({});
      setRefundTenderId(tenders[0]?.id ?? null);
    } catch {
      toast({ title: 'Could not open that sale', variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  }

  const refundTotal = useMemo(() => {
    if (!selected) return 0;

    return selected.lines.reduce((sum, line) => {
      const qty = returning[line.saleLineId] ?? 0;
      if (qty <= 0 || line.quantity === 0) return sum;

      const share = qty / line.quantity;
      return sum + (line.extendedNet + line.tax1Amount + line.tax2Amount) * share;
    }, 0);
  }, [selected, returning]);

  const rounded = Math.round(refundTotal * 100) / 100;

  async function refund() {
    if (!selected) return;

    const lines = Object.entries(returning)
      .map(([saleLineId, quantity]) => ({ saleLineId: Number(saleLineId), quantity }))
      .filter((line) => line.quantity > 0);

    if (lines.length === 0) {
      toast({ title: 'Nothing selected', description: 'Choose what is being returned.' });
      return;
    }

    if (refundTenderId === null) {
      toast({ title: 'Choose how to refund', description: 'Pick how the money goes back.' });
      return;
    }

    setBusy(true);

    try {
      const result = await mastersApi.sales.refund(
        selected.id,
        lines,
        [{ tenderTypeId: refundTenderId, amount: rounded }],
      );

      toast({
        title: `Refunded ${formatCurrency(result.refundedTotal)}`,
        description: `Receipt ${result.refundTransactionNumber}.`,
      });

      await open(selected.id);
      await load();
    } catch (error) {
      toast({
        title: 'Refund not completed',
        description: error instanceof Error ? error.message : 'Nothing was refunded. Please try again.',
        variant: 'destructive',
      });
    } finally {
      setBusy(false);
    }
  }

  /**
   * Opens the print dialog for this receipt.
   *
   * It used to post to the till's thermal printer and answer "Receipt sent to the printer", which
   * was not something the screen could know. The request only reaches the server; whether a printer
   * exists, is switched on, or has paper is decided far past the point where that toast was shown.
   * On a till whose printer was offline it announced success and produced nothing, which is the
   * failure a back-office user is least equipped to diagnose — they are not stood next to the till.
   *
   * The browser's own dialog is honest by construction: the operator sees the receipt, picks a
   * printer, and knows whether it printed because they watched it happen.
   */
  async function reprint() {
    if (!selected) return;

    setBusy(true);

    try {
      const outcome = await printPdf(await posApi.receiptPdf(selected.id));

      if (outcome === 'blocked') {
        toast({
          title: 'Pop-up blocked',
          description: 'Allow pop-ups for this site to print the receipt.',
          variant: 'destructive',
        });
      }
    } catch (error) {
      toast({
        title: 'Could not print the receipt',
        description:
          error instanceof PosApiError ? error.problem.detail : 'The receipt could not be produced.',
        variant: 'destructive',
      });
    } finally {
      setBusy(false);
    }
  }

  /** The thermal printer at the till, for a shop that wants the slip where the customer is. */
  async function sendToTill() {
    if (!selected) return;

    try {
      await mastersApi.sales.reprint(selected.id);
      toast({
        title: 'Sent to the till printer',
        description: 'If nothing prints, the till printer is offline — use Print instead.',
      });
    } catch {
      toast({
        title: 'Could not send it',
        description: 'The till did not accept the receipt.',
        variant: 'destructive',
      });
    }
  }

  const columns: Array<DataGridColumn<SalesLogRow>> = [
    {
      key: 'transactionNumber',
      header: 'Receipt',
      width: 90,
      numeric: true,
      render: (r) => `#${r.transactionNumber}`,
      sortValue: (r) => r.transactionNumber,
    },
    {
      key: 'completedAt',
      header: 'When',
      width: 170,
      render: (r) => new Date(r.completedAt).toLocaleString(),
      sortValue: (r) => r.completedAt,
    },
    { key: 'stationCode', header: 'Till', width: 70, render: (r) => r.stationCode },
    { key: 'staffName', header: 'Served by', width: 140, render: (r) => r.staffName },
    { key: 'customerName', header: 'Customer', width: 160, render: (r) => r.customerName ?? '—' },
    { key: 'lineCount', header: 'Items', width: 70, numeric: true, render: (r) => r.lineCount },
    {
      key: 'grandTotal',
      header: 'Total',
      width: 120,
      numeric: true,
      render: (r) => formatCurrency(r.grandTotal),
      sortValue: (r) => r.grandTotal,
    },
    {
      key: 'status',
      header: 'Status',
      width: 110,
      render: (r) => (
        <span className={r.status === 'Completed' ? 'text-emerald-600' : 'text-amber-600'}>{r.status}</span>
      ),
    },
  ];

  return (
    <div className="flex h-full flex-col gap-4 p-4">
      <header className="flex flex-wrap items-end gap-3">
        <div>
          <h1 className="text-lg font-semibold">Previous sales</h1>
          <p className="text-sm text-muted-foreground">
            Find a sale to reprint its receipt or take something back.
          </p>
        </div>

        <div className="ml-auto flex flex-wrap items-end gap-2">
          <label className="flex flex-col text-xs">
            From
            <input type="date" className="pos-input" value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
          <label className="flex flex-col text-xs">
            To
            <input type="date" className="pos-input" value={to} onChange={(e) => setTo(e.target.value)} />
          </label>
          <label className="flex flex-col text-xs">
            Search
            <input
              className="pos-input"
              placeholder="Receipt no., customer, total"
              value={term}
              onChange={(e) => setTerm(e.target.value)}
            />
          </label>
        </div>
      </header>

      <div className="grid flex-1 gap-4 overflow-hidden lg:grid-cols-[1.4fr_1fr]">
        <section className="min-h-0 overflow-auto rounded border">
          <DataGrid
            gridId="previous-sales"
            rows={visible}
            columns={columns}
            emptyMessage={loading ? 'Loading…' : 'No sales in that period.'}
            onRowActivate={(row) => void open(row.id)}
            rowKey={(row) => row.id}
          />
        </section>

        <section className="min-h-0 overflow-auto rounded border p-4">
          {!selected ? (
            <p className="text-sm text-muted-foreground">Choose a sale to see what was on it.</p>
          ) : (
            <div className="flex flex-col gap-4">
              <div>
                <h2 className="text-base font-semibold">Receipt #{selected.transactionNumber}</h2>
                <p className="text-sm text-muted-foreground">
                  {new Date(selected.completedAt).toLocaleString()} · Till {selected.stationCode} ·{' '}
                  {selected.staffName || 'Unknown'}
                  {selected.customerName ? ` · ${selected.customerName}` : ''}
                </p>
                {selected.status !== 'Completed' && (
                  <p className="mt-1 text-sm text-amber-600">
                    This sale is {selected.status.toLowerCase()}
                    {selected.voidReason ? ` — ${selected.voidReason}` : ''}.
                  </p>
                )}
              </div>

              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b text-left text-xs text-muted-foreground">
                    <th className="py-1">Item</th>
                    <th className="py-1 text-right">Sold</th>
                    <th className="py-1 text-right">Back</th>
                    <th className="py-1 text-right">Return</th>
                  </tr>
                </thead>
                <tbody>
                  {selected.lines.map((line) => (
                    <LineRow
                      key={line.saleLineId || line.sequence}
                      line={line}
                      value={returning[line.saleLineId] ?? 0}
                      disabled={!canRefund || selected.status !== 'Completed'}
                      onChange={(quantity) =>
                        setReturning((current) => ({ ...current, [line.saleLineId]: quantity }))
                      }
                    />
                  ))}
                </tbody>
              </table>

              <dl className="grid grid-cols-2 gap-x-4 text-sm">
                <dt className="text-muted-foreground">Subtotal</dt>
                <dd className="text-right">{formatCurrency(selected.subtotal)}</dd>
                <dt className="text-muted-foreground">{selected.tax1Name || 'Tax 1'}</dt>
                <dd className="text-right">{formatCurrency(selected.tax1Total)}</dd>
                <dt className="text-muted-foreground">{selected.tax2Name || 'Tax 2'}</dt>
                <dd className="text-right">{formatCurrency(selected.tax2Total)}</dd>
                <dt className="font-semibold">Total</dt>
                <dd className="text-right font-semibold">{formatCurrency(selected.grandTotal)}</dd>
              </dl>

              {/*
                Two groups, separated, because they are two different intentions and one of them
                moves money. Printing sits on the left as an ordinary action; refunding is boxed on
                the right, so nobody reaches it while meaning to reach the other. They used to be a
                single flat row of controls in which a dropdown, a label and two buttons all had
                different heights and no stated relationship to each other.
              */}
              <div className="flex flex-col gap-3 border-t pt-3 sm:flex-row sm:items-start sm:justify-between">
                {canReprint ? (
                  <div className="flex flex-wrap gap-2">
                    <Button onClick={() => void reprint()} disabled={busy}>
                      Print receipt
                    </Button>

                    {/*
                      The till's own printer, kept but no longer the default. It is the right answer
                      only when the customer is stood at that till, and it cannot report whether
                      anything came out -- the request stops at the server.
                    */}
                    <Button variant="outline" onClick={() => void sendToTill()} disabled={busy}>
                      Send to till printer
                    </Button>
                  </div>
                ) : (
                  <span />
                )}

                {canRefund && selected.status === 'Completed' && (
                  <div className="rounded-md border border-subtle bg-panel-sunken p-3 sm:min-w-[19rem]">
                    {rounded <= 0 ? (
                      // Says what to do instead of offering a dead button. A disabled control
                      // reading "Refund Rs 0.00" states an amount nobody asked for and gives no
                      // hint that the quantity boxes above are what make it live.
                      <p className="text-xs text-ink-muted">
                        Enter a quantity in <strong>Return</strong> against the items coming back,
                        then choose how the money goes out.
                      </p>
                    ) : (
                      <div className="flex flex-wrap items-end justify-between gap-2">
                        <label className="flex flex-col gap-1 text-xs text-ink-muted">
                          Refund to
                          <select
                            className="pos-input"
                            value={refundTenderId ?? ''}
                            onChange={(e) => setRefundTenderId(Number(e.target.value))}
                          >
                            {tenders.map((tender) => (
                              <option key={tender.id} value={tender.id}>
                                {tender.displayName}
                              </option>
                            ))}
                          </select>
                        </label>

                        <Button onClick={() => void refund()} disabled={busy}>
                          {busy ? 'Refunding…' : `Refund ${formatCurrency(rounded)}`}
                        </Button>
                      </div>
                    )}
                  </div>
                )}
              </div>
            </div>
          )}
        </section>
      </div>
    </div>
  );
}

/**
 * One line of the original sale, with what may still be given back.
 *
 * The quantity box is capped at what is left rather than at what was sold, so a second visit
 * cannot offer the shirt that came back on the first — the server refuses it either way, but a
 * cashier should not be able to promise a customer something that is then refused at the counter.
 */
function LineRow({
  line,
  value,
  disabled,
  onChange,
}: {
  line: SaleDetailLine;
  value: number;
  disabled: boolean;
  onChange: (quantity: number) => void;
}) {
  const returnable = line.refundableQuantity;

  return (
    <tr className="border-b last:border-0">
      <td className="py-1.5">
        <div>{line.name}</div>
        <div className="text-xs text-muted-foreground">
          {line.stockCode}
          {line.epc ? ` · tag ${line.epc.slice(-8)}` : ''}
        </div>
      </td>
      <td className="py-1.5 text-right tabular-nums">{line.quantity}</td>
      <td className="py-1.5 text-right tabular-nums text-muted-foreground">
        {line.refundedQuantity > 0 ? line.refundedQuantity : '—'}
      </td>
      <td className="py-1.5 text-right">
        {returnable > 0 ? (
          <input
            type="number"
            className="pos-input w-20 text-right"
            min={0}
            max={returnable}
            step={line.epc ? 1 : 'any'}
            value={value || ''}
            disabled={disabled}
            onChange={(e) => {
              const next = Number(e.target.value);
              onChange(Number.isFinite(next) ? Math.min(Math.max(next, 0), returnable) : 0);
            }}
          />
        ) : (
          <span className="text-xs text-muted-foreground">nothing left</span>
        )}
      </td>
    </tr>
  );
}
