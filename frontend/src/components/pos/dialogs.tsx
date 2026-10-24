'use client';

import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { usePosStore } from '@/stores/pos-store';
import { posApi } from '@/lib/pos-api';
import { useAllHotkeyBindings, useHotkey, useHotkeyBindings, useHotkeyScope } from '@/lib/hotkeys';
import { money, useCurrencySymbol } from '@/components/pos/panels';
import { useAuth } from '@/lib/auth-config';
import { parseTenderInput } from '@/lib/tender-input';
import type { ProductVariant, SerializedUnit, SuspendedCart, TenderType } from '@/types/pos';
import { ArrowRight, CheckCircle2, Download, Printer, ReceiptText, X } from 'lucide-react';
import { cn } from '@/lib/utils';
import { downloadPdf, printPdf } from '@/lib/print';
import { toast } from '@/components/ui/toaster';
import { describeError } from '@/lib/errors';

/**
 * Every dialog pushes the `dialog` hotkey scope, so the sale screen's F-keys stop firing while one
 * is open. That is what lets F4 mean Pay outside and Copies inside the payment window, matching the
 * legacy contract at guide p.8.
 */
function Shell({
  title,
  hint,
  onClose,
  children,
  wide,
  yieldsToScanner,
  closeHint = 'Press Esc, or tap outside, to cancel.',
}: {
  title: string;
  hint?: string;
  onClose: () => void;
  children: ReactNode;
  wide?: boolean;
  /**
   * A dialog that steps aside when the scanner starts typing, rather than holding the keyboard.
   * Only for a dialog with nothing to type into, where a scan can mean only "next sale" — see the
   * scan box, which reads this.
   */
  yieldsToScanner?: boolean;
  closeHint?: string;
}) {
  useHotkeyScope('dialog');
  useHotkey('Escape', onClose, { scope: 'dialog', label: 'Close', hidden: true });
  useHotkey('F12', onClose, { scope: 'dialog', label: 'F12 Cancel' });

  const panelRef = useRef<HTMLDivElement>(null);
  const bodyRef = useRef<HTMLDivElement>(null);

  // Read while rendering, before anything inside has mounted. By the time an effect runs, a field
  // with autoFocus already holds focus, and that field is what would have been remembered.
  const [returnFocusTo] = useState<HTMLElement | null>(() =>
    typeof document === 'undefined' ? null : (document.activeElement as HTMLElement | null),
  );

  /**
   * Focus goes into the dialog and comes back out again.
   *
   * Neither happened. Opening one left focus on the button behind it, so a keyboard user tabbed
   * through the sale underneath while the dialog covered it, and closing one dropped focus to the
   * body — which on this screen means the scan box stops receiving the next barcode.
   *
   * Into the body, and only when nothing inside has claimed it. The first focusable element in the
   * panel is the header's close button, added later, and this used to take it — pulling focus out
   * of the payment window's Tendered field the instant it autofocused. The cashier's digits then went
   * nowhere and their Enter pressed Close.
   *
   * Enabled controls only. While the payment window's tenders load, its one button is a disabled
   * Take; focusing that silently fails, which left focus on the scan box *behind* the dialog — so a
   * quick cashier's "100" landed in the scan box. With nothing enabled the panel itself takes focus.
   */
  useEffect(() => {
    const panel = panelRef.current;

    if (panel && !panel.contains(document.activeElement)) {
      const first = bodyRef.current?.querySelector<HTMLElement>(
        'input:not([type="hidden"]):not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled]), [tabindex]:not([tabindex="-1"])',
      );

      (first ?? panel).focus();
    }

    return () => {
      // A real close has already taken the panel out of the document by now. React's development
      // re-run of every effect has not, and handing focus back then pulled it out of the dialog
      // while the dialog stayed open — into the scan box behind it.
      if (panel?.isConnected) return;

      returnFocusTo?.focus?.();
    };
  }, [returnFocusTo]);

  /**
   * Tab is kept inside.
   *
   * Radix does this for the back office's dialogs; the till's own shell is hand-rolled because it
   * has to push a hotkey scope, and the focus trap never came with it.
   */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.key !== 'Tab') return;

    const focusable = [
      ...(panelRef.current?.querySelectorAll<HTMLElement>(
        'input:not([type="hidden"]):not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled]), [tabindex]:not([tabindex="-1"])',
      ) ?? []),
    ];

    if (focusable.length === 0) return;

    const first = focusable[0];
    const last = focusable[focusable.length - 1];

    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  return (
    <div
      className="fixed inset-0 z-overlay flex items-center justify-center bg-black/50 p-4"
      role="presentation"
      // Tapping outside closes it. The backdrop was inert, and with no close button either, a till
      // without a keyboard had no way out of any of these fourteen dialogs at all.
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        data-yields-to-scanner={yieldsToScanner ? 'true' : undefined}
        tabIndex={-1}
        onKeyDown={onKeyDown}
        className={cn('pos-panel w-full shadow-overlay', wide ? 'max-w-3xl' : 'max-w-md')}
      >
        <div className="pos-panel-header">
          <span>{title}</span>

          <span className="flex items-center gap-2">
            {hint ? <span className="normal-case">{hint}</span> : null}

            {/* A real target, on the screen most likely to be a touch panel. */}
            <button
              type="button"
              onClick={onClose}
              aria-label="Close"
              className="-m-2 flex h-11 w-11 shrink-0 items-center justify-center rounded-md text-ink-muted transition-colors hover:bg-panel-hover hover:text-ink"
            >
              <X className="h-6 w-6" aria-hidden />
            </button>
          </span>
        </div>

        <div ref={bodyRef} className="p-3">
          {children}
        </div>

        {/* Said out loud, because both ways out are otherwise things you have to already know. */}
        <p className="border-t border-subtle px-3 py-2 text-caption text-ink-muted">{closeHint}</p>
      </div>
    </div>
  );
}

function MenuButton({
  hotkey,
  label,
  onSelect,
  disabled,
}: {
  hotkey: string;
  label: string;
  onSelect: () => void;
  disabled?: boolean;
}) {
  useHotkey(hotkey, () => !disabled && onSelect(), { scope: 'dialog', label: `${hotkey} ${label}` });

  return (
    <button type="button" className="pos-button w-full px-3 text-left text-body" disabled={disabled} onClick={onSelect}>
      <span className="pos-fkey pl-0"><kbd>{hotkey}</kbd></span>
      {label}
    </button>
  );
}

/* ------------------------------------------------------------------- line detail drawer */

/**
 * The legacy Item Detail window (guide p.6), in the legacy tab order: quantity → price → discount →
 * level → tax 1 → tax 2. Muscle memory is fifteen years deep, so the order is not ours to improve.
 */
