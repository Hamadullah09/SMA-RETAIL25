'use client';

import { useEffect, useId, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { AlertTriangle } from 'lucide-react';
import { Dialog } from './dialog';
import { cn } from '@/lib/utils';

/**
 * Asking before doing something that cannot be undone.
 *
 * There were three different answers to this question in the application and all of them were
 * wrong in a different way.
 *
 * Six places used `window.confirm`, which cannot be styled, cannot be read by anybody using a
 * screen magnifier at the wrong scroll position, and on a kiosk-mode browser may not appear at all.
 * Five used `window.prompt` to collect a value — including a *refund amount*, which was parsed with
 * parseFloat and silently discarded when it did not parse, so typing "abc" into a refund box did
 * nothing and said nothing.
 *
 * And thirteen genuinely destructive actions asked nothing whatsoever: cancelling an order, voiding
 * an invoice, deleting a customer, a supplier, a product, a department, retiring a station, posting
 * a stock count, shipping a transfer.
 *
 * The rules here come from the one correct confirm this app already had, on revoking somebody's
 * access:
 *
 * 1. Name the specific thing. "Are you sure?" is a question nobody reads; "Delete Karachi Textile
 *    Mills?" is one they do.
 * 2. State the consequence, not the mechanism. What will be true afterwards.
 * 3. Label the button with the verb. Not "OK" — "Delete supplier", "Void invoice".
 * 4. Focus Cancel, not the action. The dialog appears under a cursor that was already moving.
 * 5. For the irreversible-and-wide operations — restoring a database over the top of a live one,
 *    reopening a closed year — require the name to be typed. A click is muscle memory; typing is a
 *    decision.
 */
export interface ConfirmRequest {
  /** The thing being acted on, named as the reader would name it. */
  subject: string;
  /** What will be true afterwards. One or two sentences. */
  consequence: string;
  /** The verb, for the button. "Delete supplier", "Void invoice", "Post count". */
  verb: string;
  /**
   * Requires this exact text to be typed before the action is available.
   *
   * For operations whose blast radius is the whole shop. A click can be muscle memory; typing the
   * name of the thing cannot be.
   */
  typeToConfirm?: string;
  /** Extra detail — a count, a total, a list of what is about to change. */
  detail?: ReactNode;
  tone?: 'danger' | 'caution';
}

export function ConfirmDialog({
  request,
  open,
  onOpenChange,
  onConfirm,
  busy = false,
}: {
  request: ConfirmRequest | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: () => void | Promise<void>;
  busy?: boolean;
}) {
  const [typed, setTyped] = useState('');
  const cancelRef = useRef<HTMLButtonElement>(null);
  const fieldId = useId();

  // Cleared whenever the dialog opens, so a half-typed name from a previous attempt is never
  // sitting there ready to be submitted against a different thing.
  useEffect(() => {
    if (open) setTyped('');
  }, [open, request?.subject]);

  // Cancel takes focus, not the action. This dialog appears under a cursor and a hand that were
  // already moving towards where the action button now is.
  useEffect(() => {
    if (!open) return;

    const timer = setTimeout(() => cancelRef.current?.focus(), 0);

    return () => clearTimeout(timer);
  }, [open]);

  if (!request) return null;

  const needsTyping = Boolean(request.typeToConfirm);
  const typedCorrectly = !needsTyping || typed.trim() === request.typeToConfirm;
  const danger = request.tone !== 'caution';

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      size="sm"
      // The title names the thing. Radix announces it on open, so this is the first words a screen
      // reader says — "Delete supplier?" rather than "Confirm".
      title={`${request.verb}?`}
      footer={
        <>
          <button ref={cancelRef} type="button" className="pos-button" onClick={() => onOpenChange(false)}>
            Cancel
          </button>

          <button
            type="button"
            className={danger ? 'pos-button-danger' : 'pos-button-primary'}
            disabled={busy || !typedCorrectly}
            onClick={() => void onConfirm()}
          >
            {busy ? 'Working…' : request.verb}
          </button>
        </>
      }
    >
      <div className="flex gap-3">
        <span
          className={cn(
            'flex h-11 w-11 shrink-0 items-center justify-center rounded-full',
            danger ? 'bg-negative-soft text-negative-text' : 'bg-warning-soft text-warning-text',
          )}
          aria-hidden
        >
          <AlertTriangle className="h-6 w-6" />
        </span>

        <div className="min-w-0 space-y-2">
          <p className="text-body-lg font-semibold text-ink">{request.subject}</p>
          <p className="text-body leading-relaxed text-ink-muted">{request.consequence}</p>

          {request.detail ? <div className="text-body text-ink-muted">{request.detail}</div> : null}

          {needsTyping ? (
            <div className="pt-1">
              <label htmlFor={fieldId} className="block text-label font-medium text-ink">
                Type <span className="font-mono text-negative-text">{request.typeToConfirm}</span> to
                confirm
              </label>

              <input
                id={fieldId}
                className="pos-input mt-1 w-full"
                value={typed}
                onChange={(event) => setTyped(event.target.value)}
                autoComplete="off"
                autoCapitalize="none"
                spellCheck={false}
              />
            </div>
          ) : null}
        </div>
      </div>
    </Dialog>
  );
}

