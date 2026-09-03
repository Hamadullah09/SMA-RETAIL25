'use client';

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { Archive, RotateCcw, Search, Undo2, type LucideIcon } from 'lucide-react';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { cn } from '@/lib/utils';
import type { DeletedEntityKind, DeletedRow } from '@/types/masters';
import { PageHeader as SharedPageHeader } from '@/components/shell/page-header';

const KINDS: Array<{ value: DeletedEntityKind | ''; label: string }> = [
  { value: '', label: 'Everything' },
  { value: 'Product', label: 'Items' },
  { value: 'Customer', label: 'Customers' },
  { value: 'Supplier', label: 'Suppliers' },
  { value: 'Department', label: 'Departments' },
  { value: 'Category', label: 'Categories' },
];

const thText = 'px-3 py-2 text-left text-label font-medium text-ink-muted';
const thNum = 'px-3 py-2 text-right text-label font-medium text-ink-muted';
const td = 'px-3 py-2 align-middle';

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

  const filtered = kind !== '' || search !== '';

  return (
    <div className="space-y-4 p-4 lg:p-6">
      <PageHeader
        title="Undelete"
        lede="Nothing in this system is destroyed when it is deleted — it is hidden, and everything that ever referred to it still resolves. This is where it comes back."
      >
        <button type="button" className="pos-button" disabled={loading} onClick={() => void load()}>
          <RotateCcw className="h-3.5 w-3.5" aria-hidden />
          Refresh
        </button>
      </PageHeader>

      <Panel title="Find a deleted record" icon={Search}>
        <div className="flex flex-wrap items-end gap-3 p-4">
          <label className="flex min-w-0 flex-col gap-1 text-label text-ink-muted">
            Kind of record
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
          </label>

          <label className="flex min-w-0 flex-col gap-1 text-label text-ink-muted">
            Name or code
            <input
              className="pos-input w-64"
              placeholder="Search by name or code"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </label>
        </div>
      </Panel>

      <Panel
        title="Deleted records"
        icon={Archive}
        action={loading ? 'Loading…' : `${rows.length} shown`}
      >
        {canRestore ? (
          <p className="border-b border-subtle px-4 py-2.5 text-body text-ink-muted">
            Restoring puts a record straight back into use — it reappears in every browse and starts
            being found at the till again. It can be deleted once more if that was not what you wanted.
          </p>
        ) : null}

        {loading && rows.length === 0 ? (
          <p className="px-4 py-12 text-center text-body text-ink-muted">Loading…</p>
        ) : rows.length === 0 ? (
          <EmptyState
            icon={Undo2}
            title={filtered ? 'Nothing deleted matches that' : 'Nothing has been deleted'}
            hint={
              filtered
                ? 'Widen the kind to “Everything” or clear the search box to see the whole list.'
                : 'Deleted items, customers, suppliers, departments and categories collect here, and every one of them can be put back.'
            }
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full border-collapse text-body">
              <thead className="border-b border-subtle bg-panel-sunken">
                <tr>
                  <th scope="col" className={thText}>Kind</th>
                  <th scope="col" className={thText}>Reference</th>
                  <th scope="col" className={thText}>Name</th>
                  <th scope="col" className={thText}>Deleted</th>
                  <th scope="col" className={thText}>By</th>
                  <th scope="col" className={thNum}>
                    <span className="sr-only">Action</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr
                    key={`${row.kind}-${row.id}`}
                    className="border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover"
                  >
                    <td className={td}>{row.kind}</td>
                    <td className={cn(td, 'pos-amount text-ink-muted')}>{row.reference || '—'}</td>
                    <td className={cn(td, 'font-medium text-ink')}>{row.name}</td>
                    <td className={cn(td, 'tabular-nums text-ink-muted')}>
                      {row.deletedAt ? new Date(row.deletedAt).toLocaleString() : '—'}
                    </td>
                    <td className={cn(td, 'text-ink-muted')}>{row.deletedByName ?? '—'}</td>
                    <td className={cn(td, 'text-right')}>
                      {canRestore ? (
                        <button
                          type="button"
                          className="pos-button"
                          onClick={() => void restore(row)}
                          title={`Put ${row.name} back in use`}
                        >
                          <Undo2 className="h-3.5 w-3.5" aria-hidden />
                          Restore
                        </button>
                      ) : (
                        <span className="text-label text-ink-faint">No permission</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>
    </div>
  );
}

/* ------------------------------------------------------------------ page furniture */

/**
 * Delegates to the shared header.
 *
 * This was copy-pasted verbatim into six admin screens, and had already drifted: year-end
 * aligned its actions to the bottom while the other five centred them. Kept behind the local
 * name so the call sites in this file do not change.
 */
function PageHeader({ title, lede, children }: { title: string; lede: string; children?: ReactNode }) {
  return <SharedPageHeader title={title} description={lede} actions={children} />;
}

function Panel({
  title,
  icon: Icon,
  action,
  children,
}: {
  title: string;
  icon?: LucideIcon;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="pos-panel overflow-hidden">
      <header className="pos-panel-header">
        <span className="pos-panel-title">
          {Icon ? <Icon /> : null}
          <span className="truncate">{title}</span>
        </span>
        {action ? <span className="pos-panel-header-action">{action}</span> : null}
      </header>
      {children}
    </section>
  );
}

function EmptyState({
  icon: Icon,
  title,
  hint,
}: {
  icon: LucideIcon;
  title: string;
  hint: string;
}) {
  return (
    <div className="flex flex-col items-center gap-1.5 px-4 py-12 text-center">
      <Icon className="mb-1 h-6 w-6 text-ink-faint" aria-hidden />
      <p className="text-body-lg font-medium text-ink">{title}</p>
      <p className="max-w-[52ch] text-body text-ink-muted">{hint}</p>
    </div>
  );
}
