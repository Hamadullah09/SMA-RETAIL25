'use client';

import Link from 'next/link';
import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';

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

  return (
    <Link
      href={item.href}
      className="pos-panel group flex flex-col gap-1.5 p-5 transition-all duration-150 hover:-translate-y-0.5 hover:border-strong hover:shadow-popover"
    >
      <span className="flex items-center gap-2.5 text-body-lg font-semibold text-ink">
        {/* An icon appears only when it is faster to parse than its label (doc 08). On an index of
            ten destinations it is — you learn the shapes and stop reading.

            On its own accent tile rather than loose beside the text: a 16px glyph next to 14px type
            is a smudge, and the tile is what gives a grid of ten cards a rhythm to scan down. */}
        <span
          aria-hidden
          className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-accent-soft text-accent-text transition-colors group-hover:bg-accent group-hover:text-accent-foreground"
        >
          <Icon className="h-4 w-4" />
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
    <div className="space-y-6 p-6">
      <header className="max-w-3xl space-y-1">
        <h1>{title}</h1>
        {description ? <p className="text-body-lg text-ink-muted">{description}</p> : null}
      </header>

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
  );
}
