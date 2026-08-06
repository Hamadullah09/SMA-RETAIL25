'use client';

import { useEffect, useState, type RefObject } from 'react';
import { usePosStore } from '@/stores/pos-store';
import type { CartLine, PriceOrigin } from '@/types/pos';
import { cn } from '@/lib/utils';

/** Money is formatted once, here, and always with tabular figures. */
export function money(amount: number, symbol = '$'): string {
  const sign = amount < 0 ? '-' : '';
  return `${sign}${symbol}${Math.abs(amount).toFixed(2)}`;
}

function quantity(value: number): string {
  return Number.isInteger(value) ? value.toString() : value.toFixed(3).replace(/0+$/, '').replace(/\.$/, '');
}

/**
 * A short badge for any price that did not come from the regular price. A cashier challenged on a
 * price should be able to answer from the screen rather than from memory.
 */
const ORIGIN_LABELS: Partial<Record<PriceOrigin, string>> = {
  Sale: 'SALE',
  Break: 'QTY',
  Bonus: 'BONUS',
  Manual: 'OVR',
  RandomWeight: 'WT',
  ClientLevel: 'CLIENT',
  Level2: 'L2',
  Level3: 'L3',
  Level4: 'L4',
};

const SOURCE_LABELS: Partial<Record<CartLine['source'], string>> = {
  Rfid: 'RFID',
  RandomWeight: 'WEIGHED',
  Unknown: 'U/I',
  TagAlong: 'AUTO',
  Serial: 'SERIAL',
};

/* ------------------------------------------------------------------ region ⑤ status bar */

export function StatusBar() {
  const { policy, connected, readerOnline, peripherals, drawer, cart } = usePosStore();

  return (
    <header className="pos-panel flex items-center justify-between px-3 py-2 text-label">
      <div className="flex items-center gap-4 text-ink-muted">
        <span>
          Station <span className="font-medium text-ink">{policy?.stationCode ?? '—'}</span>
        </span>
        <span>
          Drawer{' '}
          <span className={cn('font-medium', drawer?.status === 'Open' ? 'text-positive' : 'text-warning')}>
            {drawer?.status === 'Open' ? 'open' : 'closed'}
          </span>
        </span>
        {cart?.heldName ? <span className="text-warning">Recalled: {cart.heldName}</span> : null}
      </div>

      <div className="flex items-center gap-3">
        <Health label="Server" ok={connected} />
        <Health label="RFID" ok={readerOnline} />
        <Health label="Printer" ok={peripherals?.printerOnline ?? false} />
        <Health label="Scale" ok={peripherals?.scaleOnline ?? false} />
      </div>
    </header>
  );
}

function Health({ label, ok }: { label: string; ok: boolean }) {
  return (
    <span className="flex items-center gap-1.5 text-ink-muted">
      <span
        aria-hidden
        className="h-2 w-2 rounded-full"
        style={{ backgroundColor: ok ? 'oklch(var(--positive))' : 'oklch(var(--negative))' }}
      />
      <span className="sr-only">{ok ? `${label} online` : `${label} offline`}</span>
      <span aria-hidden>{label}</span>
    </span>
  );
}

/** A persistent amber bar, never a modal, never a silent failure (doc 08 §Realtime UX rules). */
export function ConnectionBanner() {
  const connected = usePosStore((s) => s.connected);
  if (connected) return null;

  return (
    <div
      role="status"
      className="border-b px-3 py-1.5 text-label font-medium"
      style={{ borderColor: 'oklch(var(--warning))', color: 'oklch(var(--warning))' }}
    >
      Disconnected from the server — reconnecting. Totals on screen may be stale.
    </div>
  );
}

/* ------------------------------------------------------------- region ① cart + live feed */

/**
 * The Live RFID Feed (doc 06 §2, doc 08).
 *
 * Two things it must get right. A reader that has stopped reporting looks exactly like a reader with
 * nothing in front of it, so the outage is stated in words rather than implied by an empty list — and
 * manual entry keeps working underneath it. And every refused tag shows its own plain-language
 * reason: "already sold" and "another till has it" call for completely different responses, and a
 * generic failure teaches staff to ignore the feed entirely.
 */
