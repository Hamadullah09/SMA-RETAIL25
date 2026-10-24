'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ArrowLeft, Download, Printer, ReceiptText } from 'lucide-react';
import { mastersApi } from '@/lib/masters-api';
import { posApi } from '@/lib/pos-api';
import { downloadPdf, printPdf } from '@/lib/print';
import { formatCurrency, useCurrency } from '@/lib/currency';
import { useAuth } from '@/lib/auth-config';
import { describeError } from '@/lib/errors';
import { toast } from '@/components/ui/toaster';
import { DomainStatusBadge, StatusBadge } from '@/components/ui/status-badge';
import { cn } from '@/lib/utils';
import type { CustomerSaleFilter, CustomerSaleRow, SaleDetail, SaleDetailLine } from '@/types/masters';

/**
 * A customer's own sales, and any one of them opened as its invoice.
 *
 * Asked for by a shop: click a customer, see what they have bought, filter it, open an old invoice —
 * and a cashier must be able to, with the customer standing at the counter. The sales log already
 * had every sale, but it is a takings report, gated on a permission cashiers do not hold. This reads
 * the customer-scoped endpoints instead, which need only `customer.read` and answer only for that
 * customer's sales.
 *
 * Used twice: on the customer screen in the back office, and from the till's client window (F5).
 * One component, so the two can never disagree about what a customer bought.
 */

const PAGE_SIZE = 25;

type Period = 'all' | '30' | '90' | '365' | 'custom';

const PERIODS: Array<{ value: Period; label: string }> = [
  { value: 'all', label: 'All time' },
  { value: '30', label: 'Last 30 days' },
  { value: '90', label: 'Last 3 months' },
  { value: '365', label: 'Last 12 months' },
  { value: 'custom', label: 'Between dates…' },
];

/** A local calendar date as the API takes it: yyyy-mm-dd, never a UTC midnight that drifts a day. */
function isoDate(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function daysAgo(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() - days);
  return isoDate(date);
}

