'use client';

import { useId, useState } from 'react';
import { Eye, EyeOff } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * A password field that can be read back.
 *
 * Typing a password you cannot see is how people end up with a typo they only discover at the
 * sign-in page — and on a shop floor it is also how an administrator setting somebody's first
 * password fails to notice they typed it wrong before reading it out.
 *
 * Toggling only changes the input's `type`. The value is never re-set, so a password manager's
 * autofill, an in-progress selection and the caret position all survive the switch — swapping the
 * element out, or clearing and re-writing the value, breaks all three.
 */
export function PasswordInput({
  id,
  value,
  onChange,
  autoComplete,
  minLength,
  required,
  invalid,
  describedBy,
  autoFocus,
}: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  autoComplete?: string;
  minLength?: number;
  required?: boolean;
  invalid?: boolean;
  describedBy?: string;
  autoFocus?: boolean;
}) {
  const [revealed, setRevealed] = useState(false);
  const hintId = useId();

  return (
    <div className="relative">
      <input
        id={id}
        type={revealed ? 'text' : 'password'}
        className={cn('pos-input w-full pr-11')}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        autoComplete={autoComplete}
        minLength={minLength}
        required={required}
        autoFocus={autoFocus}
        aria-invalid={invalid || undefined}
        aria-describedby={[describedBy, hintId].filter(Boolean).join(' ') || undefined}
      />

      {/*
        A real button, not a span with a click handler: it has to be reachable by keyboard and
        announce itself, and `type="button"` keeps Enter inside the form submitting the form
        rather than toggling the password.
      */}
      <button
        type="button"
        onClick={() => setRevealed((current) => !current)}
        className="absolute inset-y-0 right-0 flex w-10 items-center justify-center rounded-r text-ink-muted transition-colors hover:text-ink focus-visible:text-ink"
        aria-pressed={revealed}
        aria-controls={id}
        aria-label={revealed ? 'Hide password' : 'Show password'}
      >
        {revealed ? <EyeOff className="h-4 w-4" aria-hidden /> : <Eye className="h-4 w-4" aria-hidden />}
      </button>

      {/* Announced on toggle, so the state change is not visual-only. */}
      <span id={hintId} className="sr-only" role="status">
        {revealed ? 'Password is visible' : 'Password is hidden'}
      </span>
    </div>
  );
}
