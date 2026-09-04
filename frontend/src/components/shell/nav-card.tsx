'use client';

import Link from 'next/link';
import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { PageHeader } from '@/components/shell/page-header';
import { toneClasses } from '@/lib/tone';
import { cn } from '@/lib/utils';

/** One destination on an index page. */
export interface NavCardItem {
  key: string;
  href: string;
  title: string;
  description: string;
  icon: LucideIcon;

  /** Hidden entirely when the signed-in user lacks it. */
  permission?: string;
}

/**
 * The index-page card.
 *
 * This markup was copy-pasted into three index pages, and the three had already drifted — different
 * padding, different hover, one of them missing the description entirely at small widths.
 */
export function NavCard({ item }: { item: NavCardItem }) {
  const Icon = item.icon;

  // The same colour this destination wears on the rail. A card and a rail row that name the same
  // screen in two different colours teach that the colour is decoration.
  const tone = toneClasses(item.href);

  return (
    <Link
      href={item.href}
      className="pos-panel group flex flex-col gap-1.5 p-5 transition-all duration-150 hover:-translate-y-0.5 hover:border-strong hover:shadow-popover"
    >
      <span className="flex items-center gap-2.5 text-body-lg font-semibold text-ink">
        {/* An icon appears only when it is faster to parse than its label. On an index of ten
            destinations it is — you learn the shapes and stop reading.

            On its own tile rather than loose beside the text: a glyph next to 14px type is a
            smudge, and the tile is what gives a grid of ten cards a rhythm to scan down. The tile
            carries the destination's own colour rather than the accent, so the card and the rail
            row agree; a card that hovered into the accent said "this is the primary action" about
            all ten of them at once. */}
        <span
          aria-hidden
          className={cn(
            'inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-lg transition-colors duration-150',
            tone.soft,
            tone.text,
          )}
        >
          <Icon className="h-5 w-5" />
        </span>
        {item.title}
      </span>

      <span className="text-body text-ink-muted">{item.description}</span>
    </Link>
  );
}

/**
 * An index page: a title, an optional line of explanation, and the cards the user can actually reach.
 */
export function NavIndex({
  title,
  description,
  items,
  empty,
  children,
}: {
  title: string;
  description?: string;
  items: NavCardItem[];
  empty?: string;
  children?: ReactNode;
}) {
  return (
    // The header spans the full width and the content is padded beneath it, rather than the header
    // sitting inside the page padding. Every other screen puts its title against the same edge, and
    // an index page that inset its own would be the one page whose header did not line up.
    <div className="flex flex-col">
      <PageHeader title={title} description={description} />

      <div className="space-y-6 px-page py-panel">
        {children}

        {items.length === 0 ? (
          <p className="text-body text-ink-muted">
            {empty ?? 'Nothing here is available to your account. Ask an administrator if you need access.'}
          </p>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {items.map((item) => (
              <NavCard key={item.key} item={item} />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
