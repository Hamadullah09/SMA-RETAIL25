'use client';

import { CircleHelp } from 'lucide-react';
import { useHelp } from './help-overlay';
import { cn } from '@/lib/utils';

/**
 * The Help control, in one shape, everywhere it appears.
 *
 * Named rather than a lone "?" glyph: a question mark is only obvious to somebody who already knows
 * what it does, and the reader this application is for does not. The word is the point.
 *
 * It opens the side panel rather than navigating, so reading how a screen works never costs the
 * half-filled form — or, at the till, the cart.
 */
export function HelpButton({
  /** Force a particular guide. Otherwise it opens the one for the screen you are standing on. */
  topic,
  className,
}: {
  topic?: string;
  className?: string;
}) {
  const { open } = useHelp();

  return (
    <button type="button" onClick={() => open(topic)} className={cn('pos-button', className)}>
      <CircleHelp className="h-5 w-5 shrink-0" aria-hidden />
      Help
      {/* The shortcut, said once, where the control is — rather than in a guide nobody has opened
          yet. This is how somebody finds out Ctrl+H exists. */}
      <span className="sr-only"> for this screen. Shortcut: Control H.</span>
    </button>
  );
}
