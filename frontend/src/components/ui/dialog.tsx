'use client';

import type { ReactNode } from 'react';
import * as DialogPrimitive from '@radix-ui/react-dialog';
import { X } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * The back-office modal, on Radix.
 *
 * There were three hand-rolled modal implementations in this app and none of them trapped focus, so
 * tabbing out of an open dialog put you on the page behind it with the overlay still covering
 * everything. Radix handles the focus trap, the Escape key, the scroll lock, `aria-modal`, and
 * returning focus to whatever opened it.
 *
 * The POS dialogs keep their own shell: they push a hotkey scope so the sale screen's F-keys stop
 * firing while one is open, which is a behaviour this primitive has no business knowing about.
 */
export function Dialog({
  open,
  onOpenChange,
  title,
  description,
  footer,
  children,
  size = 'md',
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;

  /** Announced with the title. Radix warns in the console when a dialog has neither. */
  description?: string;
  footer?: ReactNode;
  children: ReactNode;
  size?: 'sm' | 'md' | 'lg' | 'xl';
}) {
  const width = {
    sm: 'max-w-md',
    md: 'max-w-xl',
    lg: 'max-w-3xl',
    xl: 'max-w-5xl',
  }[size];

  return (
    <DialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/40 data-[state=open]:animate-fade-in" />

        <DialogPrimitive.Content
          className={cn(
            'fixed left-1/2 top-1/2 z-50 flex max-h-[90vh] w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2 flex-col',
            'rounded border border-subtle bg-panel shadow-overlay data-[state=open]:animate-slide-up',
            width,
          )}
        >
          <div className="flex shrink-0 items-start justify-between gap-4 border-b border-subtle px-4 py-3">
            <div className="min-w-0">
              <DialogPrimitive.Title className="text-h3 font-medium">{title}</DialogPrimitive.Title>

              {description ? (
                <DialogPrimitive.Description className="mt-0.5 text-body text-ink-muted">
                  {description}
                </DialogPrimitive.Description>
              ) : (
                // Present but hidden: Radix wants a description for the accessible name, and a
                // visible one would be noise on a dialog whose title already says everything.
                <DialogPrimitive.Description className="sr-only">{title}</DialogPrimitive.Description>
              )}
            </div>

            <DialogPrimitive.Close
              aria-label="Close"
              className="shrink-0 rounded-sm p-1 text-ink-muted transition-colors hover:bg-panel-hover hover:text-ink"
            >
              <X className="h-4 w-4" aria-hidden />
            </DialogPrimitive.Close>
          </div>

          <div className="min-h-0 flex-1 overflow-y-auto p-4">{children}</div>

          {footer ? (
            <div className="flex shrink-0 flex-wrap items-center justify-end gap-2 border-t border-subtle px-4 py-3">
              {footer}
            </div>
          ) : null}
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}

export const DialogClose = DialogPrimitive.Close;
