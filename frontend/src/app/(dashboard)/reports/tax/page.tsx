'use client';

import { useCallback, useMemo, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { DateRangeFilter, ReportShell, isoDate, useReport } from '@/components/reports/report-shell';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { formatCurrency } from '@/lib/utils';
import type { TaxReportResult, TaxReportRow } from '@/types/masters';

/**
 * The tax report a filing is built from (guide p.56).
 *
 * Each rate gets its own row even when two share a name: a rate change part-way through a period is
 * normal, and a single merged "GST" line would reconcile against nothing.
 */
export default function TaxReportPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;

  const [from, setFrom] = useState(() => isoDate(-30));
  const [to, setTo] = useState(() => isoDate());

  const load = useCallback(() => {
    if (!locationId) return undefined;
    return mastersApi.reports.tax(locationId, from, to);
  }, [locationId, from, to]);

  const { data, loading } = useReport<TaxReportResult>(load, 'Could not run the tax report');

  const columns = useMemo<DataGridColumn<TaxReportRow>[]>(
    () => [
      { key: 'tax', header: 'Tax', width: 200, render: (r) => r.taxName },
      { key: 'rate', header: 'Rate', width: 90, numeric: true, render: (r) => `${r.rate}%` },
      {
        key: 'base',
        header: 'Taxable base',
        width: 140,
        numeric: true,
        render: (r) => formatCurrency(r.taxableBase),
      },
      {
        key: 'collected',
        header: 'Collected',
        width: 140,
        numeric: true,
        render: (r) => formatCurrency(r.taxCollected),
        sortValue: (r) => r.taxCollected,
      },
      { key: 'txns', header: 'Sales', width: 90, numeric: true, render: (r) => r.transactionCount },
    ],
    [],
  );

  if (!locationId) {
    return <p className="text-body text-ink-muted">No location is attached to this session.</p>;
  }

  return (
    <ReportShell
      title="Tax report"
      exportHref={mastersApi.reports.taxExportUrl(locationId, from, to)}
      filters={<DateRangeFilter from={from} to={to} onFrom={setFrom} onTo={setTo} />}
      grid={
        <DataGrid
          gridId="tax-report"
          rows={data?.rows ?? []}
          columns={columns}
          rowKey={(row) => `${row.taxName}-${row.rate}`}
          loading={loading}
          emptyMessage="No tax was collected in this window."
        />
      }
      summary={
        data ? (
          <span className="flex flex-wrap items-center gap-4">
            <span>
              Net sales <span className="pos-amount">{formatCurrency(data.totalNetSales)}</span>
            </span>
            <span>
              Tax collected <span className="pos-amount">{formatCurrency(data.totalTaxCollected)}</span>
            </span>
            {data.registrationNumber ? <span>Registration {data.registrationNumber}</span> : null}
          </span>
        ) : null
      }
    />
  );
}
