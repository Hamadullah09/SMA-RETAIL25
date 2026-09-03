'use client';

import { Check, Circle } from 'lucide-react';
import { PASSWORD_RULES } from '@/lib/password-policy';

/**
 * The rules, ticking themselves off as they are met.
 *
 * Shown from the start rather than appearing on the first mistake. Somebody who can see what is
 * wanted before they start typing chooses one password; somebody told afterwards chooses three.
 *
 * Unmet rules are not styled as errors. Nothing has gone wrong yet — a password halfway through
 * being typed is short because it is halfway through being typed, and a list of red crosses says
 * otherwise. They turn positive as they are satisfied and are otherwise quiet.
 */
export function PasswordRequirements({
  id,
  password,
  identity,
}: {
  id: string;
  password: string;
  identity: readonly string[];
}) {
  return (
    // aria-live, so the ticks are announced as they happen. Without it a screen-reader user gets
    // silence while typing and then a rejection, which is the exact experience this replaces.
    <ul id={id} aria-live="polite" className="mt-1.5 space-y-1">
      {PASSWORD_RULES.map((rule) => {
        const met = rule.met(password, identity);

        return (
          <li
            key={rule.id}
            className={`flex items-center gap-1.5 text-caption ${met ? 'text-positive' : 'text-ink-muted'}`}
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
