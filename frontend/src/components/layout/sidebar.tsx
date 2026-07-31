'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { PunchClock } from '@/components/staff/punch-clock';
import { cn } from '@/lib/utils';
import {
  LayoutDashboard,
  ShoppingCart,
  Package,
  Users,
  Truck,
  BarChart3,
  Settings,
  CreditCard,
  Warehouse,
  FileText,
  ClipboardList,
  LogOut,
  Menu,
} from 'lucide-react';
import { useAuth } from '@/lib/auth-config';
import { useUIStore } from '@/stores/ui-store';
import { Button } from '@/components/ui/button';

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
    <>
      <aside
        className={cn(
          'fixed left-0 top-0 z-40 h-screen border-r bg-card transition-all duration-200',
          sidebarOpen ? 'w-64' : 'w-16'
        )}
      >
        <div className="flex h-16 items-center justify-between px-4 border-b">
          {sidebarOpen && <span className="font-bold text-lg">Retail 25</span>}
          <Button variant="ghost" size="icon" onClick={toggleSidebar} className="shrink-0">
            <Menu className="h-5 w-5" />
          </Button>
        </div>
        <nav className="space-y-1 p-2">
          {navItems.map(({ href, label, icon: Icon }) => {
            const active = pathname.startsWith(href);
            return (
              <Link
                key={href}
                href={href}
                className={cn(
                  'flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors',
                  active ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                )}
                title={!sidebarOpen ? label : undefined}
              >
                <Icon className="h-5 w-5 shrink-0" />
                {sidebarOpen && <span>{label}</span>}
              </Link>
            );
          })}
        </nav>
        <div className="absolute bottom-0 left-0 right-0 p-2 border-t">
          <button
            onClick={() => void auth.signOut()}
            className="flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium text-muted-foreground hover:bg-accent w-full"
          >
            <LogOut className="h-5 w-5 shrink-0" />
            {sidebarOpen && <span>Sign Out</span>}
          </button>
        </div>
      </aside>
      <div className={cn('transition-all duration-200', sidebarOpen ? 'ml-64' : 'ml-16')} />
    </>
  );
}

export function Header() {
  const auth = useAuth();
  const user = auth.user;

  return (
    <header className="h-16 border-b bg-card flex items-center justify-between px-6">
      <div className="flex items-center gap-4">
        <h1 className="text-lg font-semibold">Retail 25</h1>
      </div>
      <div className="flex items-center gap-4 text-sm">
        <PunchClock />
        <span className="text-muted-foreground">{user?.name || user?.email || "Signed in"}</span>
        <kbd className="rounded-sm border px-1 font-mono text-[10px] text-muted-foreground">Ctrl+K</kbd>
      </div>
    </header>
  );
}
