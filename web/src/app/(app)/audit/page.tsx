'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { SidePanel } from '@/components/ui/dialog';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Select } from '@/components/ui/field';
import { Detail, PageHeader, Pager, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { api } from '@/lib/api';
import { cx } from '@/lib/cx';
import { formatCount, formatShortUtc, formatUtc } from '@/lib/format';
import { useDebounced } from '@/lib/hooks';
import { AUDIT_ACTIONS } from '@/lib/labels';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { AuditRow } from '@/lib/types';

const PAGE_SIZE = 100;

export default function AuditPage() {
  const { can } = useSession();
  const { storeId } = useStore();
  const dates = useDateRange('week');
  const [userId, setUserId] = useState('');
  const [action, setAction] = useState('');
  const [text, setText] = useState('');
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<AuditRow | null>(null);
  const search = useDebounced(text.trim());
  const filters = { storeId, from: dates.range.from, to: dates.range.to, userId, action, search };
  useEffect(() => setPage(1), [storeId, dates.range.from, dates.range.to, userId, action, search]);

  const query = useQuery({
    queryKey: ['audit', filters, page],
    queryFn: ({ signal }) => api.audit({ ...filters, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  const users = useQuery({ queryKey: ['users', storeId], queryFn: () => api.users({ storeId }), enabled: can(P.setupUsers) });

  return (
    <div>
      <PageHeader title="Audit log" aside={query.data && `${formatCount(query.data.total)} lines`} />
      <Toolbar>
        <DateRangeBar state={dates} compact />
      </Toolbar>
      <Toolbar>
        <SearchBox value={text} onChange={setText} placeholder="Words in the details" />
        {users.data && (
          <>
            <label className="sr-only" htmlFor="audit-user">
              User
            </label>
            <Select id="audit-user" className="w-[200px]" value={userId} onChange={(e) => setUserId(e.target.value)}>
              <option value="">Every user</option>
              {users.data.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.userName}
                </option>
              ))}
            </Select>
          </>
        )}
        <label className="sr-only" htmlFor="audit-action">
          Action
        </label>
        <Select id="audit-action" className="w-[200px]" value={action} onChange={(e) => setAction(e.target.value)}>
          <option value="">Every action</option>
          {AUDIT_ACTIONS.map((a) => (
            <option key={a} value={a}>
              {a}
            </option>
          ))}
        </Select>
      </Toolbar>
      <DataTable<AuditRow>
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(a) => a.id}
        selectedKey={open?.id}
        onRowClick={setOpen}
        caption="Audit log"
        empty={<EmptyState title="Nothing in the log for these days" />}
        columns={[
          { key: 'at', header: 'When', cell: (a) => formatShortUtc(a.atUtc) },
          { key: 'user', header: 'User', cell: (a) => a.userName ?? '' },
          { key: 'where', header: 'Where', cell: (a) => where(a), hideBelow: 'lg' },
          { key: 'action', header: 'Action', cell: (a) => <span className="font-semibold">{a.action}</span> },
          { key: 'table', header: 'Table', cell: (a) => a.entityName ?? '', hideBelow: 'md' },
          { key: 'details', header: 'Details', cell: (a) => <span className="line-clamp-2">{a.details ?? ''}</span>, hideBelow: 'sm', className: 'wrap min-w-[220px]' },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="lines" />}
      />
      <p className="mt-2 px-1 text-ink">Choose a line to see what changed. Changes made on the web are logged at station 000.</p>
      {open && <AuditPanel row={open} onClose={() => setOpen(null)} />}
    </div>
  );
}

function where(a: AuditRow): string {
  if (!a.storeCode) return '';
  return a.stationNumber === null || a.stationNumber === undefined ? a.storeCode : `${a.storeCode} · ${String(a.stationNumber).padStart(3, '0')}`;
}

function show(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  return JSON.stringify(value, null, 1);
}

/** Before and after, side by side, with the changed values marked. */
function AuditPanel({ row, onClose }: { row: AuditRow; onClose(): void }) {
  const keys = [...new Set([...Object.keys(row.before ?? {}), ...Object.keys(row.after ?? {})])];
  const changed = (k: string) => show(row.before?.[k]) !== show(row.after?.[k]);
  const [onlyChanged, setOnlyChanged] = useState(true);
  const shown = keys.filter((k) => !onlyChanged || changed(k));
  return (
    <SidePanel open wide title={row.action} subtitle={formatUtc(row.atUtc)} onClose={onClose}>
      <dl className="card mb-4 grid grid-cols-2 gap-4 p-5 md:grid-cols-4">
        <Detail label="User">{row.userName}</Detail>
        <Detail label="Where">{where(row)}</Detail>
        <Detail label="Table">{row.entityName}</Detail>
        <Detail label="Record">{row.entityId}</Detail>
      </dl>
      {row.details && <p className="card mb-4 p-5 text-ink">{row.details}</p>}
      {keys.length === 0 ? (
        <p className="px-1 text-ink">This line has no values before and after.</p>
      ) : (
        <>
          <label className="mb-3 flex cursor-pointer items-center gap-2.5 px-1">
            <input type="checkbox" className="check" checked={onlyChanged} onChange={(e) => setOnlyChanged(e.target.checked)} />
            Changed values only
          </label>
          <div className="grid-card">
            <div className="grid-scroll">
              <table className="grid-table" aria-label="Before and after">
                <thead>
                  <tr>
                    <th scope="col">What</th>
                    <th scope="col">Before</th>
                    <th scope="col">After</th>
                  </tr>
                </thead>
                <tbody>
                  {shown.map((k) => (
                    <tr key={k} aria-selected={changed(k) && !onlyChanged ? true : undefined}>
                      <td className="font-semibold">{k}</td>
                      <td className={cx('whitespace-pre-wrap break-words', changed(k) && 'text-danger')}>{show(row.before?.[k])}</td>
                      <td className={cx('whitespace-pre-wrap break-words', changed(k) && 'text-ok')}>{show(row.after?.[k])}</td>
                    </tr>
                  ))}
                  {shown.length === 0 && (
                    <tr>
                      <td colSpan={3} className="text-muted">
                        Nothing changed.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}
    </SidePanel>
  );
}
