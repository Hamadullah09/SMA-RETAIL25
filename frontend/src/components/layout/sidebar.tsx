'use client';

import { useEffect } from 'react';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import {
  LayoutDashboard,
  Menu,
  BarChart3,
  ClipboardList,
  CreditCard,
  FileText,
  LogOut,
  Package,
  PanelLeftClose,
  PanelLeftOpen,
  Settings,
  ShoppingCart,
  Truck,
  Users,
  Warehouse,
} from 'lucide-react';
import { PunchClock } from '@/components/staff/punch-clock';
import { ThemeToggle } from '@/components/shell/theme-toggle';
import { CompanyLogo } from '@/components/layout/branding';
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

const navItems = [
  { href: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
  { href: '/pos', label: 'Point of Sale', icon: ShoppingCart },
  { href: '/catalog/products', label: 'Inventory', icon: Package },
  { href: '/customers', label: 'Customers', icon: Users },
  { href: '/purchasing/suppliers', label: 'Suppliers', icon: Truck },
  { href: '/inventory', label: 'Stock', icon: Warehouse },
  { href: '/purchasing', label: 'Purchasing', icon: FileText },
  { href: '/receivables', label: 'Receivables', icon: CreditCard },
  { href: '/orders', label: 'Orders & Layaways', icon: ClipboardList },
  { href: '/reports', label: 'Reports', icon: BarChart3 },
  { href: '/admin', label: 'Administration', icon: Settings },
];

export function Sidebar() {
  const pathname = usePathname();
  const auth = useAuth();
  const { sidebarOpen, toggleSidebar, drawerOpen, closeDrawer } = useUIStore();

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
      <div className="flex h-header shrink-0 items-center justify-between border-b border-subtle px-3">
        {sidebarOpen ? (
          <span className="flex min-w-0 items-center gap-2.5">
            <span className="pos-brand-mark h-7 w-7 text-label" aria-hidden>
              25
            </span>
            <span className="truncate text-h3 font-semibold tracking-tight">Retail 25</span>
          </span>
        ) : null}

        <button
          type="button"
          onClick={toggleSidebar}
          aria-expanded={sidebarOpen}
          aria-label={sidebarOpen ? 'Collapse the menu' : 'Expand the menu'}
          className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded text-ink-muted transition-colors duration-150 hover:bg-panel-hover hover:text-ink"
        >
          {sidebarOpen ? <PanelLeftClose className="h-4 w-4" /> : <PanelLeftOpen className="h-4 w-4" />}
        </button>
      </div>

      <nav aria-label="Main" className="flex-1 space-y-0.5 overflow-y-auto p-2">
        {navItems.map(({ href, label, icon: Icon }) => {
          const active = pathname.startsWith(href);

          return (
            <Link
              key={href}
              href={href}

              // The active page is announced, not only coloured. A screen reader had no way to know
              // which of ten links was the current one.
              aria-current={active ? 'page' : undefined}
              className={cn(
                'relative flex items-center gap-3 rounded-md px-3.5 py-2 text-body transition-colors duration-150',
                active
                  // Tint, hue, weight and an edge marker together. Four signals rather than one, so
                  // the current page still reads for someone who cannot separate two similar greys —
                  // which the previous fill-only treatment relied on entirely.
                  ? 'bg-accent-soft font-semibold text-accent-text'
                  : 'font-medium text-ink-muted hover:bg-panel-hover hover:text-ink',
              )}
              title={!sidebarOpen ? label : undefined}
            >
              {active ? (
                <span
                  aria-hidden
                  className="absolute left-0 top-1/2 h-5 w-1 -translate-y-1/2 rounded-full bg-accent"
                />
              ) : null}
              <Icon className={cn('h-4 w-4 shrink-0', active && 'text-accent-text')} aria-hidden />
              {sidebarOpen ? <span className="truncate">{label}</span> : null}
            </Link>
          );
        })}
      </nav>

      <div className="shrink-0 border-t border-subtle p-2">
        <button
          type="button"
          onClick={() => void auth.signOut()}
          className="flex w-full items-center gap-3 rounded-md px-3.5 py-2 text-body font-medium text-ink-muted transition-colors duration-150 hover:bg-panel-hover hover:text-ink"
        >
          <LogOut className="h-4 w-4 shrink-0" aria-hidden />
          {sidebarOpen ? <span>Sign out</span> : null}
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
            tint, then the name. The initial survives a narrow header where the name truncates. */}
        <span className="hidden min-w-0 items-center gap-2 sm:flex">
          <span
            aria-hidden
            className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-accent-soft text-label font-semibold text-accent-text"
          >
            {(user?.name || user?.email || 'S').charAt(0).toUpperCase()}
          </span>
          <span className="truncate text-ink-muted">{user?.name || user?.email || 'Signed in'}</span>
        </span>

        <kbd className="pos-kbd hidden sm:inline">Ctrl+K</kbd>
      </div>
    </header>
  );
}
