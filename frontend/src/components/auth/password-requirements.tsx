'use client';

import { Check, Circle } from 'lucide-react';
import { PASSWORD_RULES } from '@/lib/password-policy';

/**
 * The rules, ticking themselves off as they are met.
 *
 * Shown when the field is reached, not on arrival and not on the first mistake.
 *
 * The original note here argued for showing them from the start: somebody who can see what is
 * wanted before they start typing chooses one password, and somebody told afterwards chooses
 * three. That reasoning is right and is preserved — the list appears the moment the field takes
 * focus, which is still before a single character is typed. What it was paying for was a wall of
 * six unticked rules sitting under an empty box on arrival, on a form somebody meets once. That is
 * a lot of unasked-for instruction above the fold, and it made the page look harder than it is.
 *
 * Once anything has been typed the list stays, whether or not the field still has focus, so
 * tabbing on to "Password again" does not take the checklist away at the moment it is being used
 * to check something.
 *
 * Unmet rules are not styled as errors. Nothing has gone wrong yet — a password halfway through
 * being typed is short because it is halfway through being typed, and a list of red crosses says
 * otherwise. They turn positive as they are satisfied and are otherwise quiet.
 */
export function PasswordRequirements({
  id,
  password,
  identity,
  focused = false,
}: {
  id: string;
  password: string;
  identity: readonly string[];

  /** Whether the password field currently has focus. */
  focused?: boolean;
}) {
  // Unmounted rather than hidden with a class. `aria-live` on a `display: none` element announces
  // nothing, so a CSS-only version would go quiet for exactly the people the live region is for.
  if (!focused && password.length === 0) return null;

  return (
    // aria-live, so the ticks are announced as they happen. Without it a screen-reader user gets
    // silence while typing and then a rejection, which is the exact experience this replaces.
    <ul id={id} aria-live="polite" className="mt-1.5 space-y-1">
      {PASSWORD_RULES.map((rule) => {
        const met = rule.met(password, identity);

        return (
          <li
            key={rule.id}
            // The readable tone, not the fill tone. `--positive` is oklch L 0.52, which measures
            // about 4.0:1 on the panel — under AA for 14px, and these are 14px.
            className={`flex items-center gap-1.5 text-caption ${met ? 'text-positive-text' : 'text-ink-muted'}`}
          >
            {met ? (
              <Check className="h-5 w-5 shrink-0" aria-hidden />
            ) : (
              <Circle className="h-5 w-5 shrink-0" aria-hidden />
            )}
            <span>{rule.label}</span>
            <span className="sr-only">{met ? ' — met' : ' — not yet met'}</span>
          </li>
        );
      })}
    </ul>
  );
}
