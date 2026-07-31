'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import {
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
import { useAuth } from '@/lib/auth-config';
import { useUIStore } from '@/stores/ui-store';
import { cn } from '@/lib/utils';

/**
 * The one place the sidebar's own width is written.
 *
 * It used to appear in three files — the aside, its spacer, and the layout's margin — so widening
 * the sidebar meant finding all three or leaving the page overlapping it.
 */
const RAIL = {
  open: 'w-sidebar',
  closed: 'w-sidebar-collapsed',
} as const;

const navItems = [
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
  const { sidebarOpen, toggleSidebar } = useUIStore();

  return (
    <aside
      className={cn(
        'fixed left-0 top-0 z-40 flex h-screen flex-col border-r border-subtle bg-panel transition-[width] duration-200',
        sidebarOpen ? RAIL.open : RAIL.closed,
      )}
    >
      <div className="flex h-header shrink-0 items-center justify-between border-b border-subtle px-3">
        {sidebarOpen ? <span className="text-h3 font-semibold tracking-tight">Retail 25</span> : null}

        <button
          type="button"
          onClick={toggleSidebar}
          aria-expanded={sidebarOpen}
          aria-label={sidebarOpen ? 'Collapse the menu' : 'Expand the menu'}
          className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded text-ink-muted transition-colors hover:bg-panel-hover hover:text-ink"
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
                'flex items-center gap-3 rounded px-3 py-2 text-body font-medium transition-colors',
                active
                  // Carried by weight and a left rule as well as by fill, so it survives being
                  // looked at by someone who cannot separate the two greys.
                  ? 'bg-panel-hover text-ink shadow-[inset_2px_0_0_0_rgb(var(--accent))]'
                  : 'text-ink-muted hover:bg-panel-hover hover:text-ink',
              )}
              title={!sidebarOpen ? label : undefined}
            >
              <Icon className="h-4 w-4 shrink-0" aria-hidden />
              {sidebarOpen ? <span className="truncate">{label}</span> : null}
            </Link>
          );
        })}
      </nav>

      <div className="shrink-0 border-t border-subtle p-2">
        <button
          type="button"
          onClick={() => void auth.signOut()}
          className="flex w-full items-center gap-3 rounded px-3 py-2 text-body font-medium text-ink-muted transition-colors hover:bg-panel-hover hover:text-ink"
        >
          <LogOut className="h-4 w-4 shrink-0" aria-hidden />
          {sidebarOpen ? <span>Sign out</span> : null}
        </button>
      </div>
    </aside>
  );
}

export function Header() {
  const auth = useAuth();
  const user = auth.user;

  return (
    <header className="sticky top-0 z-30 flex h-header items-center justify-between gap-4 border-b border-subtle bg-panel px-4">
      {/*
        The app name lives in the sidebar. Repeating it here was the second <h1> on every page, and
        two of them means neither is the page's title.
      */}
      <div className="min-w-0" />

      <div className="flex items-center gap-3 text-body">
        <PunchClock />
        <ThemeToggle />

        <span className="hidden truncate text-ink-muted sm:inline">
          {user?.name || user?.email || 'Signed in'}
        </span>

        <kbd className="pos-kbd hidden sm:inline">Ctrl+K</kbd>
      </div>
    </header>
  );
}
