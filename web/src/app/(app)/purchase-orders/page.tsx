'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ClipboardList, PackageCheck, Plus, Send, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { ConflictDialog } from '@/components/masters';
import { BinListDialog } from '@/components/supply';
import { Button } from '@/components/ui/button';
import { Dialog, SidePanel } from '@/components/ui/dialog';
import { Input, Select } from '@/components/ui/field';
import { Chip, Detail, PageHeader, Pager, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { toBusinessDate } from '@/lib/dates';
import { formatCount, formatDate, formatLocalDateTime, formatMoney, formatQuantity, formatShortDateTime } from '@/lib/format';
import { parseNumber } from '@/lib/forms';
import { useDebounced } from '@/lib/hooks';
import { words } from '@/lib/labels';
import { useStore } from '@/lib/store';
import { PO_STATUS, receiveErrors, receiveForms, receiveRequest, type ReceiveForm } from '@/lib/supply';
import type { PurchaseOrderDetail, PurchaseOrderLine, PurchaseOrderRow } from '@/lib/types';

const PAGE_SIZE = 50;

export default function PurchaseOrdersPage() {
  const { storeId, currencySymbol: symbol } = useStore();
  const [status, setStatus] = useState('');
  const [text, setText] = useState('');
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<string | null>(null);
  const search = useDebounced(text.trim());
  useEffect(() => setPage(1), [storeId, status, search]);
  const query = useQuery({
    queryKey: ['purchase-orders', storeId, status, search, page],
    queryFn: ({ signal }) => api.purchaseOrders({ storeId, status, search, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  const rows = query.data?.items;

  return (
    <div>
      <PageHeader title="Purchase orders" aside={query.data && `${formatCount(query.data.total)} orders`} />
      <Toolbar>
        <SearchBox value={text} onChange={setText} placeholder="Order number, description or supplier" />
        <label className="sr-only" htmlFor="po-status">
          Status
        </label>
        <Select id="po-status" className="w-[200px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every status</option>
          {Object.entries(PO_STATUS).map(([key, s]) => (
            <option key={key} value={key}>
              {s.label}
            </option>
          ))}
        </Select>
      </Toolbar>
      <p className="mb-3 px-1 text-ink">Orders are made at the tills, from requisitions and from the reorder suggestions. Goods are received here or at the till.</p>
      <DataTable<PurchaseOrderRow>
        rows={rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(p) => p.id}
        onRowClick={(p) => setOpen(p.id)}
        caption="Purchase orders"
        empty={<EmptyState title="No purchase orders">{search ? 'Nothing matches the search.' : null}</EmptyState>}
        columns={[
          { key: 'n', header: 'Order', cell: (p) => <span className="font-semibold">{p.number}</span> },
          { key: 'status', header: 'Status', cell: (p) => <Chip tone={PO_STATUS[p.status]?.tone ?? 'plain'}>{PO_STATUS[p.status]?.label ?? words(p.status)}</Chip> },
          { key: 'where', header: 'Location', cell: (p) => `${p.storeCode} · ${p.locationCode}`, hideBelow: 'lg' },
          { key: 'supplier', header: 'Supplier', cell: (p) => p.supplier ?? '', className: 'wrap min-w-[140px]' },
          { key: 'made', header: 'Made', cell: (p) => formatShortDateTime(p.createdAt), hideBelow: 'md' },
          { key: 'expected', header: 'Expected', cell: (p) => formatDate(p.expectedOn), hideBelow: 'xl' },
          { key: 'received', header: 'Received', cell: (p) => formatShortDateTime(p.receivedAt), hideBelow: 'lg' },
          { key: 'lines', header: 'Lines', numeric: true, cell: (p) => formatCount(p.lines), hideBelow: 'sm' },
          { key: 'cost', header: 'Cost', numeric: true, cell: (p) => formatMoney(p.totalCost, symbol) },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="orders" />}
      />
      {open && <OrderPanel id={open} onClose={() => setOpen(null)} />}
    </div>
  );
}

function OrderPanel({ id, onClose }: { id: string; onClose(): void }) {
  const symbol = useStore().currencySymbol;
  const toast = useToast();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['purchase-order', id], queryFn: () => api.purchaseOrder(id), staleTime: 0 });
  const [receiving, setReceiving] = useState(false);
  const [putAway, setPutAway] = useState(false);
  const [conflict, setConflict] = useState(false);
  const po = query.data;

  const post = useMutation({
    mutationFn: (order: PurchaseOrderDetail) => api.postPurchaseOrder(order.id, { version: order.version }),
    onSuccess: (saved) => {
      queryClient.setQueryData(['purchase-order', id], saved);
      void queryClient.invalidateQueries({ queryKey: ['purchase-orders'] });
      toast.ok(`Order ${saved.number} is posted: the pieces are on order.`);
    },
    onError: (e) => (e instanceof ApiError && e.isConflict ? setConflict(true) : toast.error(e.message)),
  });

  return (
    <SidePanel
      open
      wide
      title={po ? `Purchase order ${po.number}` : 'Purchase order'}
      subtitle={po && `${po.storeCode} · ${po.locationCode}${po.supplier ? ' · ' + po.supplier : ''}`}
      onClose={onClose}
      footer={
        po && (
          <>
            {po.status === 'Draft' && (
              <Button icon={Send} shortcut="Ctrl+Enter" busy={post.isPending} onClick={() => post.mutate(po)}>
                Post order
              </Button>
            )}
            {po.status !== 'Received' && (
              <Button variant="sun" icon={PackageCheck} shortcut="F9" onClick={() => setReceiving(true)}>
                Receive
              </Button>
            )}
            {po.status === 'Received' && (
              <Button icon={ClipboardList} shortcut="Ctrl+L" onClick={() => setPutAway(true)}>
                Put-away list
              </Button>
            )}
          </>
        )
      }
    >
      {query.error ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : !po ? (
        <Skeleton className="h-64 rounded-card" />
      ) : (
        <div className="space-y-4">
          <dl className="card grid grid-cols-2 gap-4 p-5 md:grid-cols-4">
            <Detail label="Status">{PO_STATUS[po.status]?.label ?? words(po.status)}</Detail>
            <Detail label="Made">{formatLocalDateTime(po.createdAt)}</Detail>
            <Detail label="Expected">{formatDate(po.expectedOn)}</Detail>
            <Detail label="Received">{formatLocalDateTime(po.receivedAt)}</Detail>
            <Detail label="Made by">{po.createdBy}</Detail>
            <Detail label="Received by">{po.receivedBy}</Detail>
            <Detail label="Description" className="col-span-2">
              {po.description}
            </Detail>
          </dl>
          <DataTable<PurchaseOrderLine>
            rows={po.lines}
            rowKey={(l) => l.id}
            caption="Order lines"
            showTotals
            columns={[
              { key: 'plu', header: 'Stock code', cell: (l) => l.plu },
              { key: 'desc', header: 'Description', cell: (l) => l.description, className: 'wrap min-w-[150px]', hideBelow: 'sm' },
              { key: 'pieces', header: 'Ordered', numeric: true, cell: (l) => formatQuantity(l.pieces), total: formatQuantity(po.lines.reduce((s, l) => s + l.pieces, 0)) },
              { key: 'cost', header: 'Cost each', numeric: true, cell: (l) => formatMoney(l.costEach, symbol), hideBelow: 'md' },
              { key: 'value', header: 'Cost', numeric: true, cell: (l) => formatMoney(l.orderCost, symbol), total: formatMoney(po.totalCost, symbol) },
              { key: 'recv', header: 'Received', numeric: true, cell: (l) => formatQuantity(l.quantityReceived), hideBelow: 'sm' },
              {
                key: 'batches',
                header: 'Batches',
                cell: (l) => (l.batches.length ? l.batches.map((b) => `${b.batchNumber} (${formatQuantity(b.pieces)})`).join(', ') : l.tracksBatches ? <Chip tone="warn">In batches</Chip> : ''),
                className: 'wrap',
                hideBelow: 'lg',
              },
              { key: 'bin', header: 'Bin', cell: (l) => l.bin ?? '', hideBelow: 'xl' },
            ]}
          />
        </div>
      )}
      {receiving && po && (
        <ReceiveDialog
          order={po}
          onClose={() => setReceiving(false)}
          onReceived={(saved) => {
            queryClient.setQueryData(['purchase-order', id], saved);
            void queryClient.invalidateQueries({ queryKey: ['purchase-orders'] });
            setReceiving(false);
            setPutAway(true);
          }}
          onConflict={() => {
            setReceiving(false);
            setConflict(true);
          }}
        />
      )}
      {putAway && <PutAway orderId={id} onClose={() => setPutAway(false)} />}
      <ConflictDialog
        open={conflict}
        what="order"
        onReload={() => {
          setConflict(false);
          void query.refetch();
        }}
        onClose={() => setConflict(false)}
      />
    </SidePanel>
  );
}

function PutAway({ orderId, onClose }: { orderId: string; onClose(): void }) {
  const query = useQuery({ queryKey: ['put-away', orderId], queryFn: () => api.putAway(orderId), staleTime: 0 });
  return <BinListDialog list={query.data} loading={query.isLoading} error={query.error} onClose={onClose} />;
}

/**
 * Receiving on the web: what arrived of each line, at what cost; for an item kept in batches, each batch's number, pieces,
 * expiry (offered from the item's shelf life) and the day it was made.
 */
function ReceiveDialog({
  order,
  onClose,
  onReceived,
  onConflict,
}: {
  order: PurchaseOrderDetail;
  onClose(): void;
  onReceived(saved: PurchaseOrderDetail): void;
  onConflict(): void;
}) {
  const toast = useToast();
  const today = toBusinessDate(new Date());
  const [forms, setForms] = useState<ReceiveForm[]>(() => receiveForms(order.lines, today));
  const [shipping, setShipping] = useState('0');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const receive = useMutation({
    mutationFn: () => api.receivePurchaseOrder(order.id, receiveRequest(order.version, parseNumber(shipping) ?? 0, order.lines, forms)),
    onSuccess: (saved) => {
      toast.ok(`Order ${saved.number} is received: stock and costs are updated.`);
      onReceived(saved);
    },
    onError: (e) => e instanceof ApiError && e.isConflict && onConflict(),
  });

  function update(lineId: string, change: (f: ReceiveForm) => ReceiveForm) {
    setForms((list) => list.map((f) => (f.lineId === lineId ? change(f) : f)));
    setErrors((e) => ({ ...e, [lineId]: '' }));
  }

  function submit() {
    const found = receiveErrors(order.lines, forms);
    const ship = parseNumber(shipping);
    if (ship === null || ship < 0) found.shipping = 'Enter the shipping cost (0 for none).';
    setErrors(found);
    if (Object.values(found).some(Boolean)) return;
    receive.mutate();
  }

  return (
    <Dialog
      open
      width="xl"
      title={`Receive order ${order.number}`}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button type="submit" variant="sun" icon={PackageCheck} shortcut="Enter" busy={receive.isPending}>
            Post the receipt
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <p className="mb-3 text-muted">Enter what arrived. Items kept in batches need the batch number and expiry of each batch, as printed on the goods.</p>
      <div className="space-y-3">
        {order.lines.map((line) => {
          const form = forms.find((f) => f.lineId === line.id)!;
          return (
            <section key={line.id} className="card p-4" aria-label={`Line ${line.plu}`}>
              <div className="flex flex-wrap items-end gap-3">
                <div className="min-w-[160px] flex-1">
                  <p className="font-semibold">{line.plu}</p>
                  <p className="text-muted">
                    {line.description} · {formatQuantity(line.pieces)} ordered
                  </p>
                </div>
                <label className="flex flex-col gap-1">
                  <span className="text-note text-muted">Pieces received</span>
                  <Input
                    aria-label={`Pieces received of ${line.plu}`}
                    inputMode="decimal"
                    className="w-[130px]"
                    value={form.pieces}
                    onChange={(e) => update(line.id, (f) => ({ ...f, pieces: e.target.value, batches: f.batches.length === 1 ? [{ ...f.batches[0], pieces: e.target.value }] : f.batches }))}
                  />
                </label>
                <label className="flex flex-col gap-1">
                  <span className="text-note text-muted">Cost each</span>
                  <Input aria-label={`Cost each of ${line.plu}`} inputMode="decimal" className="w-[130px]" value={form.costEach} onChange={(e) => update(line.id, (f) => ({ ...f, costEach: e.target.value }))} />
                </label>
              </div>
              {line.tracksBatches && (
                <div className="mt-3 space-y-2">
                  {form.batches.map((b, i) => (
                    <div key={i} className="flex flex-wrap items-end gap-2.5">
                      <label className="flex flex-col gap-1">
                        <span className="text-note text-muted">Batch number</span>
                        <Input
                          aria-label={`Batch number ${i + 1} of ${line.plu}`}
                          className="w-[160px]"
                          maxLength={30}
                          value={b.batchNumber}
                          onChange={(e) => update(line.id, (f) => ({ ...f, batches: f.batches.map((x, j) => (j === i ? { ...x, batchNumber: e.target.value } : x)) }))}
                        />
                      </label>
                      <label className="flex flex-col gap-1">
                        <span className="text-note text-muted">Pieces</span>
                        <Input
                          aria-label={`Pieces of batch ${i + 1} of ${line.plu}`}
                          inputMode="decimal"
                          className="w-[100px]"
                          value={b.pieces}
                          onChange={(e) => update(line.id, (f) => ({ ...f, batches: f.batches.map((x, j) => (j === i ? { ...x, pieces: e.target.value } : x)) }))}
                        />
                      </label>
                      <label className="flex flex-col gap-1">
                        <span className="text-note text-muted">Expires</span>
                        <input
                          type="date"
                          aria-label={`Expiry of batch ${i + 1} of ${line.plu}`}
                          className="input w-[170px]"
                          value={b.expiresOn}
                          onChange={(e) => update(line.id, (f) => ({ ...f, batches: f.batches.map((x, j) => (j === i ? { ...x, expiresOn: e.target.value } : x)) }))}
                        />
                      </label>
                      <label className="flex flex-col gap-1">
                        <span className="text-note text-muted">Made on</span>
                        <input
                          type="date"
                          aria-label={`Made on of batch ${i + 1} of ${line.plu}`}
                          className="input w-[170px]"
                          value={b.madeOn}
                          onChange={(e) => update(line.id, (f) => ({ ...f, batches: f.batches.map((x, j) => (j === i ? { ...x, madeOn: e.target.value } : x)) }))}
                        />
                      </label>
                      {form.batches.length > 1 && (
                        <button
                          type="button"
                          className="btn btn-icon h-11 min-h-0 w-11"
                          aria-label={`Remove batch ${i + 1} of ${line.plu}`}
                          onClick={() => update(line.id, (f) => ({ ...f, batches: f.batches.filter((_, j) => j !== i) }))}
                        >
                          <Trash2 className="h-5 w-5" />
                        </button>
                      )}
                    </div>
                  ))}
                  <Button
                    icon={Plus}
                    onClick={() => update(line.id, (f) => ({ ...f, batches: [...f.batches, { batchNumber: '', pieces: '0', expiresOn: f.batches[0]?.expiresOn ?? '', madeOn: '' }] }))}
                  >
                    Another batch
                  </Button>
                  {line.shelfLifeDays > 0 && <p className="text-note text-muted">Keeps {line.shelfLifeDays} days: the expiry is offered from the day made (or today).</p>}
                </div>
              )}
              {errors[line.id] && (
                <p className="mt-2 text-note font-semibold text-danger" role="alert">
                  {errors[line.id]}
                </p>
              )}
            </section>
          );
        })}
        <label className="flex flex-wrap items-center gap-3">
          <span className="text-muted">Shipping to spread over the items (by value)</span>
          <Input aria-label="Shipping cost" inputMode="decimal" className="w-[130px]" value={shipping} onChange={(e) => setShipping(e.target.value)} />
        </label>
        {errors.shipping && (
          <p className="text-note font-semibold text-danger" role="alert">
            {errors.shipping}
          </p>
        )}
      </div>
      <FormError error={receive.error && !(receive.error instanceof ApiError && receive.error.isConflict) ? receive.error : null} />
    </Dialog>
  );
}
