'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ArrowLeft, Download, Play, Printer } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useState } from 'react';
import { ReportGrid } from '@/components/report-grid';
import { Button, LinkButton } from '@/components/ui/button';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { isBusinessDate, type DateRange } from '@/lib/dates';
import { formatCount, formatDate } from '@/lib/format';
import { useHotkey } from '@/lib/hotkeys';
import { useStore } from '@/lib/store';

export default function ReportPage() {
  return (
    <Suspense>
      <Report />
    </Suspense>
  );
}

function Report() {
  const params = useSearchParams();
  const key = params.get('key') ?? '';
  const from = params.get('from');
  const to = params.get('to');
  const start: DateRange | null = isBusinessDate(from) && isBusinessDate(to) ? { from, to } : null;
  return <ReportView key={key} reportKey={key} start={start} />;
}

function ReportView({ reportKey, start }: { reportKey: string; start: DateRange | null }) {
  const router = useRouter();
  const toast = useToast();
  const { storeId, store, currencySymbol: symbol } = useStore();
  const dates = useDateRange('month', start);
  // The dates the shown report was run for; Run report (F5) takes the ones in the boxes.
  const [ran, setRan] = useState<DateRange>(dates.range);
  const [downloading, setDownloading] = useState(false);

  const catalog = useQuery({ queryKey: ['reports'], queryFn: () => api.reports(), staleTime: 10 * 60_000 });
  const info = catalog.data?.find((r) => r.key === reportKey);
  const usesDates = info?.usesDates ?? true;
  const query = useQuery({
    queryKey: ['report', reportKey, storeId, usesDates ? ran : null],
    queryFn: () => api.report(reportKey, { storeId, ...(usesDates ? ran : {}) }),
    enabled: !!reportKey,
    placeholderData: keepPreviousData,
  });
  const table = query.data;

  useHotkey('Esc', () => router.push('/reports/'));

  function run() {
    setRan(dates.range);
    const url = `/reports/view/?key=${encodeURIComponent(reportKey)}` + (usesDates ? `&from=${dates.range.from}&to=${dates.range.to}` : '');
    router.replace(url);
    if (ran.from === dates.range.from && ran.to === dates.range.to) void query.refetch();
  }

  async function downloadCsv() {
    setDownloading(true);
    try {
      const blob = await api.reportCsv(reportKey, { storeId, ...(usesDates ? ran : {}) });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${reportKey}${usesDates ? `-${ran.from}-to-${ran.to}` : ''}${store ? `-${store.code}` : ''}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : 'The CSV file did not download.');
    } finally {
      setDownloading(false);
    }
  }

  if (!reportKey) {
    return (
      <div className="card">
        <EmptyState title="No report chosen">Choose one on the Reports page.</EmptyState>
      </div>
    );
  }

  const title = table?.title ?? info?.title ?? 'Report';
  const period = usesDates ? (ran.from === ran.to ? formatDate(ran.from) : `${formatDate(ran.from)} to ${formatDate(ran.to)}`) : 'As of now';

  return (
    <div>
      <Toolbar>
        <LinkButton href="/reports/" icon={ArrowLeft} shortcut="Esc">
          Reports
        </LinkButton>
        {usesDates && <DateRangeBar state={dates} compact />}
        <Button variant="sun" icon={Play} shortcut="F5" onClick={run} busy={query.isFetching}>
          Run report
        </Button>
      </Toolbar>

      <header className="mb-3 flex flex-wrap items-end justify-between gap-3">
        <div className="min-w-0">
          <h1 className="page-title">{title}</h1>
          <p className="text-muted">
            {table?.subtitle ?? period}
            {store ? ` · ${store.code} ${store.name}` : ' · All stores'}
            {table ? ` · ${formatCount(table.rows.length)} rows` : ''}
          </p>
        </div>
        <div className="no-print flex flex-wrap gap-2.5">
          <Button variant="sun" icon={Printer} shortcut="Ctrl+P" onClick={() => window.print()} disabled={!table}>
            Print
          </Button>
          <Button icon={Download} shortcut="Ctrl+E" onClick={() => void downloadCsv()} busy={downloading} disabled={!table}>
            Download CSV
          </Button>
        </div>
      </header>

      {query.error && !table ? (
        <div className="card">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      ) : !table ? (
        <div className="grid-card">
          <TableSkeleton rows={8} columns={6} />
        </div>
      ) : (
        <ReportGrid table={table} symbol={symbol} stale={query.isPlaceholderData} />
      )}
      <p className="print-only mt-3 text-[9pt]">
        SMA RETAIL · printed {new Date().toLocaleString()}
      </p>
    </div>
  );
}
