'use client';

import { useEffect, useRef, useState, type RefObject } from 'react';
import Link from 'next/link';
import {
  ChevronLeft,
  Printer,
  Radio,
  Scale,
  ScanLine,
  ShoppingCart,
  Server,
  TriangleAlert,
  Unplug,
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
/**
 * Why a line is priced the way it is, in words.
 *
 * These were "OVR", "L2", "L3", "U/I" — a legend somebody had to be taught and then remember, on
 * the screen where a customer is waiting and the question is "why is this the price?". A cashier
 * who cannot answer that either overrides it or calls a supervisor.
 *
 * Longer strings cost nothing here: the badge sizes to its text and these appear one at a time.
 */
const ORIGIN_LABELS: Partial<Record<PriceOrigin, string>> = {
  Sale: 'On sale',
  Break: 'Quantity break',
  Bonus: 'Bonus',
  Manual: 'Price overridden',
  RandomWeight: 'By weight',
  ClientLevel: 'Customer price',
  Level2: 'Price level 2',
  Level3: 'Price level 3',
  Level4: 'Price level 4',
};

/** Where the line came from. "U/I" meant unidentified item — which nobody could have guessed. */
const SOURCE_LABELS: Partial<Record<CartLine['source'], string>> = {
  Rfid: 'RFID tag',
  RandomWeight: 'Weighed',
  Unknown: 'Unidentified item',
  TagAlong: 'Added automatically',
  Serial: 'Serial number',
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
  const { policy, connected, hasConnected, readerOnline, peripherals, drawer, cart, lastSale } = usePosStore();

  /*
    Whether anything is going to answer for the hardware.

    A station with no terminal agent never publishes a peripheral status at all -- the server only
    broadcasts one when an agent reports -- so `peripherals` stays null forever. Read as "not yet
    known" that produced three badges saying "checking" for the rest of the shift, which is a
    promise the screen cannot keep: nothing is being checked and nothing ever will be.

    Six seconds is long enough that a healthy agent has reported and short enough that nobody has
    started a sale. After it, silence is an answer: there is no agent on this machine, so the
    printer, the scale and the reader are not connected to this till.
  */
  const [waitedForAgent, setWaitedForAgent] = useState(false);

  useEffect(() => {
    if (peripherals !== null) {
      setWaitedForAgent(false);
      return undefined;
    }

    const timer = window.setTimeout(() => setWaitedForAgent(true), 6000);
    return () => window.clearTimeout(timer);
  }, [peripherals]);

  // `undefined` while the answer may still arrive; `null` once we know none is coming.
  const fromAgent = (value: boolean | undefined) =>
    peripherals !== null ? value : waitedForAgent ? null : undefined;

  return (
    /*
      Wraps, rather than colliding.

      Both halves of this row were `shrink-0` inside one non-wrapping flex, which is fine at 1366
      and a pile-up at 390: on a phone the four peripheral badges, the station, the drawer and the
      way back to the back office were drawn on top of each other as one illegible line. The till
      is a counter screen, but it is opened on a phone often enough -- a manager checking a station,
      somebody stocktaking -- that the first row of it should not be rubble.

      `flex-wrap` and a `w-full` break before the badges: the badges take their own line rather than
      squeezing the station name to nothing.
    */
    <header className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 px-2.5 py-1 text-label">
      <div className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 text-ink-muted">
        {/*
          The way out. The till is the one screen with no sidebar — it takes the whole display on
          purpose — and without this the only route back to the back office is the browser's own
          back button, which a counter terminal in kiosk mode does not necessarily have.
        */}
        <Link
          href="/dashboard"
          className="-ml-1 inline-flex shrink-0 items-center gap-1 rounded px-1.5 py-0.5 font-medium text-ink-muted transition-colors hover:bg-panel-hover hover:text-ink"
        >
          <ChevronLeft className="h-5 w-5" aria-hidden />
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
          <span className="pos-badge shrink-0 text-warning-text">Recalled: {cart.heldName}</span>
        ) : null}

        {/*
          The change owed is not a footnote.
          
          It was in this row in text-ink-faint — the faintest token in the system, inside a truncate
          — which made the amount to hand back one of the least legible things on the till. It is
          the number a cashier reads out loud while a customer holds their hand out, and it was
          rendered smaller and paler than the station name.
          
          The sale number stays quiet, because that is a receipt. The change is a figure.
        */}
        {lastSale ? (
          <span className="flex shrink-0 items-center gap-2" role="status">
            <span className="truncate text-ink-muted">Sale #{lastSale.transactionNumber} saved</span>

            {lastSale.changeGiven > 0 ? (
              <span className="inline-flex items-center gap-1.5 rounded-full bg-positive-soft px-3 py-1 font-semibold text-positive-text">
                Change
                <span className="pos-amount text-body-lg">{money(lastSale.changeGiven)}</span>
              </span>
            ) : null}
          </span>
        ) : null}
      </div>

      <div
        className="flex w-full shrink-0 flex-wrap items-center gap-1 sm:w-auto"
        aria-label="Peripheral status"
      >
        {/*
          The same three-way distinction the connection banner already makes: opening for the first
          time is not the same as having dropped. This badge used to read `connected`, which is
          false during the handshake — so every cold start showed a red "Server offline" for the
          second before it went green, to a cashier with a customer in front of them.
        */}
        <Health label="Server" icon={Server} ok={connected ? true : hasConnected ? false : undefined} />
        {/* Three answers, not two: `undefined` while a report may still arrive, `null` once we know
            no agent is going to send one, and a boolean when one has. */}
        <Health label="RFID" icon={Radio} ok={fromAgent(readerOnline)} />
        <Health label="Printer" icon={Printer} ok={fromAgent(peripherals?.printerOnline)} />
        <Health label="Scale" icon={Scale} ok={fromAgent(peripherals?.scaleOnline)} />
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
 *
 * Three states, not two. `ok` was a boolean and the call sites read `peripherals?.printerOnline ??
 * false`, so for the second or so before the first status arrives — every single time the till is
 * opened — the printer and the scale announced themselves as *offline*. They were not offline;
 * nobody had asked them yet. A badge that raises a false alarm on every page load is one a cashier
 * learns to ignore, and it is the same badge that has to be believed when the printer really has
 * died mid-queue. `undefined` now means "no answer yet" and says so quietly.
 */
function Health({
  label,
  ok,
  icon: Icon,
}: {
  label: string;
  /**
   * Four answers.
   *
   * `undefined` while a report may still arrive, `null` once we know no agent is going to send one,
   * `false` when a device reported itself down, `true` when it reported itself up. The middle two
   * are different facts and were being drawn as the same one: "checking" for a device nothing is
   * checking said the till was still trying, so a station with no agent at all looked like a
   * station mid-handshake, permanently.
   */
  ok: boolean | undefined | null;
  icon: typeof Server;
}) {
  const state = ok === undefined ? 'unknown' : ok === null ? 'absent' : ok ? 'up' : 'down';

  const said = {
    up: `${label} online`,
    down: `${label} offline`,
    absent: `${label} not connected to this till`,
    unknown: `${label} — checking`,
  }[state];

  return (
    <span className="pos-health" data-state={state} title={said}>
      {state === 'down' ? (
        <TriangleAlert className="h-5 w-5 shrink-0" aria-hidden />
      ) : state === 'absent' ? (
        // A plug that is not in anything. Not the alarm triangle: a shop that has never installed
        // an agent has nothing wrong with it, and a permanent red warning on a working till is how
        // you teach somebody to stop reading the row.
        <Unplug className="h-5 w-5 shrink-0 opacity-70" aria-hidden />
      ) : (
        <Icon className={cn('h-5 w-5 shrink-0', state === 'up' ? 'opacity-70' : 'opacity-45')} aria-hidden />
      )}

      {/* The whole sentence to a screen reader, not the label and a colour. */}
      <span className="sr-only">{said}</span>
      <span aria-hidden>{label}</span>

      {state === 'up' ? null : (
        <span aria-hidden className="opacity-80">
          {state === 'down' ? 'offline' : state === 'absent' ? 'not connected' : 'checking'}
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
            <TriangleAlert className="h-5 w-5 shrink-0" aria-hidden />
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
        {/*
          The region's mark.

          Doc 08 says the till shows exactly five functional groups at rest, and it does -- but they
          were five white panels titled in the same 16px semibold, so "five groups" was a fact about
          the markup rather than something the screen said. A glyph per region gives each one a
          shape, which is what lets somebody glance back after handling cash and land on the right
          panel instead of re-reading three headings.

          Muted rather than coloured: these mark a region, and the till's colour budget belongs to
          the money and the alarms. A cart icon in green here would be the fourth green thing on a
          screen where green already means "committed".
        */}
        <span className="flex items-center gap-2">
          <ShoppingCart className="h-5 w-5 shrink-0 text-ink-muted" aria-hidden />
          Sale
        </span>
        <span className="tabular text-ink-muted">
          {lines.length === 0 ? 'empty' : `${itemCount} item${itemCount === 1 ? '' : 's'} · ${lines.length} lines`}
        </span>
      </div>

      <LiveFeed />

      {/*
        Weight is always a column.

        It was briefly conditional -- present only when a line in the cart had a weight -- to buy the
        item name room it did not otherwise have. That solved the wrong half of the problem: a
        column that appears and disappears between sales is a column whose position has to be
        re-found every time, and on a counter that sells loose goods it is one of the figures being
        checked against the scale. The cart column is wider instead, so the name has its room and
        the header stays put.
      */}
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
                      <span className="pos-badge shrink-0 text-positive-text">{ORIGIN_LABELS[line.priceOrigin]}</span>
                    ) : null}

                    {/*
                      A return and a trade-in, spelled out.
                      
                      "TRADE" is the legacy abbreviation and it is not one anybody arrives already
                      knowing; it sits beside "RETURN", which reads as a word, so the pair looked
                      like one term shortened and one not. Both are now what they are.
                    */}
                    {line.lineType !== 'Sale' ? (
                      <span className="pos-badge shrink-0 text-negative-text">
                        {line.lineType === 'Return' ? 'Return' : 'Trade-in'}
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
 * The empty sale — one line, and deliberately not a page.
 *
 * This has been both extremes. It carried an icon, a heading, a sentence explaining the three ways
 * an item can reach the screen, and an F9 button; all of that was rightly cut, because a cashier
 * opening a till is not reading a tutorial and the same panel should not look like a different
 * screen when the sale happens to be empty. F9 Find is in the key bar a few centimetres below,
 * permanently, and the scan box already has the caret.
 *
 * Then it was nothing at all, which is the other failure: roughly five hundred pixels of white
 * with no caption. Somebody who has used a till for years reads that as "ready". Somebody on their
 * first shift, or the owner who came in to cover it, reads it as "not working" — and the one
 * question they have is the one question the screen was not answering.
 *
 * So: one sentence, at the top of the region rather than floating in the middle of it, in the
 * faint tone. It names the physical thing to do, which is the part that is not already on screen —
 * the key bar says what F9 does, and nothing says "you may simply scan". It is a caption, not a
 * heading, and it introduces no control that exists elsewhere.
 *
 * The region keeps `flex-1` either way, so the totals below do not jump upward when the first item
 * lands.
 */
function EmptySale() {
  return (
    <div className="min-h-0 flex-1 px-4 pt-6">
      <p className="text-body text-ink-faint">Scan an item to start the sale.</p>
    </div>
  );
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
      <div className="pos-totals-band flex items-end justify-between gap-2 px-3 py-3">
        {/*
          Full ink, not muted.

          Strengthening the band behind it left "TOTAL" at 4.61:1 -- over the 4.5 floor, but with
          almost nothing in hand, so the next person to deepen this wash would have broken it
          without noticing. It is also the label of the largest figure on the till, and there was
          never a reason for it to be the quiet half of that pair.
        */}
        <dt className="flex flex-col text-label font-semibold uppercase tracking-wide text-ink">
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
  const symbol = useCurrencySymbol();
  const canPay = Boolean(cart && cart.lines.length > 0 && !busy);

  /*
    The button says what it will charge.

    "Pay" alone asks the cashier to carry the figure from the totals block above it, and the moment
    that matters is the one where they are also handling money, a queue and a customer talking to
    them. It is also the check that catches the wrong basket before the drawer opens rather than
    after: the amount is on the control that commits it, so confirming and reading are one glance
    instead of two.

    Blank until there is something to charge, rather than "Pay 0.00" — a zero on a disabled button
    is a figure to double-take at, and the button is disabled at exactly that moment anyway.
  */
  const payable = canPay ? money(cart!.totals?.grandTotal ?? 0, symbol) : null;

  return (
    <section className="p-2.5" aria-label="Payment">
      <button
        type="button"
        className="pos-button-primary w-full justify-between gap-3 px-4 text-body-lg"
        style={{ minHeight: '3.75rem' }}
        disabled={!canPay}
        onClick={onPay}
      >
        <span className="flex items-center gap-1.5">
          Pay
          <span className="pos-fkey text-accent-foreground/80">
            <kbd>F4</kbd>
          </span>
        </span>

        {/* Tabular, so the figure does not shuffle sideways as digits change under a scanner. */}
        {payable ? <span className="pos-amount text-h3 font-semibold">{payable}</span> : null}
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
        <span className="flex items-center gap-2">
          <User className="h-5 w-5 shrink-0 text-ink-muted" aria-hidden />
          Customer
        </span>
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
              <X className="h-4 w-4" aria-hidden />
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
 *
 * It is also drawn as the busiest thing on the till, which it had stopped being. At 44px with an
 * 11px label it read as a form field on a screen of panels, and the one instruction a person who
 * has never worked a till needs is "put the barcode here". It is 56px now, the label is set in the
 * accent while the field holds focus, and the whole band tints — so "this is live and listening"
 * is answered without anybody having to find the caret. That focus state earns its keep on a
 * touchscreen, where tapping a dialog shut can leave focus somewhere else and the next scan goes
 * nowhere with no visible reason.
 */
export function ScanBox({ inputRef }: { inputRef: RefObject<HTMLInputElement> }) {
  const { scan, busy, error, clearError } = usePosStore();
  const [value, setValue] = useState('');

  return (
    <form
      className={cn(
        'group flex h-14 items-center gap-2.5 border-t border-subtle px-3 transition-colors duration-150',
        'focus-within:bg-accent-soft',
      )}
      onSubmit={(event) => {
        event.preventDefault();
        const entry = value;
        setValue('');
        void scan(entry);
      }}
    >
      <label
        htmlFor="pos-scan"
        className="flex shrink-0 items-center gap-2 text-body font-semibold uppercase tracking-wide text-ink-muted transition-colors duration-150 group-focus-within:text-accent-text"
      >
        <ScanLine className="h-6 w-6" aria-hidden />
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
        className="h-full min-w-0 flex-1 bg-transparent text-h3 text-ink outline-none placeholder:text-body-lg placeholder:text-ink-faint"
        disabled={busy}
      />

      {/*
        A failed scan is the most common thing that goes wrong at a till, and it was reported in an
        11px badge at the end of the row — the smallest text on the screen, for the message a
        cashier most needs to read with a customer waiting. It is a banner at reading size now, in
        the negative tone, with the glyph beside it.
      */}
      {error ? (
        <span
          className="flex min-w-0 shrink items-center gap-2 rounded-md bg-negative-soft px-3 py-1.5 text-body-lg font-semibold text-negative-text"
          role="alert"
        >
          <TriangleAlert className="h-6 w-6 shrink-0" aria-hidden />
          <span className="min-w-0">{error.message}</span>
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
          {/*
            The weight lives in `.pos-keybar > button .pos-kbd`, not here. A utility class on this
            element loses to that selector on specificity and is silently ignored — which is exactly
            what happened when the weight was first set here and appeared to change nothing.
          */}
          <kbd className="pos-kbd shrink-0">{entry.key}</kbd>
          {/* Not truncated. The bar sizes itself to these words now — a key bar whose whole job is
              to say what a key does has failed the moment it says "Sho…". */}
          <span>{entry.label}</span>
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
