'use client';

import { useQuery } from '@tanstack/react-query';
import { ChevronRight, FileText } from 'lucide-react';
import Link from 'next/link';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states';
import { api } from '@/lib/api';
import type { ReportInfo } from '@/lib/types';

/** The desktop's Reports menu: the groups in the catalog's order, each report a line that opens it. */
export default function ReportsPage() {
  const dates = useDateRange('month');
  const query = useQuery({ queryKey: ['reports'], queryFn: () => api.reports(), staleTime: 10 * 60_000 });

  const groups: Array<{ name: string; reports: ReportInfo[] }> = [];
  for (const report of query.data ?? []) {
    let group = groups.find((g) => g.name === report.group);
    if (!group) groups.push((group = { name: report.group, reports: [] }));
    group.reports.push(report);
  }

  const href = (r: ReportInfo) =>
    `/reports/view/?key=${encodeURIComponent(r.key)}` + (r.usesDates ? `&from=${dates.range.from}&to=${dates.range.to}` : '');

  return (
    <div>
      <PageHeader title="Reports" aside={query.data && `${query.data.length} reports`} />
      <Toolbar>
        <DateRangeBar state={dates} compact />
      </Toolbar>
      <p className="mb-4 px-1 text-ink">Choose a report. Reports with dates use the days above; the others show things as they are now.</p>
      {query.error ? (
        <div className="card">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      ) : query.isLoading ? (
        <div className="grid gap-4 lg:grid-cols-2 2xl:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-72 rounded-card" />
          ))}
        </div>
      ) : groups.length === 0 ? (
        <div className="card">
          <EmptyState title="No reports for your role" />
        </div>
      ) : (
        <div className="columns-1 gap-4 lg:columns-2 2xl:columns-3">
          {groups.map((group) => (
            <section key={group.name} className="card mb-4 break-inside-avoid p-3" aria-labelledby={`group-${group.name}`}>
              <h2 id={`group-${group.name}`} className="px-3 pb-1 pt-2 font-heading text-section">
                {group.name}
              </h2>
              <ul>
                {group.reports.map((r) => (
                  <li key={r.key}>
                    <Link href={href(r)} className="group flex min-h-[50px] items-center gap-3 rounded-[14px] px-3 hover:bg-charcoal hover:text-white focus-visible:bg-charcoal focus-visible:text-white">
                      <FileText className="h-5 w-5 shrink-0 opacity-70" strokeWidth={1.7} aria-hidden="true" />
                      <span className="flex-1">{r.title}</span>
                      {!r.usesDates && <span className="text-note text-muted group-hover:text-cream group-focus-visible:text-cream">As of now</span>}
                      <ChevronRight className="h-5 w-5 shrink-0 opacity-60" aria-hidden="true" />
                    </Link>
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}
