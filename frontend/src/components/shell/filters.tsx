'use client';

import type { ReactNode, SelectHTMLAttributes, InputHTMLAttributes } from 'react';
import { cn } from '@/lib/utils';

/**
 * The filter controls that sit above every browse grid.
 *
 * These existed as the same four-class string inlined about thirty times across a dozen files, which
 * is how three of them ended up a pixel taller than the rest and two lost their focus ring. One
 * definition, and the label is part of the control rather than something each page remembers to add.
 */

export function FilterInput({
  label,
  className,
  ...rest
}: { label: string } & InputHTMLAttributes<HTMLInputElement>) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-label text-ink-muted">{label}</span>
      <input className={cn('pos-input', className)} {...rest} />
    </label>
  );
}

export function FilterSelect({
  label,
  children,
  className,
  ...rest
}: { label: string; children: ReactNode } & SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-label text-ink-muted">{label}</span>
      <select className={cn('pos-input', className)} {...rest}>
        {children}
      </select>
    </label>
  );
}

/**
 * A checkbox filter.
 *
 * The label wraps the input, so the whole thing is a click target — a 13px checkbox on its own is a
 * poor one, and every page was doing this by hand with slightly different gaps.
 */
export function FilterCheck({
  label,
  checked,
  onChange,
  disabled,
}: {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
}) {
  return (
    <label className="flex cursor-pointer select-none items-center gap-2 self-end pb-1.5 text-body">
      <input
        type="checkbox"
        className="h-3.5 w-3.5 accent-accent"
        checked={checked}
        disabled={disabled}
        onChange={(event) => onChange(event.target.checked)}
      />
      {label}
    </label>
  );
}
