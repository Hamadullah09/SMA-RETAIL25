'use client';

import Link from 'next/link';
import { useEffect, useRef } from 'react';
import { cx } from '@/lib/cx';
import type { PageDef } from '@/lib/nav';
import { ICONS } from './icons';

/**
 * The white rounded navigation panel's entries, as on the desktop: an icon, the page and its key on each line, the
 * current page a charcoal pill. `collapsible` (the side panel) shows icons only between 768 and 1279 px, with the names
 * in tooltips; the phone drawer always shows the names.
 */
export function NavList({
  pages,
  current,
  collapsible,
  onNavigate,
  keys = true,
}: {
  pages: PageDef[];
  current: string | undefined;
  collapsible?: boolean;
  onNavigate?: () => void;
  /** Show each page's key (not in the phone's menu, where there is no keyboard). */
  keys?: boolean;
}) {
  const list = useRef<HTMLUListElement>(null);
  // Keep the current page in sight when the panel is shorter than the list (as the desktop's list does).
  useEffect(() => {
    list.current?.querySelector('[aria-current="page"]')?.scrollIntoView({ block: 'nearest' });
  }, [current]);
  return (
    <ul ref={list} className="flex flex-col gap-1.5">
      {pages.map((page, index) => {
        const Icon = ICONS[page.icon];
        const active = page.key === current;
        // A group's heading before its first page (a line only, where the panel shows icons).
        const heading = page.group && page.group !== pages[index - 1]?.group ? page.group : null;
        return (
          <li key={page.key}>
            {heading && (
              <p className={cx('mb-1 mt-3 border-t border-line px-[18px] pt-3 text-note font-semibold uppercase tracking-wide text-muted', collapsible && 'max-xl:sr-only')}>
                {heading}
              </p>
            )}
            <Link
              href={page.path}
              onClick={onNavigate}
              aria-current={active ? 'page' : undefined}
              aria-keyshortcuts={page.shortcut}
              title={collapsible ? `${page.title}${page.shortcut ? ` (${page.shortcut})` : ''}` : undefined}
              className={cx(
                'group flex min-h-[54px] items-center gap-3.5 rounded-full px-[18px] transition-colors',
                collapsible && 'max-xl:justify-center max-xl:px-0',
                active ? 'bg-charcoal text-white' : 'text-ink hover:bg-stone-light',
              )}
            >
              <Icon className="h-[22px] w-[22px] shrink-0" strokeWidth={1.7} aria-hidden="true" />
              <span className={cx('flex-1 truncate text-lead font-semibold', collapsible && 'max-xl:sr-only')}>{page.title}</span>
              {page.shortcut && keys && (
                <span className={cx('text-note', active ? 'text-cream' : 'text-muted', collapsible && 'max-xl:hidden')} aria-hidden="true">
                  {page.shortcut}
                </span>
              )}
            </Link>
          </li>
        );
      })}
    </ul>
  );
}
