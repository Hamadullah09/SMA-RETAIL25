'use client';

import { useEffect, useRef, useState, type RefObject } from 'react';
import Link from 'next/link';
import {
  ChevronLeft,
  Printer,
  Radio,
  Scale,
  ScanLine,
  Server,
  TriangleAlert,
  User,
  X,
} from 'lucide-react';
import { usePosStore } from '@/stores/pos-store';
import type { CartLine, PriceOrigin } from '@/types/pos';
import { cn } from '@/lib/utils';
import { connectionCopy } from '@/lib/connection-state';

/**
 * Money is formatted once, here, and always with tabular figures.
 *
 * The symbol defaults to nothing rather than to a dollar. A shop trading in rupees was shown
 * `$2,000.00` in the find-item dialog beside `Rs 0.00` in the sale panel — the same amount in two
 * currencies on one screen, and no way for a cashier to know which to believe. An amount with no
 * symbol is incomplete and reads as such; an amount with the wrong symbol is a wrong price.
 */
export function money(amount: number, symbol = ''): string {
  const sign = amount < 0 ? '-' : '';
  return `${sign}${symbol}${Math.abs(amount).toFixed(2)}`;
}

/**
 * The shop's currency symbol, from the station's policy.
 *
 * One place, so a component cannot invent its own. It is empty until the policy arrives, which is a
 * few hundred milliseconds at till start — briefly showing an unadorned number is honest, and it
 * settles as soon as the till knows what the shop trades in.
 */
