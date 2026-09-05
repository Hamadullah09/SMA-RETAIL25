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
import { PageHeader } from '@/components/shell/page-header';
import { DomainStatusBadge } from '@/components/ui/status-badge';
import { KpiTile } from '@/components/dashboard/kpi';
import { Banknote, Coins, Printer, Receipt, ReceiptText, Undo2 } from 'lucide-react';

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

  /**
   * The period in four figures, from the rows already fetched.
   *
   * No extra call: everything here is summed from the same page the grid is drawing, so the strip
   * costs a loop rather than a round trip and can never disagree with the list underneath it.
   *
   * It answers the three questions somebody actually opens this screen with -- how much did we
   * take, over how many sales, and did anything go back -- which a bare grid of receipt numbers
   * makes you add up by eye.
   */
  const summary = useMemo(() => {
    let takings = 0;
    let completed = 0;
    let reversed = 0;

    for (const row of visible) {
      // Voided and refunded transactions are counted, not banked. Summing every row would report
      // takings the drawer never held, which is the one number on this screen that has to be right.
      if (row.status === 'Completed') {
        takings += row.grandTotal;
        completed += 1;
      } else {
        reversed += 1;
      }
    }

    return { takings, completed, reversed, average: completed > 0 ? takings / completed : 0 };
  }, [visible]);

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
      width: 150,
      render: (r) => <DomainStatusBadge status={r.status} />,
    },
  ];

  return (
    <div className="flex h-full flex-col">
      <PageHeader
        title="Previous sales"
        description="Find a sale to reprint its receipt or take something back."
      />

      <div className="flex min-h-0 flex-1 flex-col gap-4 px-page py-panel">
        {/*
          The period in four figures.

          A grid of receipt numbers makes somebody add up the day by eye. These are summed from the
          rows already on screen, so they cost nothing and cannot disagree with the list.
        */}
        <div className="grid shrink-0 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <KpiTile
            label="Takings"
            value={formatCurrency(summary.takings)}
            hint={term ? 'Matching your search' : 'Completed sales only'}
            tone="positive"
            icon={Banknote}
          />
          <KpiTile label="Sales" value={summary.completed} hint="Completed" icon={Receipt} />
          <KpiTile
            label="Average sale"
            value={formatCurrency(summary.average)}
            hint="Takings divided by sales"
            icon={Coins}
          />
          <KpiTile
            label="Returned or voided"
            value={summary.reversed}
            hint={summary.reversed > 0 ? 'Not counted in takings' : 'Nothing went back'}
            // The tone follows the figure: a zero here is good news and should not be drawn in the
            // colour that means something is wrong.
            tone={summary.reversed > 0 ? 'warning' : 'neutral'}
            icon={Undo2}
          />
        </div>

        {/*
          The filters, in the sunken bar every other browse screen uses.

          They were three loose inputs floating on the page ground with the labels stacked above
          them, so the row read as part of the content rather than as the controls over it.
        */}
        <div className="flex shrink-0 flex-wrap items-end gap-3 rounded-md border border-subtle bg-panel-sunken px-3 py-2.5">
          <label className="flex flex-col gap-1 text-label font-medium text-ink">
            From
            <input type="date" className="pos-input" value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
          <label className="flex flex-col gap-1 text-label font-medium text-ink">
            To
            <input type="date" className="pos-input" value={to} onChange={(e) => setTo(e.target.value)} />
          </label>
          <label className="flex min-w-[16rem] flex-1 flex-col gap-1 text-label font-medium text-ink">
            Search
            <input
              className="pos-input"
              aria-label="Search sales"
              placeholder="Receipt no., customer, total"
              value={term}
              onChange={(e) => setTerm(e.target.value)}
            />
          </label>
        </div>

      <div className="grid min-h-0 flex-1 gap-4 overflow-hidden lg:grid-cols-[1.4fr_minmax(0,27rem)]">
        <section className="pos-panel min-h-0 overflow-hidden">
          <DataGrid
            gridId="previous-sales"
            rows={visible}
            columns={columns}
            loading={loading}
            emptyMessage="No sales in that period."
            onRowActivate={(row) => void open(row.id)}
            rowKey={(row) => row.id}
          />
        </section>

        {/*
          The sale, drawn as the receipt it is.

          This was a bordered box holding an unstyled heading, a four-column table and a definition
          list -- accurate, and shaped like nothing in particular. A receipt is the one document
          everybody in a shop already knows how to read, so the panel is laid out as one: the lines,
          a rule, the build-up, the total, then what was handed over. Somebody comparing this screen
          against the paper slip in their hand is doing a visual match rather than a translation.

          The figures are mono and right-aligned, which is what makes the column scannable and is
          the same treatment the till gives them.
        */}
        <section className="pos-panel min-h-0 overflow-y-auto">
          {!selected ? (
            <div className="flex h-full flex-col items-center justify-center gap-3 p-8 text-center">
              <span
                aria-hidden
                className="flex h-14 w-14 items-center justify-center rounded-full bg-panel-sunken text-ink-muted"
              >
                <ReceiptText className="h-7 w-7" />
              </span>
              <p className="text-body font-medium text-ink">No sale open</p>
              <p className="max-w-[22rem] text-body text-ink-muted">
                Pick a row on the left to see everything that was on it, reprint the receipt, or take
                something back.
              </p>
            </div>
          ) : (
            <div className="flex flex-col">
              <header className="border-b border-subtle px-4 py-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h2 className="text-h3 font-semibold text-ink">
                    Receipt <span className="pos-amount">#{selected.transactionNumber}</span>
                  </h2>
                  <DomainStatusBadge status={selected.status} />
                </div>

                <p className="mt-1 text-label text-ink-muted">
                  {new Date(selected.completedAt).toLocaleString()} · Till {selected.stationCode} ·{' '}
                  {selected.staffName || 'Unknown'}
                  {selected.customerName ? ` · ${selected.customerName}` : ''}
                </p>

                {selected.status !== 'Completed' && (
                  // The badge above already carries the state; this carries the reason, which is
                  // the part somebody actually needs. `text-amber-600` was a raw palette colour
                  // that does not move with the theme.
                  <p className="mt-2 rounded-md bg-warning-soft px-3 py-2 text-label font-medium text-warning-text">
                    This sale is {selected.status.toLowerCase()}
                    {selected.voidReason ? ` — ${selected.voidReason}` : ''}.
                  </p>
                )}
              </header>

              <table className="w-full px-4 text-body">
                <thead>
                  <tr className="text-left text-caption uppercase tracking-wide text-ink-muted">
                    <th className="py-2 pl-4 font-medium">Item</th>
                    {/* Narrow columns need their own gutters; without them "SOLD" and "BACK" met
                        in the middle and read as one word. */}
                    <th className="w-14 px-2 py-2 text-right font-medium">Sold</th>
                    <th className="w-14 px-2 py-2 text-right font-medium">Back</th>
                    <th className="w-24 py-2 pl-2 pr-4 text-right font-medium">Return</th>
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

              <dl className="mt-1 border-t border-dashed border-strong px-4 py-3 text-body">
                <Money label="Subtotal" value={selected.subtotal} />
                {selected.discountTotal > 0 ? (
                  <Money label="Discounts" value={-selected.discountTotal} />
                ) : null}
                {selected.tax1Total !== 0 ? (
                  <Money label={selected.tax1Name || 'Tax 1'} value={selected.tax1Total} />
                ) : null}
                {selected.tax2Total !== 0 ? (
                  <Money label={selected.tax2Name || 'Tax 2'} value={selected.tax2Total} />
                ) : null}
                {selected.addOnCharge !== 0 ? (
                  <Money label="Charge" value={selected.addOnCharge} />
                ) : null}

                {/* The figure the customer paid, at the size it is read at. */}
                <div className="mt-2 flex items-baseline justify-between gap-3 border-t border-strong pt-2">
                  <dt className="text-label font-semibold uppercase tracking-wide text-ink-muted">Total</dt>
                  <dd className="pos-amount text-h3 font-semibold text-ink">
                    {formatCurrency(selected.grandTotal)}
                  </dd>
                </div>
              </dl>

              {/*
                How it was paid, which the screen already had and never showed.

                `SaleDetail.tenders` was fetched on every open and dropped on the floor. "Was this
                cash or card?" is one of the two questions somebody opens an old sale to answer --
                the other being what was on it -- and the answer was a round trip away for no
                reason.
              */}
              {selected.tenders.length > 0 ? (
                <dl className="border-t border-dashed border-strong px-4 py-3 text-body">
                  {selected.tenders.map((tender, index) => (
                    <Money
                      key={`${tender.tenderName}-${index}`}
                      label={tender.tenderName}
                      value={tender.amount}
                    />
                  ))}
                  {selected.changeGiven > 0 ? (
                    <Money label="Change given" value={selected.changeGiven} />
                  ) : null}
                </dl>
              ) : null}

              {/*
                Two groups, separated, because they are two different intentions and one of them
                moves money. Printing sits on the left as an ordinary action; refunding is boxed on
                the right, so nobody reaches it while meaning to reach the other. They used to be a
                single flat row of controls in which a dropdown, a label and two buttons all had
                different heights and no stated relationship to each other.
              */}
              <div className="flex flex-col gap-3 border-t border-subtle px-4 py-3 sm:flex-row sm:items-start sm:justify-between">
                {canReprint ? (
                  <div className="flex flex-wrap gap-2">
                    <Button variant="default" onClick={() => void reprint()} disabled={busy}>
                      <Printer className="h-5 w-5 shrink-0" aria-hidden />
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
                      <p className="text-label text-ink-muted">
                        Enter a quantity in <strong className="font-semibold text-ink">Return</strong>{' '}
                        against the items coming back, then choose how the money goes out.
                      </p>
                    ) : (
                      <div className="flex flex-wrap items-end justify-between gap-2">
                        <label className="flex flex-col gap-1 text-label font-medium text-ink">
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

                        <Button variant="destructive" onClick={() => void refund()} disabled={busy}>
                          <Undo2 className="h-5 w-5 shrink-0" aria-hidden />
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
    <tr className="border-t border-subtle align-top">
      <td className="py-2 pl-4">
        <div className="font-medium text-ink">{line.name}</div>
        {/*
          `text-muted-foreground` is a shadcn token this app does not define, so it resolved to
          nothing and this line inherited full-strength ink -- the stock code was drawn exactly as
          loud as the product name it sits under. There were nine of them on this screen.
        */}
        <div className="font-mono text-caption text-ink-muted">
          {line.stockCode}
          {line.epc ? ` · tag ${line.epc.slice(-8)}` : ''}
        </div>
      </td>
      <td className="pos-amount px-2 py-2 text-right text-ink">{line.quantity}</td>
      <td className="pos-amount px-2 py-2 text-right text-ink-muted">
        {line.refundedQuantity > 0 ? line.refundedQuantity : '—'}
      </td>
      <td className="py-2 pl-2 pr-4 text-right">
        {returnable > 0 ? (
          <input
            type="number"
            aria-label={`Return quantity for ${line.name}`}
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
          <span className="text-caption text-ink-muted">nothing left</span>
        )}
      </td>
    </tr>
  );
}

/**
 * A label and a figure, on one baseline.
 *
 * The build-up was a two-column `<dl>` with the labels in a dead class, so every line of the
 * arithmetic read at the same weight as the total underneath it. One row, mono figure, right
 * aligned -- the column scans downwards the way a receipt does.
 */
function Money({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex items-baseline justify-between gap-3 py-0.5">
      <dt className="text-ink-muted">{label}</dt>
      <dd className="pos-amount text-ink">{formatCurrency(value)}</dd>
    </div>
  );
}
