'use client';

import type { ReactNode } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { ChevronRight } from 'lucide-react';
import { HelpButton } from '@/components/help/help-button';
import { breadcrumbFor } from '@/lib/route-match';

/**
 * The top of every screen, in one place.
 *
 * There were seven implementations of this, and one of them was copy-pasted verbatim into six admin
 * files where it had already drifted into three variants. Seventeen further routes had no `<h1>` at
 * all, so a screen reader announcing the page had nothing to announce and the browser's own
 * "headings" navigation skipped straight past them.
 *
 * The parts are fixed and the order is fixed, because the value of a page header is that it is in
 * the same place on the next page too. Somebody who has learned where the title, the actions and
 * the help are on one screen has learned it everywhere.
 */
export function PageHeader({
  title,
  description,
  actions,
  breadcrumb = true,
  help = true,
  className,
}: {
  title: string;
  description?: ReactNode;
  /** The screen's own controls. Primary action last, so it sits nearest the content it acts on. */
  actions?: ReactNode;
  /** Off for a top-level screen where the trail would be a single entry — that is decoration. */
  breadcrumb?: boolean;
  /**
   * The Help link. On by default and rarely off: a control that is only sometimes there is one
   * nobody learns to look for, which is the whole failure this component exists to fix.
   */
  help?: boolean;
  className?: string;
}) {
  const pathname = usePathname();
  const trail = breadcrumb ? breadcrumbFor(pathname) : [];

  return (
    <header
      className={`flex flex-wrap items-start justify-between gap-x-4 gap-y-2 border-b border-subtle px-page py-panel ${className ?? ''}`}
    >
      <div className="min-w-0">
        {/*
          Only drawn when there is somewhere above here to go. A breadcrumb whose only entry is the
          page you are already on tells you nothing and takes a line to do it.
        */}
        {trail.length > 1 ? (
          <nav aria-label="Breadcrumb" className="mb-1 flex items-center gap-1 text-label text-ink-muted">
            {trail.slice(0, -1).map((crumb) => (
              <span key={crumb.href} className="flex items-center gap-1">
                <Link href={crumb.href} className="hover:text-ink hover:underline">
                  {crumb.label}
                </Link>
                <ChevronRight className="h-4 w-4 shrink-0 text-ink-faint" aria-hidden />
              </span>
            ))}
            <span className="text-ink">{trail[trail.length - 1].label}</span>
          </nav>
        ) : null}

        {/* The page's only h1, at the page-title size. Several screens rendered theirs at card-title
            size, which made the biggest thing on the page a heading inside it. */}
        <h1 className="text-h1 font-semibold">{title}</h1>

        {description ? <p className="mt-1 max-w-2xl text-body text-ink-muted">{description}</p> : null}
      </div>

      <div className="flex flex-wrap items-center gap-2">
        {actions}

        {help ? <HelpButton /> : null}
      </div>
    </header>
  );
}
