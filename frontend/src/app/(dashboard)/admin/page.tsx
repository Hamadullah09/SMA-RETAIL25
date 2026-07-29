'use client';

import Link from 'next/link';
import { Building2, RotateCcw, ScrollText, Settings, Shield, Users } from 'lucide-react';
import { useAuth } from '@/lib/auth-config';

/**
 * The administration index.
 *
 * Every card leads somewhere that exists. A tile that opens nothing teaches people the screen is
 * decorative, and they stop reading it.
 */
const sections = [
  {
    key: 'setup',
    href: '/admin/settings',
    title: 'Setup',
    description:
      'Business identity, taxes, POS behaviour, printers, hardware, stations, tenders, currencies, numbering and users.',
    icon: Settings,
    permission: 'settings.read',
  },
  {
    key: 'undelete',
    href: '/admin/undelete',
    title: 'Undelete items',
    description: 'Bring back an item, customer, supplier or grouping that was deleted by mistake.',
    icon: RotateCcw,
    permission: 'catalog.delete',
  },
  {
    key: 'audit',
    href: '/admin/audit',
    title: 'Audit log',
    description: 'Who changed money, stock, prices, taxes or permissions — and what the values were before.',
    icon: ScrollText,
    permission: 'audit.read',
  },
  {
    key: 'users',
    href: '/admin/settings',
    title: 'Users and access',
    description: 'Staff codes, access levels and PIN state. Authorisation is by permission; the level is only a preset.',
    icon: Users,
    permission: 'users.manage',
  },
  {
    key: 'groupings',
    href: '/admin/settings',
    title: 'Departments and categories',
    description: 'The two lists every item is filed under and every sales report groups by.',
    icon: Building2,
    permission: 'catalog.write',
  },
  {
    key: 'pricing',
    href: '/admin/settings',
    title: 'Price precedence',
    description: 'The order the pricing rules are consulted in. Reordering it is a settings change, not a release.',
    icon: Shield,
    permission: 'settings.write',
  },
];

export default function AdminPage() {
  const { can } = useAuth();
  const visible = sections.filter((section) => can(section.permission));

  return (
    <div className="space-y-4">
      <h1 className="text-lg font-semibold">Administration</h1>

      {visible.length === 0 ? (
        <p className="text-sm text-[rgb(var(--text-muted))]">
          Nothing here is available to your account. Ask an administrator if you need access.
        </p>
      ) : (
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {visible.map((section) => (
            <Link
              key={section.key}
              href={section.href}
              className="pos-panel block p-4 transition-colors hover:bg-[rgb(var(--surface))]"
            >
              <span className="mb-1 flex items-center gap-2 text-sm font-medium">
                <section.icon className="h-4 w-4" />
                {section.title}
              </span>
              <span className="block text-xs text-[rgb(var(--text-muted))]">{section.description}</span>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
