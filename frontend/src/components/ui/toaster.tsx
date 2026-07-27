'use client';

import { useEffect, useState } from 'react';
import { cn } from '@/lib/utils';
import { X } from 'lucide-react';

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

export function toast(props: Omit<Toast, 'id'>) {
  const id = Math.random().toString(36).slice(2);
  toasts = [...toasts, { ...props, id }];
  emitChange();
  setTimeout(() => {
    toasts = toasts.filter((t) => t.id !== id);
    emitChange();
  }, 5000);
}

export function Toaster() {
  const [, setUpdate] = useState(0);

  useEffect(() => {
    const listener = () => setUpdate((n) => n + 1);
    listeners.push(listener);
    return () => { listeners = listeners.filter((l) => l !== listener); };
  }, []);

  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-sm">
      {toasts.map((t) => (
        <div
          key={t.id}
          className={cn(
            'rounded-lg border p-4 shadow-lg bg-background animate-in slide-in-from-bottom-5',
            t.variant === 'destructive' && 'border-destructive bg-destructive text-destructive-foreground'
          )}
        >
          <div className="font-semibold text-sm">{t.title}</div>
          {t.description && <div className="text-sm mt-1 opacity-90">{t.description}</div>}
        </div>
      ))}
    </div>
  );
}
