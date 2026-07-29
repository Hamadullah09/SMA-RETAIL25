'use client';

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { cn } from '@/lib/utils';

/**
 * The Browse + Form View pairing the legacy system used everywhere (guide p.23–24, p.30, p.46).
 *
 * Both halves are on screen at once rather than the form replacing the list. Back-office work is
 * mostly "check twenty items, fix three": a modal that hides the list makes the user memorise where
 * they were, and they lose their place every time they save.
 */

export function BrowseFormShell({
  title,
  toolbar,
  filters,
  grid,
  form,
  status,
}: {
  title: string;
  toolbar?: ReactNode;
  filters?: ReactNode;
  grid: ReactNode;
  form: ReactNode | null;
  status?: ReactNode;
}) {
  return (
    <div className="flex h-[calc(100vh-8rem)] min-h-0 flex-col gap-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-lg font-semibold">{title}</h1>
        <div className="flex flex-wrap items-center gap-2">{toolbar}</div>
      </div>

      {filters ? <div className="flex flex-wrap items-center gap-2 text-sm">{filters}</div> : null}

      <div className={cn('grid min-h-0 flex-1 gap-2', form ? 'grid-cols-1 xl:grid-cols-[1fr_28rem]' : 'grid-cols-1')}>
        <div className="min-h-0">{grid}</div>
        {form ? <div className="min-h-0 overflow-y-auto">{form}</div> : null}
      </div>

      {status ? <div className="text-xs text-[rgb(var(--text-muted))]">{status}</div> : null}
    </div>
  );
}

/** A titled group of fields inside a form panel. Mirrors one tab of the legacy item screen. */
export function FormSection({
  title,
  hint,
  children,
  actions,
}: {
  title: string;
  hint?: string;
  children: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <section className="pos-panel mb-2">
      <header className="pos-panel-header">
        <span>{title}</span>
        {actions ? <span className="normal-case">{actions}</span> : null}
      </header>
      <div className="space-y-2 p-3">
        {hint ? <p className="text-xs text-[rgb(var(--text-muted))]">{hint}</p> : null}
        {children}
      </div>
    </section>
  );
}

export function Field({
  label,
  children,
  hint,
}: {
  label: string;
  children: ReactNode;
  hint?: string;
}) {
  return (
    <label className="block text-sm">
      <span className="mb-0.5 block text-xs text-[rgb(var(--text-muted))]">{label}</span>
      {children}
      {hint ? <span className="mt-0.5 block text-[11px] text-[rgb(var(--text-muted))]">{hint}</span> : null}
    </label>
  );
}

const inputClass =
  'w-full rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1 outline-none focus:border-[rgb(var(--accent))]';

export function TextField({
  label,
  value,
  onChange,
  hint,
  placeholder,
  disabled,
  autoFocus,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  hint?: string;
  placeholder?: string;
  disabled?: boolean;
  autoFocus?: boolean;
}) {
  return (
    <Field label={label} hint={hint}>
      <input
        className={inputClass}
        value={value}
        placeholder={placeholder}
        disabled={disabled}
        autoFocus={autoFocus}
        onChange={(event) => onChange(event.target.value)}
      />
    </Field>
  );
}

/**
 * A numeric field that keeps what the user typed while they are typing.
 *
 * Parsing on every keystroke and writing the number back is what makes "1.0" collapse to "1" mid-edit
 * and "0.05" impossible to type — the leading "0." parses to 0 and the field resets. The raw string
 * is held locally and only committed as a number when it parses.
 */
export function NumberField({
  label,
  value,
  onChange,
  hint,
  step = '0.01',
  disabled,
}: {
  label: string;
  value: number;
  onChange: (value: number) => void;
  hint?: string;
  step?: string;
  disabled?: boolean;
}) {
  const [draft, setDraft] = useState(String(value));
  const focused = useRef(false);

  useEffect(() => {
    if (!focused.current) setDraft(String(value));
  }, [value]);

  return (
    <Field label={label} hint={hint}>
      <input
        className={cn(inputClass, 'pos-amount text-right')}
        type="number"
        step={step}
        value={draft}
        disabled={disabled}
        onFocus={() => {
          focused.current = true;
        }}
        onBlur={() => {
          focused.current = false;
          setDraft(String(value));
        }}
        onChange={(event) => {
          setDraft(event.target.value);
          const parsed = Number.parseFloat(event.target.value);
          if (!Number.isNaN(parsed)) onChange(parsed);
        }}
      />
    </Field>
  );
}

export function CheckField({
  label,
  checked,
  onChange,
  hint,
  disabled,
}: {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  hint?: string;
  disabled?: boolean;
}) {
  return (
    <label className="flex items-start gap-2 text-sm">
      <input
        type="checkbox"
        className="mt-1"
        checked={checked}
        disabled={disabled}
        onChange={(event) => onChange(event.target.checked)}
      />
      <span>
        {label}
        {hint ? <span className="block text-[11px] text-[rgb(var(--text-muted))]">{hint}</span> : null}
      </span>
    </label>
  );
}

export function SelectField<T extends string>({
  label,
  value,
  options,
  onChange,
  hint,
  disabled,
}: {
  label: string;
  value: T | '';
  options: Array<{ value: T | ''; label: string }>;
  onChange: (value: T | '') => void;
  hint?: string;
  disabled?: boolean;
}) {
  return (
    <Field label={label} hint={hint}>
      <select
        className={inputClass}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value as T | '')}
      >
        {options.map((option) => (
          <option key={String(option.value)} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </Field>
  );
}

/**
 * The connection lamp for a live grid.
 *
 * Stated in words rather than a bare coloured dot: a grid that has silently stopped updating looks
 * exactly like a grid where nothing has changed, and the user has no way to tell which they are
 * looking at.
 */
export function LiveBadge({ connected }: { connected: boolean }) {
  return (
    <span
      className={cn(
        'pos-badge',
        connected ? 'text-[rgb(var(--live))]' : 'text-[rgb(var(--warning))]',
      )}
    >
      {connected ? 'Live' : 'Not updating — reconnecting'}
    </span>
  );
}