export function LineDetailDialog() {
  const { cart, selectedLineSequence, closeDialog, updateLine, removeLine, policy } = usePosStore();
  const line = cart?.lines.find((l) => l.sequence === selectedLineSequence);
  const symbol = useCurrencySymbol();

  const [quantity, setQuantity] = useState('1');
  const [price, setPrice] = useState('');
  const [discount, setDiscount] = useState('');
  const [level, setLevel] = useState('');

  const quantityRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!line) return;
    setQuantity(String(line.quantity));
    setPrice(line.hasManualPrice ? String(line.unitPrice) : '');
    setDiscount(line.discountPct ? String(line.discountPct) : '');
    setLevel(line.requestedPriceLevel ? String(line.requestedPriceLevel) : '');
    // Quantity is focused on open, because changing it is the overwhelmingly common reason to be here.
    quantityRef.current?.select();
  }, [line]);

  if (!line) return null;

  const tagged = Boolean(line.epc || line.serialNumber);

  const commit = () => {
    void updateLine(line.sequence, {
      quantity: tagged ? line.quantity : Number(quantity) || line.quantity,
      manualPrice: price === '' ? null : Number(price),
      manualDiscountPct: discount === '' ? null : Number(discount),
      priceLevel: level === '' ? null : Number(level),
      clear: [price === '' ? 'price' : '', discount === '' ? 'discount' : '', level === '' ? 'level' : ''].filter(Boolean),
    });
    closeDialog();
  };

  return (
    <Shell title={line.name} hint="Enter accepts · F12 cancels" onClose={closeDialog}>
      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault();
          commit();
        }}
      >
        {/*
          A tagged or serialized line is one physical thing, so the server refuses any other
          quantity. Saying so on the field is the difference between a rule and a rejection: the
          cashier who wants three scans three tags, rather than typing 3, pressing Accept, and
          hunting for the error message that explains why nothing happened.
        */}
        <Field label="Quantity" hint={tagged ? 'one per tag' : undefined}>
          <input
            ref={quantityRef}
            value={quantity}
            onChange={(event) => setQuantity(event.target.value)}
            inputMode="decimal"
            disabled={tagged}
            title={tagged ? 'This line is a tagged item. Scan another tag to sell another one.' : undefined}
            className="pos-amount w-full bg-transparent text-right outline-none disabled:text-ink-faint"
          />
        </Field>

        <Field label={`Price (${symbol})`} hint={line.hasManualPrice ? 'overridden' : 'automatic'}>
          <input
            value={price}
            onChange={(event) => setPrice(event.target.value)}
            inputMode="decimal"
            placeholder={line.unitPrice.toFixed(2)}
            className="pos-amount w-full bg-transparent text-right outline-none"
          />
        </Field>

        <Field label="Discount %">
          <input
            value={discount}
            onChange={(event) => setDiscount(event.target.value)}
            inputMode="decimal"
            placeholder="0"
            className="pos-amount w-full bg-transparent text-right outline-none"
          />
        </Field>

        <Field label="Price level" hint="F5">
          <input
            value={level}
            onChange={(event) => setLevel(event.target.value)}
            inputMode="numeric"
            placeholder="auto"
            className="pos-amount w-full bg-transparent text-right outline-none"
          />
        </Field>

        <div className="grid grid-cols-2 gap-2">
          <TaxToggle
            label={`${cart?.totals.tax1Name || 'Tax 1'}`}
            hotkey="F6"
            active={line.tax1Applies}
            disabled={!policy?.allowTaxOverride}
            onToggle={() => void updateLine(line.sequence, { tax1Override: !line.tax1Applies })}
          />
          <TaxToggle
            label={`${cart?.totals.tax2Name || 'Tax 2'}`}
            hotkey="F7"
            active={line.tax2Applies}
            disabled={!policy?.allowTaxOverride}
            onToggle={() => void updateLine(line.sequence, { tax2Override: !line.tax2Applies })}
          />
        </div>

        <div className="flex gap-2 pt-1">
          <button type="submit" className="pos-button-primary flex-1 text-body">Accept</button>
          <button
            type="button"
            className="pos-button-danger px-3"
            onClick={() => {
              void removeLine(line.sequence);
              closeDialog();
            }}
          >
            Delete line
          </button>
        </div>
      </form>
    </Shell>
  );
}

function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <label className="flex items-center justify-between gap-3 border-b border-subtle pb-1">
      <span className="text-body text-ink-muted">
        {label}
        {hint ? <span className="ml-1 text-label">({hint})</span> : null}
      </span>
      <span className="w-32">{children}</span>
    </label>
  );
}

function TaxToggle({
  label,
  hotkey,
  active,
  disabled,
  onToggle,
}: {
  label: string;
  hotkey: string;
  active: boolean;
  disabled?: boolean;
  onToggle: () => void;
}) {
  useHotkey(hotkey, () => !disabled && onToggle(), { scope: 'dialog', label: `${hotkey} ${label}`, disabled });

  return (
    <button
      type="button"
      onClick={onToggle}
      disabled={disabled}
      aria-pressed={active}
      className="pos-button px-2 text-body"
      style={active ? { borderColor: 'oklch(var(--positive))', color: 'oklch(var(--positive))' } : undefined}
    >
      <span className="pos-fkey pl-0"><kbd>{hotkey}</kbd></span>
      {label} {active ? 'on' : 'off'}
    </button>
  );
}

/* ------------------------------------------------------------------------ payment window */

/**
 * The tender list from the last time it was asked for.
 *
 * The payment window fetched it on every open and drew nothing until it arrived, so for the first
 * tenth of a second or so there was no Tendered field to type into. Starting from the last answer
 * puts the field on screen, focused, the moment F4 is pressed; the fetch still runs on every open,
 * so a tender an administrator changes is picked up on the next one.
 */
let tenderTypesSeen: TenderType[] | null = null;

function activeTenders(list: TenderType[]): TenderType[] {
  return list.filter((t) => t.isActive).sort((a, b) => a.sortOrder - b.sortOrder);
}

/** Warms {@link tenderTypesSeen} when the till starts, so even the first sale's window opens ready. */
export function prefetchTenderTypes(): void {
  posApi
    .tenderTypes()
    .then((list) => {
      tenderTypesSeen = activeTenders(list);
    })
    .catch(() => {
      // The payment window asks again when it opens, and reports nothing to pay with if that fails too.
    });
}

