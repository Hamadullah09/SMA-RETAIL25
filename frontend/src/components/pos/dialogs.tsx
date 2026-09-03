'use client';

import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { usePosStore } from '@/stores/pos-store';
import { posApi } from '@/lib/pos-api';
import { useAllHotkeyBindings, useHotkey, useHotkeyBindings, useHotkeyScope } from '@/lib/hotkeys';
import { money, useCurrencySymbol } from '@/components/pos/panels';
import { parseTenderInput } from '@/lib/tender-input';
import type { ProductVariant, SerializedUnit, SuspendedCart, TenderType } from '@/types/pos';
import { X } from 'lucide-react';
import { cn } from '@/lib/utils';

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
}: {
  title: string;
  hint?: string;
  onClose: () => void;
  children: ReactNode;
  wide?: boolean;
}) {
  useHotkeyScope('dialog');
  useHotkey('Escape', onClose, { scope: 'dialog', label: 'Close', hidden: true });
  useHotkey('F12', onClose, { scope: 'dialog', label: 'F12 Cancel' });

  const panelRef = useRef<HTMLDivElement>(null);
  const returnFocusTo = useRef<HTMLElement | null>(null);

  /**
   * Focus goes into the dialog and comes back out again.
   *
   * Neither happened. Opening one left focus on the button behind it, so a keyboard user tabbed
   * through the sale underneath while the dialog covered it, and closing one dropped focus to the
   * body — which on this screen means the scan box stops receiving the next barcode.
   */
  useEffect(() => {
    returnFocusTo.current = document.activeElement as HTMLElement | null;

    const first = panelRef.current?.querySelector<HTMLElement>(
      'input:not([type="hidden"]), select, textarea, button, [tabindex]:not([tabindex="-1"])',
    );

    (first ?? panelRef.current)?.focus();

    return () => {
      returnFocusTo.current?.focus?.();
    };
  }, []);

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

        <div className="p-3">{children}</div>

        {/* Said out loud, because both ways out are otherwise things you have to already know. */}
        <p className="border-t border-subtle px-3 py-2 text-caption text-ink-muted">
          Press Esc, or tap outside, to cancel.
        </p>
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

export function PaymentDialog() {
  const { cart, policy, closeDialog, complete, busy } = usePosStore();
  const [tenders, setTenders] = useState<TenderType[]>([]);
  const [selected, setSelected] = useState<TenderType | null>(null);
  const [tendered, setTendered] = useState('');
  const [copies, setCopies] = useState(1);

  const due = cart?.totals.grandTotal ?? 0;
  const symbol = useCurrencySymbol();

  useEffect(() => {
    posApi
      .tenderTypes()
      .then((list) => {
        const active = list.filter((t) => t.isActive).sort((a, b) => a.sortOrder - b.sortOrder);
        setTenders(active);
        setSelected(active.find((t) => t.id === policy?.defaultTenderTypeId) ?? active[0] ?? null);
      })
      .catch(() => setTenders([]));
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

    const ok = await complete([
      {
        tenderTypeId: selected.id,
        amount: due,
        amountTendered: selected.roundsToMinimumTender ? amount : due,
      },
    ]);

    if (ok) closeDialog();
  };

  return (
    <Shell title="Payment" hint={`${copies} cop${copies === 1 ? 'y' : 'ies'} · F4 changes`} onClose={closeDialog}>
      <div className="mb-3 flex items-baseline justify-between border-b border-subtle pb-2">
        <span className="text-body text-ink-muted">Amount due</span>
        <span className="pos-amount text-2xl font-semibold">{money(due, symbol)}</span>
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
              tenderError && 'text-negative',
            )}
          />
        </label>
      ) : null}

      {tenderError ? (
        <p id="tender-error" role="alert" className="mt-2 text-body text-negative">
          {tenderError}
        </p>
      ) : null}

      {change > 0 ? (
        <div className="mt-2 flex justify-between text-body">
          <span className="text-ink-muted">Change</span>
          <span className="pos-amount text-h3 font-semibold text-positive">
            {money(change, symbol)}
          </span>
        </div>
      ) : null}

      <button
        type="button"
        className="pos-button-primary mt-4 w-full text-base"
        disabled={busy || !canPay}
        onClick={() => void submit()}
      >
        {busy ? 'Saving…' : `Take ${money(due, symbol)}`}
      </button>
    </Shell>
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
  const { cart, closeDialog, setTaxOverride, suspend, openDialog, policy, clearLines } = usePosStore();
  const [confirmingClear, setConfirmingClear] = useState(false);

  const lineCount = cart?.lines.length ?? 0;

  return (
    <Shell title="Special" onClose={closeDialog}>
      <div className="space-y-2">
        <MenuButton hotkey="F4" label="Suspend this sale" onSelect={() => void suspend()} disabled={!cart} />
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
  const symbol = useCurrencySymbol();

  const run = async () => {
    if (!stationId) return;
    const amount = Number(value);

    try {
      if (mode === 'float') await posApi.drawer.open(stationId, amount);
      if (mode === 'payIn') await posApi.drawer.payIn(stationId, amount, reason);
      if (mode === 'payOut') await posApi.drawer.payOut(stationId, amount, reason);
      if (mode === 'close') await posApi.drawer.close(stationId, amount);
    } finally {
      setMode(null);
      setValue('');
      setReason('');
      await refreshDrawer();
    }
  };

  if (mode) {
    const needsReason = mode === 'payIn' || mode === 'payOut';

    return (
      <Shell title={{ float: 'Opening float', payIn: 'Pay in', payOut: 'Pay out', close: 'Close drawer' }[mode]} onClose={() => setMode(null)}>
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

          <button type="submit" className="pos-button-primary w-full text-body">Confirm</button>
        </form>
      </Shell>
    );
  }

  return (
    <Shell title="Drawer" onClose={closeDialog} wide>
      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-2">
          <MenuButton hotkey="F4" label="Opening float" onSelect={() => setMode('float')} disabled={drawer?.status === 'Open'} />
          <MenuButton hotkey="F5" label="Pay in" onSelect={() => setMode('payIn')} disabled={drawer?.status !== 'Open'} />
          <MenuButton hotkey="F6" label="Pay out" onSelect={() => setMode('payOut')} disabled={drawer?.status !== 'Open'} />
          <MenuButton
            hotkey="F7"
            label="Pop drawer (no sale)"
            disabled={drawer?.status !== 'Open'}
            onSelect={() => stationId && void posApi.drawer.pop(stationId).then(refreshDrawer)}
          />
          <MenuButton hotkey="F8" label="Close drawer" onSelect={() => setMode('close')} disabled={drawer?.status !== 'Open'} />
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
                  <span className="font-medium">{cart.label ?? 'Unlabelled hold'}</span>
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
    <Shell title="Supervisor approval" hint={pendingApproval.context ?? undefined} onClose={closeDialog}>
      <p className="mb-3 text-body text-ink-muted">
        This needs a supervisor. Enter a PIN here, or ask one to approve from any till.
      </p>

      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault();
          void approveWithPin(staffCode, pin);
        }}
      >
        <label className="flex items-center justify-between gap-3 border-b border-subtle pb-1">
          <span className="text-body text-ink-muted">Supervisor code</span>
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
          Approve
        </button>
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
