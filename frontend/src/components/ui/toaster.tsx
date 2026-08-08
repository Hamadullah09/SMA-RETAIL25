'use client';

import { useEffect, useState } from 'react';
import * as ToastPrimitive from '@radix-ui/react-toast';
import { X } from 'lucide-react';
import { cn } from '@/lib/utils';

interface Toast {
  id: string;
  title: string;
  description?: string;
  variant?: 'default' | 'destructive';
}

let toasts: Toast[] = [];
let listeners: Array<() => void> = [];

function emitChange() {
  for (const listener of listeners) listener();
}

/**
 * Raises a toast from anywhere, including outside React.
 *
 * The imperative shape is deliberate — it is called from forty catch blocks, and threading a hook
 * through every one of them would be worse than a module-level store.
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
 * Built on Radix Toast rather than by hand.
 *
 * The previous version was a fixed-position div with no live region, so a screen reader was never
 * told that a save had failed — the only feedback this app gives for a failed save simply did not
 * exist for anyone not watching the bottom-right corner. Radix supplies the region, the F8 hotkey
 * that moves focus into the stack, swipe-to-dismiss, and the timers.
 *
 * It also carried `animate-in slide-in-from-bottom-5`, which does nothing here: those classes come
 * from `tailwindcss-animate`, which is not installed.
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
    <ToastPrimitive.Provider swipeDirection="right" duration={5000}>
      {toasts.map((t) => (
        <ToastPrimitive.Root
          key={t.id}
          onOpenChange={(open) => {
            if (!open) dismiss(t.id);
          }}
          className={cn(
            'pointer-events-auto flex w-full items-start gap-3 rounded border bg-panel p-3 shadow-overlay',
            'data-[state=open]:animate-slide-up',
            t.variant === 'destructive' ? 'border-negative' : 'border-subtle',
          )}
        >
          <div className="min-w-0 flex-1">
            <ToastPrimitive.Title
              className={cn('text-body font-medium', t.variant === 'destructive' && 'text-negative')}
            >
              {t.title}
            </ToastPrimitive.Title>

            {t.description ? (
              <ToastPrimitive.Description className="mt-0.5 text-body text-ink-muted">
                {t.description}
              </ToastPrimitive.Description>
            ) : null}
          </div>

          <ToastPrimitive.Close
            aria-label="Dismiss"
            className="shrink-0 rounded-sm p-0.5 text-ink-muted transition-colors hover:bg-panel-hover hover:text-ink"
          >
            <X className="h-3.5 w-3.5" aria-hidden />
          </ToastPrimitive.Close>
        </ToastPrimitive.Root>
      ))}

      {/*
        `pointer-events-none` on the viewport with `pointer-events-auto` on each toast: otherwise the
        empty region sits over the bottom-right corner of every screen and swallows clicks on
        whatever is underneath it.
      */}
      <ToastPrimitive.Viewport className="pointer-events-none fixed bottom-4 right-4 z-50 flex w-full max-w-sm flex-col gap-2 outline-none" />
    </ToastPrimitive.Provider>
  );
}