export function PaymentDialog() {
  const { cart, policy, closeDialog, complete, busy, error } = usePosStore();
  const [tenders, setTenders] = useState<TenderType[]>(() => tenderTypesSeen ?? []);
  const [selected, setSelected] = useState<TenderType | null>(() =>
    tenderTypesSeen
      ? (tenderTypesSeen.find((t) => t.id === policy?.defaultTenderTypeId) ?? tenderTypesSeen[0] ?? null)
      : null,
  );
  const [tendered, setTendered] = useState('');
  const [copies, setCopies] = useState(1);
  const [attempted, setAttempted] = useState(false);

  const due = cart?.totals.grandTotal ?? 0;
  const symbol = useCurrencySymbol();

  useEffect(() => {
    posApi
      .tenderTypes()
      .then((list) => {
        const active = activeTenders(list);
        tenderTypesSeen = active;
        setTenders(active);

        // Keep the cashier's choice if they have already made one and it still exists; a refreshed
        // list arriving must not snap the selection back to the default under their finger.
        setSelected(
          (current) =>
            active.find((t) => t.id === current?.id) ??
            active.find((t) => t.id === policy?.defaultTenderTypeId) ??
            active[0] ??
            null,
        );
      })
      .catch(() => {
        if (!tenderTypesSeen) setTenders([]);
      });
  }, [policy?.defaultTenderTypeId]);

  // Inside this window F4 means Copies, not Pay — the legacy contract at guide p.8.
  useHotkey('F4', () => setCopies((c) => (c >= 3 ? 1 : c + 1)), { scope: 'dialog', label: 'F4 Copies' });

  // Parsed, not coerced. `Number(tendered) || due` used to turn anything falsy — `abc`, an empty
  // field, a stray scanner character — into the exact amount owed, settling the sale with an empty
  // drawer and no way to tell afterwards.
  const parsed = parseTenderInput(tendered, due);
  const amount = parsed.ok ? (parsed.exact ? due : parsed.amount) : null;
  const change = selected?.allowsOverTender && amount !== null ? Math.max(0, amount - due) : 0;
  const tenderError = parsed.ok ? null : parsed.message;

  // A cash tender needs a valid figure before Pay does anything. Non-cash legs settle to the exact
  // amount and have no tendered field to get wrong.
  const canPay = Boolean(selected) && (!selected?.allowsOverTender || parsed.ok);

  const submit = async () => {
    if (!selected || !canPay || amount === null) return;

    setAttempted(true);

    // On success the store moves straight to the sale-complete screen; closing here would close that.
    await complete(
      [
        {
          tenderTypeId: selected.id,
          amount: due,
          amountTendered: selected.roundsToMinimumTender ? amount : due,
        },
      ],
      { copies },
    );
  };

  return (
    <Shell title="Payment" hint={`${copies} cop${copies === 1 ? 'y' : 'ies'} · F4 changes`} onClose={closeDialog}>
      {/*
        The figure, drawn exactly as the till behind this dialog draws it.

        It was `text-2xl` — off the scale, so it would not move if the scale were ever re-pitched,
        and two-thirds the size of the total sitting a few centimetres behind it on the same screen.
        The point of a payment window is to confirm a number, and a number that shrinks between the
        screen and the confirmation is one nobody can compare at a glance. `.pos-grand-total` is the
        same class the totals panel uses, so the two cannot drift apart.
      */}
      <div className="mb-3 flex items-baseline justify-between gap-3 border-b border-subtle pb-2">
        <span className="text-body text-ink-muted">Amount due</span>
        <span className="pos-grand-total text-ink">{money(due, symbol)}</span>
      </div>

      <div className="grid grid-cols-3 gap-2">
        {tenders.map((tender) => (
          <button
            key={String(tender.id)}
            type="button"
            onClick={() => setSelected(tender)}
            aria-pressed={selected?.id === tender.id}
            className="pos-button px-2 text-body"
            style={selected?.id === tender.id ? { borderColor: 'oklch(var(--accent))', borderWidth: 2 } : undefined}
          >
            {tender.displayName}
          </button>
        ))}
      </div>

      {selected?.allowsOverTender ? (
        <label className="mt-3 flex items-center justify-between gap-3 border-b border-subtle pb-1">
          <span className="text-body text-ink-muted">Tendered</span>
          <input
            value={tendered}
            onChange={(event) => setTendered(event.target.value)}
            inputMode="decimal"
            placeholder={due.toFixed(2)}
            autoFocus
            aria-invalid={tenderError !== null}
            aria-describedby={tenderError ? 'tender-error' : undefined}
            className={cn(
              'pos-amount w-32 bg-transparent text-right text-h3 outline-none',
              // The readable tone, not the fill tone: `--negative` measures about 4.3:1 on white,
              // under AA, and this is the field somebody is being told they mistyped.
              tenderError && 'text-negative-text',
            )}
          />
        </label>
      ) : null}

      {tenderError ? (
        <p id="tender-error" role="alert" className="mt-2 text-body text-negative-text">
          {tenderError}
        </p>
      ) : null}

      {change > 0 ? (
        <div className="mt-2 flex justify-between text-body">
          <span className="text-ink-muted">Change</span>
          <span className="pos-amount text-h3 font-semibold text-positive-text">
            {money(change, symbol)}
          </span>
        </div>
      ) : null}

      {/*
        Why the payment was refused, here where the cashier is looking. It was reported only in the
        scan bar, behind this window and under its shade, so "Open a drawer before taking cash" read
        as a Take button that did nothing. Only after a press here: an error left over from a scan
        before the window opened is not this window's to repeat.
      */}
      {attempted && error && !busy ? (
        <p role="alert" className="mt-3 rounded-md bg-negative-soft px-3 py-2 text-body font-semibold text-negative-text">
          {error.message}
        </p>
      ) : null}

      <button
        type="button"
        className="pos-button-primary mt-4 w-full text-body-lg"
        disabled={busy || !canPay}
        onClick={() => void submit()}
      >
        {busy ? 'Saving…' : `Take ${money(due, symbol)}`}
      </button>
    </Shell>
  );
}

/* ------------------------------------------------------------------------ sale complete */

/**
 * What the till shows the moment a sale is paid: the receipt, and what to do with it.
 *
 * Paying used to close straight onto an empty sale. The change owed survived as a small line in the
 * status bar, and getting a receipt on paper or keeping a copy meant knowing that Ctrl+P and F7
 * existed. A shop asked for the obvious thing — pay, then print or save — so this is the screen
 * after Pay.
 *
 * Built for somebody on their first day, or whose eyesight is not what it was: one large figure
 * (the change to hand over), large buttons that say in words what they do, and the next customer one
 * press away. It never holds the queue up. A till printer has already printed the receipt by the
 * time this appears, Esc or Enter moves on, and scanning the next item closes it by itself.
 */
