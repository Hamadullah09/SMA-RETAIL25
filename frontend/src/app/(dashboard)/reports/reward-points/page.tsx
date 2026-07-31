'use client';

import { useCallback, useMemo, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { DateRangeFilter, ReportShell, isoDate, useReport } from '@/components/reports/report-shell';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import type { RewardPointsResult, RewardPointsRow } from '@/types/masters';

/**
 * Points earned and spent, per customer (guide p.83–84).
 *
 * The balance column is what the customer actually holds today, not the window's running total —
 * someone who earned nothing this month still has whatever they were carrying, and a report that
 * implied otherwise would have staff telling people their points had vanished.
 */
export default function RewardPointsPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;

  const [from, setFrom] = useState(() => isoDate(-90));
  const [to, setTo] = useState(() => isoDate());

  const load = useCallback(() => {
    if (!locationId) return undefined;
    return mastersApi.reports.rewardPoints(locationId, from, to);
  }, [locationId, from, to]);

  const { data, loading } = useReport<RewardPointsResult>(load, 'Could not load reward-point activity');

  const columns = useMemo<DataGridColumn<RewardPointsRow>[]>(
    () => [
      { key: 'customer', header: 'Customer', width: 240, render: (r) => r.customerName },
      { key: 'earned', header: 'Earned', width: 100, numeric: true, render: (r) => r.earned, sortValue: (r) => r.earned },
      { key: 'redeemed', header: 'Redeemed', width: 110, numeric: true, render: (r) => r.redeemed },
      {
        key: 'adjusted',
        header: 'Adjusted',
        width: 100,
        numeric: true,
        render: (r) => (r.adjusted === 0 ? '—' : r.adjusted),
      },
      {
        key: 'net',
        header: 'Net change',
        width: 120,
        numeric: true,
        render: (r) => (r.netChange > 0 ? `+${r.netChange}` : r.netChange),
        sortValue: (r) => r.netChange,
      },
      {
        key: 'balance',
        header: 'Balance now',
        width: 130,
        numeric: true,
        render: (r) => r.currentBalance,
        sortValue: (r) => r.currentBalance,
      },
    ],
    [],
  );

  if (!locationId) {
    return <p className="text-sm text-[rgb(var(--text-muted))]">No location is attached to this session.</p>;
  }

  return (
    <ReportShell
      title="Reward points"
      exportHref={mastersApi.reports.rewardPointsExportUrl(locationId, from, to)}
      filters={<DateRangeFilter from={from} to={to} onFrom={setFrom} onTo={setTo} />}
      grid={
        <DataGrid
          gridId="reward-points"
          rows={data?.rows ?? []}
          columns={columns}
          rowKey={(row) => row.customerId}
          emptyMessage={loading ? 'Loading…' : 'No point activity in this window.'}
        />
      }
      summary={
        data ? (
          <span className="flex flex-wrap items-center gap-4">
            <span>
              {data.rows.length} customer{data.rows.length === 1 ? '' : 's'}
            </span>
            <span>Earned {data.totalEarned}</span>
            <span>Redeemed {data.totalRedeemed}</span>
            <span>Balance is today&rsquo;s, not the window&rsquo;s.</span>
          </span>
        ) : null
      }
    />
  );
}
