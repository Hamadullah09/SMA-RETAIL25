'use client';

import { useMutation, useQuery } from '@tanstack/react-query';
import { Calculator, Plus } from 'lucide-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { OrderDialog } from '@/components/production';
import { Button } from '@/components/ui/button';
import { Chip, PageHeader, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { formatCount, formatDate, formatQuantity } from '@/lib/format';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { boardColumns, ORDER_STATUS } from '@/lib/production';
import { productionApi } from '@/lib/production-api';
import type { ProductionOrderRow } from '@/lib/production-types';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';

export default function ProductionPage() {
  return (
    <Suspense>
      <Board />
    </Suspense>
  );
}

/** The production orders as a board: a column per status, the most urgent first in each. */
function Board() {
  const { can } = useSession();
  const { storeId } = useStore();
  const toast = useToast();
  const router = useRouter();
  const params = useSearchParams();
  const [search, setSearch] = useState('');
  const text = useDebounced(search.trim());
  const [creating, setCreating] = useState(false);
  const board = useQuery({ queryKey: ['production-board', storeId, text], queryFn: () => productionApi.board({ storeId, search: text }) });
  const columns = boardColumns(board.data);
  const open = columns.filter((c) => c.status === 'Released' || c.status === 'InProgress').reduce((s, c) => s + c.total, 0);

  useEffect(() => {
    if (params.get('new') === '1' && can(P.productionPlan)) {
      setCreating(true);
      router.replace('/production/');
    }
  }, [params, can, router]);

  const post = useMutation({
    mutationFn: () => productionApi.postStoreCosts({ storeId }),
    onSuccess: (r) => (r.problems.length === 0 ? toast.ok(`The costs of ${formatCount(r.orders)} order(s) are posted to the ledger.`) : toast.error(r.problems.join(' '))),
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <div>
      <PageHeader title="Production orders" aside={board.data && `${formatCount(open)} on the floor`} />
      <Toolbar>
        <SearchBox value={search} onChange={setSearch} placeholder="Order number or product" label="Find an order" />
        {can(P.productionPlan) && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setCreating(true)}>
            New order
          </Button>
        )}
        {can(P.productionCosting) && (
          <Button icon={Calculator} onClick={() => post.mutate()} busy={post.isPending}>
            Post costs to the ledger
          </Button>
        )}
      </Toolbar>
      {board.error ? (
        <div className="card">
          <ErrorState error={board.error} onRetry={() => void board.refetch()} />
        </div>
      ) : !board.data ? (
        <Skeleton className="h-[50vh] rounded-card" />
      ) : (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-6" aria-label="Orders by status">
          {columns.map((column) => (
            <section key={column.status} className="card min-w-0 p-3" aria-label={ORDER_STATUS[column.status].label}>
              <h2 className="mb-2 flex items-center justify-between px-1 font-heading text-section text-ink">
                {ORDER_STATUS[column.status].label}
                <span className="text-note text-muted">{formatCount(column.total)}</span>
              </h2>
              {column.orders.length === 0 ? (
                <EmptyState title="None" className="py-4" />
              ) : (
                <ul className="space-y-2">
                  {column.orders.map((o) => (
                    <OrderCard key={o.id} order={o} />
                  ))}
                </ul>
              )}
              {column.total > column.orders.length && <p className="mt-2 px-1 text-note text-muted">and {formatCount(column.total - column.orders.length)} more</p>}
            </section>
          ))}
        </div>
      )}
      {creating && (
        <OrderDialog
          onClose={() => setCreating(false)}
          onSaved={(o) => {
            setCreating(false);
            router.push(`/production/view/?id=${o.id}`);
          }}
        />
      )}
    </div>
  );
}

function OrderCard({ order }: { order: ProductionOrderRow }) {
  const late = order.plannedEnd && order.status !== 'Completed' && order.status !== 'Closed' && order.status !== 'Cancelled' && order.plannedEnd < new Date().toISOString().slice(0, 10);
  return (
    <li>
      <Link href={`/production/view/?id=${order.id}`} className="block rounded-control border border-line bg-page px-3 py-2 hover:bg-stone-light" aria-label={`Order ${order.number} ${order.productName}`}>
        <div className="flex items-center justify-between gap-2">
          <span className="font-semibold text-ink">{order.number}</span>
          <span className="flex gap-1">
            {order.priority <= 2 && <Chip tone="warn">Priority {order.priority}</Chip>}
            {order.madeToOrder && <Chip>To order</Chip>}
            {late && <Chip tone="danger">Late</Chip>}
          </span>
        </div>
        <p className="truncate text-ink">
          {order.productCode} {order.productName}
        </p>
        <p className="text-note text-muted">
          {formatQuantity(order.received)} of {formatQuantity(order.quantity)} made
          {order.plannedStart ? ` · start ${formatDate(order.plannedStart)}` : ''}
          {order.storeCode ? ` · ${order.storeCode}` : ''}
        </p>
      </Link>
    </li>
  );
}