export function SaleCompleteDialog() {
  const { lastSale, stationId, peripherals, closeDialog } = usePosStore();
  const symbol = useCurrencySymbol();

  const [pdf, setPdf] = useState<Blob | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [previewFailed, setPreviewFailed] = useState(false);
  const [working, setWorking] = useState<'print' | 'save' | 'till' | null>(null);

  const transactionId = lastSale?.transactionId;

  useEffect(() => {
    if (!transactionId) return undefined;

    let cancelled = false;
    let url: string | null = null;

    posApi
      .receiptPdf(transactionId)
      .then((blob) => {
        if (cancelled) return;

        setPdf(blob);

        // Drawn only where the browser shows a PDF itself. Where it does not, an embedded PDF is
        // downloaded instead, and a file landing in Downloads after every sale is worse than no
        // preview at all.
        if (navigator.pdfViewerEnabled) {
          url = URL.createObjectURL(blob);
          setPreviewUrl(url);
        }
      })
      .catch(() => {
        if (!cancelled) setPreviewFailed(true);
      });

    return () => {
      cancelled = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [transactionId]);

  // The copy already fetched for the preview, or a fresh one if that fetch failed or is still out.
  const receipt = async (): Promise<Blob> => pdf ?? posApi.receiptPdf(transactionId!);

  const print = async () => {
    if (!transactionId || working) return;
    setWorking('print');

    try {
      const outcome = await printPdf(await receipt());

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
    if (!lastSale || working) return;
    setWorking('save');

    try {
      downloadPdf(await receipt(), `receipt-${lastSale.transactionNumber}.pdf`);
      toast({ variant: 'success', title: 'Receipt saved', description: 'Look in your Downloads folder.' });
    } catch (error) {
      toast({ title: 'Could not save the receipt', description: describeError(error), variant: 'destructive' });
    } finally {
      setWorking(null);
    }
  };

  // Only offered where an agent reports a printer: without one there is nothing to send it to, and a
  // button that cannot work is one more thing to get wrong.
  const hasTillPrinter = Boolean(stationId) && peripherals !== null;

  const printOnTill = async () => {
    if (!stationId || !hasTillPrinter || working) return;
    setWorking('till');

    try {
      await posApi.reprintLast(stationId);
      toast({ variant: 'success', title: 'Sent to the till printer' });
    } catch (error) {
      toast({ title: 'Could not print', description: describeError(error), variant: 'destructive' });
    } finally {
      setWorking(null);
    }
  };

  // Ctrl+P and F7 are the keys the sale screen already uses for these two, so they mean the same here.
  useHotkey('Ctrl+P', () => void print(), { scope: 'dialog', label: 'Ctrl+P Print receipt' });
  useHotkey('Ctrl+S', () => void save(), { scope: 'dialog', label: 'Ctrl+S Save receipt' });
  useHotkey('F7', () => void printOnTill(), { scope: 'dialog', label: 'F7 Till printer', disabled: !hasTillPrinter });

  if (!lastSale) return null;

  const change = lastSale.changeGiven;

  return (
    <Shell
      title="Sale complete"
      hint={`Receipt #${lastSale.transactionNumber}`}
      onClose={closeDialog}
      wide
      yieldsToScanner
      closeHint="Scan the next item, or press Esc, to start the next sale."
    >
      <div className={cn('grid gap-4', previewUrl && 'md:grid-cols-[minmax(0,1fr)_19rem]')}>
        {previewUrl ? (
          <iframe
            src={`${previewUrl}#toolbar=0&navpanes=0`}
            title={`Receipt ${lastSale.transactionNumber}`}
            className="h-[28rem] w-full rounded-md border border-subtle bg-white"
          />
        ) : null}

        <div className="flex flex-col gap-3">
          {/* The one thing the cashier must act on before anything else: money back to the customer. */}
          <div role="status" className="rounded-lg bg-positive-soft px-4 py-4 text-center">
            <CheckCircle2 className="mx-auto h-10 w-10 text-positive-text" aria-hidden />
            <p className="mt-1 text-h3 font-semibold text-positive-text">Paid {money(lastSale.total, symbol)}</p>

            {change > 0 ? (
              <>
                <p className="mt-3 text-body-lg text-ink">Give the customer</p>
                <p className="pos-grand-total mt-1 text-positive-text">{money(change, symbol)}</p>
                <p className="mt-1 text-body-lg text-ink">change</p>
              </>
            ) : (
              <p className="mt-2 text-body-lg text-ink">No change to give</p>
            )}
          </div>

          <button
            type="button"
            className="pos-button-primary h-16 w-full justify-between px-5 text-body-lg"
            onClick={closeDialog}
            autoFocus
          >
            <span className="flex items-center gap-3">
              <ArrowRight className="h-7 w-7" aria-hidden />
              Next customer
            </span>
            <kbd className="pos-kbd">Enter</kbd>
          </button>

          <ReceiptAction
            icon={<Printer className="h-7 w-7" aria-hidden />}
            label={working === 'print' ? 'Printing…' : 'Print receipt'}
            keys="Ctrl+P"
            onSelect={() => void print()}
            disabled={working !== null}
          />

          <ReceiptAction
            icon={<Download className="h-7 w-7" aria-hidden />}
            label={working === 'save' ? 'Saving…' : 'Save as PDF'}
            keys="Ctrl+S"
            onSelect={() => void save()}
            disabled={working !== null}
          />

          {hasTillPrinter ? (
            <ReceiptAction
              icon={<ReceiptText className="h-7 w-7" aria-hidden />}
              label={
                working === 'till'
                  ? 'Sending…'
                  : peripherals?.printerOnline
                    ? 'Another copy on the till printer'
                    : 'Till printer is offline'
              }
              keys="F7"
              onSelect={() => void printOnTill()}
              disabled={working !== null || !peripherals?.printerOnline}
            />
          ) : null}

          {previewFailed ? (
            <p role="alert" className="text-body text-ink-muted">
              The receipt could not be shown here. Print and Save will try again.
            </p>
          ) : null}
        </div>
      </div>
    </Shell>
  );
}

function ReceiptAction({
  icon,
  label,
  keys,
  onSelect,
  disabled,
}: {
  icon: ReactNode;
  label: string;
  keys: string;
  onSelect: () => void;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      className="pos-button h-16 w-full justify-between px-5 text-body-lg"
      onClick={onSelect}
      disabled={disabled}
    >
      <span className="flex items-center gap-3">
        {icon}
        {label}
      </span>
      <kbd className="pos-kbd">{keys}</kbd>
    </button>
  );
}

/* ------------------------------------------------------------------------- credits menu */

/** F8 Credits (guide p.7). Returns and trade-ins are lines, not sale-level credits. */
export function CreditsDialog() {
  const { closeDialog, addAdjustment, openDialog } = usePosStore();
  const [mode, setMode] = useState<null | 'discount' | 'coupon' | 'bottle'>(null);

  if (mode) {
    return <AmountPrompt mode={mode} onCancel={() => setMode(null)} onSubmit={addAdjustment} />;
  }

  return (
    <Shell title="Credits" onClose={closeDialog}>
      <div className="space-y-2">
        <MenuButton hotkey="F4" label="Return an item" onSelect={() => openDialog('find')} />
        <MenuButton hotkey="F5" label="Subtotal discount" onSelect={() => setMode('discount')} />
        <MenuButton hotkey="F6" label="Bottle return" onSelect={() => setMode('bottle')} />
        <MenuButton hotkey="F7" label="Coupon" onSelect={() => setMode('coupon')} />
        <MenuButton
          hotkey="F8"
          label="Redeem loyalty reward"
          onSelect={() => void addAdjustment({ type: 'LoyaltyReward', label: 'Loyalty reward' })}
        />
      </div>
    </Shell>
  );
}

function AmountPrompt({
  mode,
  onCancel,
  onSubmit,
}: {
  mode: 'discount' | 'coupon' | 'bottle';
  onCancel: () => void;
  onSubmit: (body: { type: string; label: string; amount?: number; percent?: number }) => Promise<void>;
}) {
  const [value, setValue] = useState('');
  const [asPercent, setAsPercent] = useState(mode === 'discount');

  const titles = { discount: 'Subtotal discount', coupon: 'Coupon', bottle: 'Bottle return' } as const;
  const types = { discount: 'SubtotalDiscount', coupon: 'Coupon', bottle: 'BottleReturn' } as const;

  return (
    <Shell title={titles[mode]} onClose={onCancel}>
      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault();
          const numeric = Number(value);
          if (!numeric) return;

          void onSubmit({
            type: types[mode],
            label: titles[mode],
            amount: asPercent ? 0 : numeric,
            percent: asPercent ? numeric : 0,
          });
        }}
      >
        <label className="flex items-center justify-between gap-3 border-b border-subtle pb-1">
          <span className="text-body text-ink-muted">{asPercent ? 'Percent' : 'Amount'}</span>
          <input
            value={value}
            onChange={(event) => setValue(event.target.value)}
            inputMode="decimal"
            autoFocus
            className="pos-amount w-32 bg-transparent text-right text-h3 outline-none"
          />
        </label>

        {mode === 'discount' ? (
          <button type="button" className="pos-button w-full text-body" onClick={() => setAsPercent((p) => !p)}>
            Switch to {asPercent ? 'a fixed amount' : 'a percentage'}
          </button>
        ) : null}

        <button type="submit" className="pos-button-primary w-full text-body">Apply</button>
      </form>
    </Shell>
  );
}

/* ------------------------------------------------------------------------- special menu */

/** F11 Special (guide p.11). */
export function SpecialDialog() {
  const { cart, closeDialog, setTaxOverride, openDialog, policy, clearLines } = usePosStore();
  const [confirmingClear, setConfirmingClear] = useState(false);

  // Only to word the question honestly. The server decides; this just avoids telling a cashier
  // the sale will clear when what happens next is a manager being asked.
  const canClearAlone = useAuth().can('pos.clear_sale');

  const lineCount = cart?.lines.length ?? 0;

  return (
    <Shell title="Special" onClose={closeDialog}>
      <div className="space-y-2">
        {/*
          Asks what to call it.

          This called `suspend()` with no argument, so every held sale went on the shelf as
          "Unlabelled hold". One is fine. Three, on a Saturday, are three identical rows and a
          cashier guessing which belongs to the customer who went back for milk -- recalling the
          wrong one in front of them, then having to suspend it again. The name is the whole point
          of being able to hold more than one.
        */}
        <MenuButton hotkey="F4" label="Suspend this sale" onSelect={() => openDialog('suspend')} disabled={!cart} />
        <MenuButton hotkey="F5" label="Recall a suspended sale" onSelect={() => openDialog('suspended')} />
        <MenuButton
          hotkey="F6"
          label={cart?.taxOverride1 === false ? 'Restore taxes for this sale' : 'Suspend taxes for the rest of this sale'}
          disabled={!policy?.allowTaxOverride || !cart}
          onSelect={() =>
            void setTaxOverride(
              cart?.taxOverride1 === false ? null : false,
              cart?.taxOverride2 === false ? null : false,
            )
          }
        />
        <MenuButton hotkey="F7" label="Unknown item" onSelect={() => openDialog('unknownItem')} />

        {/*
          Clearing the sale, which until now could not be done at all.
          
          `clearLines` has existed in the store and in the API the whole time and was wired to no
          control and no key — so a cashier facing a sale that had gone wrong had to delete the lines
          one at a time with F6, or suspend it and abandon it. It asks first, because it throws away
          work and sits in a menu somebody is holding open mid-transaction.
        */}
        {confirmingClear ? (
          <div className="rounded-md border border-negative/40 bg-negative-soft p-3">
            <p className="text-body font-semibold text-negative-text">
              Take {lineCount} line{lineCount === 1 ? '' : 's'} off this sale?
            </p>
            <p className="mt-1 text-body text-ink-muted">
              The sale stays open and empty. Nothing is recorded and no stock moves.
            </p>

            {canClearAlone ? null : (
              <p className="mt-2 text-body font-semibold text-ink">
                A manager has to approve this. They will be asked for their staff code and PIN.
              </p>
            )}

            <div className="mt-3 flex gap-2">
              <button type="button" className="pos-button" onClick={() => setConfirmingClear(false)}>
                Keep the sale
              </button>
              <button
                type="button"
                className="pos-button-danger"
                onClick={() => {
                  void clearLines();
                  setConfirmingClear(false);
                  closeDialog();
                }}
              >
                Clear the sale
              </button>
            </div>
          </div>
        ) : (
          <MenuButton
            hotkey="F8"
            label="Clear this sale"
            disabled={lineCount === 0}
            onSelect={() => setConfirmingClear(true)}
          />
        )}

        <MenuButton hotkey="F9" label="Keyboard shortcuts" onSelect={() => openDialog('cheatSheet')} />
      </div>

      {cart?.taxOverride1 === false ? (
        <p className="mt-3 text-label text-warning">
          Taxes are suspended for items rung from here on. Lines already on the screen keep the tax they were rung with.
        </p>
      ) : null}
    </Shell>
  );
}

/* -------------------------------------------------------------------------- drawer menu */

/** F10 Drawer (guide p.10–11). */
export function DrawerDialog() {
  const { drawer, closeDialog, stationId, refreshDrawer, policy } = usePosStore();
  const [mode, setMode] = useState<null | 'float' | 'payIn' | 'payOut' | 'close'>(null);
  const [value, setValue] = useState('');
  const [reason, setReason] = useState('');
  const [failure, setFailure] = useState<string | null>(null);
  const symbol = useCurrencySymbol();

  const choose = (next: typeof mode) => {
    setFailure(null);
    setMode(next);
  };

  /**
   * Says what went wrong, and stays put so it can be corrected.
   *
   * This was try/finally with no catch. A refusal — opening a float on a drawer that was already
   * open, a pay-out with no reason — reset the form and was thrown away as an unhandled rejection,
   * so the cashier saw the window simply go back to the menu, with nothing done and nothing said.
   * The amount went through `Number()` too, which reads a blank field as zero and passes `abc` on
   * as NaN; it is parsed the way the payment window parses cash now.
   */
  const run = async () => {
    if (!stationId) return;

    const parsed = parseTenderInput(value, 0);

    if (!parsed.ok || parsed.amount === null) {
      setFailure(parsed.ok ? 'Enter an amount.' : parsed.message);
      return;
    }

    const amount = parsed.amount;

    try {
      if (mode === 'float') await posApi.drawer.open(stationId, amount);
      if (mode === 'payIn') await posApi.drawer.payIn(stationId, amount, reason);
      if (mode === 'payOut') await posApi.drawer.payOut(stationId, amount, reason);
      if (mode === 'close') await posApi.drawer.close(stationId, amount);

      setFailure(null);
      setMode(null);
      setValue('');
      setReason('');
    } catch (error) {
      setFailure(describeError(error));
    } finally {
      await refreshDrawer();
    }
  };

  if (mode) {
    const needsReason = mode === 'payIn' || mode === 'payOut';

    return (
      <Shell title={{ float: 'Opening float', payIn: 'Pay in', payOut: 'Pay out', close: 'Close drawer' }[mode]} onClose={() => choose(null)}>
        <form
          className="space-y-3"
          onSubmit={(event) => {
            event.preventDefault();
            void run();
          }}
        >
          <label className="flex items-center justify-between gap-3 border-b border-subtle pb-1">
            <span className="text-body text-ink-muted">
              {mode === 'close' ? 'Cash counted' : 'Amount'}
            </span>
            <input
              value={value}
              onChange={(event) => setValue(event.target.value)}
              inputMode="decimal"
              autoFocus
              className="pos-amount w-32 bg-transparent text-right text-h3 outline-none"
            />
          </label>

          {needsReason ? (
            <label className="flex items-center justify-between gap-3 border-b border-subtle pb-1">
              <span className="text-body text-ink-muted">Reason</span>
              <input
                value={reason}
                onChange={(event) => setReason(event.target.value)}
                required
                className="w-48 bg-transparent text-right outline-none"
              />
            </label>
          ) : null}

          {failure ? (
            <p role="alert" className="rounded-md bg-negative-soft px-3 py-2 text-body font-semibold text-negative-text">
              {failure}
            </p>
          ) : null}

          <button type="submit" className="pos-button-primary w-full text-body">Confirm</button>
        </form>
      </Shell>
    );
  }

  return (
    <Shell title="Drawer" onClose={closeDialog} wide>
      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-2">
          <MenuButton hotkey="F4" label="Opening float" onSelect={() => choose('float')} disabled={drawer?.status === 'Open'} />
          <MenuButton hotkey="F5" label="Pay in" onSelect={() => choose('payIn')} disabled={drawer?.status !== 'Open'} />
          <MenuButton hotkey="F6" label="Pay out" onSelect={() => choose('payOut')} disabled={drawer?.status !== 'Open'} />
          <MenuButton
            hotkey="F7"
            label="Pop drawer (no sale)"
            disabled={drawer?.status !== 'Open'}
            onSelect={() =>
              stationId &&
              void posApi.drawer
                .pop(stationId)
                .then(refreshDrawer)
                .catch((error: unknown) =>
                  toast({ title: 'The drawer did not open', description: describeError(error), variant: 'destructive' }),
                )
            }
          />
          <MenuButton hotkey="F8" label="Close drawer" onSelect={() => choose('close')} disabled={drawer?.status !== 'Open'} />
        </div>

        <dl className="space-y-1 text-body">
          {drawer ? (
            <>
              <DrawerRow label="Opening float" value={money(drawer.openingFloat, symbol)} />
              <DrawerRow label="Cash sales" value={money(drawer.cashSales, symbol)} />
              <DrawerRow label="Refunds" value={money(drawer.cashRefunds, symbol)} />
              <DrawerRow label="Pay ins" value={money(drawer.payIns, symbol)} />
              <DrawerRow label="Pay outs" value={money(drawer.payOuts, symbol)} />
              <div className="border-t border-subtle pt-1">
                <DrawerRow label="Expected cash" value={money(drawer.expectedCash, symbol)} strong />
              </div>
              {drawer.countedCash !== null ? (
                <>
                  <DrawerRow label="Counted" value={money(drawer.countedCash, symbol)} />
                  <DrawerRow
                    label="Variance"
                    value={money(drawer.variance ?? 0, symbol)}
                    tone={(drawer.variance ?? 0) === 0 ? 'positive' : 'negative'}
                    strong
                  />
                </>
              ) : null}
            </>
          ) : (
            <p className="text-ink-muted">No drawer session is open at this till.</p>
          )}
        </dl>
      </div>
    </Shell>
  );
}

function DrawerRow({
  label,
  value,
  tone,
  strong,
}: {
  label: string;
  value: string;
  tone?: 'positive' | 'negative';
  strong?: boolean;
}) {
  const colour = tone === 'positive' ? 'oklch(var(--positive))' : tone === 'negative' ? 'oklch(var(--negative))' : undefined;

  return (
    <div className="flex justify-between">
      <dt className="text-ink-muted">{label}</dt>
      <dd className={cn('pos-amount', strong && 'font-semibold')} style={colour ? { color: colour } : undefined}>
        {value}
      </dd>
    </div>
  );
}

/* ------------------------------------------------------------------------ find / recall */

export function FindDialog() {
  const { closeDialog, scan, locationId } = usePosStore();
  const [term, setTerm] = useState('');
  const [results, setResults] = useState<Array<{ id: number; stockCode: string; name: string; regularPrice: number }>>([]);

  useEffect(() => {
    if (term.length < 2 || !locationId) {
      setResults([]);
      return undefined;
    }

    const timer = setTimeout(() => {
      posApi
        .searchProducts(term, locationId)
        .then((products) => setResults(products as never))
        .catch(() => setResults([]));
    }, 200);

    return () => clearTimeout(timer);
  }, [term, locationId]);

  return (
    <Shell title="Find item" hint="Enter picks the first result" onClose={closeDialog} wide>
      <input
        value={term}
        onChange={(event) => setTerm(event.target.value)}
        autoFocus
        placeholder="Stock code or name"
        className="mb-2 w-full border-b border-subtle bg-transparent pb-1 outline-none"
        onKeyDown={(event) => {
          if (event.key === 'Enter' && results[0]) {
            void scan(results[0].stockCode);
            closeDialog();
          }
        }}
      />

      <ul className="max-h-72 overflow-y-auto">
        {results.map((product) => (
          <li key={String(product.id)}>
            <button
              type="button"
              className="flex w-full items-center justify-between px-1 py-1.5 text-left text-body hover:bg-panel-hover"
              onClick={() => {
                void scan(product.stockCode);
                closeDialog();
              }}
            >
              <span className="flex gap-3">
                <span className="tabular w-24 text-ink-muted">{product.stockCode}</span>
                <span>{product.name}</span>
              </span>
              <span className="pos-amount">{money(product.regularPrice)}</span>
            </button>
          </li>
        ))}
      </ul>
    </Shell>
  );
}

/**
 * What to call a sale being put on hold.
 *
 * Pre-filled rather than blank, because the useful answer is nearly always already known: the
 * customer's name when one is attached, and otherwise the time it went on hold, which is enough to
 * tell two holds apart ten minutes later. A cashier who agrees presses Enter and is done; the field
 * is selected on open so typing over it costs nothing either.
 *
 * The name can still be emptied. A hold is better than a lost sale, and refusing to suspend until
 * somebody types something would be this dialog getting in the way of the thing it exists to help.
 */
export function SuspendDialog() {
  const { cart, suspend, closeDialog, busy, error } = usePosStore();

  const suggested =
    cart?.customer?.name ??
    new Date().toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });

  const [label, setLabel] = useState(suggested);

  return (
    <Shell title="Put this sale on hold" hint="F4" onClose={closeDialog}>
      <form
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault();
          void suspend(label.trim() || undefined);
        }}
      >
        <label className="block">
          <span className="text-body text-ink-muted">Name this hold, so you can find it again</span>
          <input
            value={label}
            onChange={(event) => setLabel(event.target.value)}
            autoFocus
            onFocus={(event) => event.target.select()}
            autoComplete="off"
            maxLength={60}
            className="mt-1 w-full rounded-md border border-control bg-panel px-3 text-body text-ink outline-none focus-visible:border-accent"
            style={{ minHeight: 'var(--control-height)' }}
          />
        </label>

        <p className="text-label text-ink-muted">
          The sale stays on hold until somebody recalls it with F5. Nothing is sold and no stock moves.
        </p>

        {error ? (
          <p className="text-body text-negative" role="alert">
            {error.message}
          </p>
        ) : null}

        <div className="flex gap-2">
          <button type="button" className="pos-button flex-1" onClick={closeDialog}>
            Cancel
          </button>
          <button type="submit" className="pos-button-primary flex-1 text-body" disabled={busy}>
            {busy ? 'Putting on hold\u2026' : 'Put on hold'}
          </button>
        </div>
      </form>
    </Shell>
  );
}

