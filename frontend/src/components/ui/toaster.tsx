'use client';

import { useEffect, useState, type ReactNode } from 'react';
import * as ToastPrimitive from '@radix-ui/react-toast';
import { CheckCircle2, CircleAlert, Info, TriangleAlert, X } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { cn } from '@/lib/utils';

export type ToastVariant = 'default' | 'success' | 'warning' | 'destructive';

interface Toast {
  id: string;
  title: string;
  description?: string;
  variant?: ToastVariant;
  /**
   * One button, for the thing somebody would otherwise have to go and find.
   *
   * Mostly "Try again" after a failed save. A toast that reports a failure and offers nothing makes
   * the person reconstruct what they were doing from memory.
   */
  action?: { label: string; onClick: () => void };
}

let toasts: Toast[] = [];
let listeners: Array<() => void> = [];

function emitChange() {
  for (const listener of listeners) listener();
}

/**
 * Raises a toast from anywhere, including outside React.
 *
 * The imperative shape is deliberate — it is called from two hundred places, most of them catch
 * blocks, and threading a hook through every one would be worse than a module-level store.
 */
export function toast(props: Omit<Toast, 'id'>) {
  const id = Math.random().toString(36).slice(2);
  toasts = [...toasts, { ...props, id }];
  emitChange();
}

function dismiss(id: string) {
  toasts = toasts.filter((t) => t.id !== id);
  emitChange();
}

/**
 * How long each kind stays.
 *
 * Everything used to sit for five seconds, including a hundred and forty-five error messages. Five
 * seconds is fine for "Saved" — it confirms something the person already knows. It is not fine for
 * "Could not post the order: somebody else changed this while you were working on it", which is a
 * sentence somebody has to read, understand, and act on, while a customer waits.
 *
 * So a failure does not dismiss itself at all. It stays until it is dismissed or acted on, because
 * the alternative is a shop where the answer to "what went wrong?" is "it said something and then
 * it went away".
 */
const DURATION: Record<ToastVariant, number> = {
  default: 5000,
  success: 4000,
  warning: 10_000,
  destructive: Infinity,
};

const ICONS: Record<ToastVariant, LucideIcon> = {
  default: Info,
  success: CheckCircle2,
  warning: TriangleAlert,
  destructive: CircleAlert,
};

/** Ground, text and edge per variant. Each is a glyph as well as a hue. */
const TONES: Record<ToastVariant, { border: string; chip: string; title: string }> = {
  default: { border: 'border-subtle', chip: 'bg-accent-soft text-accent-text', title: 'text-ink' },
  success: { border: 'border-positive/40', chip: 'bg-positive-soft text-positive-text', title: 'text-positive-text' },
  warning: { border: 'border-warning/40', chip: 'bg-warning-soft text-warning-text', title: 'text-warning-text' },
  destructive: { border: 'border-negative/50', chip: 'bg-negative-soft text-negative-text', title: 'text-negative-text' },
};

/**
 * Built on Radix Toast rather than by hand.
 *
 * The previous version was a fixed-position div with no live region, so a screen reader was never
 * told that a save had failed — the only feedback this app gives for a failed save simply did not
 * exist for anyone not watching the bottom-right corner. Radix supplies the region, the F8 hotkey
 * that moves focus into the stack, swipe-to-dismiss, and the timers.
 */
export function Toaster() {
  const [, setUpdate] = useState(0);

  useEffect(() => {
    const listener = () => setUpdate((n) => n + 1);
    listeners.push(listener);
    return () => {
      listeners = listeners.filter((l) => l !== listener);
    };
  }, []);

  return (
    <ToastPrimitive.Provider swipeDirection="right">
      {toasts.map((t) => {
        const variant = t.variant ?? 'default';
        const tone = TONES[variant];
        const Icon = ICONS[variant];

        return (
          <ToastPrimitive.Root
            key={t.id}
            duration={DURATION[variant]}
            onOpenChange={(open) => {
              if (!open) dismiss(t.id);
            }}
            className={cn(
              'pointer-events-auto flex w-full items-start gap-3 rounded-lg border bg-panel p-4 shadow-overlay',
              'data-[state=open]:animate-slide-up',
              tone.border,
            )}
          >
            {/* The glyph, so the tone is not carried by the border colour alone. */}
            <span
              className={cn('flex h-9 w-9 shrink-0 items-center justify-center rounded-full', tone.chip)}
              aria-hidden
            >
              <Icon className="h-5 w-5" />
            </span>

            <div className="min-w-0 flex-1">
              <ToastPrimitive.Title className={cn('text-body-lg font-semibold', tone.title)}>
                {t.title}
              </ToastPrimitive.Title>

              {t.description ? (
                <ToastPrimitive.Description className="mt-1 text-body leading-relaxed text-ink-muted">
                  {t.description}
                </ToastPrimitive.Description>
              ) : null}

              {t.action ? (
                <ToastPrimitive.Action asChild altText={t.action.label}>
                  <button
                    type="button"
                    className="pos-button mt-3"
                    onClick={() => {
                      t.action?.onClick();
                      dismiss(t.id);
                    }}
                  >
                    {t.action.label}
                  </button>
                </ToastPrimitive.Action>
              ) : null}
            </div>

            {/* A real target. This was an 18px box with a 14px glyph — a thing to aim at rather than
                press, and the only way to clear a message that now does not clear itself. */}
            <ToastPrimitive.Close
              aria-label="Dismiss"
              className="-m-2 flex h-11 w-11 shrink-0 items-center justify-center rounded-md text-ink-muted transition-colors hover:bg-panel-hover hover:text-ink"
            >
              <X className="h-5 w-5" aria-hidden />
            </ToastPrimitive.Close>
          </ToastPrimitive.Root>
        );
      })}

      {/*
        `pointer-events-none` on the viewport with `pointer-events-auto` on each toast: otherwise the
        empty region sits over the bottom-right corner of every screen and swallows clicks on
        whatever is underneath it.
      */}
      <ToastPrimitive.Viewport className="pointer-events-none fixed inset-x-4 bottom-4 z-overlay flex flex-col gap-2 outline-none sm:left-auto sm:right-4 sm:w-full sm:max-w-md" />
    </ToastPrimitive.Provider>
  );
}

/** Re-exported so a call site can name the variant without importing the union separately. */
export type { Toast as ToastMessage, ReactNode };
