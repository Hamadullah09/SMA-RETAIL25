'use client';

import { CircleCheck, CircleAlert, X } from 'lucide-react';
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { cx } from '@/lib/cx';

type Tone = 'ok' | 'danger';

interface Toast {
  id: number;
  tone: Tone;
  text: string;
}

interface ToastApi {
  ok(text: string): void;
  error(text: string): void;
}

const ToastContext = createContext<ToastApi>({ ok: () => undefined, error: () => undefined });

/** Short notes in the bottom right: "Saved.", or what went wrong. */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const remove = useCallback((id: number) => setToasts((list) => list.filter((t) => t.id !== id)), []);
  const push = useCallback(
    (tone: Tone, text: string) => {
      const id = Date.now() + Math.random();
      setToasts((list) => [...list.slice(-3), { id, tone, text }]);
      setTimeout(() => remove(id), tone === 'ok' ? 3500 : 7000);
    },
    [remove],
  );
  const api = useMemo<ToastApi>(() => ({ ok: (t) => push('ok', t), error: (t) => push('danger', t) }), [push]);

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div className="no-print pointer-events-none fixed bottom-4 right-4 z-toast flex w-[min(420px,calc(100vw-32px))] flex-col gap-2" aria-live="polite">
        {toasts.map((t) => (
          <div
            key={t.id}
            role={t.tone === 'danger' ? 'alert' : 'status'}
            className={cx(
              'pointer-events-auto flex animate-rise-in items-start gap-3 rounded-card border px-4 py-3 shadow-dialog',
              t.tone === 'ok' ? 'border-ok/30 bg-ok-light text-ok-ink' : 'border-danger/30 bg-danger-light text-danger-ink',
            )}
          >
            {t.tone === 'ok' ? <CircleCheck className="mt-0.5 h-5 w-5 shrink-0" /> : <CircleAlert className="mt-0.5 h-5 w-5 shrink-0" />}
            <p className="flex-1 font-semibold">{t.text}</p>
            <button type="button" className="rounded-full p-1 hover:bg-black/5" onClick={() => remove(t.id)} aria-label="Close">
              <X className="h-4 w-4" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastApi {
  return useContext(ToastContext);
}
