'use client';

import { useEffect, useState } from 'react';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
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

interface NavItem {
  href: string;
  label: string;
  icon: LucideIcon;

  /**
   * Sub-destinations, revealed by the row's chevron.
   *
   * A group's own row stays a link to `href`. Making the parent a toggle instead would cost a click
   * on the destination people actually want — nobody opens "Stock" in order to look at the word
   * "Stock".
   */
  children?: Array<{ href: string; label: string }>;
}

/**
 * The rail, in sections.
 *
 * Eleven undifferentiated rows is a list to be read; three short ones are a shape to be learned.
 * Every destination the flat list had is still here under its original label — the sections and the
 * two groups are grouping, not a renaming, so nobody has to relearn where anything lives.
 */
const navSections: Array<{ heading: string; items: NavItem[] }> = [
  {
    heading: 'Main',
    items: [
      { href: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
      { href: '/pos', label: 'Point of Sale', icon: ShoppingCart },
      { href: '/sales', label: 'Previous sales', icon: Receipt },
      {
        href: '/catalog/products',
        label: 'Inventory',
        icon: Package,
        children: [
          { href: '/catalog/products', label: 'Products' },
          { href: '/catalog/bulk', label: 'Bulk changes' },
        ],
      },
      {
        href: '/inventory',
        label: 'Stock',
        icon: Warehouse,
        children: [
          { href: '/inventory', label: 'Stock on hand' },
          { href: '/inventory/counts', label: 'Counts' },
          { href: '/inventory/transfers', label: 'Transfers' },
        ],
      },
      { href: '/customers', label: 'Customers', icon: Users },
    ],
  },
  {
    heading: 'Operations',
    items: [
      { href: '/purchasing/suppliers', label: 'Suppliers', icon: Truck },
      { href: '/purchasing', label: 'Purchasing', icon: FileText },
      { href: '/receivables', label: 'Receivables', icon: CreditCard },
      { href: '/orders', label: 'Orders & Layaways', icon: ClipboardList },
    ],
  },
  {
    heading: 'Others',
    items: [
      { href: '/reports', label: 'Reports', icon: BarChart3 },
      { href: '/admin', label: 'Administration', icon: Settings },
    ],
  },
];

/** Every href in the rail, parents and children alike. */
const allHrefs = navSections.flatMap((section) =>
  section.items.flatMap((item) => [item.href, ...(item.children ?? []).map((child) => child.href)]),
);

/**
 * Exactly one row is the current page.
 *
 * `startsWith` alone lit up two rows at once — `/purchasing/suppliers` is inside `/purchasing`, and
 * `/inventory/counts` is inside `/inventory`. That was survivable when the active state was a faint
 * tint; with a solid accent fill, two filled rows is simply wrong. The longest matching prefix wins,
 * which is the same rule a router uses.
 */
function activeHref(pathname: string): string | undefined {
  return allHrefs
    .filter((href) => pathname === href || pathname.startsWith(`${href}/`))
    .sort((a, b) => b.length - a.length)[0];
}

export function Sidebar() {
  const pathname = usePathname();
  const auth = useAuth();
  const user = auth.user;
  const { sidebarOpen, toggleSidebar, drawerOpen, closeDrawer } = useUIStore();

  const current = activeHref(pathname);

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
          className="fixed inset-0 z-30 bg-black/40 lg:hidden"
          onClick={closeDrawer}
          role="presentation"
        />
      ) : null}

      <aside
        className={cn(
          'fixed left-0 top-0 z-40 flex h-screen flex-col border-r border-subtle bg-panel',
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
        {navSections.map((section) => (
          <div key={section.heading}>
            {/* The section headings are the one place uppercase still earns its keep: they are
                signposts between groups rather than labels on anything, and at 11px they read as
                structure instead of as shouting. Hidden on the icon rail, where they would be three
                stray words with nothing under them. */}
            {showLabels ? <p className="pos-nav-section">{section.heading}</p> : null}

            <div className="space-y-0.5">
              {section.items.map((item) => {
                const Icon = item.icon;
                const hasChildren = Boolean(item.children?.length) && showLabels;
                const childActive = (item.children ?? []).some((child) => child.href === current);

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
                        {item.children!.map((child) => (
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
    <header className="sticky top-0 z-30 flex h-header items-center justify-between gap-4 border-b border-subtle bg-panel/85 px-4 backdrop-blur-md">
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

      <div className="flex items-center gap-3 text-body">
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
