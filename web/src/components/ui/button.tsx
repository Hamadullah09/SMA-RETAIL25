'use client';

import type { LucideIcon } from 'lucide-react';
import Link from 'next/link';
import { forwardRef, useImperativeHandle, useRef, type ButtonHTMLAttributes, type ReactNode } from 'react';
import { cx } from '@/lib/cx';
import { useHotkey } from '@/lib/hotkeys';

export type ButtonVariant = 'plain' | 'sun' | 'dark' | 'danger';

const VARIANT: Record<ButtonVariant, string> = {
  plain: '',
  sun: 'btn-sun',
  dark: 'btn-dark',
  danger: 'btn-danger',
};

/** The key cap on a button, as the desktop draws it: `Ctrl+N`, `F5`, `Enter`. */
export function KeyCap({ keys, className }: { keys: string; className?: string }) {
  return (
    <kbd className={cx('keycap font-body', className)} aria-hidden="true">
      {keys}
    </kbd>
  );
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  icon?: LucideIcon;
  /**
   * The key that presses the button, shown on its key cap. Keys with Ctrl, Alt, an F-key or Del also press it while
   * it is on screen and enabled; Enter and Esc are left to the form and the dialog.
   */
  shortcut?: string;
  /** Hide the label below 640 px and keep the icon (toolbars on a phone). */
  compact?: boolean;
  busy?: boolean;
  children?: ReactNode;
}

function bindable(shortcut?: string): string | null {
  if (!shortcut) return null;
  const lower = shortcut.toLowerCase();
  return lower === 'enter' || lower === 'esc' ? null : shortcut;
}

/** A pill, at least 48 px high: icon, label and key cap. `sun` is the main action of a screen. */
export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = 'plain', icon: Icon, shortcut, compact, busy, className, children, type = 'button', disabled, ...rest },
  ref,
) {
  const inner = useRef<HTMLButtonElement>(null);
  useImperativeHandle(ref, () => inner.current as HTMLButtonElement);
  useHotkey(bindable(shortcut), () => inner.current?.click(), !disabled && !busy);

  return (
    <button
      ref={inner}
      type={type}
      disabled={disabled || busy}
      aria-busy={busy || undefined}
      aria-keyshortcuts={shortcut}
      className={cx('btn', VARIANT[variant], className)}
      {...rest}
    >
      {Icon && <Icon className={cx('h-5 w-5 shrink-0', busy && 'animate-pulse')} strokeWidth={1.9} aria-hidden="true" />}
      {children !== undefined && <span className={cx(compact && Icon && 'max-sm:sr-only')}>{children}</span>}
      {shortcut && <KeyCap keys={shortcut} className={cx(compact ? 'max-md:hidden' : 'max-sm:hidden')} />}
    </button>
  );
});

/** A link that looks like a button. */
export function LinkButton({
  href,
  variant = 'plain',
  icon: Icon,
  shortcut,
  className,
  children,
}: {
  href: string;
  variant?: ButtonVariant;
  icon?: LucideIcon;
  shortcut?: string;
  className?: string;
  children: ReactNode;
}) {
  const ref = useRef<HTMLAnchorElement>(null);
  useHotkey(bindable(shortcut), () => ref.current?.click());
  return (
    <Link ref={ref} href={href} className={cx('btn', VARIANT[variant], className)} aria-keyshortcuts={shortcut}>
      {Icon && <Icon className="h-5 w-5 shrink-0" strokeWidth={1.9} aria-hidden="true" />}
      <span>{children}</span>
      {shortcut && <KeyCap keys={shortcut} className="max-sm:hidden" />}
    </Link>
  );
}

/** A round icon-only button (52 px, like the desktop's RoundButton). */
export function IconButton({
  icon: Icon,
  label,
  className,
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & { icon: LucideIcon; label: string }) {
  return (
    <button type="button" aria-label={label} title={label} className={cx('btn btn-icon h-12 w-12 rounded-full', className)} {...rest}>
      <Icon className="h-5 w-5" strokeWidth={1.9} aria-hidden="true" />
    </button>
  );
}
