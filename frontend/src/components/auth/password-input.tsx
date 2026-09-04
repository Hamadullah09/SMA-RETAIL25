'use client';

import { useId, useState } from 'react';
import { ArrowBigUp, Eye, EyeOff } from 'lucide-react';
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
  onFocusChange,
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

  /**
   * Told when the field gains or loses focus.
   *
   * Here rather than left to the caller to wire onto the input, because the caller does not own the
   * input -- this component does, and a second `onFocus` passed through would have to be merged
   * with the caps-lock handler below by hand at every call site.
   */
  onFocusChange?: (focused: boolean) => void;
}) {
  const [revealed, setRevealed] = useState(false);
  const hintId = useId();

  /**
   * Caps Lock, said out loud.
   *
   * This is the single most common reason a correct password is rejected, and a password field is
   * the one place the usual evidence is missing -- the characters are dots, so the one clue that
   * would give it away is exactly what is hidden. Without this the screen's answer is "that
   * username or password is not right", which sends somebody to look for the wrong mistake, and
   * after three tries to their manager.
   *
   * Read from the keyboard event rather than held as device state, because there is no way to ask
   * the browser: `getModifierState` answers only while a key is being pressed. Checked on both key
   * down and key up so that pressing Caps Lock itself both raises and clears the warning -- on
   * keydown for that key the state is still the old one, and on keyup it is the new one.
   */
  const [capsLock, setCapsLock] = useState(false);

  const readCapsLock = (event: React.KeyboardEvent<HTMLInputElement>) => {
    setCapsLock(event.getModifierState?.('CapsLock') ?? false);
  };

  return (
    <div className="relative">
      <input
        id={id}
        type={revealed ? 'text' : 'password'}
        className={cn('pos-input w-full pr-11')}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        onKeyDown={readCapsLock}
        onKeyUp={readCapsLock}
        onFocus={() => onFocusChange?.(true)}
        // Cleared on the way out rather than left standing: a warning about a keyboard nobody is
        // typing on is noise, and it would otherwise persist over the rest of the form.
        onBlur={() => {
          setCapsLock(false);
          onFocusChange?.(false);
        }}
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

      {/*
        Below the field, in flow, so it pushes the form down rather than covering the next label.
        `role="status"` rather than `alert`: it is worth saying, but it is not an error and should
        not interrupt what a screen reader is in the middle of.
      */}
      {capsLock ? (
        <p
          role="status"
          className="mt-1.5 flex items-center gap-1.5 text-caption font-medium text-warning-text"
        >
          <ArrowBigUp className="h-4 w-4 shrink-0" aria-hidden />
          Caps Lock is on.
        </p>
      ) : null}
    </div>
  );
}
