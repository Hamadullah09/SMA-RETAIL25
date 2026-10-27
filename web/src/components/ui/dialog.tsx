'use client';

import { CircleHelp, X } from 'lucide-react';
import { useEffect, useId, useRef, type FormEvent, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { cx } from '@/lib/cx';
import { HotkeyScope, useHotkey } from '@/lib/hotkeys';
import { Button } from './button';

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

function Overlay({ children, onClose, side }: { children: ReactNode; onClose(): void; side?: boolean }) {
  return createPortal(
    <div
      className={cx('fixed inset-0 z-overlay flex animate-fade-in bg-ink/40 backdrop-blur-[1px]', side ? 'justify-end' : 'items-start justify-center overflow-y-auto p-3 sm:p-6 md:pt-[8vh]')}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      {children}
    </div>,
    document.body,
  );
}

/** Keeps Tab inside the dialog, focuses the first box, and gives focus back when it closes. */
function useFocusTrap(open: boolean) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const previous = document.activeElement as HTMLElement | null;
    const node = ref.current;
    const first = node?.querySelector<HTMLElement>('[data-autofocus]') ?? node?.querySelector<HTMLElement>('input:not([type=hidden]):not([disabled]), select, textarea') ?? node?.querySelector<HTMLElement>(FOCUSABLE);
    first?.focus();
    function onKey(event: KeyboardEvent) {
      if (event.key !== 'Tab' || !node) return;
      const items = Array.from(node.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((el) => el.offsetParent !== null);
      if (items.length === 0) return;
      const firstItem = items[0];
      const lastItem = items[items.length - 1];
      if (event.shiftKey && document.activeElement === firstItem) {
        event.preventDefault();
        lastItem.focus();
      } else if (!event.shiftKey && document.activeElement === lastItem) {
        event.preventDefault();
        firstItem.focus();
      }
    }
    node?.addEventListener('keydown', onKey);
    return () => {
      node?.removeEventListener('keydown', onKey);
      previous?.focus?.();
    };
  }, [open]);
  return ref;
}

function EscToClose({ onClose }: { onClose(): void }) {
  useHotkey('Esc', onClose);
  return null;
}

export interface DialogProps {
  open: boolean;
  title: ReactNode;
  onClose(): void;
  children: ReactNode;
  /** The buttons at the bottom right (Save Enter, Cancel Esc). */
  footer?: ReactNode;
  /** Makes the dialog a form: Enter submits it. */
  onSubmit?(event: FormEvent<HTMLFormElement>): void;
  width?: 'sm' | 'md' | 'lg' | 'xl';
  className?: string;
}

const WIDTH = { sm: 'max-w-[460px]', md: 'max-w-[620px]', lg: 'max-w-[860px]', xl: 'max-w-[1120px]' };

/** A dialog like the desktop's: a white card with radius 14, the title in Century Gothic, buttons at the bottom right. */
export function Dialog({ open, title, onClose, children, footer, onSubmit, width = 'md', className }: DialogProps) {
  const ref = useFocusTrap(open);
  const titleId = useId();
  if (!open || typeof document === 'undefined') return null;

  const body = (
    <>
      <div className="flex items-start justify-between gap-4 px-6 pb-2 pt-5 sm:px-7">
        <h2 id={titleId} className="font-heading text-[26px] leading-9 text-ink">
          {title}
        </h2>
        <button type="button" onClick={onClose} className="-mr-2 rounded-full p-2 text-muted hover:bg-stone-light hover:text-ink" aria-label="Close">
          <X className="h-5 w-5" />
        </button>
      </div>
      <div className="px-6 pb-2 sm:px-7">{children}</div>
      {footer && <div className="flex flex-wrap items-center justify-end gap-3 px-6 pb-6 pt-4 sm:px-7">{footer}</div>}
    </>
  );

  return (
    <Overlay onClose={onClose}>
      <HotkeyScope>
        <EscToClose onClose={onClose} />
        <div
          ref={ref}
          role="dialog"
          aria-modal="true"
          aria-labelledby={titleId}
          className={cx('my-auto w-full animate-rise-in rounded-card bg-page shadow-dialog md:my-0', WIDTH[width], className)}
        >
          {onSubmit ? (
            <form
              noValidate
              onSubmit={(e) => {
                e.preventDefault();
                // The dialog is portalled, but React still bubbles its submit to a form around the opener.
                e.stopPropagation();
                onSubmit(e);
              }}
            >
              {body}
            </form>
          ) : (
            body
          )}
        </div>
      </HotkeyScope>
    </Overlay>
  );
}

/** A panel that slides in from the right (details beside a list). */
export function SidePanel({
  open,
  title,
  subtitle,
  onClose,
  children,
  footer,
  wide,
}: {
  open: boolean;
  title: ReactNode;
  subtitle?: ReactNode;
  onClose(): void;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
}) {
  const ref = useFocusTrap(open);
  const titleId = useId();
  if (!open || typeof document === 'undefined') return null;
  return (
    <Overlay onClose={onClose} side>
      <HotkeyScope>
        <EscToClose onClose={onClose} />
        <aside
          ref={ref}
          role="dialog"
          aria-modal="true"
          aria-labelledby={titleId}
          className={cx('flex h-full w-full animate-slide-left flex-col bg-page shadow-dialog sm:rounded-l-card', wide ? 'max-w-[980px]' : 'max-w-[640px]')}
        >
          <div className="flex items-start justify-between gap-4 px-5 pb-3 pt-5 sm:px-7">
            <div className="min-w-0">
              <h2 id={titleId} className="font-heading text-[26px] leading-9 text-ink">
                {title}
              </h2>
              {subtitle && <p className="text-muted">{subtitle}</p>}
            </div>
            <Button icon={X} onClick={onClose} shortcut="Esc" className="shrink-0">
              Close
            </Button>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto px-5 pb-6 sm:px-7">{children}</div>
          {footer && <div className="flex flex-wrap justify-end gap-3 border-t border-line px-5 py-4 sm:px-7">{footer}</div>}
        </aside>
      </HotkeyScope>
    </Overlay>
  );
}

/** "Cancel this sale?" with Yes (Enter) and No (Esc), like the desktop's message window. */
export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = 'Yes',
  cancelLabel = 'No',
  danger,
  busy,
  onConfirm,
  onClose,
}: {
  open: boolean;
  title: string;
  message?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
  busy?: boolean;
  onConfirm(): void;
  onClose(): void;
}) {
  return (
    <Dialog
      open={open}
      title={
        <span className="flex items-center gap-4">
          <span className="grid h-14 w-14 shrink-0 place-items-center rounded-full bg-sun-light">
            <CircleHelp className="h-7 w-7 text-ink" strokeWidth={1.6} />
          </span>
          {title}
        </span>
      }
      onClose={onClose}
      width="sm"
      onSubmit={onConfirm}
      footer={
        <>
          <Button type="submit" variant={danger ? 'danger' : 'sun'} shortcut="Enter" busy={busy} data-autofocus>
            {confirmLabel}
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            {cancelLabel}
          </Button>
        </>
      }
    >
      {message && <div className="pl-[72px] text-ink">{message}</div>}
    </Dialog>
  );
}
