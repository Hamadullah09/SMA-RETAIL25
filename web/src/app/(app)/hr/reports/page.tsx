'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Download, Play, Printer } from 'lucide-react';
import { useState } from 'react';
import { EmployeeSelect } from '@/components/hr/common';
import { Button } from '@/components/ui/button';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Input, Select } from '@/components/ui/field';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api, ApiError, type Query } from '@/lib/api';
import { cx } from '@/lib/cx';
import { formatCount } from '@/lib/format';
import { monthKey, saveBlob, taxYearName, taxYearOf } from '@/lib/hr';
import { formatCell, isRightAligned, totalsOf } from '@/lib/report';
import { useStore } from '@/lib/store';
import type { HrReportInfo, ReportTable } from '@/lib/types';

export default function HrReportsPage() {
  const catalog = useQuery({ queryKey: ['hr-reports'], queryFn: () => api.hrReports(), staleTime: 10 * 60_000 });
  const [key, setKey] = useState<string | null>(null);
  const report = catalog.data?.find((r) => r.key === key) ?? catalog.data?.[0];
  const groups = [...new Set((catalog.data ?? []).map((r) => r.group))];
  return (
    <div>
      <PageHeader title="HR reports" />
      {catalog.error ? (
        <div className="card">
          <ErrorState error={catalog.error} onRetry={() => void catalog.refetch()} />
        </div>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[260px_minmax(0,1fr)]">
          <nav aria-label="HR reports" className="no-print card h-fit p-3">
            {groups.map((g) => (
              <div key={g} className="mb-2">
                <p className="px-3 pb-1 pt-2 text-note font-semibold uppercase tracking-wide text-muted">{g}</p>
                <ul className="flex flex-col gap-1">
                  {(catalog.data ?? []).filter((r) => r.group === g).map((r) => (
                    <li key={r.key}>
                      <button
                        type="button"
                        aria-current={r.key === report?.key ? 'true' : undefined}
                        className={cx('w-full rounded-full px-4 py-2 text-left font-semibold', r.key === report?.key ? 'bg-charcoal text-white' : 'text-ink hover:bg-stone-light')}
                        onClick={() => setKey(r.key)}
                      >
                        {r.title}
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </nav>
          <div className="min-w-0">{report ? <ReportRunner key={report.key} report={report} /> : <TableSkeleton rows={6} columns={5} />}</div>
        </div>
      )}
    </div>
  );
}

function ReportRunner({ report }: { report: HrReportInfo }) {
  const toast = useToast();
  const { storeId, store, currencySymbol: symbol } = useStore();
  const has = (p: string) => report.parameters.includes(p);
  const dates = useDateRange('month');
  const [month, setMonth] = useState(monthKey());
  const [employeeId, setEmployeeId] = useState('');
  const [taxYear, setTaxYear] = useState(String(taxYearOf()));
  const [runId, setRunId] = useState('');
  const [ran, setRan] = useState<Query | null>(null);
  const [downloading, setDownloading] = useState(false);
  const runs = useQuery({ queryKey: ['hr-runs', storeId, 'reports'], queryFn: () => api.payrollRuns({ storeId }), enabled: has('run') });

  function params(): Query | null {
    const q: Query = { storeId };
    if (has('run')) {
      if (!runId) return null;
      q.runId = runId;
    }
    if (has('employee')) {
      if (!employeeId) return null;
      q.employeeId = employeeId;
    }
    if (has('taxYear')) q.taxYear = taxYear;
    if (has('month')) q.month = month;
    if (has('dates')) {
      q.from = dates.range.from;
      q.to = dates.range.to;
    }
    return q;
  }
  const ready = params() !== null;
  const query = useQuery({
    queryKey: ['hr-report', report.key, ran],
    queryFn: () => api.hrReport(report.key, ran!),
    enabled: ran !== null,
    placeholderData: keepPreviousData,
  });
  const table = query.data;

  async function csv() {
    if (!ran) return;
    setDownloading(true);
    try {
      saveBlob(await api.hrReportCsv(report.key, ran), `${report.key}${store ? `-${store.code}` : ''}.csv`);
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : 'The CSV file did not download.');
    } finally {
      setDownloading(false);
    }
  }

  return (
    <div>
      <Toolbar>
        {has('run') && (
          <>
            <label className="sr-only" htmlFor="report-run">
              Payroll run
            </label>
            <Select id="report-run" className="w-[300px]" value={runId} onChange={(e) => setRunId(e.target.value)}>
              <option value="">{runs.isLoading ? 'Loading…' : 'Choose the payroll run…'}</option>
              {(runs.data ?? []).map((r) => (
                <option key={r.id} value={r.id}>
                  #{r.number} {r.title}
                  {r.storeCode ? ` · ${r.storeCode}` : ''}
                </option>
              ))}
            </Select>
          </>
        )}
        {has('employee') && (
          <>
            <label className="sr-only" htmlFor="report-employee">
              Employee
            </label>
            <EmployeeSelect id="report-employee" className="w-[280px]" value={employeeId} onChange={setEmployeeId} empty="Choose the employee…" all />
          </>
        )}
        {has('taxYear') && (
          <>
            <label className="text-muted" htmlFor="report-tax-year">
              Tax year from
            </label>
            <Input id="report-tax-year" inputMode="numeric" className="w-[100px]" value={taxYear} maxLength={4} onChange={(e) => setTaxYear(e.target.value)} />
            <span className="text-muted">{Number(taxYear) > 1999 ? taxYearName(Number(taxYear)) : ''}</span>
          </>
        )}
        {has('month') && (
          <>
            <label className="text-muted" htmlFor="report-month">
              Month
            </label>
            <Input id="report-month" type="month" className="w-[200px]" value={month} onChange={(e) => e.target.value && setMonth(e.target.value)} />
          </>
        )}
        {has('dates') && <DateRangeBar state={dates} compact />}
        <Button variant="sun" icon={Play} shortcut="F5" disabled={!ready} busy={query.isFetching} onClick={() => setRan(params())}>
          Run report
        </Button>
      </Toolbar>

      <header className="mb-3 flex flex-wrap items-end justify-between gap-3">
        <div className="min-w-0">
          <h2 className="page-title">{table?.title ?? report.title}</h2>
          <p className="text-muted">
            {table?.subtitle ?? 'Choose what to report on, then run it.'}
            {store ? ` · ${store.code} ${store.name}` : ' · All stores'}
            {table ? ` · ${formatCount(table.rows.length)} rows` : ''}
          </p>
        </div>
        <div className="no-print flex flex-wrap gap-2.5">
          <Button icon={Printer} shortcut="Ctrl+P" onClick={() => window.print()} disabled={!table}>
            Print
          </Button>
          <Button icon={Download} shortcut="Ctrl+E" onClick={() => void csv()} busy={downloading} disabled={!table}>
            Download CSV
          </Button>
        </div>
      </header>

      {!ran ? (
        <div className="card">
          <EmptyState title="Not run yet" />
        </div>
      ) : query.error && !table ? (
        <div className="card">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      ) : !table ? (
        <div className="grid-card">
          <TableSkeleton rows={6} columns={6} />
        </div>
      ) : (
        <Grid table={table} symbol={symbol} stale={query.isPlaceholderData} />
      )}
    </div>
  );
}

function Grid({ table, symbol, stale }: { table: ReportTable; symbol: string; stale: boolean }) {
  const totals = totalsOf(table);
  if (table.rows.length === 0) {
    return (
      <div className="card">
        <EmptyState title="Nothing to report">No rows for this choice and this store.</EmptyState>
      </div>
    );
  }
  return (
    <div className={cx('grid-card transition-opacity', stale && 'opacity-60')}>
      <div className="grid-scroll max-h-[calc(100dvh-320px)] overflow-y-auto print:max-h-none">
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
                    {formatCell(c, row[i] ?? null, symbol)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
          {totals && (
            <tfoot>
              <tr>
                {table.columns.map((c, i) => (
                  <td key={i} className={cx(isRightAligned(c) && 'num')}>
                    {i === 0 && totals[0] === 'Total' ? 'Total' : formatCell(c, totals[i] ?? null, symbol)}
                  </td>
                ))}
              </tr>
            </tfoot>
          )}
        </table>
      </div>
    </div>
  );
}
