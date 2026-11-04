'use client';

import { useQuery } from '@tanstack/react-query';
import { Checkbox, Select } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states';
import { formatDate, formatPercent, formatQuantity } from '@/lib/format';
import { capacityGrid, loadTone } from '@/lib/production';
import { productionApi } from '@/lib/production-api';
import { useStore } from '@/lib/store';
import { useState } from 'react';

/** The load of each work centre week by week, against what it can do. */
export default function CapacityPage() {
  const { storeId } = useStore();
  const [weeks, setWeeks] = useState('8');
  const [suggestions, setSuggestions] = useState(false);
  const query = useQuery({
    queryKey: ['capacity', storeId, weeks, suggestions],
    queryFn: () => productionApi.capacity({ storeId, weeks: Number(weeks), suggestions: suggestions || undefined }),
  });
  const grid = capacityGrid(query.data ?? []);
  return (
    <div>
      <PageHeader title="Capacity" />
      <Toolbar>
        <label className="flex items-center gap-2">
          <span className="text-muted">Weeks</span>
          <Select aria-label="Weeks" value={weeks} className="w-[110px]" onChange={(e) => setWeeks(e.target.value)}>
            {['4', '8', '12', '26'].map((w) => (
              <option key={w} value={w}>
                {w}
              </option>
            ))}
          </Select>
        </label>
        <Checkbox label="With what planning suggests to make" checked={suggestions} onChange={setSuggestions} />
        <span className="text-muted">Open orders’ remaining work, spread over their planned days.</span>
      </Toolbar>
      {query.error ? (
        <div className="card"><ErrorState error={query.error} onRetry={() => void query.refetch()} /></div>
      ) : !query.data ? (
        <Skeleton className="h-64 rounded-card" />
      ) : grid.centres.length === 0 ? (
        <div className="card"><EmptyState title="No work centres">Add work centres, then their load shows here.</EmptyState></div>
      ) : (
        <div className="grid-card">
          <div className="grid-scroll">
            <table className="grid-table" aria-label="Capacity load">
              <thead>
                <tr>
                  <th scope="col">Work centre</th>
                  {grid.weeks.map((w) => (
                    <th key={w} scope="col" className="num">
                      {formatDate(w)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {grid.centres.map((c) => (
                  <tr key={c.id}>
                    <th scope="row" className="text-left font-semibold">
                      {c.label}
                      <span className="block text-note font-normal text-muted">{c.storeCode}</span>
                    </th>
                    {grid.weeks.map((w) => {
                      const cell = c.cells[w];
                      return (
                        <td key={w} className="num">
                          {cell && (
                            <span className="flex flex-col items-end gap-0.5">
                              <Chip tone={loadTone(cell.loadPercent)}>{formatPercent(cell.loadPercent, undefined, 0)}</Chip>
                              <span className="text-note text-muted">
                                {formatQuantity(cell.loadHours)} of {formatQuantity(cell.capacityHours)} h
                              </span>
                            </span>
                          )}
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
