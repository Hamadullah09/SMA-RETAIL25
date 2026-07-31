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
      className="group flex flex-col gap-1 rounded border border-subtle bg-panel p-4 transition-colors hover:border-strong hover:bg-panel-hover"
    >
      <span className="flex items-center gap-2 text-body-lg font-medium text-ink">
        {/* An icon appears only when it is faster to parse than its label (doc 08). On an index of
            ten destinations it is — you learn the shapes and stop reading. */}
        <Icon className="h-4 w-4 shrink-0 text-ink-muted transition-colors group-hover:text-ink" aria-hidden />
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