export function LiveFeed() {
  const { rejectedTags, readerOnline, dismissTag, setReaderMode, peripherals } = usePosStore();

  // Rejections stay visible for ten seconds, then clear themselves (doc 08).
  useEffect(() => {
    if (rejectedTags.length === 0) return undefined;

    const timer = setInterval(() => {
      const cutoff = Date.now() - 10_000;
      rejectedTags.filter((tag) => tag.at < cutoff).forEach((tag) => dismissTag(tag.epc));
    }, 1000);

    return () => clearInterval(timer);
  }, [rejectedTags, dismissTag]);

  // The reading state, rate and the start/stop control live on the Tag reader panel — one place to
  // look, one place to press. This strip only speaks up when something needs the cashier: the
  // reader dropping offline, or a tag being refused.
  const hasReader = peripherals !== null;
  const showOffline = hasReader && !readerOnline;
  if (!showOffline && rejectedTags.length === 0) return null;

  return (
    <div className="border-b border-subtle">
      {showOffline ? (
        <div
          className="flex items-center justify-between gap-2 px-3 py-1.5 text-caption font-medium text-negative"
          role="status"
        >
          <span>Reader offline — scan or key items as normal.</span>
          <button type="button" className="underline" onClick={() => void setReaderMode('OnDemand')}>
            Retry
          </button>
        </div>
      ) : null}

      {rejectedTags.length > 0 ? (
        <ul className="max-h-24 overflow-y-auto px-3 pb-1.5 text-label" aria-live="polite">
          {rejectedTags.map((tag) => (
            <li key={`${tag.epc}-${tag.at}`} className="pos-settling flex justify-between gap-2 py-0.5">
              <span className="truncate font-mono text-caption text-ink-muted">
                …{tag.epc.slice(-12)}
              </span>
              <span className="text-negative">{tag.message}</span>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

export function CartList() {
  const { cart, selectedLineSequence, openDialog, policy } = usePosStore();
  const symbol = policy?.currencySymbol ?? '$';
  const lines = cart?.lines ?? [];

  return (
    <section className="pos-panel flex h-full min-h-0 flex-col" aria-label="Sale lines">
      <div className="pos-panel-header">
        <span>Sale</span>
        <span className="tabular">{lines.length} lines</span>
      </div>

      <LiveFeed />

      <div className="grid grid-cols-[64px_1fr_96px_96px] gap-2 border-b border-subtle px-3 py-1 text-caption font-medium uppercase tracking-wide text-ink-muted">
        <span className="text-right">Qty</span>
        <span>Item</span>
        <span className="text-right">Price</span>
        <span className="text-right">Ext</span>
      </div>

      <ol className="min-h-0 flex-1 overflow-y-auto">
        {lines.length === 0 ? (
          <li className="px-3 py-8 text-center text-body text-ink-muted">
            Scan an item, or press <kbd className="font-mono">F2</kbd> to find one.
          </li>
        ) : (
          lines.map((line) => (
            <li key={line.sequence}>
              <button
                type="button"
                onClick={() => openDialog('lineDetail', line.sequence)}
                className={cn(
                  'grid w-full grid-cols-[64px_1fr_96px_96px] items-center gap-2 px-3 text-left text-body',
                  'h-8 hover:bg-panel-hover',
                  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent',
                  selectedLineSequence === line.sequence && 'bg-surface',
                )}
              >
                <span className="pos-amount text-right">{quantity(line.quantity)}</span>

                <span className="flex min-w-0 items-center gap-1.5">
                  <span className="truncate">{line.name}</span>
                  {line.variantLabel ? (
                    <span className="shrink-0 text-label text-ink-muted">{line.variantLabel}</span>
                  ) : null}
                  {SOURCE_LABELS[line.source] ? (
                    <span className="pos-badge shrink-0 border border-subtle text-ink-muted">
                      {SOURCE_LABELS[line.source]}
                    </span>
                  ) : null}
                  {ORIGIN_LABELS[line.priceOrigin] ? (
                    <span
                      className="pos-badge shrink-0"
                      style={{ backgroundColor: 'oklch(var(--positive) / 0.12)', color: 'oklch(var(--positive))' }}
                    >
                      {ORIGIN_LABELS[line.priceOrigin]}
                    </span>
                  ) : null}
                  {line.lineType !== 'Sale' ? (
                    <span
                      className="pos-badge shrink-0"
                      style={{ backgroundColor: 'oklch(var(--negative) / 0.12)', color: 'oklch(var(--negative))' }}
                    >
                      {line.lineType === 'Return' ? 'RETURN' : 'TRADE'}
                    </span>
                  ) : null}
                </span>

                <span className="pos-amount text-right">{money(line.unitPrice, symbol)}</span>
                <span className="pos-amount text-right font-medium">{money(line.extendedNet, symbol)}</span>
              </button>
            </li>
          ))
        )}
      </ol>
    </section>
  );
}

/* ---------------------------------------------------------------------- region ② totals */

export function TotalsPanel() {
  const { cart, policy } = usePosStore();
  const symbol = policy?.currencySymbol ?? '$';
  const totals = cart?.totals;

  return (
    <section className="pos-panel" aria-label="Totals">
      <div className="pos-panel-header">
        <span>Totals</span>
        {totals?.taxInclusive ? <span className="normal-case">tax included</span> : null}
      </div>

      <dl className="space-y-1 px-3 py-2 text-body" aria-live="polite">
        <Row label="Subtotal" value={money(totals?.subtotal ?? 0, symbol)} />

        {totals && totals.discountTotal > 0 ? (
          <Row
            label="Discounts"
            value={`-${money(totals.discountTotal, symbol)}`}
            tone="positive"
          />
        ) : null}

        {totals && totals.addOnCharge !== 0 ? (
          <Row label={totals.addOnChargeName || 'Charge'} value={money(totals.addOnCharge, symbol)} />
        ) : null}

        {totals && totals.tax1Total !== 0 ? (
          <Row label={totals.tax1Name} value={money(totals.tax1Total, symbol)} />
        ) : null}

        {totals && totals.tax2Total !== 0 ? (
          <Row label={totals.tax2Name} value={money(totals.tax2Total, symbol)} />
        ) : null}

        <div className="mt-1 flex items-baseline justify-between border-t border-subtle pt-2">
          <dt className="text-label font-medium uppercase tracking-wide text-ink-muted">Total</dt>
          <dd className="pos-amount text-2xl font-semibold">{money(totals?.grandTotal ?? 0, symbol)}</dd>
        </div>

        {totals && totals.loyaltyPointsEarned > 0 ? (
          <p className="pt-1 text-label text-ink-muted">
            Earns <span className="tabular">{totals.loyaltyPointsEarned}</span> points
          </p>
        ) : null}
      </dl>
    </section>
  );
}

function Row({ label, value, tone }: { label: string; value: string; tone?: 'positive' }) {
  return (
    <div className="flex justify-between">
      <dt className="text-ink-muted">{label}</dt>
      <dd className="pos-amount" style={tone === 'positive' ? { color: 'oklch(var(--positive))' } : undefined}>
        {value}
      </dd>
    </div>
  );
}

/* ------------------------------------------------------------- region ③ payment matrix */

export function PaymentMatrix({ onPay }: { onPay: () => void }) {
  const { cart, busy, openDialog } = usePosStore();
  const canPay = Boolean(cart && cart.lines.length > 0 && !busy);

  return (
    <section className="pos-panel p-3" aria-label="Payment">
      <button type="button" className="pos-button-primary w-full text-base" disabled={!canPay} onClick={onPay}>
        Pay <span className="pos-fkey"><kbd>F4</kbd></span>
      </button>

      <div className="mt-2 grid grid-cols-2 gap-2">
        <button type="button" className="pos-button text-body" disabled={!cart} onClick={() => openDialog('credits')}>
          Credits <span className="pos-fkey"><kbd>F3</kbd></span>
        </button>
        <button type="button" className="pos-button text-body" disabled={!cart} onClick={() => openDialog('special')}>
          Special <span className="pos-fkey"><kbd>F11</kbd></span>
        </button>
      </div>
    </section>
  );
}

/* ------------------------------------------------------------ region ④ customer context */

export function CustomerPanel() {
  const { cart, policy, openDialog, setCustomer } = usePosStore();
  const symbol = policy?.currencySymbol ?? '$';
  const customer = cart?.customer;

  return (
    <section className="pos-panel" aria-label="Customer">
      <div className="pos-panel-header">
        <span>Customer</span>
        <button type="button" className="text-caption normal-case underline" onClick={() => openDialog('client')}>
          F5 Client
        </button>
      </div>

      {customer ? (
        <div className="space-y-1 px-3 py-2 text-body">
          <div className="flex items-baseline justify-between">
            <span className="font-medium">{customer.name}</span>
            <span className="tabular text-label text-ink-muted">#{customer.customerNumber}</span>
          </div>

          <p className="text-label text-ink-muted">
            Level {customer.priceLevel}
            {customer.usualDiscountPct > 0 ? ` · ${customer.usualDiscountPct}% usual` : ''}
            {customer.exemptTax1 ? ' · tax 1 exempt' : ''}
            {customer.exemptTax2 ? ' · tax 2 exempt' : ''}
          </p>

          <div className="flex justify-between text-label">
            <span className="text-ink-muted">Points</span>
            <span className="tabular">{customer.rewardPoints}</span>
          </div>

          <div className="flex justify-between text-label">
            <span className="text-ink-muted">Account balance</span>
            <span className="pos-amount">{money(customer.accountBalance, symbol)}</span>
          </div>

          <button
            type="button"
            className="mt-1 text-label underline text-ink-muted"
            onClick={() => void setCustomer(null)}
          >
            Remove customer
          </button>
        </div>
      ) : (
        <p className="px-3 py-3 text-body text-ink-muted">Walk-in sale.</p>
      )}
    </section>
  );
}

/* ------------------------------------------------------------------------ the scan box */

export function ScanBox({ inputRef }: { inputRef: RefObject<HTMLInputElement> }) {
  const { scan, busy, error, clearError } = usePosStore();
  const [value, setValue] = useState('');

  return (
    <form
      className="pos-panel flex items-center gap-2 px-3 py-2"
      onSubmit={(event) => {
        event.preventDefault();
        const entry = value;
        setValue('');
        void scan(entry);
      }}
    >
      <label htmlFor="pos-scan" className="text-label uppercase tracking-wide text-ink-muted">
        Scan
      </label>

      <input
        id="pos-scan"
        ref={inputRef}
        value={value}
        autoComplete="off"
        // The scan box holds focus for the whole sale: a barcode wedge types into whatever has
        // focus, and a cashier should never have to click before scanning.
        autoFocus
        onChange={(event) => {
          setValue(event.target.value);
          if (error) clearError();
        }}
        placeholder="Barcode, stock code, tag or serial"
        className="flex-1 bg-transparent text-body outline-none placeholder:text-ink-muted"
        disabled={busy}
      />

      {error ? (
        <span className="text-label text-negative" role="alert">
          {error.message}
        </span>
      ) : null}
    </form>
  );
}

/* --------------------------------------------------------------------- the F-key strip */

export interface FunctionKey {
  key: string;
  label: string;
  onSelect: () => void;
  disabled?: boolean;
}

export function FunctionKeyBar({ keys }: { keys: FunctionKey[] }) {
  return (
    <nav className="pos-panel flex flex-wrap items-center gap-1 px-2 py-1" aria-label="Function keys">
      {keys.map((entry) => (
        <button
          key={entry.key}
          type="button"
          onClick={entry.onSelect}
          disabled={entry.disabled}
          className="pos-fkey rounded-sm hover:bg-panel-hover disabled:opacity-40"
        >
          <kbd>{entry.key}</kbd>
          {entry.label}
        </button>
      ))}
    </nav>
  );
}

/** The per-item prompt a product can carry (guide p.43). Replaced by the next item that has one. */
export function PosMessageBanner() {
  const message = usePosStore((s) => s.posMessage);

  if (!message) return null;

  return (
    <div
      role="status"
      className="pos-panel px-3 py-2 text-body"
      style={{ borderColor: 'oklch(var(--warning))', color: 'oklch(var(--warning))' }}
    >
      {message}
    </div>
  );
}
