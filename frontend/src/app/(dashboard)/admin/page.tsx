'use client';

import { NavIndex } from '@/components/shell/nav-card';
import { BookOpenCheck, Building2, DatabaseBackup, Radio, RotateCcw, ScrollText, Settings, Shield, Users } from 'lucide-react';
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
    key: 'accounting',
    href: '/admin/accounting',
    title: 'Accounting',
    description:
      'Post the day’s takings, customers, items and open invoices to the bookkeeping system — and see what was sent when something looks wrong.',
    icon: BookOpenCheck,
    permission: 'sync.run',
  },
  {
    key: 'staff',
    href: '/admin/staff',
    title: 'Staff',
    description:
      'Who is on the clock, what each person earns in commission, and the hours worked over a period.',
    icon: ScrollText,
    permission: 'staff.read',
  },
  {
    key: 'year-end',
    href: '/admin/year-end',
    title: 'Year end',
    description:
      'Close a trading year: roll it up into the sales history and checkpoint the stock. Nothing is deleted, and a close can be undone.',
    icon: ScrollText,
    permission: 'inventory.year_end',
  },
  {
    key: 'migration',
    href: '/admin/migration',
    title: 'Bring data across',
    description:
      'Read the old system’s files in, check them, rehearse the import, then do it. Nothing is written until the last step.',
    icon: ScrollText,
    permission: 'migration.run',
  },
  {
    key: 'rfid',
    href: '/admin/rfid',
    title: 'RFID readers',
    description:
      'Connect a tag reader over the network, tune its antennas and radio, see its temperature and firmware, and bring tags in from a supplier file.',
    icon: Radio,
    permission: 'terminals.read',
  },
  {
    key: 'backup',
    href: '/admin/backup',
    title: 'Backup and restore',
    description: 'Take a copy of the whole database, and put one back when the worst has happened.',
    icon: DatabaseBackup,
    permission: 'system.backup',
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

  return (
    <NavIndex
      title="Administration"
      description="Setup, staff, the accounting link, the year-end close and bringing data across from the old system."
      items={sections.filter((section) => can(section.permission))}
    />
  );
}
