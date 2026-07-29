'use client';

import Link from 'next/link';
import { useQuery } from '@tanstack/react-query';
import { Package, RotateCcw, Settings, ShoppingCart, Truck, Users } from 'lucide-react';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';

/**
 * The landing screen.
 *
 * It used to show "Total products" and "Total customers" counted from the first page of a list, so
 * a store with forty thousand items was told it had a hundred. A wrong number on a dashboard is
 * worse than no number: people quote it. What is shown now is a figure that is actually computed —
 * how many items need reordering — and the way in to each screen.
 */
export default function DashboardPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;

  const { data: reorder } = useQuery({
    queryKey: ['reorder-count', locationId],
    queryFn: () => mastersApi.products.browse(locationId!, { belowReorderPoint: true, pageSize: 200 }),
    enabled: Boolean(locationId),
  });

  const needsReorder = reorder ? `${reorder.items.length}${reorder.hasMore ? '+' : ''}` : '—';

  const destinations = [
    { href: '/pos', title: 'Point of sale', hint: 'Ring a sale, take a return, close the drawer.', icon: ShoppingCart, permission: 'pos.sell' },
    { href: '/catalog/products', title: 'Inventory', hint: `${needsReorder} items at or below their reorder point.`, icon: Package, permission: 'catalog.read' },
    { href: '/customers', title: 'Customers', hint: 'Accounts, price levels and balances.', icon: Users, permission: 'customer.read' },
    { href: '/purchasing/suppliers', title: 'Suppliers', hint: 'Who you buy from, and at what cost.', icon: Truck, permission: 'purchasing.read' },
    { href: '/admin/settings', title: 'Setup', hint: 'Taxes, POS behaviour, hardware, stations, users.', icon: Settings, permission: 'settings.read' },
    { href: '/admin/undelete', title: 'Undelete items', hint: 'Bring back something deleted by mistake.', icon: RotateCcw, permission: 'catalog.delete' },
  ];

  const visible = destinations.filter((destination) => auth.can(destination.permission));

  return (
    <div className="space-y-4">
      <h1 className="text-lg font-semibold">Retail 25</h1>

      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {visible.map((destination) => (
          <Link
            key={destination.href}
            href={destination.href}
            className="pos-panel block p-4 transition-colors hover:bg-[rgb(var(--surface))]"
          >
            <span className="mb-1 flex items-center gap-2 text-sm font-medium">
              <destination.icon className="h-4 w-4" />
              {destination.title}
            </span>
            <span className="block text-xs text-[rgb(var(--text-muted))]">{destination.hint}</span>
          </Link>
        ))}
      </div>
    </div>
  );
}