/**
 * Collecting one value before doing something, with the validation the browser box never had.
 *
 * `window.prompt` returns a string or null and offers no way to say "that is not a number", so
 * every caller wrote its own silent `return` for bad input. The worst was a refund: type anything
 * unparseable and the dialog closed, nothing happened, and nothing said why.
 */
export function PromptDialog({
  open,
  onOpenChange,
  title,
  label,
  hint,
  verb,
  initialValue = '',
  kind = 'text',
  max,
  busy = false,
  onSubmit,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  label: string;
  hint?: string;
  verb: string;
  initialValue?: string;
  /** `money` validates and formats to two places; `text` requires only that something was typed. */
  kind?: 'text' | 'money';
  /** For money: the largest permitted value, checked here rather than refused by the server. */
  max?: number;
  busy?: boolean;
  onSubmit: (value: string) => void | Promise<void>;
}) {
  const [value, setValue] = useState(initialValue);
  const [problem, setProblem] = useState<string | null>(null);
  const fieldId = useId();

  useEffect(() => {
    if (open) {
      setValue(initialValue);
      setProblem(null);
    }
  }, [open, initialValue]);

  const validate = (): string | null => {
    const text = value.trim();

    if (text.length === 0) return 'Enter a value.';

    if (kind === 'money') {
      // Commas and a currency prefix are what people actually type.
      const cleaned = text.replace(/[,\s]/g, '').replace(/^[^\d.-]+/, '');

      if (!/^\d+(\.\d{1,2})?$/.test(cleaned)) {
        return 'Enter an amount, like 25 or 25.50.';
      }

      const amount = Number(cleaned);

      if (amount <= 0) return 'Enter an amount greater than nothing.';
      if (max != null && amount > max) return `That is more than the ${max.toFixed(2)} available.`;
    }

    return null;
  };

  const submit = (event: React.FormEvent) => {
    event.preventDefault();

    const failure = validate();

    if (failure) {
      // Said, not swallowed. The browser box had no way to report this, so every caller returned
      // silently and the person was left wondering whether they had pressed the button.
      setProblem(failure);
      return;
    }

    void onSubmit(value.trim());
  };

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      size="sm"
      title={title}
      footer={
        <>
          <button type="button" className="pos-button" onClick={() => onOpenChange(false)}>
            Cancel
          </button>

          <button type="submit" form={`${fieldId}-form`} className="pos-button-primary" disabled={busy}>
            {busy ? 'Working…' : verb}
          </button>
        </>
      }
    >
      <form id={`${fieldId}-form`} onSubmit={submit} noValidate>
        <label htmlFor={fieldId} className="block text-label font-medium text-ink">
          {label}
        </label>

        <input
          id={fieldId}
          className="pos-input mt-1 w-full"
          value={value}
          onChange={(event) => {
            setValue(event.target.value);
            setProblem(null);
          }}
          inputMode={kind === 'money' ? 'decimal' : 'text'}
          aria-invalid={problem !== null}
          aria-describedby={problem ? `${fieldId}-error` : hint ? `${fieldId}-hint` : undefined}
          autoComplete="off"
          autoFocus
        />

        {problem ? (
          <p id={`${fieldId}-error`} role="alert" className="mt-1.5 text-body text-negative-text">
            {problem}
          </p>
        ) : hint ? (
          <p id={`${fieldId}-hint`} className="mt-1.5 text-body text-ink-muted">
            {hint}
          </p>
        ) : null}
      </form>
    </Dialog>
  );
}

/**
 * The state a screen needs to drive one ConfirmDialog for many actions.
 *
 * Without this every page grows a boolean and a pending-action ref per destructive button, which is
 * how a screen ends up with six of them and one that was never wired to anything.
 */
export function useConfirm() {
  const [request, setRequest] = useState<ConfirmRequest | null>(null);
  const [busy, setBusy] = useState(false);
  const action = useRef<(() => void | Promise<void>) | null>(null);

  return {
    request,
    open: request !== null,
    busy,

    /** Opens the dialog; `run` happens only if the person confirms. */
    ask(next: ConfirmRequest, run: () => void | Promise<void>) {
      action.current = run;
      setRequest(next);
    },

    setOpen(next: boolean) {
      if (!next) {
        setRequest(null);
        action.current = null;
      }
    },

    async confirm() {
      const run = action.current;

      if (!run) return;

      setBusy(true);

      try {
        await run();
        setRequest(null);
        action.current = null;
      } finally {
        setBusy(false);
      }
    },
  };
}
