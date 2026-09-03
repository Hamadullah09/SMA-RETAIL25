'use client';

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Eye, EyeOff } from 'lucide-react';
import { cn } from '@/lib/utils';
import { connectionCopy, connectionStateFrom } from '@/lib/connection-state';
import { PageHeader } from '@/components/shell/page-header';

/**
 * The Browse + Form View pairing the legacy system used everywhere (guide p.23–24, p.30, p.46).
 *
 * Both halves are on screen at once rather than the form replacing the list. Back-office work is
 * mostly "check twenty items, fix three": a modal that hides the list makes the user memorise where
 * they were, and they lose their place every time they save.
 */

export function BrowseFormShell({
  title,
  description,
  toolbar,
  filters,
  grid,
  form,
  status,
}: {
  title: string;

  /** One line saying what the screen is for. Optional — most browses do not need one. */
  description?: string;
  toolbar?: ReactNode;
  filters?: ReactNode;
  grid: ReactNode;
  form: ReactNode | null;
  status?: ReactNode;
}) {
  return (
    /*
     * Height comes from a token rather than a hard-coded `calc(100vh - 8rem)`, which was written out
     * in three files and silently wrong the moment the header height changed. `h-below-header`
     * resolves through --header-height, so there is one number and it lives with the header.
     */
    <div className="flex h-below-header min-h-0 flex-col">
      <PageHeader title={title} description={description} actions={toolbar} />

      {filters ? (
        <div className="flex flex-wrap items-center gap-2 border-b border-subtle bg-panel-sunken px-4 py-2 text-body">
          {filters}
        </div>
      ) : null}

      <div
        className={cn(
          'grid min-h-0 flex-1 gap-3 p-4',
          // The form only takes its own column once there is room for both. Below that it stacks,
          // rather than squeezing a 28rem panel into whatever is left.
          form ? 'grid-cols-1 xl:grid-cols-[minmax(0,1fr)_28rem]' : 'grid-cols-1',
        )}
      >
        <div className="min-h-0">{grid}</div>
        {form ? <div className="min-h-0 overflow-y-auto">{form}</div> : null}
      </div>

      {status ? (
        <div className="border-t border-subtle px-4 py-2 text-label text-ink-muted">{status}</div>
      ) : null}
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
        {hint ? <p className="text-label text-ink-muted">{hint}</p> : null}
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
    <label className="block text-body">
      <span className="mb-0.5 block text-label text-ink-muted">{label}</span>
      {children}
      {hint ? <span className="mt-0.5 block text-caption text-ink-muted">{hint}</span> : null}
    </label>
  );
}

/**
 * One input definition, shared by every field below.
 *
 * The old one called `outline-none` and replaced it with a border colour change, which is invisible
 * to anyone navigating by keyboard. The focus ring now comes from the global `:focus-visible` rule.
 */
const inputClass = 'pos-input w-full';

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
 * A masked field, with a reveal.
 *
 * The reveal matters as much as the masking here: these are typed at a counter, in front of whoever
 * is next in the queue, and an administrator setting someone's first password needs to be able to
 * check they typed what they are about to read out.
 */
export function PasswordField({
  label,
  value,
  onChange,
  hint,
  placeholder,
  disabled,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  hint?: string;
  placeholder?: string;
  disabled?: boolean;
}) {
  const [revealed, setRevealed] = useState(false);

  return (
    <Field label={label} hint={hint}>
      <div className="relative">
        <input
          className={cn(inputClass, 'pr-11')}
          type={revealed ? 'text' : 'password'}
          value={value}
          placeholder={placeholder}
          disabled={disabled}
          autoComplete="new-password"
          onChange={(event) => onChange(event.target.value)}
        />
        {/*
          An eye, matching the sign-in and sign-up pages. This was the word "Show", which meant the
          application offered the same control three different ways depending which screen you were
          on — the sort of small inconsistency that makes software feel like several products.
        */}
        <button
          type="button"
          className="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-ink-muted transition-colors hover:text-ink focus-visible:text-ink"
          onClick={() => setRevealed((current) => !current)}
          disabled={disabled}
          aria-pressed={revealed}
          aria-label={revealed ? 'Hide password' : 'Show password'}
        >
          {revealed ? <EyeOff className="h-4 w-4" aria-hidden /> : <Eye className="h-4 w-4" aria-hidden />}
        </button>
      </div>
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
    <label className="flex items-start gap-2 text-body">
      <input
        type="checkbox"
        className="mt-1"
        checked={checked}
        disabled={disabled}
        onChange={(event) => onChange(event.target.checked)}
      />
      <span>
        {label}
        {hint ? <span className="block text-caption text-ink-muted">{hint}</span> : null}
      </span>
    </label>
  );
}

/**
 * A select whose options are either an enum or a set of record ids.
 *
 * `T` widened from `string` to `string | number` when entity keys became integers. A select is the
 * one control that legitimately carries both — a product type is a string, a department is a row —
 * and splitting it in two would have meant two components that drift apart. The empty string stays
 * the "nothing chosen" value in both cases, because that is what an HTML `<option>` with no value
 * actually reports.
 */
export function SelectField<T extends string | number>({
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
        // A <select> always reports a string, whatever went into the option. Casting that straight
        // to T would hand a caller "5" while the type says 5 — which type-checks, renders, and then
        // fails the moment anything compares it to a real id. The option it came from is looked up
        // instead, so the value handed back is the one that was put in.
        onChange={(event) =>
          onChange(options.find((option) => String(option.value) === event.target.value)?.value ?? '')
        }
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
export function LiveBadge({
  connected,
  hasEverConnected = true,
}: {
  connected: boolean;

  /**
   * Whether this screen has ever been connected. Defaults to true so an existing caller keeps its
   * current behaviour, but passing it is what separates "still opening" from "dropped out" — the
   * two used to look identical and read as a fault either way.
   */
  hasEverConnected?: boolean;
}) {
  const state = connectionStateFrom(connected, hasEverConnected);
  const { label, detail, tone } = connectionCopy(state);

  return (
    <span
      className={cn(
        'pos-badge',
        tone === 'live' && 'text-live',
        tone === 'warning' && 'text-warning',
        tone === 'muted' && 'text-ink-muted',
      )}
      role="status"
      title={detail}
    >
      {label}
    </span>
  );
}
