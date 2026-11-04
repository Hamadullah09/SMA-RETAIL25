'use client';

import { useQuery } from '@tanstack/react-query';
import { ChevronRight, Download, ScrollText } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { saveBlob } from '@/components/finance';
import { Button } from '@/components/ui/button';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { exportName } from '@/lib/finance';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { FinanceReportInfo, LedgerExportFormat } from '@/lib/types';

/** The statements first, then the books' other reports. */
const STATEMENTS = ['trial-balance', 'profit-and-loss', 'balance-sheet', 'cash-flow', 'budget-vs-actual'];

const EXPORTS: ReadonlyArray<{ format: LedgerExportFormat; title: string; note: string }> = [
  { format: 'tally', title: 'Tally', note: 'XML of journal vouchers, for Import of Data in Tally' },
  { format: 'iif', title: 'QuickBooks Desktop', note: 'IIF file of general journal entries' },
  { format: 'qbo', title: 'QuickBooks Online', note: 'CSV for Import data › Journal entries (dates as dd/mm/yyyy)' },
  { format: 'csv', title: 'Spreadsheet', note: 'Every posted line as CSV' },
];

/**
 * The statements (trial balance, profit and loss, balance sheet, cash flow, budget against actual) and the other reports
 * of the books, for the store chosen in the header or the whole company; and the ledger exported for the accountant's
 * program.
 */
export default function StatementsPage() {
  const { can } = useSession();
  const { storeId, store } = useStore();
  const toast = useToast();
  const dates = useDateRange('month');
  const [busy, setBusy] = useState<string | null>(null);
  const catalog = useQuery({ queryKey: ['finance-reports'], queryFn: () => api.financeReports(), staleTime: 10 * 60_000 });
  const statements = (catalog.data ?? []).filter((r) => STATEMENTS.includes(r.key)).sort((a, b) => STATEMENTS.indexOf(a.key) - STATEMENTS.indexOf(b.key));
  const others = (catalog.data ?? []).filter((r) => !STATEMENTS.includes(r.key));
  const href = (r: FinanceReportInfo) => `/finance/reports/view/?key=${encodeURIComponent(r.key)}&from=${dates.range.from}&to=${dates.range.to}`;
  const scope = store ? `${store.code} ${store.name}` : 'the whole company';

  async function download(what: LedgerExportFormat | 'journal') {
    setBusy(what);
    try {
      const query = { storeId, ...dates.range };
      if (what === 'journal') saveBlob(await api.dailyJournalCsv(query), `journal-${dates.range.from.replaceAll('-', '')}-${dates.range.to.replaceAll('-', '')}.csv`);
      else saveBlob(await api.ledgerExport(what, query), exportName(what, dates.range.from, dates.range.to));
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : 'The file did not download.');
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <PageHeader title="Statements" aside={`For ${scope}`} />
      <Toolbar>
        <DateRangeBar state={dates} compact />
      </Toolbar>
      <p className="mb-4 px-1 text-ink">
        Choose a statement. It covers the days above and {store ? 'the store chosen at the top' : 'every store together'}; choose a store at the top for
        one store.
      </p>
      {catalog.error ? (
        <div className="card">
          <ErrorState error={catalog.error} onRetry={() => void catalog.refetch()} />
        </div>
      ) : catalog.isLoading ? (
        <div className="grid gap-4 lg:grid-cols-2">
          <Skeleton className="h-72 rounded-card" />
          <Skeleton className="h-72 rounded-card" />
        </div>
      ) : (catalog.data ?? []).length === 0 ? (
        <div className="card">
          <EmptyState title="No statements" />
        </div>
      ) : (
        <div className="grid items-start gap-4 lg:grid-cols-2">
          <ReportList title="Statements" reports={statements} href={href} />
          <ReportList title="Ledgers, tax and ageing" reports={others} href={href} />
        </div>
      )}

      {can(P.accountingExport) && (
        <section className="card mt-4 p-5" aria-labelledby="exports">
          <h2 id="exports" className="section-title">
            For the accountant’s program
          </h2>
          <p className="mb-3 text-muted">
            The entries posted on the days above, for {scope}. A store’s file sets what other stores share in an entry against a ledger named “Between stores”.
          </p>
          <ul className="grid gap-3 md:grid-cols-2">
            {EXPORTS.map((e) => (
              <li key={e.format} className="flex flex-wrap items-center justify-between gap-3 rounded-[14px] border border-line px-4 py-3">
                <div className="min-w-0">
                  <p className="font-semibold">{e.title}</p>
                  <p className="text-note text-muted">{e.note}</p>
                </div>
                <Button icon={Download} busy={busy === e.format} onClick={() => void download(e.format)}>
                  Download
                </Button>
              </li>
            ))}
            <li className="flex flex-wrap items-center justify-between gap-3 rounded-[14px] border border-line px-4 py-3">
              <div className="min-w-0">
                <p className="font-semibold">Daily journal</p>
                <p className="text-note text-muted">The tills’ business days by account code, as before</p>
              </div>
              <Button icon={Download} busy={busy === 'journal'} onClick={() => void download('journal')}>
                Download
              </Button>
            </li>
          </ul>
        </section>
      )}
    </div>
  );
}

function ReportList({ title, reports, href }: { title: string; reports: FinanceReportInfo[]; href(r: FinanceReportInfo): string }) {
  const id = `group-${title.replace(/\W+/g, '-').toLowerCase()}`;
  return (
    <section className="card p-3" aria-labelledby={id}>
      <h2 id={id} className="px-3 pb-1 pt-2 font-heading text-section">
        {title}
      </h2>
      <ul>
        {reports.map((r) => (
          <li key={r.key}>
            <Link href={href(r)} className="group flex min-h-[50px] items-center gap-3 rounded-[14px] px-3 hover:bg-charcoal hover:text-white focus-visible:bg-charcoal focus-visible:text-white">
              <ScrollText className="h-5 w-5 shrink-0 opacity-70" strokeWidth={1.7} aria-hidden="true" />
              <span className="flex-1">{r.title}</span>
              {r.asOfDate && <span className="text-note text-muted group-hover:text-cream group-focus-visible:text-cream">On the last day</span>}
              <ChevronRight className="h-5 w-5 shrink-0 opacity-60" aria-hidden="true" />
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
