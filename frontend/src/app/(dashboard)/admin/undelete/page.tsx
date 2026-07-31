'use client';

import { useCallback, useEffect, useState } from 'react';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { cn } from '@/lib/utils';
import type { DeletedEntityKind, DeletedRow } from '@/types/masters';

const KINDS: Array<{ value: DeletedEntityKind | ''; label: string }> = [
  { value: '', label: 'Everything' },
  { value: 'Product', label: 'Items' },
  { value: 'Customer', label: 'Customers' },
  { value: 'Supplier', label: 'Suppliers' },
  { value: 'Department', label: 'Departments' },
  { value: 'Category', label: 'Categories' },
];

/**
 * Undelete Items (guide p.24), widened to every soft-deleted record.
 *
 * One screen rather than a deleted tab on each browse: someone who has just deleted the wrong thing
 * does not always remember which screen they were on, and asking them to guess is the difference
 * between a five-second recovery and a support call.
 */
export default function UndeletePage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canRestore = auth.can('catalog.delete');

  const [kind, setKind] = useState<DeletedEntityKind | ''>('');
  const [search, setSearch] = useState('');
  const [rows, setRows] = useState<DeletedRow[]>([]);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    if (!locationId) return;

    setLoading(true);

    try {
      setRows(await mastersApi.deleted.list(locationId, kind || undefined, search || undefined));
    } catch (error) {
      toast({
        title: 'Could not load deleted records',
        description: error instanceof PosApiError ? error.problem.detail : 'Something went wrong.',
        variant: 'destructive',
      });
    } finally {
      setLoading(false);
    }
  }, [locationId, kind, search]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 200);
    return () => window.clearTimeout(timer);
  }, [load]);

  const restore = async (row: DeletedRow) => {
    try {
      await mastersApi.deleted.restore(row.kind, row.id);
      setRows((current) => current.filter((r) => r.id !== row.id));
      toast({ title: 'Restored', description: `${row.name} is back in use.` });
    } catch (error) {
      // The refusals here are the interesting ones: a stock code reused since the delete, a customer
      // who still owes money. Showing the server's own words tells the user what to do next.
      toast({
        title: 'Not restored',
        description: error instanceof PosApiError ? error.problem.detail : 'Something went wrong.',
        variant: 'destructive',
      });
    }
  };

  return (
    <div className="space-y-3">
      <h1 className="text-h3 font-semibold">Undelete items</h1>

      <p className="max-w-2xl text-body text-ink-muted">
        Nothing in this system is destroyed when it is deleted — it is hidden, and everything that ever referred to it
        still resolves. This is where it comes back.
      </p>

      <div className="flex flex-wrap items-center gap-2 text-body">
        <select
          className="pos-input"
          value={kind}
          onChange={(event) => setKind(event.target.value as DeletedEntityKind | '')}
        >
          {KINDS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>

        <input
          className="pos-input w-64"
          placeholder="Search by name or code"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
      </div>

      <div className="pos-panel">
        <div className="pos-panel-header">
          <span>{rows.length} deleted records</span>
        </div>

        {loading ? (
          <p className="px-3 py-8 text-center text-body text-ink-muted">Loading…</p>
        ) : rows.length === 0 ? (
          <p className="px-3 py-8 text-center text-body text-ink-muted">Nothing has been deleted.</p>
        ) : (
          <table className="w-full text-body">
            <thead className="text-caption uppercase tracking-wide text-ink-muted">
              <tr className="border-b border-subtle">
                <th className="px-2 py-1 text-left">Kind</th>
                <th className="px-2 py-1 text-left">Reference</th>
                <th className="px-2 py-1 text-left">Name</th>
                <th className="px-2 py-1 text-left">Deleted</th>
                <th className="px-2 py-1 text-left">By</th>
                <th className="px-2 py-1" />
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={`${row.kind}-${row.id}`} className="border-b border-subtle last:border-0">
                  <td className="px-2 py-1">{row.kind}</td>
                  <td className={cn('px-2 py-1', 'pos-amount')}>{row.reference || '—'}</td>
                  <td className="px-2 py-1">{row.name}</td>
                  <td className="px-2 py-1">{row.deletedAt ? new Date(row.deletedAt).toLocaleString() : '—'}</td>
                  <td className="px-2 py-1">{row.deletedByName ?? '—'}</td>
                  <td className="px-2 py-1 text-right">
                    {canRestore ? (
                      <button type="button" className="pos-button" onClick={() => void restore(row)}>
                        Restore
                      </button>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