export function useCurrencySymbol(): string {
  return usePosStore((state) => state.policy?.currencySymbol) ?? '';
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

/**
 * The till's meta row: where this station is, what the drawer is doing, and whether the four things
 * it depends on are alive.
 *
 * It renders bare rather than as its own panel. The status row, the scan box and the two banners are
 * one object — the state of the till and the way into it — and drawing four bordered cards around
 * four things a cashier reads as one is what made the old screen look like a settings page.
 */
export function StatusBar() {
  const { policy, connected, readerOnline, peripherals, drawer, cart, lastSale } = usePosStore();

  return (
    <header className="flex items-center justify-between gap-3 px-2.5 py-1 text-label">
      <div className="flex min-w-0 items-center gap-3 text-ink-muted">
        {/*
          The way out. The till is the one screen with no sidebar — it takes the whole display on
          purpose — and without this the only route back to the back office is the browser's own
          back button, which a counter terminal in kiosk mode does not necessarily have.
        */}
        <Link
          href="/dashboard"
          className="-ml-1 inline-flex shrink-0 items-center gap-1 rounded px-1.5 py-0.5 font-medium text-ink-muted transition-colors hover:bg-panel-hover hover:text-ink"
        >
          <ChevronLeft className="h-3.5 w-3.5" aria-hidden />
          Back office
        </Link>

        <span className="h-3.5 w-px shrink-0 bg-subtle" aria-hidden />

        <span className="shrink-0">
          Station <span className="font-semibold text-ink">{policy?.stationCode ?? '—'}</span>
        </span>

        {/*
          The drawer is stated as a word in both states rather than as a colour swap, for the same
          reason the peripherals below are: "closed" in amber and "open" in green are the same shape
          to a reader who cannot separate the hues.
        */}
        <span className="shrink-0">
          Drawer{' '}
          <span className={cn('font-semibold', drawer?.status === 'Open' ? 'text-positive' : 'text-ink')}>
            {drawer?.status === 'Open' ? 'open' : 'closed'}
          </span>
        </span>

        {cart?.heldName ? (
          <span className="pos-badge shrink-0 text-warning">Recalled: {cart.heldName}</span>
        ) : null}

        {/* Folded into this row rather than given a line of its own; it is a receipt, not an event. */}
        {lastSale ? (
          <span className="truncate text-ink-faint" role="status">
            Sale #{lastSale.transactionNumber} saved
            {lastSale.changeGiven > 0 ? ` · change ${money(lastSale.changeGiven)}` : ''}
          </span>
        ) : null}
      </div>

      <div className="flex shrink-0 items-center gap-1" aria-label="Peripheral status">
        <Health label="Server" icon={Server} ok={connected} />
        <Health label="RFID" icon={Radio} ok={readerOnline} />
        <Health label="Printer" icon={Printer} ok={peripherals?.printerOnline ?? false} />
        <Health label="Scale" icon={Scale} ok={peripherals?.scaleOnline ?? false} />
      </div>
    </header>
  );
}

/**
 * One peripheral, told four ways: a glyph that changes shape, a word, a weight and — last — a hue.
 *
 * Colour is the cue a till can least afford to lean on. These four sit under fluorescent light on a
 * cheap panel seen at an angle, and roughly one man in twelve cannot separate the green from the
 * red that used to be the *only* difference between a working printer and a dead one.
 */
function Health({
  label,
  ok,
  icon: Icon,
}: {
  label: string;
  ok: boolean;
  icon: typeof Server;
}) {
  return (
    <span className="pos-health" data-state={ok ? 'up' : 'down'} title={ok ? `${label} online` : `${label} offline`}>
      {ok ? (
        <Icon className="h-3.5 w-3.5 shrink-0 opacity-70" aria-hidden />
      ) : (
        <TriangleAlert className="h-3.5 w-3.5 shrink-0" aria-hidden />
      )}
      <span className="sr-only">{ok ? `${label} online` : `${label} offline`}</span>
      <span aria-hidden>{label}</span>
      {ok ? null : (
        <span aria-hidden className="opacity-80">
          offline
        </span>
      )}
    </span>
  );
}

/** A persistent amber bar, never a modal, never a silent failure (doc 08 §Realtime UX rules). */
export function ConnectionBanner() {
  const connected = usePosStore((s) => s.connected);
  const hasConnected = usePosStore((s) => s.hasConnected);

  // Silent until this till has actually been connected once. Before that it is opening, not
  // failing, and the hub takes a moment to shake hands — so this used to flash across every page
  // load. A till that genuinely cannot reach the server says so through its own banner; this one is
  // reserved for losing a connection that was working, which is when "totals may be stale" is true.
  if (connected || !hasConnected) return null;

  return (
    <div
      role="status"
      className="flex items-center gap-2 border-b border-subtle px-3 py-1.5 text-label font-semibold"
      style={{ backgroundColor: 'oklch(var(--warning) / 0.12)', color: 'oklch(var(--warning))' }}
    >
      <TriangleAlert className="h-4 w-4 shrink-0" aria-hidden />
      {/*
        The same sentence the badges use, from the same place. This screen was the only one that
        already waited for a first connection before complaining — the wording is now shared so the
        rest inherit that judgement rather than each re-deciding it.
      */}
      {connectionCopy('reconnecting').detail} Totals on screen may be stale.
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
    <div className="shrink-0 border-b border-subtle">
      {showOffline ? (
        <div
          className="flex items-center justify-between gap-2 px-3 py-1.5 text-label font-semibold text-negative"
          style={{ backgroundColor: 'oklch(var(--negative) / 0.10)' }}
          role="status"
        >
          <span className="flex items-center gap-1.5">
            <TriangleAlert className="h-3.5 w-3.5 shrink-0" aria-hidden />
            Reader offline — scan or key items as normal.
          </span>
          <button type="button" className="shrink-0 rounded px-1 underline" onClick={() => void setReaderMode('OnDemand')}>
            Retry
          </button>
        </div>
      ) : null}

      {rejectedTags.length > 0 ? (
        <ul className="max-h-20 overflow-y-auto px-3 py-1 text-label" aria-live="polite">
          {rejectedTags.map((tag) => (
            <li key={`${tag.epc}-${tag.at}`} className="pos-settling flex justify-between gap-2 py-0.5">
              <span className="truncate font-mono text-caption text-ink-muted">…{tag.epc.slice(-12)}</span>
              <span className="shrink-0 font-medium text-negative">{tag.message}</span>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

/**
 * The sale itself — the thing on this screen a cashier looks at all day.
 *
 * It is given the whole left column and everything else is sized around it. Four columns, one shared
 * grid definition, and exactly one line marked as the one being worked on.
 */
export function CartList() {
  const { cart, selectedLineSequence, openDialog, policy } = usePosStore();
  const symbol = useCurrencySymbol();
  const lines = cart?.lines ?? [];
  const itemCount = cart?.totals?.itemCount ?? 0;

  /*
   * The line under the cashier's attention: the one they opened, or failing that the one that just
   * landed. Selection is only ever set by opening a line, so on the ordinary scan-scan-scan path
   * this resolves to the newest line — which is the one they are checking against the item in hand.
   */
  const currentSequence =
    lines.find((line) => line.sequence === selectedLineSequence)?.sequence ??
    (lines.length > 0 ? lines[lines.length - 1].sequence : null);

  const currentRef = useRef<HTMLLIElement>(null);

  // Line fifteen of a sale is below the fold, and a cashier cannot confirm a price they cannot see.
  useEffect(() => {
    currentRef.current?.scrollIntoView({ block: 'nearest' });
  }, [currentSequence, lines.length]);

  return (
    <section className="pos-panel flex h-full min-h-0 flex-col overflow-hidden" aria-label="Sale lines">
      <div className="pos-panel-header shrink-0">
        <span>Sale</span>
        <span className="tabular text-ink-muted">
          {lines.length === 0 ? 'empty' : `${itemCount} item${itemCount === 1 ? '' : 's'} · ${lines.length} lines`}
        </span>
      </div>

      <LiveFeed />

      <div className="pos-cart-grid shrink-0 border-b border-subtle bg-panel-sunken py-1 pl-2 pr-3 text-caption font-semibold uppercase tracking-wide text-ink-muted">
        <span className="pl-[3px] text-center">Qty</span>
        <span>Item</span>
        <span className="text-right">Weight</span>
        <span className="text-right">Price</span>
        <span className="text-right">Ext</span>
      </div>

      {lines.length === 0 ? (
        <EmptySale />
      ) : (
        <ol className="min-h-0 flex-1 overflow-y-auto">
          {lines.map((line) => {
            const current = line.sequence === currentSequence;

            return (
              <li key={line.sequence} ref={current ? currentRef : undefined} className="border-b border-subtle/60">
                <button
                  type="button"
                  onClick={() => openDialog('lineDetail', line.sequence)}
                  data-current={current}
                  aria-current={current ? 'true' : undefined}
                  className="pos-cart-row pos-cart-grid focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-accent"
                >
                  <span className="flex justify-center">
                    {line.quantity === 1 ? (
                      <span className="pos-amount text-ink-muted">1</span>
                    ) : (
                      <span className="pos-qty-chip text-ink">{quantity(line.quantity)}</span>
                    )}
                  </span>

                  <span className="flex min-w-0 items-center gap-1.5">
                    <span className="truncate text-body-lg text-ink">{line.name}</span>
                    {line.variantLabel ? (
                      <span className="shrink-0 text-label text-ink-muted">{line.variantLabel}</span>
                    ) : null}
                    {SOURCE_LABELS[line.source] ? (
                      <span className="pos-badge shrink-0 text-ink-muted">{SOURCE_LABELS[line.source]}</span>
                    ) : null}
                    {ORIGIN_LABELS[line.priceOrigin] ? (
                      <span className="pos-badge shrink-0 text-positive">{ORIGIN_LABELS[line.priceOrigin]}</span>
                    ) : null}
                    {line.lineType !== 'Sale' ? (
                      <span className="pos-badge shrink-0 text-negative">
                        {line.lineType === 'Return' ? 'RETURN' : 'TRADE'}
                      </span>
                    ) : null}

                  </span>

                  {/*
                    What this line weighs, in its own column beside the money.

                    No unit is printed, because the catalogue does not store one — the product form
                    asks for a number and lets the shop decide what it means, so printing "kg" here
                    would invent a fact.

                    Blank at zero rather than "0": no weight on file and weighing nothing are
                    different claims, and a till should not make the second one on the catalogue's
                    behalf. A column of blanks is also the honest signal that the catalogue has not
                    been weighed yet.
                  */}
                  <span className="pos-amount text-right text-label text-ink-muted">
                    {line.lineWeight > 0 ? quantity(line.lineWeight) : null}
                  </span>

                  <span className="pos-amount pos-line-unit text-right text-label text-ink-muted">
                    {money(line.unitPrice, symbol)}
                  </span>

                  {/* The figure the customer is being charged for this line: full ink, semibold,
                      and the only thing in the row heavier than the item's own name. */}
                  <span className="pos-amount text-right text-body-lg font-semibold text-ink">
                    {money(line.extendedNet, symbol)}
                  </span>
                </button>
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}

/**
 * The empty sale — deliberately empty.
 *
 * This carried an icon, a heading, a sentence explaining the three ways an item can reach the
 * screen, and an F9 button. All of it was already on screen: F9 Find sits in the key bar a few
 * centimetres below, permanently, and the scan box has the caret. A cashier opening a till is not
 * reading a tutorial, and the same panel that shows the sale should not look like a different
 * screen when the sale is empty.
 *
 * Left as an empty region rather than deleted outright so the panel keeps its height and the
 * totals below it do not jump upward the moment the first item is scanned.
 */
function EmptySale() {
  return <div className="min-h-0 flex-1" aria-hidden />;
}

/* ---------------------------------------------------------------------- region ② totals */

/**
 * The money.
 *
 * The build-up is deliberately quiet and the grand total is deliberately enormous. It is the number
 * read aloud at every sale and read off the screen by a customer standing a metre away, and at the
 * old 24px it was the same size as the panel headings around it.
 */
export function TotalsPanel() {
  const { cart, policy } = usePosStore();
  const symbol = useCurrencySymbol();
  const totals = cart?.totals;

  return (
    <section aria-label="Totals">
      <dl className="space-y-0.5 px-3 pb-2 pt-2 text-label" aria-live="polite">
        <Row label="Subtotal" value={money(totals?.subtotal ?? 0, symbol)} />

        {totals && totals.discountTotal > 0 ? (
          <Row label="Discounts" value={`-${money(totals.discountTotal, symbol)}`} tone="positive" />
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
      </dl>

      {/* Its own band, not another row in the list. The rule above it and the wash behind it are
          what stop the eye reading straight past it as the sixth line of an arithmetic. */}
      <div
        className="flex items-end justify-between gap-2 border-y border-subtle px-3 py-2.5"
        style={{ backgroundColor: 'oklch(var(--accent) / 0.07)' }}
      >
        <dt className="flex flex-col text-label font-semibold uppercase tracking-wide text-ink-muted">
          Total
          {totals?.taxInclusive ? (
            <span className="text-caption font-normal normal-case tracking-normal">tax included</span>
          ) : null}
        </dt>
        <dd className="pos-grand-total text-ink">{money(totals?.grandTotal ?? 0, symbol)}</dd>
      </div>

      {totals && totals.loyaltyPointsEarned > 0 ? (
        <p className="px-3 pt-1.5 text-label text-ink-muted">
          Earns <span className="tabular font-medium text-ink">{totals.loyaltyPointsEarned}</span> points
        </p>
      ) : null}
    </section>
  );
}

function Row({ label, value, tone }: { label: string; value: string; tone?: 'positive' }) {
  return (
    <div className="flex justify-between gap-2">
      <dt className="truncate text-ink-muted">{label}</dt>
      <dd
        className="pos-amount shrink-0 text-ink"
        style={tone === 'positive' ? { color: 'oklch(var(--positive))' } : undefined}
      >
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
    <section className="p-2.5" aria-label="Payment">
      <button
        type="button"
        className="pos-button-primary w-full text-body-lg"
        style={{ minHeight: '3.25rem' }}
        disabled={!canPay}
        onClick={onPay}
      >
        Pay
        <span className="pos-fkey text-accent-foreground/80">
          <kbd>F4</kbd>
        </span>
      </button>

      <div className="mt-1.5 grid grid-cols-2 gap-1.5">
        <button type="button" className="pos-button text-body" disabled={!cart} onClick={() => openDialog('credits')}>
          Credits{' '}
          <span className="pos-fkey px-0">
            <kbd>F8</kbd>
          </span>
        </button>
        <button type="button" className="pos-button text-body" disabled={!cart} onClick={() => openDialog('special')}>
          Special{' '}
          <span className="pos-fkey px-0">
            <kbd>F11</kbd>
          </span>
        </button>
      </div>
    </section>
  );
}

/* ------------------------------------------------------------ region ④ customer context */

export function CustomerPanel() {
  const { cart, policy, openDialog, setCustomer } = usePosStore();
  const symbol = useCurrencySymbol();
  const customer = cart?.customer;

  return (
    <section aria-label="Customer">
      <div className="pos-panel-header">
        <span>Customer</span>
        <button
          type="button"
          className="pos-fkey rounded-sm px-1 normal-case hover:bg-panel-hover hover:text-ink"
          onClick={() => openDialog('client')}
        >
          <kbd>F5</kbd>
          {customer ? 'Change' : 'Attach'}
        </button>
      </div>

      {customer ? (
        <div className="space-y-1 px-3 py-1.5">
          <div className="flex items-baseline justify-between gap-2">
            <span className="truncate text-body-lg font-semibold text-ink">{customer.name}</span>
            <span className="tabular shrink-0 text-label text-ink-muted">#{customer.customerNumber}</span>
          </div>

          <div className="flex flex-wrap items-center gap-1">
            <span className="pos-badge text-ink-muted">Level {customer.priceLevel}</span>
            {customer.usualDiscountPct > 0 ? (
              <span className="pos-badge text-positive">{customer.usualDiscountPct}% usual</span>
            ) : null}
            {customer.exemptTax1 ? <span className="pos-badge text-ink-muted">Tax 1 exempt</span> : null}
            {customer.exemptTax2 ? <span className="pos-badge text-ink-muted">Tax 2 exempt</span> : null}
          </div>

          <div className="flex items-baseline justify-between gap-2 text-label">
            <span className="text-ink-muted">
              Points <span className="tabular font-medium text-ink">{customer.rewardPoints}</span>
            </span>
            <span className="text-ink-muted">
              Balance{' '}
              <span className="pos-amount font-medium text-ink">{money(customer.accountBalance, symbol)}</span>
            </span>
            <button
              type="button"
              className="inline-flex items-center gap-0.5 rounded px-1 text-label text-ink-muted underline hover:text-ink"
              onClick={() => void setCustomer(null)}
            >
              <X className="h-3 w-3" aria-hidden />
              Remove
            </button>
          </div>
        </div>
      ) : (
        <p className="flex items-center gap-2 px-3 py-2 text-body text-ink-muted">
          <User className="h-4 w-4 shrink-0 text-ink-faint" aria-hidden />
          Walk-in sale
        </p>
      )}
    </section>
  );
}

/**
 * The side column, as one object.
 *
 * Customer, then money, then the way to take it — read top to bottom in the order the sale happens.
 * They were three separate bordered cards, which made three unrelated things out of one sequence and
 * spent 16px of the till's scarcest axis on the gaps between them.
 */
export function SidePanel({ onPay }: { onPay: () => void }) {
  return (
    <div className="pos-panel shrink-0 divide-y divide-subtle overflow-hidden">
      <CustomerPanel />
      <TotalsPanel />
      <PaymentMatrix onPay={onPay} />
    </div>
  );
}

/* ------------------------------------------------------------------------ the scan box */

/**
 * Where every sale starts.
 *
 * Full width, touch height, and the only field on the screen — so there is never a question about
 * where a wedge scanner's keystrokes are going.
 */
export function ScanBox({ inputRef }: { inputRef: RefObject<HTMLInputElement> }) {
  const { scan, busy, error, clearError } = usePosStore();
  const [value, setValue] = useState('');

  return (
    <form
      className="flex h-11 items-center gap-2 border-t border-subtle px-3"
      onSubmit={(event) => {
        event.preventDefault();
        const entry = value;
        setValue('');
        void scan(entry);
      }}
    >
      <label htmlFor="pos-scan" className="flex shrink-0 items-center gap-1.5 text-label font-semibold uppercase tracking-wide text-ink-muted">
        <ScanLine className="h-4 w-4" aria-hidden />
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
        className="h-full min-w-0 flex-1 bg-transparent text-body-lg text-ink outline-none placeholder:text-ink-faint"
        disabled={busy}
      />

      {error ? (
        <span
          className="pos-badge shrink-0 font-semibold text-negative"
          role="alert"
        >
          <TriangleAlert className="h-3.5 w-3.5 shrink-0" aria-hidden />
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
    <nav className="pos-panel pos-keybar p-1" aria-label="Function keys">
      {keys.map((entry) => (
        <button key={entry.key} type="button" onClick={entry.onSelect} disabled={entry.disabled}>
          <kbd className="pos-kbd shrink-0">{entry.key}</kbd>
          <span className="truncate">{entry.label}</span>
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
      className="border-t border-subtle px-3 py-1.5 text-body font-medium"
      style={{ backgroundColor: 'oklch(var(--warning) / 0.12)', color: 'oklch(var(--warning))' }}
    >
      {message}
    </div>
  );
}
