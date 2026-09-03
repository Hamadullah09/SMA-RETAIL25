'use client';

import { useEffect, useState } from 'react';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { NAV_SECTIONS, railChildrenOf, type AppRoute } from '@/lib/routes';
import { matchRoute } from '@/lib/route-match';
import {
  LayoutDashboard,
  Menu,
  BarChart3,
  ChevronDown,
  ClipboardList,
  CreditCard,
  FileText,
  LogOut,
  Package,
  PanelLeftClose,
  PanelLeftOpen,
  Receipt,
  Settings,
  ShoppingCart,
  Truck,
  Users,
  Warehouse,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { PunchClock } from '@/components/staff/punch-clock';
import { ThemeToggle } from '@/components/shell/theme-toggle';
import { CompanyLogo } from '@/components/layout/branding';
import { SmaMark } from '@/components/layout/logo';
import { useAuth } from '@/lib/auth-config';
import { useUIStore } from '@/stores/ui-store';
import { cn } from '@/lib/utils';

/**
 * The one place the sidebar's own width is written.
 *
 * It used to appear in three files — the aside, its spacer, and the layout's margin — so widening
 * the sidebar meant finding all three or leaving the page overlapping it.
 */
/*
 * Written out in full rather than composed, because Tailwind's scanner reads source text: a class
 * built as `lg:${RAIL.open}` is never seen, never generated, and silently does nothing.
 */
const RAIL = {
  open: 'lg:w-sidebar',
  closed: 'lg:w-sidebar-collapsed',
} as const;

/**
 * The rail is drawn from src/lib/routes.ts, not from a list kept here.
 *
 * There were three such lists — this one, the command palette's, and the index-page cards — and
 * nothing kept them in step. "Inventory" named /catalog/products here and /inventory in the
 * palette: opposite screens behind one word. Highlighting is resolved by the same matcher help
 * uses, so the row that looks current and the guide Ctrl+H opens cannot disagree.
 */
export function Sidebar() {
  const pathname = usePathname();
  const auth = useAuth();
  const user = auth.user;
  const { sidebarOpen, toggleSidebar, drawerOpen, closeDrawer } = useUIStore();

  // The longest declared prefix, so exactly one row reads as current — /purchasing/suppliers sits
  // inside /purchasing, and against a solid accent fill two filled rows is simply wrong.
  const current = matchRoute(pathname)?.href;

  /**
   * Rows the signed-in person can actually open.
   *
   * The rail used to show everything while the palette filtered by permission, so the same person
   * saw different destinations depending on how they went looking. Filtering happens only where a
   * route declares a permission the page itself already enforces; the report leaves carry none and
   * stay visible, because hiding a row somebody is entitled to is worse than showing one that turns
   * out to be refused — a hidden row cannot even be asked about.
   */
  const permitted = (route: AppRoute) => !route.permission || auth.can(route.permission);

  /**
   * Which groups are open, over and above the one containing the current page.
   *
   * Held as a set of the groups the user has *toggled*, not of the groups that are open — otherwise
   * navigating to a page inside a closed group would leave the current row hidden inside it.
   */
  const [toggled, setToggled] = useState<ReadonlySet<string>>(() => new Set());

  // Labels are hidden only when the desktop rail is deliberately collapsed. In the mobile drawer the
  // rail is full width and always labelled: a 240px overlay showing nothing but eleven glyphs is a
  // menu that has to be guessed at.
  const showLabels = sidebarOpen || drawerOpen;

  // Below `lg` the rail becomes a drawer: off-canvas by default, slid in over the page with a
  // backdrop. A 240px rail on a 375px phone leaves 135px for the content, which is not a layout.
  //
  // Navigating closes it, because a drawer left open over the page someone just asked for is the
  // most common way a mobile menu becomes annoying.
  useEffect(() => {
    closeDrawer();
  }, [pathname, closeDrawer]);

  // Escape closes it. Standard for anything overlaying the page, and the only exit that does not
  // require finding a small target with a thumb.
  useEffect(() => {
    if (!drawerOpen) return undefined;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') closeDrawer();
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [drawerOpen, closeDrawer]);

  return (
    <>
      {/* Only rendered when open, so it can never swallow clicks on a desktop. */}
      {drawerOpen ? (
        <div
          className="fixed inset-0 z-drawer bg-black/40 lg:hidden"
          onClick={closeDrawer}
          role="presentation"
        />
      ) : null}

      <aside
        className={cn(
          'fixed left-0 top-0 z-overlay flex h-screen flex-col border-r border-subtle bg-panel',
          'transition-transform duration-200 lg:transition-[width]',

          // Off-canvas until opened, and always full width when it is — a collapsed icon rail on a
          // phone is a row of unlabelled glyphs.
          'w-sidebar -translate-x-full',
          drawerOpen && 'translate-x-0 shadow-overlay',

          // From `lg` up it is a fixed rail again and the transform stops applying.
          'lg:translate-x-0',
          sidebarOpen ? RAIL.open : RAIL.closed,
        )}
      >
      {/* The brand tile. The mark is the same gradient square the sign-in hero uses, so the product
          is drawn once; the tagline under the name is what tells a new starter what they are looking
          at, and it costs one line of a rail that has room for it. */}
      <div className="flex shrink-0 items-center justify-between gap-2 px-3 py-3.5">
        {showLabels ? (
          <span className="flex min-w-0 items-center gap-2.5">
            <SmaMark className="h-9 w-9 shrink-0 rounded-[10px]" />
            <span className="flex min-w-0 flex-col">
              <span className="truncate text-body-lg font-semibold leading-tight tracking-tight">SMA Retail</span>
              <span className="truncate text-caption leading-tight text-ink-muted">Retail management</span>
            </span>
          </span>
        ) : null}

        <button
          type="button"
          onClick={toggleSidebar}
          aria-expanded={sidebarOpen}
          aria-label={sidebarOpen ? 'Collapse the menu' : 'Expand the menu'}
          className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-md text-ink-muted transition-colors duration-150 hover:bg-panel-hover hover:text-ink"
        >
          {sidebarOpen ? <PanelLeftClose className="h-4 w-4" /> : <PanelLeftOpen className="h-4 w-4" />}
        </button>
      </div>

      <nav aria-label="Main" className="flex-1 overflow-y-auto px-2 pb-2">
        {NAV_SECTIONS.map((section) => (
          <div key={section.heading}>
            {/* The section headings are the one place uppercase still earns its keep: they are
                signposts between groups rather than labels on anything, and at 11px they read as
                structure instead of as shouting. Hidden on the icon rail, where they would be three
                stray words with nothing under them. */}
            {showLabels ? <p className="pos-nav-section">{section.heading}</p> : null}

            <div className="space-y-0.5">
              {section.items.filter(permitted).map((item) => {
                const Icon = item.icon;
                const children = railChildrenOf(item.href).filter(permitted);
                const hasChildren = children.length > 0 && showLabels;
                const childActive = children.some((child) => child.href === current);

                // Open because the user opened it, or because the current page is inside it — the
                // exclusive-or being the case where they closed the group they are standing in.
                const expanded = hasChildren && toggled.has(item.href) !== childActive;

                // A group's first child is usually the parent's own href, so an expanded group would
                // otherwise fill two rows for one page. The child wins, because it is the more
                // specific of the two labels. On the icon rail there is no child to win, so the
                // parent carries the state for everything inside it.
                const active =
                  (item.href === current && !(expanded && childActive)) || (!showLabels && childActive);

                return (
                  <div key={item.href}>
                    <div className="relative">
                      <Link
                        href={item.href}
                        // The active page is announced, not only coloured. A screen reader had no way
                        // to know which of ten links was the current one.
                        aria-current={active ? 'page' : undefined}
                        data-active={active ? 'true' : undefined}
                        className={cn('pos-nav-item', hasChildren && 'pr-9', !showLabels && 'justify-center px-0')}
                        title={!showLabels ? item.label : undefined}
                      >
                        <Icon className="h-4 w-4 shrink-0" aria-hidden />
                        {showLabels ? <span className="truncate">{item.label}</span> : null}
                      </Link>

                      {/* A separate control from the link, because it does a different thing. One row
                          that both navigates and expands is a row you cannot expand without leaving
                          the page you were on. */}
                      {hasChildren ? (
                        <button
                          type="button"
                          onClick={() =>
                            setToggled((prev) => {
                              const next = new Set(prev);
                              if (next.has(item.href)) next.delete(item.href);
                              else next.add(item.href);
                              return next;
                            })
                          }
                          aria-expanded={expanded}
                          aria-label={`${expanded ? 'Collapse' : 'Expand'} ${item.label}`}
                          className={cn(
                            'absolute right-1 top-1/2 inline-flex h-7 w-7 -translate-y-1/2 items-center justify-center rounded transition-colors duration-150',
                            active
                              ? 'text-accent-foreground/80 hover:bg-white/15 hover:text-accent-foreground'
                              : 'text-ink-faint hover:bg-panel-hover hover:text-ink',
                          )}
                        >
                          <ChevronDown
                            className={cn('h-3.5 w-3.5 transition-transform duration-150', !expanded && '-rotate-90')}
                            aria-hidden
                          />
                        </button>
                      ) : null}
                    </div>

                    {hasChildren && expanded ? (
                      <div className="mt-0.5 space-y-0.5">
                        {children.map((child) => (
                          <Link
                            key={child.href}
                            href={child.href}
                            aria-current={child.href === current ? 'page' : undefined}
                            data-active={child.href === current ? 'true' : undefined}
                            className="pos-nav-item pos-nav-child"
                          >
                            <span className="truncate">{child.label}</span>
                          </Link>
                        ))}
                      </div>
                    ) : null}
                  </div>
                );
              })}
            </div>
          </div>
        ))}
      </nav>

      {/*
        Who is signed in, and the way out.

        The card is the sign-out control rather than sitting above one, so the rail's last row is a
        single target instead of two stacked ones. The icon and the tooltip both say what it does —
        a chevron here would promise a menu that does not exist.
      */}
      <div className="shrink-0 border-t border-subtle p-2">
        <button
          type="button"
          onClick={() => void auth.signOut()}
          title="Sign out"
          className={cn(
            'flex w-full items-center gap-2.5 rounded-md p-2 text-left transition-colors duration-150 hover:bg-panel-hover',
            !showLabels && 'justify-center',
          )}
        >
          <span
            aria-hidden
            className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-accent-soft text-body font-semibold text-accent-text"
          >
            {(user?.name || user?.email || 'S').charAt(0).toUpperCase()}
          </span>

          {showLabels ? (
            <>
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="truncate text-body font-medium leading-tight text-ink">
                  {user?.name || 'Signed in'}
                </span>
                {user?.email ? (
                  <span className="truncate text-caption leading-tight text-ink-muted">{user.email}</span>
                ) : null}
              </span>

              <LogOut className="h-4 w-4 shrink-0 text-ink-faint" aria-hidden />
            </>
          ) : null}

          <span className="sr-only">Sign out</span>
        </button>
      </div>
      </aside>
    </>
  );
}

export function Header() {
  const auth = useAuth();
  const user = auth.user;
  const { toggleDrawer } = useUIStore();

  return (
    <header className="sticky top-0 z-sticky flex h-header flex-wrap items-center justify-between gap-x-4 gap-y-1 border-b border-subtle bg-panel/85 px-4 backdrop-blur-md">
      {/*
        The only way to reach navigation below `lg`, where the rail is off-canvas. Hidden from `lg`
        up, where the rail is always visible and this would be a button that appears to do nothing.

        The app name itself lives in the sidebar. Repeating it here was the second <h1> on every
        page, and two of them means neither is the page's title.
      */}
      <div className="flex min-w-0 items-center gap-3">
        <button
          type="button"
          onClick={toggleDrawer}
          aria-label="Open the menu"
          className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded text-ink-muted transition-colors duration-150 hover:bg-panel-hover hover:text-ink lg:hidden"
        >
          <Menu className="h-5 w-5" aria-hidden />
        </button>

        {/*
          The shop's own mark, uploaded through the admin screens. Nothing renders when a store has
          not uploaded one, so an unbranded installation is a header that looks deliberate rather
          than one with a hole in it.
        */}
        <CompanyLogo className="h-7 w-auto max-w-[180px] shrink-0 object-contain" />
      </div>

      <div className="flex min-w-0 items-center gap-3 text-body">
        <PunchClock />
        <ThemeToggle />

        {/* Who is signed in, as a face rather than a floating string: an initial on the accent
            tint, then the name. Only below `lg`, where the rail is off-canvas and its user card
            cannot be seen — above that this was the same name written twice on one screen. */}
        <span className="hidden min-w-0 items-center gap-2 sm:flex lg:hidden">
          <span
            aria-hidden
            className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-accent-soft text-label font-semibold text-accent-text"
          >
            {(user?.name || user?.email || 'S').charAt(0).toUpperCase()}
          </span>
          <span className="truncate text-ink-muted">{user?.name || user?.email || 'Signed in'}</span>
        </span>

        {/* The Ctrl+K hint used to live here. A shortcut badge pinned to the chrome is read once
            and then occupies the corner of every screen forever; the palette is still on the key,
            and it is listed with the rest of them in the shortcuts sheet. */}
      </div>
    </header>
  );
}