export function SuspendedCartsDialog() {
  const { closeDialog, recall, locationId } = usePosStore();
  const [carts, setCarts] = useState<SuspendedCart[]>([]);

  useEffect(() => {
    if (!locationId) return;
    posApi.listSuspended(locationId).then(setCarts).catch(() => setCarts([]));
  }, [locationId]);

  return (
    <Shell title="Suspended sales" onClose={closeDialog} wide>
      {carts.length === 0 ? (
        <p className="py-4 text-center text-body text-ink-muted">Nothing is on hold.</p>
      ) : (
        <ul className="max-h-72 overflow-y-auto">
          {carts.map((cart) => (
            <li key={String(cart.id)}>
              <button
                type="button"
                className="flex w-full items-center justify-between px-1 py-2 text-left text-body hover:bg-panel-hover"
                onClick={() => void recall(cart.id)}
              >
                <span>
                  <span className="font-medium">{cart.label ?? 'Unnamed hold'}</span>
                  <span className="ml-2 text-label text-ink-muted">
                    {cart.customerName ?? 'Walk-in'} · {cart.lineCount} lines
                  </span>
                </span>
                <span className="pos-amount">{money(cart.grandTotal)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </Shell>
  );
}

/* ------------------------------------------------------------- staff switch and supervisor step-up */

/**
 * Ctrl+I staff switch (guide p.13, doc 07 §POS fast user switching).
 *
 * A cashier cannot type a full password between customers, so a PIN re-attributes the sale inside
 * the station's existing session. It is not a login: the station is already authenticated, and the
 * PIN only decides whose name goes on the receipt and the commission report.
 */
export function StaffSwitchDialog() {
  const { closeDialog, switchStaff, stationId, busy, error } = usePosStore();
  const [staffCode, setStaffCode] = useState('');
  const [pin, setPin] = useState('');

  return (
    <Shell title="Staff" hint="Ctrl+I" onClose={closeDialog}>
      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault();
          if (stationId) void switchStaff(staffCode, pin);
        }}
      >
        <label className="flex items-center justify-between gap-3 border-b border-subtle pb-1">
          <span className="text-body text-ink-muted">Staff code</span>
          <input
            value={staffCode}
            onChange={(event) => setStaffCode(event.target.value.toUpperCase())}
            autoFocus
            autoComplete="off"
            className="w-32 bg-transparent text-right uppercase outline-none"
          />
        </label>

        <label className="flex items-center justify-between gap-3 border-b border-subtle pb-1">
          <span className="text-body text-ink-muted">PIN</span>
          <input
            value={pin}
            onChange={(event) => setPin(event.target.value)}
            type="password"
            inputMode="numeric"
            autoComplete="off"
            className="pos-amount w-32 bg-transparent text-right text-h3 outline-none"
          />
        </label>

        {error ? (
          <p className="text-body text-negative" role="alert">
            {error.message}
          </p>
        ) : null}

        <button type="submit" className="pos-button-primary w-full text-body" disabled={busy}>
          Switch
        </button>
      </form>
    </Shell>
  );
}

/**
 * The supervisor override prompt (doc 07 §Step-up).
 *
 * Opened when a command answers 428. A supervisor either types their PIN here, or approves from
 * whichever till they are standing at — the request is broadcast to the whole location, which is the
 * improvement over a legacy prompt that required them to walk over and type into someone else's
 * session.
 */
export function SupervisorApprovalDialog() {
  const { closeDialog, pendingApproval, approveWithPin, busy, error } = usePosStore();
  const [staffCode, setStaffCode] = useState('');
  const [pin, setPin] = useState('');

  if (!pendingApproval) return null;

  return (
    <Shell title="Manager approval" onClose={closeDialog}>
      {/*
        What is being approved, first and largest. The manager is being asked to vouch for something
        with their own PIN; they should read what it is before they type it, not find it in a hint
        in the corner of the header.
      */}
      {pendingApproval.context ? (
        <p className="mb-3 rounded-md border border-warning/40 bg-warning-soft px-3 py-2 text-body-lg font-semibold text-ink">
          {pendingApproval.context}
        </p>
      ) : null}

      <p className="mb-4 text-body-lg text-ink">
        A manager needs to approve this. Manager: enter your staff code and PIN.
      </p>

      <form
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault();
          void approveWithPin(staffCode, pin);
        }}
      >
        <label className="block">
          <span className="mb-1 block text-body font-semibold text-ink">Manager&apos;s staff code</span>
          <input
            value={staffCode}
            onChange={(event) => setStaffCode(event.target.value.toUpperCase())}
            autoFocus
            autoComplete="off"
            className="h-14 w-full rounded-md border border-strong bg-panel px-3 text-h3 uppercase text-ink outline-none focus:border-accent focus:ring-2 focus:ring-accent/30"
          />
        </label>

        <label className="block">
          <span className="mb-1 block text-body font-semibold text-ink">PIN</span>
          <input
            value={pin}
            onChange={(event) => setPin(event.target.value.replace(/\D/g, ''))}
            type="password"
            inputMode="numeric"
            autoComplete="off"
            className="pos-amount h-14 w-full rounded-md border border-strong bg-panel px-3 text-h3 tracking-[0.4em] text-ink outline-none focus:border-accent focus:ring-2 focus:ring-accent/30"
          />
        </label>

        {error ? (
          <p className="rounded-md bg-negative-soft px-3 py-2 text-body-lg font-semibold text-negative-text" role="alert">
            {error.message}
          </p>
        ) : null}

        <div className="flex gap-2">
          <button type="button" className="pos-button h-14 flex-1 text-body-lg" onClick={closeDialog}>
            Cancel
          </button>
          <button
            type="submit"
            className="pos-button-primary h-14 flex-[2] text-body-lg"
            disabled={busy || staffCode.trim() === '' || pin.length < 4}
          >
            Approve
          </button>
        </div>
      </form>
    </Shell>
  );
}

/* ----------------------------------------------------------- matrix and serial pickers (Phase 4) */

/**
 * Which colour and size (guide p.39–40).
 *
 * Opened automatically when a matrix parent is scanned, because the parent code identifies a grid
 * rather than a thing. Out-of-stock cells are hidden by default: offering a variant the shop does
 * not have is how a cashier promises a customer something that is not there.
 */
export function VariantPickerDialog() {
  const { closeDialog, addVariant, pendingSelection, locationId } = usePosStore();
  const [variants, setVariants] = useState<ProductVariant[]>([]);
  const [showAll, setShowAll] = useState(false);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!pendingSelection || !locationId) return;

    setLoading(true);
    posApi
      .listVariants(pendingSelection.productId, locationId, !showAll)
      .then(setVariants)
      .catch(() => setVariants([]))
      .finally(() => setLoading(false));
  }, [pendingSelection, locationId, showAll]);

  return (
    <Shell title="Choose a variant" hint={pendingSelection?.identifier} onClose={closeDialog} wide>
      {loading ? (
        <p className="py-4 text-center text-body text-ink-muted">Loading…</p>
      ) : variants.length === 0 ? (
        <p className="py-4 text-center text-body text-ink-muted">
          {showAll ? 'This item has no variants configured.' : 'Nothing in stock at this location.'}
        </p>
      ) : (
        <ul className="grid max-h-72 grid-cols-2 gap-1 overflow-y-auto">
          {variants.map((variant) => (
            <li key={String(variant.id)}>
              <button
                type="button"
                className="pos-button w-full px-2 text-left text-body"
                disabled={!showAll && variant.onHand <= 0}
                onClick={() => void addVariant(variant.id)}
              >
                <span className="flex items-center justify-between gap-2">
                  <span>{[variant.dim1Value, variant.dim2Value, variant.dim3Value].filter(Boolean).join(' / ')}</span>
                  <span
                    className="tabular text-label"
                    style={{ color: variant.onHand > 0 ? 'rgb(var(--text-muted))' : 'oklch(var(--negative))' }}
                  >
                    {variant.onHand}
                  </span>
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}

      <button type="button" className="mt-3 text-label underline text-ink-muted" onClick={() => setShowAll((s) => !s)}>
        {showAll ? 'Show only what is in stock' : 'Show every variant, including out of stock'}
      </button>
    </Shell>
  );
}

/**
 * Which physical unit (guide p.42).
 *
 * A serialized product is N distinct things, and which one leaves the shop matters for warranty,
 * recall and theft. The list is oldest-received first, which is what a store wants to shift.
 */
export function SerialPickerDialog() {
  const { closeDialog, addUnit, pendingSelection, locationId } = usePosStore();
  const [units, setUnits] = useState<SerializedUnit[]>([]);
  const [filter, setFilter] = useState('');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!pendingSelection || !locationId) return;

    setLoading(true);
    posApi
      .listAvailableUnits(pendingSelection.productId, locationId)
      .then(setUnits)
      .catch(() => setUnits([]))
      .finally(() => setLoading(false));
  }, [pendingSelection, locationId]);

  const visible = useMemo(() => {
    const needle = filter.trim().toUpperCase();
    if (!needle) return units;

    return units.filter(
      (unit) => unit.serialNumber?.toUpperCase().includes(needle) || unit.epc?.toUpperCase().includes(needle),
    );
  }, [units, filter]);

  return (
    <Shell title="Choose a unit" hint={pendingSelection?.identifier} onClose={closeDialog} wide>
      <input
        value={filter}
        onChange={(event) => setFilter(event.target.value)}
        autoFocus
        placeholder="Serial number or tag"
        className="mb-2 w-full border-b border-subtle bg-transparent pb-1 outline-none"
      />

      {loading ? (
        <p className="py-4 text-center text-body text-ink-muted">Loading…</p>
      ) : visible.length === 0 ? (
        <p className="py-4 text-center text-body text-ink-muted">
          No units of this item are in stock at this location.
        </p>
      ) : (
        <ul className="max-h-72 overflow-y-auto">
          {visible.map((unit) => (
            <li key={String(unit.id)}>
              <button
                type="button"
                className="flex w-full items-center justify-between px-1 py-1.5 text-left text-body hover:bg-panel-hover"
                onClick={() => void addUnit(unit.id)}
              >
                <span className="font-mono text-label">{unit.serialNumber ?? unit.epc}</span>
                <span className="text-label text-ink-muted">
                  {unit.variantLabel ?? ''}
                  {unit.epc && unit.serialNumber ? ' · tagged' : ''}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </Shell>
  );
}

/** F5 Client (guide p.9). Attaching a customer reprices the whole sale, including lines already rung. */
export function ClientDialog() {
  const { closeDialog, setCustomer, locationId, cart } = usePosStore();
  const [term, setTerm] = useState('');
  const [results, setResults] = useState<Array<{ id: number; customerNumber: number; fullName: string }>>([]);

  useEffect(() => {
    if (term.length < 2 || !locationId) {
      setResults([]);
      return undefined;
    }

    const timer = setTimeout(() => {
      posApi
        .searchCustomers(term, locationId)
        .then((customers) => setResults(customers as never))
        .catch(() => setResults([]));
    }, 200);

    return () => clearTimeout(timer);
  }, [term, locationId]);

  return (
    <Shell title="Client" hint="Attaching reprices the whole sale" onClose={closeDialog} wide>
      <input
        value={term}
        onChange={(event) => setTerm(event.target.value)}
        autoFocus
        placeholder="Name, company or account number"
        className="mb-2 w-full border-b border-subtle bg-transparent pb-1 outline-none"
      />

      <ul className="max-h-72 overflow-y-auto">
        {results.map((customer) => (
          <li key={String(customer.id)}>
            <button
              type="button"
              className="flex w-full items-center justify-between px-1 py-1.5 text-left text-body hover:bg-panel-hover"
              onClick={() => void setCustomer(customer.id)}
            >
              <span>{customer.fullName}</span>
              <span className="tabular text-label text-ink-muted">#{customer.customerNumber}</span>
            </button>
          </li>
        ))}
      </ul>

      {cart?.customer ? (
        <button
          type="button"
          className="pos-button mt-3 w-full text-body"
          onClick={() => void setCustomer(null)}
        >
          Remove {cart.customer.name} from this sale
        </button>
      ) : null}
    </Shell>
  );
}

export function UnknownItemDialog() {
  const { closeDialog, addUnknownItem } = usePosStore();
  const [description, setDescription] = useState('');
  const [price, setPrice] = useState('');
  const [qty, setQty] = useState('1');

  return (
    <Shell title="Unknown item" hint="Rings now; tidy up the catalogue later" onClose={closeDialog}>
      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault();
          void addUnknownItem(description, Number(price), Number(qty) || 1);
        }}
      >
        <label className="flex items-center justify-between gap-3 border-b border-subtle pb-1">
          <span className="text-body text-ink-muted">Description</span>
          <input
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            required
            autoFocus
            className="w-48 bg-transparent text-right outline-none"
          />
        </label>

        <label className="flex items-center justify-between gap-3 border-b border-subtle pb-1">
          <span className="text-body text-ink-muted">Price</span>
          <input
            value={price}
            onChange={(event) => setPrice(event.target.value)}
            inputMode="decimal"
            required
            className="pos-amount w-32 bg-transparent text-right outline-none"
          />
        </label>

        <label className="flex items-center justify-between gap-3 border-b border-subtle pb-1">
          <span className="text-body text-ink-muted">Quantity</span>
          <input
            value={qty}
            onChange={(event) => setQty(event.target.value)}
            inputMode="decimal"
            className="pos-amount w-32 bg-transparent text-right outline-none"
          />
        </label>

        <button type="submit" className="pos-button-primary w-full text-body">Add to sale</button>
      </form>
    </Shell>
  );
}

/**
 * The cheat sheet is generated from the live hotkey registry, so it can never describe a key that
 * is not actually bound.
 */
export function CheatSheetDialog() {
  const closeDialog = usePosStore((s) => s.closeDialog);

  // Every registered shortcut, not only the ones live in the current scope. This is a reference
  // somebody is reading to learn what exists — filtering it to what happens to be active meant it
  // could not tell a cashier that a shortcut existed at all.
  const bindings = useAllHotkeyBindings();

  const grouped = useMemo(() => {
    const map = new Map<string, typeof bindings>();
    bindings.forEach((binding) => {
      const group = binding.group ?? 'Sale';
      map.set(group, [...(map.get(group) ?? []), binding]);
    });
    return [...map.entries()];
  }, [bindings]);

  return (
    <Shell title="Keyboard shortcuts" onClose={closeDialog} wide>
      <div className="grid grid-cols-2 gap-6">
        {grouped.map(([group, entries]) => (
          <div key={group}>
            <h3 className="mb-1 text-label font-medium uppercase tracking-wide text-ink-muted">{group}</h3>
            <ul className="space-y-0.5 text-body">
              {entries.map((entry) => (
                <li key={`${group}-${entry.combo}`} className="flex items-baseline justify-between gap-3">
                  <span className="font-normal text-ink-muted">{entry.label}</span>
                  <kbd className="pos-kbd shrink-0 font-semibold">{entry.combo}</kbd>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </Shell>
  );
}