function when(value: string): string {
  return new Date(value).toLocaleString(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function count(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(3).replace(/0+$/, '').replace(/\.$/, '');
}

/* --------------------------------------------------------------------------- the list */

export function CustomerSalesHistory({
  customerId,
  onOpen,
}: {
  customerId: number;
  onOpen: (transactionId: number) => void;
}) {
  useCurrency();

  const [period, setPeriod] = useState<Period>('all');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [receipt, setReceipt] = useState('');
  const [item, setItem] = useState('');
  const [show, setShow] = useState<CustomerSaleFilter>('All');

  const [rows, setRows] = useState<CustomerSaleRow[]>([]);
  const [totals, setTotals] = useState<{ count: number; spent: number; hasMore: boolean } | null>(null);
  const [loading, setLoading] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  // Filters change faster than answers arrive. Only the answer to the latest question is drawn,
  // so a slow reply to an old filter cannot overwrite the list for the new one.
  const latest = useRef(0);

  const trimmedReceipt = receipt.trim().replace(/^#/, '');
  const receiptNumber = /^\d+$/.test(trimmedReceipt) ? Number(trimmedReceipt) : undefined;
  const receiptInvalid = trimmedReceipt !== '' && receiptNumber === undefined;

  const filters = useMemo(() => {
    const range =
      period === 'custom'
        ? { from: from || undefined, to: to || undefined }
        : period === 'all'
          ? {}
          : { from: daysAgo(Number(period)) };

    return { ...range, number: receiptNumber, item: item.trim() || undefined, show };
  }, [period, from, to, receiptNumber, item, show]);

  const load = useCallback(
    async (skip: number) => {
      const ask = ++latest.current;
      setLoading(true);

      try {
        const page = await mastersApi.customers.sales(customerId, { ...filters, skip, take: PAGE_SIZE });
        if (ask !== latest.current) return;

        setRows((current) => (skip === 0 ? page.rows : [...current, ...page.rows]));
        setTotals({ count: page.totalCount, spent: page.totalSpent, hasMore: page.hasMore });
        setFailure(null);
      } catch (error) {
        if (ask === latest.current) setFailure(describeError(error));
      } finally {
        if (ask === latest.current) setLoading(false);
      }
    },
    [customerId, filters],
  );

  useEffect(() => {
    if (receiptInvalid) return undefined;

    const timer = window.setTimeout(() => void load(0), 250);
    return () => window.clearTimeout(timer);
  }, [load, receiptInvalid]);

  const filtered = period !== 'all' || receiptNumber !== undefined || item.trim() !== '' || show !== 'All';

  return (
    <div className="flex flex-col gap-3">
      {/* Wrapping rather than a viewport grid: this sits in a narrow side panel on the customer
          screen and in a wide dialog at the till, and only its own width says how many fit. */}
      <div className="flex flex-wrap gap-2">
        <label className="flex min-w-[9.5rem] flex-1 flex-col gap-1 text-label font-medium text-ink">
          When
          <select className="pos-input" value={period} onChange={(event) => setPeriod(event.target.value as Period)}>
            {PERIODS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>

        <label className="flex min-w-[9.5rem] flex-1 flex-col gap-1 text-label font-medium text-ink">
          Receipt number
          <input
            className="pos-input"
            inputMode="numeric"
            placeholder="Any"
            value={receipt}
            aria-invalid={receiptInvalid}
            onChange={(event) => setReceipt(event.target.value)}
          />
        </label>

        <label className="flex min-w-[9.5rem] flex-1 flex-col gap-1 text-label font-medium text-ink">
          Item
          <input
            className="pos-input"
            placeholder="Name, code or tag"
            value={item}
            onChange={(event) => setItem(event.target.value)}
          />
        </label>

        <label className="flex min-w-[9.5rem] flex-1 flex-col gap-1 text-label font-medium text-ink">
          Show
          <select
            className="pos-input"
            value={show}
            onChange={(event) => setShow(event.target.value as CustomerSaleFilter)}
          >
            <option value="All">Everything</option>
            <option value="Completed">Sales and refunds</option>
            <option value="Voided">Voided sales</option>
          </select>
        </label>

        {period === 'custom' ? (
          <>
            <label className="flex min-w-[9.5rem] flex-1 flex-col gap-1 text-label font-medium text-ink">
              From
              <input className="pos-input" type="date" value={from} max={to || undefined} onChange={(e) => setFrom(e.target.value)} />
            </label>
            <label className="flex min-w-[9.5rem] flex-1 flex-col gap-1 text-label font-medium text-ink">
              To
              <input className="pos-input" type="date" value={to} min={from || undefined} onChange={(e) => setTo(e.target.value)} />
            </label>
          </>
        ) : null}
      </div>

      {receiptInvalid ? (
        <p role="alert" className="text-body text-negative-text">
          A receipt number is digits only, like 1052.
        </p>
      ) : null}

      {failure ? (
        <p role="alert" className="rounded-md bg-negative-soft px-3 py-2 text-body font-medium text-negative-text">
          {failure}
        </p>
      ) : null}

      {totals ? (
        <p className="text-body text-ink-muted" role="status">
          <strong className="font-semibold text-ink">
            {totals.count} {totals.count === 1 ? 'sale' : 'sales'}
          </strong>
          {filtered ? ' matching' : ''} · spent{' '}
          <strong className="pos-amount font-semibold text-ink">{formatCurrency(totals.spent)}</strong>
          <span className="text-caption"> (after refunds, not counting voided sales)</span>
        </p>
      ) : null}

      {totals && rows.length === 0 && !loading ? (
        <div className="flex flex-col items-center gap-2 rounded-md border border-dashed border-strong px-4 py-8 text-center">
          <ReceiptText className="h-8 w-8 text-ink-muted" aria-hidden />
          <p className="text-body font-medium text-ink">
            {filtered ? 'No sales match these filters.' : 'This customer has not bought anything yet.'}
          </p>
          {filtered ? (
            <p className="text-body text-ink-muted">Try “All time” and “Everything”, or clear the receipt and item boxes.</p>
          ) : (
            <p className="text-body text-ink-muted">Sales appear here when the customer is attached at the till (F5).</p>
          )}
        </div>
      ) : null}

      {rows.length > 0 ? (
        <ul className="divide-y divide-subtle rounded-md border border-subtle" aria-label="Sales">
          {rows.map((row) => (
            <li key={row.id}>
              <button
                type="button"
                onClick={() => onOpen(row.id)}
                className="flex min-h-[3.5rem] w-full flex-wrap items-center gap-x-4 gap-y-1 px-3 py-2 text-left transition-colors hover:bg-panel-hover focus-visible:bg-panel-hover"
              >
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="text-body font-semibold text-ink">
                    Receipt <span className="pos-amount">#{row.transactionNumber}</span>
                  </span>
                  <span className="text-label text-ink-muted">
                    {when(row.completedAt)} · {count(Math.abs(row.itemCount))}{' '}
                    {Math.abs(row.itemCount) === 1 ? 'item' : 'items'}
                    {row.paidWith ? ` · ${row.paidWith}` : ''}
                  </span>
                </span>

                {row.status !== 'Completed' ? (
                  <DomainStatusBadge status={row.status} />
                ) : row.isRefund ? (
                  <StatusBadge tone="warning" label="Refund" />
                ) : null}

                <span
                  className={cn(
                    'pos-amount text-body-lg font-semibold',
                    row.status !== 'Completed' ? 'text-ink-muted line-through' : 'text-ink',
                  )}
                >
                  {formatCurrency(row.grandTotal)}
                </span>
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      {totals?.hasMore ? (
        <button type="button" className="pos-button self-center" disabled={loading} onClick={() => void load(rows.length)}>
          {loading ? 'Loading…' : 'Show more'}
        </button>
      ) : null}

      {loading && !totals ? <p className="text-body text-ink-muted">Loading sales…</p> : null}
    </div>
  );
}

/* ------------------------------------------------------------------------ the invoice */

interface InvoiceRow {
  key: string;
  line: SaleDetailLine;
  quantity: number;
  extendedNet: number;
  epcs: string[];
}

/**
 * Tagged units of one item as one row, with their tags beneath — exactly as the receipt prints them
 * and the till shows them. Three of a shirt is one line with three EPCs, not three lines.
 */
function invoiceRows(lines: SaleDetailLine[]): InvoiceRow[] {
  const groups = new Map<string, InvoiceRow>();

  for (const line of lines) {
    const key = line.epc
      ? ['tagged', line.stockCode, line.unitPrice, line.discountPct, line.lineType, line.priceOrigin].join('|')
      : `line|${line.sequence}`;

    const existing = groups.get(key);

    if (existing) {
      existing.quantity += line.quantity;
      existing.extendedNet += line.extendedNet;
      if (line.epc) existing.epcs.push(line.epc);
    } else {
      groups.set(key, {
        key,
        line,
        quantity: line.quantity,
        extendedNet: line.extendedNet,
        epcs: line.epc ? [line.epc] : [],
      });
    }
  }

  return [...groups.values()];
}

export function CustomerInvoice({
  customerId,
  transactionId,
  onBack,
}: {
  customerId: number;
  transactionId: number;
  onBack: () => void;
}) {
  useCurrency();

  const auth = useAuth();
  const canPrint = auth.can('pos.reprint');

  const [sale, setSale] = useState<SaleDetail | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [working, setWorking] = useState<'print' | 'save' | null>(null);

  useEffect(() => {
    let cancelled = false;

    setSale(null);
    setFailure(null);

    mastersApi.customers
      .sale(customerId, transactionId)
      .then((detail) => {
        if (!cancelled) setSale(detail);
      })
      .catch((error: unknown) => {
        if (!cancelled) setFailure(describeError(error));
      });

    return () => {
      cancelled = true;
    };
  }, [customerId, transactionId]);

  const rows = useMemo(() => (sale ? invoiceRows(sale.lines) : []), [sale]);

  const print = async () => {
    if (!sale || working) return;
    setWorking('print');

    try {
      const outcome = await printPdf(await posApi.receiptPdf(sale.id));

      if (outcome === 'blocked') {
        toast({
          title: 'Pop-up blocked',
          description: 'Allow pop-ups for this site to print the receipt.',
          variant: 'destructive',
        });
      }
    } catch (error) {
      toast({ title: 'Could not print the receipt', description: describeError(error), variant: 'destructive' });
    } finally {
      setWorking(null);
    }
  };

  const save = async () => {
    if (!sale || working) return;
    setWorking('save');

    try {
      downloadPdf(await posApi.receiptPdf(sale.id), `receipt-${sale.transactionNumber}.pdf`);
      toast({ variant: 'success', title: 'Receipt saved', description: 'Look in your Downloads folder.' });
    } catch (error) {
      toast({ title: 'Could not save the receipt', description: describeError(error), variant: 'destructive' });
    } finally {
      setWorking(null);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <button type="button" className="pos-button" onClick={onBack} autoFocus>
          <ArrowLeft className="h-5 w-5" aria-hidden />
          All sales
        </button>

        {sale && canPrint ? (
          <div className="flex flex-wrap gap-2">
            <button type="button" className="pos-button" disabled={working !== null} onClick={() => void print()}>
              <Printer className="h-5 w-5" aria-hidden />
              {working === 'print' ? 'Printing…' : 'Print'}
            </button>
            <button type="button" className="pos-button" disabled={working !== null} onClick={() => void save()}>
              <Download className="h-5 w-5" aria-hidden />
              {working === 'save' ? 'Saving…' : 'Save as PDF'}
            </button>
          </div>
        ) : null}
      </div>

      {failure ? (
        <p role="alert" className="rounded-md bg-negative-soft px-3 py-2 text-body font-medium text-negative-text">
          {failure}
        </p>
      ) : null}

      {!sale && !failure ? <p className="text-body text-ink-muted">Opening the invoice…</p> : null}

      {sale ? (
        <article className="rounded-md border border-subtle" aria-label={`Receipt ${sale.transactionNumber}`}>
          <header className="border-b border-subtle px-4 py-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h3 className="text-h3 font-semibold text-ink">
                Receipt <span className="pos-amount">#{sale.transactionNumber}</span>
              </h3>
              <DomainStatusBadge status={sale.status} />
            </div>

            <p className="mt-1 text-body text-ink-muted">
              {when(sale.completedAt)} · Till {sale.stationCode}
              {sale.staffName ? ` · served by ${sale.staffName}` : ''}
              {sale.customerName ? ` · ${sale.customerName}` : ''}
            </p>

            {sale.status !== 'Completed' ? (
              <p className="mt-2 rounded-md bg-warning-soft px-3 py-2 text-body font-medium text-warning-text">
                This sale was {sale.status === 'Reversal' ? 'a reversal of an earlier sale' : sale.status.toLowerCase()}
                {sale.voidReason ? ` — ${sale.voidReason}` : ''}.
              </p>
            ) : null}
          </header>

          <table className="w-full text-body">
            <thead>
              <tr className="text-left text-caption uppercase tracking-wide text-ink-muted">
                <th className="w-16 py-2 pl-4 font-medium">Qty</th>
                <th className="py-2 font-medium">Item</th>
                <th className="w-24 py-2 text-right font-medium">Price</th>
                <th className="w-28 py-2 pl-2 pr-4 text-right font-medium">Amount</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.key} className="border-t border-subtle align-top">
                  <td className="pos-amount py-2 pl-4 text-ink">{count(row.quantity)}</td>
                  <td className="py-2">
                    <div className="font-medium text-ink">
                      {row.line.name}
                      {row.line.lineType !== 'Sale' ? (
                        <span className="ml-2 text-label font-normal text-ink-muted">({row.line.lineType.toLowerCase()})</span>
                      ) : null}
                    </div>
                    <div className="font-mono text-caption text-ink-muted">
                      {row.line.stockCode}
                      {row.line.discountPct > 0 ? ` · ${row.line.discountPct}% off` : ''}
                      {row.line.refundedQuantity > 0 ? ` · ${count(row.line.refundedQuantity)} given back` : ''}
                    </div>
                    {row.epcs.length > 0 ? (
                      <ul className="mt-1 flex flex-wrap gap-1" aria-label="Tags">
                        {row.epcs.map((epc) => (
                          <li key={epc} className="rounded bg-panel-sunken px-1.5 py-0.5 font-mono text-caption text-ink-muted" title={epc}>
                            EPC {epc}
                          </li>
                        ))}
                      </ul>
                    ) : null}
                  </td>
                  <td className="pos-amount py-2 text-right text-ink">{formatCurrency(row.line.unitPrice)}</td>
                  <td className="pos-amount py-2 pl-2 pr-4 text-right text-ink">{formatCurrency(row.extendedNet)}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <dl className="border-t border-dashed border-strong px-4 py-3 text-body">
            <Figure label="Subtotal" value={sale.subtotal} />
            {sale.discountTotal > 0 ? <Figure label="Discounts" value={-sale.discountTotal} /> : null}
            {sale.tax1Total !== 0 ? <Figure label={sale.tax1Name || 'Tax 1'} value={sale.tax1Total} /> : null}
            {sale.tax2Total !== 0 ? <Figure label={sale.tax2Name || 'Tax 2'} value={sale.tax2Total} /> : null}
            {sale.addOnCharge !== 0 ? <Figure label="Charge" value={sale.addOnCharge} /> : null}

            <div className="mt-2 flex items-baseline justify-between gap-3 border-t border-strong pt-2">
              <dt className="text-label font-semibold uppercase tracking-wide text-ink-muted">Total</dt>
              <dd className="pos-amount text-h3 font-semibold text-ink">{formatCurrency(sale.grandTotal)}</dd>
            </div>
          </dl>

          {sale.tenders.length > 0 ? (
            <dl className="border-t border-dashed border-strong px-4 py-3 text-body">
              {sale.tenders.map((tender, index) => (
                <Figure key={`${tender.tenderName}-${index}`} label={`Paid by ${tender.tenderName}`} value={tender.amount} />
              ))}
              {sale.changeGiven > 0 ? <Figure label="Change given" value={sale.changeGiven} /> : null}
            </dl>
          ) : null}
        </article>
      ) : null}

      {sale && !canPrint ? (
        <p className="text-body text-ink-muted">
          Printing or saving a copy needs the reprint permission. Ask a supervisor if the customer wants one.
        </p>
      ) : null}
    </div>
  );
}

function Figure({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex items-baseline justify-between gap-3 py-0.5">
      <dt className="text-ink-muted">{label}</dt>
      <dd className="pos-amount text-ink">{formatCurrency(value)}</dd>
    </div>
  );
}

/**
 * The list and the invoice together: pick a sale, read it, go back to the list where you left it.
 *
 * The list stays mounted while an invoice is open, hidden rather than thrown away, so going back
 * keeps the filters, the scroll position and every page already loaded.
 */
export function CustomerSales({ customerId }: { customerId: number }) {
  const [open, setOpen] = useState<number | null>(null);

  return (
    <>
      <div hidden={open !== null}>
        <CustomerSalesHistory customerId={customerId} onOpen={setOpen} />
      </div>

      {open !== null ? (
        <CustomerInvoice customerId={customerId} transactionId={open} onBack={() => setOpen(null)} />
      ) : null}
    </>
  );
}
