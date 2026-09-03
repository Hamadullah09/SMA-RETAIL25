'use client';

import Link from 'next/link';
import {
  BarChart3,
  Boxes,
  Coins,
  FileText,
  Gift,
  PackageCheck,
  Receipt,
  ScrollText,
  TrendingDown,
  Truck,
} from 'lucide-react';
import { useAuth } from '@/lib/auth-config';
import { PageHeader } from '@/components/shell/page-header';

/**
 * The reports index.
 *
 * This screen once showed six cards with a "Generate Report" button that did nothing — they looked
 * like navigation and taught people the screen was decorative. It lists only what exists, grouped
 * the way someone looking for a number thinks about it, and each card is hidden from anyone whose
 * permissions would refuse the report behind it.
 */
interface ReportLink {
  href: string;
  title: string;
  description: string;
  icon: typeof FileText;
  permission: string;
}

const GROUPS: { heading: string; reports: ReportLink[] }[] = [
  {
    heading: 'Sales',
    reports: [
      {
        href: '/reports/sales-analysis',
        title: 'Sales analysis',
        description:
          'Revenue by product, department, client or period — and, capped and sorted by quantity, the top sellers. Margin and cost appear if you may see them.',
        icon: BarChart3,
        permission: 'reports.sales',
      },
      {
        href: '/reports/sales',
        title: 'Sales log',
        description:
          'Every sale in a window: filter by date, drill into the lines and payment, reprint, export to CSV. Voided sales stay visible.',
        icon: FileText,
        permission: 'reports.sales',
      },
      {
        href: '/reports/tax',
        title: 'Tax report',
        description:
          'What was collected, per rate, for a filing period. A rate change mid-period gets its own line rather than being merged.',
        icon: Receipt,
        permission: 'reports.financial',
      },
    ],
  },
  {
    heading: 'Inventory',
    reports: [
      {
        href: '/reports/stock-value',
        title: 'Stock valuation',
        description: 'What the shelves are worth at cost and at retail, by department or item by item.',
        icon: Coins,
        permission: 'reports.cost_visibility',
      },
      {
        href: '/reports/stock-position',
        title: 'Understock and overstock',
        description:
          'What is running short and what is drowning the shelf, using three weeks of demand against base stock and what is on order.',
        icon: TrendingDown,
        permission: 'reports.inventory',
      },
      {
        href: '/reports/on-order',
        title: 'On order',
        description: 'Everything bought but not yet arrived, by supplier — the answer to "did we already order that?"',
        icon: Truck,
        permission: 'reports.inventory',
      },
      {
        href: '/reports/stock-received',
        title: 'Stock received',
        description: 'What actually arrived in a window, read from the stock ledger rather than the paperwork.',
        icon: PackageCheck,
        permission: 'reports.inventory',
      },
    ],
  },
  {
    heading: 'Customers',
    reports: [
      {
        href: '/reports/reward-points',
        title: 'Reward points',
        description: 'Points earned and spent per customer, alongside the balance each of them holds today.',
        icon: Gift,
        permission: 'customer.read',
      },
      {
        href: '/receivables',
        title: 'Receivables and aging',
        description: 'Who owes what, how overdue it is, and the statement behind each balance.',
        icon: Boxes,
        permission: 'ar.read',
      },
    ],
  },
  {
    heading: 'System',
    reports: [
      {
        href: '/admin/audit',
        title: 'Audit log',
        description: 'Who changed money, stock, prices, taxes or permissions — with the before and after values.',
        icon: ScrollText,
        permission: 'audit.read',
      },
    ],
  },
];

export default function ReportsPage() {
  const { can } = useAuth();

  const groups = GROUPS.map((group) => ({
    ...group,
    reports: group.reports.filter((report) => can(report.permission)),
  })).filter((group) => group.reports.length > 0);

  return (
    <div className="flex flex-col">
      <PageHeader title="Reports" description="Figures for a period you choose. Every report can be exported." />

      <div className="space-y-6 px-page py-panel">

      {groups.map((group) => (
        <section key={group.heading} className="space-y-3">
          {/* `pos-panel-header` was on this heading, which drew a panel's bottom rule and padding
              across a page with no panel under it. A section heading is not a card title. */}
          <h2 className="pos-nav-section px-0 pt-0">{group.heading}</h2>

          <div className="grid gap-3 md:grid-cols-2">
            {group.reports.map((report) => (
              <Link
                key={report.href}
                href={report.href}
                className="pos-panel group block p-5 transition-all duration-150 hover:-translate-y-0.5 hover:border-strong hover:shadow-popover"
              >
                <span className="mb-1.5 flex items-center gap-2.5 text-body-lg font-semibold text-ink">
                  <span
                    aria-hidden
                    className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-accent-soft text-accent-text transition-colors group-hover:bg-accent group-hover:text-accent-foreground"
                  >
                    <report.icon className="h-4 w-4" />
                  </span>
                  {report.title}
                </span>
                <span className="block text-body text-ink-muted">{report.description}</span>
              </Link>
            ))}
          </div>
        </section>
      ))}

      {/*
        This paragraph used to say these six were "still being built". Every one of them shipped, and
        a notice claiming otherwise sends the person who needs them away from a system that has them.
        They are not reports — each is a screen of its own — so they are named here with a route
        rather than described in the past tense.
      */}
      <section className="space-y-3">
        <h2 className="pos-nav-section px-0 pt-0">Not reports, but often looked for here</h2>

        <div className="flex flex-wrap gap-2">
          <Link className="pos-button" href="/catalog/products">
            Labels and price tags
          </Link>
          <Link className="pos-button" href="/admin/staff">
            Staff hours and commissions
          </Link>
          <Link className="pos-button" href="/catalog/bulk">
            Bulk price changes
          </Link>
          <Link className="pos-button" href="/inventory/counts">
            Stock counts
          </Link>
          <Link className="pos-button" href="/inventory/transfers">
            Stock transfers
          </Link>
          <Link className="pos-button" href="/admin/year-end">
            Year-end close
          </Link>
          <Link className="pos-button" href="/admin/accounting">
            Accounting sync
          </Link>
        </div>
      </section>
      </div>
    </div>
  );
}
