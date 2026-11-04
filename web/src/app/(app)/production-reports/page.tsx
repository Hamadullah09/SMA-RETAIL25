'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ArrowLeft, Download, Play, Printer } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useState } from 'react';
import { Button } from '@/components/ui/button';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { cx } from '@/lib/cx';
import { type DateRange } from '@/lib/dates';
import { formatCount } from '@/lib/format';
import { productionApi } from '@/lib/production-api';
import { formatCell, isRightAligned } from '@/lib/report';
import { useStore } from '@/lib/store';

export default function ProductionReportsPage() {
  return (
    <Suspense>
      <Reports />
    </Suspense>
  );
}

/** The production reports: the list, and the one chosen (`?key=`) with its dates, print and CSV. */
function Reports() {
  const key = useSearchParams().get('key');
  const catalog = useQuery({ queryKey: ['production-reports'], queryFn: () => productionApi.reports(), staleTime: 10 * 60_000 });
  const router = useRouter();
  if (key) return <ReportView key={key} reportKey={key} usesDates={catalog.data?.find((r) => r.key === key)?.usesDates ?? true} />;
  return (
    <div>
      <PageHeader title="Production reports" />
      {catalog.error ? (
        <div className="card"><ErrorState error={catalog.error} onRetry={() => void catalog.refetch()} /></div>
      ) : (
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {(catalog.data ?? []).map((r) => (
            <button key={r.key} type="button" className="card p-5 text-left hover:bg-stone-light" onClick={() => router.push(`/production-reports/?key=${r.key}`)}>
              <span className="block font-heading text-section text-ink">{r.title}</span>
              <span className="text-muted">{r.usesDates ? 'For the days you choose' : 'As of now'}</span>
            </button>
          ))}
          {catalog.data?.length === 0 && <EmptyState title="No reports for your role" />}
        </div>
      )}
    </div>
  );
}

function ReportView({ reportKey, usesDates }: { reportKey: string; usesDates: boolean }) {
  const router = useRouter();
  const toast = useToast();
  const { storeId, store, currencySymbol: symbol } = useStore();
  const dates = useDateRange('month');
  const [ran, setRan] = useState<DateRange>(dates.range);
  const query = useQuery({
    queryKey: ['production-report', reportKey, storeId, usesDates ? ran : null],
    queryFn: () => productionApi.reportResult(reportKey, { storeId, ...(usesDates ? ran : {}) }),
    placeholderData: keepPreviousData,
  });
  const table = query.data;

  async function csv() {
    try {
      const blob = await productionApi.reportCsv(reportKey, { storeId, ...(usesDates ? ran : {}) });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${reportKey}${usesDates ? `-${ran.from}-to-${ran.to}` : ''}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'The CSV file did not download.');
    }
  }

  return (
    <div>
      <Toolbar>
        <Button icon={ArrowLeft} shortcut="Esc" onClick={() => router.push('/production-reports/')}>
          Production reports
        </Button>
        {usesDates && <DateRangeBar state={dates} compact />}
        {usesDates && (
          <Button variant="sun" icon={Play} shortcut="F5" busy={query.isFetching} onClick={() => setRan(dates.range)}>
            Run report
          </Button>
        )}
        <Button icon={Printer} shortcut="Ctrl+P" disabled={!table} onClick={() => window.print()}>
          Print
        </Button>
        <Button icon={Download} shortcut="Ctrl+E" disabled={!table} onClick={() => void csv()}>
          CSV
        </Button>
      </Toolbar>
      <PageHeader title={table?.title ?? 'Report'} aside={`${table?.subtitle ?? ''}${store ? ` · ${store.code}` : ' · All stores'}${table ? ` · ${formatCount(table.rows.length)} rows` : ''}`} />
      {query.error ? (
        <div className="card"><ErrorState error={query.error} onRetry={() => void query.refetch()} /></div>
      ) : !table ? (
        <div className="card"><TableSkeleton /></div>
      ) : table.rows.length === 0 ? (
        <div className="card"><EmptyState title="Nothing to report" /></div>
      ) : (
        <div className="grid-card">
          <div className="grid-scroll">
            <table className="grid-table" aria-label={table.title}>
              <thead>
                <tr>
                  {table.columns.map((c, i) => (
                    <th key={i} scope="col" className={cx(isRightAligned(c) && 'num')}>
                      {c.header}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {table.rows.map((row, r) => (
                  <tr key={r}>
                    {table.columns.map((c, i) => (
                      <td key={i} className={cx(isRightAligned(c) && 'num')}>
                        {formatCell(c, row[i], symbol)}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
              {table.columns.some((c) => c.total) && table.totals && (
                <tfoot>
                  <tr>
                    {table.columns.map((c, i) => (
                      <td key={i} className={cx(isRightAligned(c) && 'num')}>
                        {formatCell(c, table.totals![i], symbol)}
                      </td>
                    ))}
                  </tr>
                </tfoot>
              )}
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
