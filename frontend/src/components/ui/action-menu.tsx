'use client';

import Link from 'next/link';
import * as MenuPrimitive from '@radix-ui/react-dropdown-menu';
import { ChevronDown, type LucideIcon } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * The occasional actions on a screen, behind one button.
 *
 * A page header that offers five equally-weighted buttons offers no answer to "what do I press?".
 * Products had exactly that — Print labels, Price list (PDF), Import CSV, Batch changes, New item —
 * five controls of the same size and shade beside the title, four of which a shopkeeper touches
 * once a month and one of which is the thing they came to do. Everything competes, so nothing wins,
 * and the primary action is found by reading all five every time.
 *
 * The trade this makes is discoverability for legibility, and it is worth making *here* because a
 * menu row is a better home for these than a button was:
 *
 * - A row has space for a sentence. "Print labels" is a guess until you have pressed it once;
 *   "Print labels — a sheet of price labels for the items listed below" is not. That sentence never
 *   fits on a toolbar button, so collapsing the toolbar is what buys the explanation.
 * - A disabled row can say *why*. The Print labels button was greyed out whenever the grid was
 *   empty and gave no reason for it, which reads as broken rather than as not-applicable.
 * - The list is vertical, so labels are read rather than scanned past, and nothing truncates.
 *
 * What stays outside the menu is the one action the screen exists for. That is the rule: one
 * primary, visible, filled; everything else in here.
 */
export interface ActionMenuItem {
  key: string;
  label: string;

  /** One line saying what it does. This is most of the point — see the note above. */
  description?: string;
  icon?: LucideIcon;

  /** A button item. Mutually exclusive with `href`. */
  onSelect?: () => void;

  /** A link item. Internal paths route through Next; anything else opens in a new tab. */
  href?: string;

  disabled?: boolean;

  /**
   * Why it is unavailable, shown in place of the description.
   *
   * A disabled control with no explanation is indistinguishable from a broken one, and the person
   * most likely to meet it is the one least able to guess.
   */
  disabledReason?: string;
}

export function ActionMenu({
  label = 'More',
  items,
  align = 'end',
}: {
  label?: string;
  items: ActionMenuItem[];
  align?: 'start' | 'end';
}) {
  // Nothing to offer, nothing to draw. A menu button that opens an empty list is worse than the
  // absence of the button — permissions routinely leave a user with none of these.
  if (items.length === 0) return null;

  return (
    <MenuPrimitive.Root>
      <MenuPrimitive.Trigger className="pos-button" aria-label={`${label} actions`}>
        {label}
        <ChevronDown className="h-5 w-5 shrink-0" aria-hidden />
      </MenuPrimitive.Trigger>

      <MenuPrimitive.Portal>
        <MenuPrimitive.Content
          align={align}
          sideOffset={6}
          className={cn(
            'z-overlay min-w-[18rem] max-w-[22rem] overflow-hidden rounded border border-subtle bg-panel p-1',
            'shadow-popover data-[state=open]:animate-fade-in',
          )}
        >
          {items.map((item) => (
            <Row key={item.key} item={item} />
          ))}
        </MenuPrimitive.Content>
      </MenuPrimitive.Portal>
    </MenuPrimitive.Root>
  );
}

function Row({ item }: { item: ActionMenuItem }) {
  const Icon = item.icon;

  // The reason replaces the description when there is one: a row that says both what it would do
  // and why it will not is two sentences to read at the moment somebody is already stuck.
  const line = item.disabled ? (item.disabledReason ?? item.description) : item.description;

  const body = (
    <>
      {Icon ? (
        <Icon className="mt-0.5 h-5 w-5 shrink-0 text-ink-muted" aria-hidden />
      ) : null}

      <span className="flex min-w-0 flex-col">
        <span className="text-body font-medium text-ink">{item.label}</span>
        {line ? <span className="text-label text-ink-muted">{line}</span> : null}
      </span>
    </>
  );

  const className = cn(
    'flex w-full cursor-pointer select-none items-start gap-2.5 rounded-sm px-2.5 py-2 text-left outline-none',
    'data-[highlighted]:bg-panel-hover',
    // 2px offset focus ring, as everywhere else — a menu opened from the keyboard has to show
    // where it is.
    'focus-visible:ring-2 focus-visible:ring-accent focus-visible:ring-offset-2',
    item.disabled && 'cursor-not-allowed opacity-60',
  );

  if (item.href && !item.disabled) {
    // An absolute URL is a document the server renders — a PDF, an export. Those open in a new tab
    // so the browse and its filters are still there when the reader comes back from a price list.
    const external = /^https?:\/\//.test(item.href) || item.href.startsWith('/api/');

    return (
      <MenuPrimitive.Item asChild>
        {external ? (
          <a href={item.href} target="_blank" rel="noopener noreferrer" className={className}>
            {body}
          </a>
        ) : (
          <Link href={item.href} className={className}>
            {body}
          </Link>
        )}
      </MenuPrimitive.Item>
    );
  }

  return (
    <MenuPrimitive.Item
      disabled={item.disabled}
      onSelect={item.onSelect}
      className={className}
    >
      {body}
    </MenuPrimitive.Item>
  );
}
