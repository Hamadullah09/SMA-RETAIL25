'use client';

import Link from 'next/link';
import { FileText, ScrollText } from 'lucide-react';
import { useAuth } from '@/lib/auth-config';

/**
 * The reports index.
 *
 * This screen used to show six cards with a "Generate Report" button that did nothing — they looked
 * like navigation and taught people the screen was decorative. Now it lists only what exists, and
 * says plainly that the analytical reports are later-phase work rather than pretending otherwise.
 */
const available = [
  {
    href: '/reports/sales',
    title: 'Sales log',
    description:
      'Every sale in a window: filter by date, drill into the lines and payment, reprint, export to CSV. Voided sales stay visible.',
    icon: FileText,
    permission: 'reports.sales',
  },
  {
    href: '/admin/audit',
    title: 'Audit log',
    description: 'Who changed money, stock, prices, taxes or permissions — with the before and after values.',
    icon: ScrollText,
    permission: 'audit.read',
  },
];

export default function ReportsPage() {
  const { can } = useAuth();
  const visible = available.filter((report) => can(report.permission));

  return (
    <div className="space-y-4">
      <h1 className="text-lg font-semibold">Reports</h1>

      <div className="grid gap-3 md:grid-cols-2">
        {visible.map((report) => (
          <Link
            key={report.href}
            href={report.href}
            className="pos-panel block p-4 transition-colors hover:bg-[rgb(var(--surface))]"
          >
            <span className="mb-1 flex items-center gap-2 text-sm font-medium">
              <report.icon className="h-4 w-4" />
              {report.title}
            </span>
            <span className="block text-xs text-[rgb(var(--text-muted))]">{report.description}</span>
          </Link>
        ))}
      </div>

      <p className="max-w-2xl text-xs text-[rgb(var(--text-muted))]">
        Sales summaries, department breakdowns, top products, customer statements, the tax report and stock valuation
        are Phase 6 work and are not built yet. The sales log&apos;s CSV export covers ad-hoc analysis until they are.
      </p>
    </div>
  );
}
