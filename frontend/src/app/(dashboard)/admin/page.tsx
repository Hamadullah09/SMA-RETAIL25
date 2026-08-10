'use client';

import { NavCard, type NavCardItem } from '@/components/shell/nav-card';
import {
  BookOpenCheck,
  Building2,
  DatabaseBackup,
  Radio,
  RotateCcw,
  ScrollText,
  Settings,
  Shield,
  Users,
} from 'lucide-react';
import { useAuth } from '@/lib/auth-config';

/**
 * The administration index.
 *
 * Every card leads somewhere that exists. A tile that opens nothing teaches people the screen is
 * decorative, and they stop reading it.
 *
 * The twelve destinations are filed under four headings rather than dropped into one undifferentiated
 * grid. Twelve equally-weighted tiles is a list you have to read end to end every time; four named
 * groups is a screen you navigate by knowing what *kind* of thing you came for — configuration,
 * people, the books, or getting data back. The groups are presentational only: every card keeps the
 * href and the permission it already had.
 */
interface AdminGroup {
  key: string;
  title: string;

  /** One line saying what the group is for, so the heading is not a bare noun. */
  description: string;
  items: NavCardItem[];
}

const groups: AdminGroup[] = [
  {
    key: 'configuration',
    title: 'Setup and configuration',
    description: 'How the store behaves: the rules, the lists everything is filed under, and the hardware.',
    items: [
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
        key: 'groupings',
        href: '/admin/settings?tab=Groupings',
        title: 'Departments and categories',
        description: 'The two lists every item is filed under and every sales report groups by.',
        icon: Building2,
        permission: 'catalog.write',
      },
      {
        key: 'pricing',
        href: '/admin/settings?tab=Pricing',
        title: 'Price precedence',
        description: 'The order the pricing rules are consulted in. Reordering it is a settings change, not a release.',
        icon: Shield,
        permission: 'settings.write',
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
    ],
  },
  {
    key: 'people',
    title: 'People and access',
    description: 'Who may do what, who is on the clock, and a record of what was changed.',
    items: [
      {
        key: 'users',
        href: '/admin/settings?tab=Users',
        title: 'Users and access',
        description: 'Staff codes, access levels and PIN state. Authorisation is by permission; the level is only a preset.',
        icon: Users,
        permission: 'users.manage',
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
        key: 'audit',
        href: '/admin/audit',
        title: 'Audit log',
        description: 'Who changed money, stock, prices, taxes or permissions — and what the values were before.',
        icon: ScrollText,
        permission: 'audit.read',
      },
    ],
  },
  {
    key: 'books',
    title: 'The books',
    description: 'Sending the day out to the bookkeeping system, and closing a trading year.',
    items: [
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
        key: 'year-end',
        href: '/admin/year-end',
        title: 'Year end',
        description:
          'Close a trading year: roll it up into the sales history and checkpoint the stock. Nothing is deleted, and a close can be undone.',
        icon: ScrollText,
        permission: 'inventory.year_end',
      },
    ],
  },
  {
    key: 'recovery',
    title: 'Data and recovery',
    description: 'Getting data in, and getting it back when something has gone wrong.',
    items: [
      {
        key: 'undelete',
        href: '/admin/undelete',
        title: 'Undelete items',
        description: 'Bring back an item, customer, supplier or grouping that was deleted by mistake.',
        icon: RotateCcw,
        permission: 'catalog.delete',
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
        key: 'migration',
        href: '/admin/migration',
        title: 'Bring data across',
        description:
          'Read the old system’s files in, check them, rehearse the import, then do it. Nothing is written until the last step.',
        icon: ScrollText,
        permission: 'migration.run',
      },
    ],
  },
];

export default function AdminPage() {
  const { can } = useAuth();

  // A group whose every card is behind a permission the reader does not hold disappears with its
  // heading. A named heading over an empty space reads as something broken rather than as something
  // withheld.
  const visible = groups
    .map((group) => ({ ...group, items: group.items.filter((item) => can(item.permission ?? '')) }))
    .filter((group) => group.items.length > 0);

  return (
    <div className="space-y-6 p-6">
      <header className="max-w-3xl space-y-1">
        <h1>Administration</h1>
        <p className="text-body-lg text-ink-muted">
          Setup, staff, the accounting link, the year-end close and bringing data across from the old system.
        </p>
      </header>

      {visible.length === 0 ? (
        <p className="text-body text-ink-muted">
          Nothing here is available to your account. Ask an administrator if you need access.
        </p>
      ) : (
        <div className="space-y-7">
          {visible.map((group) => (
            <section key={group.key} aria-labelledby={`admin-group-${group.key}`} className="space-y-3">
              <div className="flex flex-wrap items-baseline gap-x-3 gap-y-0.5 border-b border-subtle pb-2">
                <h2 id={`admin-group-${group.key}`} className="pos-nav-section px-0 pb-0 pt-0 text-ink">
                  {group.title}
                </h2>
                <p className="text-body text-ink-muted">{group.description}</p>
              </div>

              <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                {group.items.map((item) => (
                  <NavCard key={item.key} item={item} />
                ))}
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}
