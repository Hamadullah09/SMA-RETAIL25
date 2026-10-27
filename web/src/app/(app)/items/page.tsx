'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Undo2 } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { Button, LinkButton } from '@/components/ui/button';
import { ConfirmDialog } from '@/components/ui/dialog';
import { Select } from '@/components/ui/field';
import { Chip, PageHeader, Pager, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { cx } from '@/lib/cx';
import { formatCount, formatMoney, formatQuantity } from '@/lib/format';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { ItemRow } from '@/lib/types';

const PAGE_SIZE = 50;

export default function InventoryPage() {
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { can } = useSession();
  const { storeId, currencySymbol: symbol } = useStore();
  const [text, setText] = useState('');
  const [departmentId, setDepartmentId] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [deleted, setDeleted] = useState(false);
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<ItemRow | null>(null);
  const [restoring, setRestoring] = useState<ItemRow | null>(null);
  const search = useDebounced(text.trim());
  const edit = can(P.inventoryEdit);

  const filters = { search, departmentId, categoryId, deleted: deleted || undefined, storeId };
  useEffect(() => setPage(1), [search, departmentId, categoryId, deleted, storeId]);
  useEffect(() => setCategoryId(''), [departmentId]);

  const query = useQuery({
    queryKey: ['items', filters, page],
    queryFn: ({ signal }) => api.items({ ...filters, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  const departments = useQuery({ queryKey: ['departments'], queryFn: () => api.departments(), staleTime: 5 * 60_000 });
  const categories = useQuery({ queryKey: ['categories', departmentId], queryFn: () => api.categories(departmentId || null), staleTime: 5 * 60_000 });

  const undelete = useMutation({
    mutationFn: (id: string) => api.undeleteItem(id),
    onSuccess: () => {
      toast.ok(`${restoring?.name ?? 'The item'} is back.`);
      setRestoring(null);
      void queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  function open(row: ItemRow) {
    if (deleted) {
      if (edit) setRestoring(row);
      return;
    }
    if (edit) router.push(`/items/edit/?id=${encodeURIComponent(row.id)}`);
    else setSelected(row);
  }

  return (
    <div>
      <PageHeader
        title="Inventory"
        aside={query.data && `${formatCount(query.data.items.length)} shown · ${formatCount(query.data.total)} ${deleted ? 'deleted items' : 'items'}`}
      />
      <Toolbar>
        <SearchBox value={text} onChange={setText} placeholder="Stock code or description" />
        <label className="sr-only" htmlFor="item-dept">
          Department
        </label>
        <Select id="item-dept" className="w-[200px]" value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
          <option value="">Every department</option>
          {(departments.data ?? []).map((d) => (
            <option key={d.id} value={d.id}>
              {d.name}
            </option>
          ))}
        </Select>
        <label className="sr-only" htmlFor="item-cat">
          Category
        </label>
        <Select id="item-cat" className="w-[200px]" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
          <option value="">Every category</option>
          {(categories.data ?? []).map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </Select>
        <label className="flex cursor-pointer items-center gap-2.5 px-1">
          <input type="checkbox" className="check" checked={deleted} onChange={(e) => setDeleted(e.target.checked)} />
          Show deleted
        </label>
      </Toolbar>
      {edit && (
        <Toolbar>
          <LinkButton href="/items/edit/" variant="sun" icon={Plus} shortcut="Ctrl+N">
            New item
          </LinkButton>
          <Button icon={Pencil} shortcut="Ctrl+O" disabled={!selected || deleted} onClick={() => selected && open(selected)}>
            Edit
          </Button>
          <Button icon={Undo2} shortcut="Ctrl+U" disabled={!selected || !deleted} onClick={() => selected && setRestoring(selected)}>
            Undelete
          </Button>
        </Toolbar>
      )}

      <DataTable<ItemRow>
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(i) => i.id}
        selectedKey={selected?.id}
        onRowClick={(row) => {
          if (selected?.id === row.id || !edit) open(row);
          else setSelected(row);
        }}
        caption="Items"
        empty={
          <EmptyState title={deleted ? 'No deleted items' : 'No items found'}>
            {search ? 'Nothing matches the search. Try fewer letters.' : edit && !deleted ? 'Add one with New item.' : null}
          </EmptyState>
        }
        columns={[
          { key: 'plu', header: 'Stock code', cell: (i) => <span className="font-semibold">{i.plu}</span> },
          { key: 'name', header: 'Description', cell: (i) => i.name, className: 'wrap min-w-[200px]' },
          { key: 'dept', header: 'Department', cell: (i) => i.department ?? '', hideBelow: 'md' },
          { key: 'cat', header: 'Category', cell: (i) => i.category ?? '', hideBelow: 'xl' },
          { key: 'price', header: 'Price', numeric: true, cell: (i) => formatMoney(i.price1, symbol) },
          { key: 'cost', header: 'Average cost', numeric: true, cell: (i) => formatMoney(i.averageCost, symbol), hideBelow: 'lg' },
          {
            key: 'onhand',
            header: storeId ? 'On hand' : 'On hand, all stores',
            numeric: true,
            cell: (i) => <span className={cx(i.onHand < 0 && 'font-semibold text-danger')}>{formatQuantity(i.onHand)}</span>,
            hideBelow: 'sm',
          },
          { key: 'active', header: 'Active', cell: (i) => (i.isActive ? '' : <Chip>Inactive</Chip>), hideBelow: 'md' },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="items" />}
      />
      <p className="mt-2 px-1 text-ink">{edit ? (deleted ? 'Choose a deleted item to bring it back.' : 'Choose an item, then Edit (or choose it again).') : 'Your role can look at items but not change them.'}</p>

      <ConfirmDialog
        open={!!restoring}
        title={`Bring back ${restoring?.name ?? 'this item'}?`}
        message="It returns to the item list and goes down to the tills with the next download."
        confirmLabel="Undelete"
        cancelLabel="Cancel"
        busy={undelete.isPending}
        onConfirm={() => restoring && undelete.mutate(restoring.id)}
        onClose={() => setRestoring(null)}
      />
    </div>
  );
}
