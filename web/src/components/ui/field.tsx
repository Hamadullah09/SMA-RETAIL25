'use client';

import {
  forwardRef,
  useId,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from 'react';
import { cx } from '@/lib/cx';

/**
 * A label and its control, side by side on wide screens like the desktop's forms (label on the left in Muted), stacked
 * on a phone. `error` shows under the control in Danger.
 */
export function Field({
  label,
  hint,
  error,
  children,
  className,
  htmlFor,
  stacked,
}: {
  label: ReactNode;
  hint?: ReactNode;
  error?: string | null;
  children: ReactNode;
  className?: string;
  htmlFor?: string;
  /** Label above the control at every width. */
  stacked?: boolean;
}) {
  return (
    <div className={cx('grid grid-cols-1 gap-x-4 gap-y-1', !stacked && 'sm:grid-cols-[170px_minmax(0,1fr)] sm:items-start', className)}>
      <label htmlFor={htmlFor} className={cx('text-muted', !stacked && 'sm:pt-2.5')}>
        {label}
      </label>
      <div className="min-w-0">
        {children}
        {hint && !error && <p className="mt-1 text-note text-muted">{hint}</p>}
        {error && (
          <p className="mt-1 text-note font-semibold text-danger" role="alert">
            {error}
          </p>
        )}
      </div>
    </div>
  );
}

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement> & { invalid?: boolean }>(function Input(
  { className, invalid, ...rest },
  ref,
) {
  return <input ref={ref} className={cx('input', className)} aria-invalid={invalid || undefined} {...rest} />;
});

export const TextArea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement>>(function TextArea(
  { className, rows = 3, ...rest },
  ref,
) {
  return <textarea ref={ref} rows={rows} className={cx('input min-h-[96px] resize-y', className)} {...rest} />;
});

export const Select = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement>>(function Select(
  { className, children, ...rest },
  ref,
) {
  return (
    <select ref={ref} className={cx('input', className)} {...rest}>
      {children}
    </select>
  );
});

/** A round check like the desktop's (charcoal when on), with its label. */
export function Checkbox({
  label,
  checked,
  onChange,
  disabled,
  className,
  hint,
  name,
}: {
  label: ReactNode;
  checked: boolean;
  onChange(checked: boolean): void;
  disabled?: boolean;
  className?: string;
  hint?: ReactNode;
  name?: string;
}) {
  const id = useId();
  return (
    <div className={cx('flex min-h-[40px] items-start gap-3', className)}>
      <input
        id={id}
        name={name}
        type="checkbox"
        className="check mt-[3px]"
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
      />
      <label htmlFor={id} className={cx('cursor-pointer select-none', disabled && 'cursor-not-allowed opacity-60')}>
        {label}
        {hint && <span className="block text-note text-muted">{hint}</span>}
      </label>
    </div>
  );
}

/** A labelled date box (`yyyy-MM-dd`). */
export function DateInput({
  label,
  value,
  onChange,
  id,
  className,
}: {
  label: string;
  value: string;
  onChange(value: string): void;
  id?: string;
  className?: string;
}) {
  const own = useId();
  const inputId = id ?? own;
  return (
    <div className={cx('flex items-center gap-2', className)}>
      <label htmlFor={inputId} className="shrink-0 text-muted">
        {label}
      </label>
      <input id={inputId} type="date" className="input w-[170px]" value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  );
}
