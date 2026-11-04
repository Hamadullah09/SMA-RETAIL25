'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ArrowLeft, Download, Play, Printer } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useId, useState } from 'react';
import { AccountSelect, saveBlob, useAccounts } from '@/components/finance';
import { ReportGrid } from '@/components/report-grid';
import { Button, LinkButton } from '@/components/ui/button';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { DateInput, Select } from '@/components/ui/field';
import { Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { isBusinessDate, type DateRange } from '@/lib/dates';
import { comparisonLabel, reportInputs, statementRow } from '@/lib/finance';
import { formatCount, formatDate } from '@/lib/format';
import { useHotkey } from '@/lib/hotkeys';
import { useStore } from '@/lib/store';
import type { FinanceComparison } from '@/lib/types';

export default function StatementPage() {
  return (
    <Suspense>
      <Statement />
    </Suspense>
  );
}

function Statement() {
  const params = useSearchParams();
  const key = params.get('key') ?? '';
  const from = params.get('from');
  const to = params.get('to');
  const start: DateRange | null = isBusinessDate(from) && isBusinessDate(to) ? { from, to } : null;
  const compare = (params.get('compare') as FinanceComparison | null) ?? 'none';
  return <StatementView key={key} reportKey={key} start={start} startCompare={compare} startAccount={params.get('accountId')} />;
}

interface Ran {
  range: DateRange;
  compare: FinanceComparison;
  accountId: string | null;
}

/** One statement or report of the books: its days (or the day it is at), what it is compared with, print and CSV. */
function StatementView({ reportKey, start, startCompare, startAccount }: { reportKey: string; start: DateRange | null; startCompare: FinanceComparison; startAccount: string | null }) {
  const router = useRouter();
  const toast = useToast();
  const { storeId, store, currencySymbol: symbol } = useStore();
  const ids = { compare: useId(), account: useId() };
  const dates = useDateRange('month', start);
  const [compare, setCompare] = useState<FinanceComparison>(startCompare);
  const [accountId, setAccountId] = useState<string | null>(startAccount);
  // What the shown statement was run with; Run (F5) takes what the boxes say.
  const [ran, setRan] = useState<Ran>({ range: dates.range, compare: startCompare, accountId: startAccount });
  const [downloading, setDownloading] = useState(false);

  const catalog = useQuery({ queryKey: ['finance-reports'], queryFn: () => api.financeReports(), staleTime: 10 * 60_000 });
  const info = catalog.data?.find((r) => r.key === reportKey);
  const inputs = info ? reportInputs(info) : { oneDay: false, comparisons: [], account: 'none' as const };
  const accounts = useAccounts();
  const waiting = inputs.account === 'required' && !ran.accountId;
  const query = {
    storeId,
    ...(inputs.oneDay ? { from: ran.range.to, to: ran.range.to } : ran.range),
    compare: inputs.comparisons.length && ran.compare !== 'none' ? ran.compare : null,
    accountId: inputs.account !== 'none' ? ran.accountId : null,
  };
  const report = useQuery({
    queryKey: ['finance-report', reportKey, query],
    queryFn: () => api.financeReport(reportKey, query),
    enabled: !!reportKey && !!info && !waiting,
    placeholderData: keepPreviousData,
  });
  const table = report.data;

  useHotkey('Esc', () => router.push('/finance/reports/'));

  function run() {
    const next = { range: dates.range, compare, accountId };
    setRan(next);
    const url =
      `/finance/reports/view/?key=${encodeURIComponent(reportKey)}&from=${next.range.from}&to=${next.range.to}` +
      (next.compare !== 'none' ? `&compare=${next.compare}` : '') +
      (next.accountId ? `&accountId=${encodeURIComponent(next.accountId)}` : '');
    router.replace(url);
    if (JSON.stringify(next) === JSON.stringify(ran)) void report.refetch();
  }

  async function downloadCsv() {
    setDownloading(true);
    try {
      saveBlob(await api.financeReportCsv(reportKey, query), `${reportKey}-${query.from}-to-${query.to}${store ? `-${store.code}` : ''}.csv`);
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : 'The CSV file did not download.');
    } finally {
      setDownloading(false);
    }
  }

  if (!reportKey || (catalog.data && !info)) {
    return (
      <div className="card">
        <EmptyState title="No statement chosen">Choose one on the Statements page.</EmptyState>
      </div>
    );
  }

  const title = table?.title ?? info?.title ?? 'Statement';
  return (
    <div>
      <Toolbar>
        <LinkButton href="/finance/reports/" icon={ArrowLeft} shortcut="Esc">
          Statements
        </LinkButton>
        {inputs.oneDay ? (
          <DateInput label="At" value={dates.range.to} onChange={(d) => d && dates.setRange({ from: d, to: d })} />
        ) : (
          <DateRangeBar state={dates} compact />
        )}
        {inputs.comparisons.length > 0 && (
          <>
            <label htmlFor={ids.compare} className="text-muted">
              Compared with
            </label>
            <Select id={ids.compare} className="w-[230px]" value={compare} onChange={(e) => setCompare(e.target.value as FinanceComparison)}>
              {inputs.comparisons.map((c) => (
                <option key={c} value={c}>
                  {comparisonLabel(c)}
                </option>
              ))}
            </Select>
          </>
        )}
        {inputs.account !== 'none' && (
          <div className="w-[300px]">
            <AccountSelect
              id={ids.account}
              ariaLabel="Account"
              value={accountId}
              accounts={accounts.data}
              allowEmpty={inputs.account === 'optional'}
              emptyLabel={inputs.account === 'optional' ? 'Every account' : 'Choose an account'}
              onChange={setAccountId}
            />
          </div>
        )}
        <Button variant="sun" icon={Play} shortcut="F5" onClick={run} busy={report.isFetching}>
          Run
        </Button>
      </Toolbar>

      <header className="mb-3 flex flex-wrap items-end justify-between gap-3">
        <div className="min-w-0">
          <h1 className="page-title">{title}</h1>
          <p className="text-muted">
            {table?.subtitle ?? (inputs.oneDay ? `At ${formatDate(ran.range.to)}` : `${formatDate(ran.range.from)} to ${formatDate(ran.range.to)}`)}
            {store ? ` · ${store.code} ${store.name}` : ' · the company'}
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

      {waiting ? (
        <div className="card">
          <EmptyState title="Choose the account">Choose the account above, then Run.</EmptyState>
        </div>
      ) : report.error && !table ? (
        <div className="card">
          <ErrorState error={report.error} onRetry={() => void report.refetch()} />
        </div>
      ) : !table ? (
        <div className="grid-card">
          <TableSkeleton rows={8} columns={5} />
        </div>
      ) : (
        <ReportGrid
          table={table}
          symbol={symbol}
          stale={report.isPlaceholderData}
          empty="Nothing was posted on these days."
          rowKind={(row) => statementRow(reportKey, row)}
        />
      )}
      <p className="print-only mt-3 text-[9pt]">SMA RETAIL · printed {new Date().toLocaleString()}</p>
    </div>
  );
}
