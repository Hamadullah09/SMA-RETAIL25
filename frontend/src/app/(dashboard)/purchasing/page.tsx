'use client';
import { formatCurrency, recordIdFrom } from '@/lib/utils';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { BrowseFormShell, Field, FormSection, NumberField, LiveBadge } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { useLiveGrid } from '@/lib/inventory-hub';
import { mastersApi } from '@/lib/masters-api';
import { posApi, PosApiError } from '@/lib/pos-api';
import {
  orderQuantityStrategies,
  type OrderQuantityStrategy,
  type PurchaseOrderDetail,
  type PurchaseOrderRow,
  type PurchaseOrderStatus,
  type SupplierRow,
} from '@/types/masters';
import type { Product } from '@/types';
import { describeError } from '@/lib/errors';
import { DomainStatusBadge } from '@/components/ui/status-badge';
import { ConfirmDialog, useConfirm } from '@/components/ui/confirm-dialog';

const selectClass =
  'pos-input';

const statusLabels: Record<PurchaseOrderStatus, string> = {
  Draft: 'Draft',
  Posted: 'Posted',
  PartiallyReceived: 'Partially received',
  Received: 'Received',
  Closed: 'Closed',
  Cancelled: 'Cancelled',
};

/** Purchase orders: generate, edit while Draft, post, receive with freight, or cancel (guide p.63–71). */
export default function PurchasingPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canWrite = auth.can('purchasing.write');
  const canPostOrder = auth.can('purchasing.post_order');
  const canPostShipment = auth.can('purchasing.post_shipment');

  const [statusFilter, setStatusFilter] = useState<PurchaseOrderStatus | ''>('');
  const [rows, setRows] = useState<PurchaseOrderRow[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [showGenerate, setShowGenerate] = useState(false);

  const load = useCallback(
    async (append: boolean, from: string | null) => {
      if (!locationId) return;
      setLoading(true);

      try {
        const page = await mastersApi.purchaseOrders.browse(locationId, {
          status: statusFilter || undefined,
          cursor: from ?? undefined,
          pageSize: 100,
        });

        setRows((current) => (append ? [...current, ...page.items] : page.items));
        setCursor(page.nextCursor);
        setHasMore(page.hasMore);
      } catch (error) {
        toast({ title: 'Could not load purchase orders', description: describeError(error), variant: 'destructive' });
      } finally {
        setLoading(false);
      }
    },
    [locationId, statusFilter],
  );

  useEffect(() => {
    void load(false, null);
  }, [load]);

  const { connected, hasEverConnected, changed } = useLiveGrid('purchase_order', locationId, setRows);

  const columns = useMemo<DataGridColumn<PurchaseOrderRow>[]>(
    () => [
      { key: 'poNumber', header: 'PO #', width: 90, numeric: true, render: (r) => r.poNumber, sortValue: (r) => r.poNumber },
      { key: 'supplier', header: 'Supplier', width: 220, render: (r) => r.supplierCompany, sortValue: (r) => r.supplierCompany },
      { key: 'status', header: 'Status', width: 160, render: (r) => <DomainStatusBadge status={r.status} /> },
      { key: 'lines', header: 'Lines', width: 70, numeric: true, render: (r) => r.lineCount },
      { key: 'total', header: 'Total', width: 110, numeric: true, render: (r) => currency(r.total), sortValue: (r) => r.total },
      { key: 'posted', header: 'Posted', width: 110, render: (r) => r.postedOn ?? '—' },
      { key: 'due', header: 'Due', width: 110, render: (r) => r.dueOn ?? '—' },
    ],
    [],
  );

  return (
    <BrowseFormShell
      title="Purchase Orders"
      toolbar={
        <>
          {/* One badge, one vocabulary — this screen used to invent its own wording, and said it in
              red from the moment the page opened. */}
          <LiveBadge connected={connected} hasEverConnected={hasEverConnected} />
          {canWrite ? (
            <button type="button" className="pos-button-primary" onClick={() => setShowGenerate(true)}>
              New Purchase Order
            </button>
          ) : null}
        </>
      }
      filters={
        <select aria-label="All statuses"
          className={selectClass}
          value={statusFilter}
          onChange={(event) => setStatusFilter(event.target.value as PurchaseOrderStatus | '')}
        >
          <option value="">All statuses</option>
          {Object.entries(statusLabels).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      }
      grid={
        <DataGrid
          gridId="purchase-orders"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          recentlyChanged={changed}
          onRowActivate={(row) => setSelectedId(row.id)}
          loading={loading}
          emptyMessage="No purchase orders match these filters."
        />
      }
      form={
        selectedId !== null && locationId ? (
          <PurchaseOrderPanel
            key={String(selectedId)}
            purchaseOrderId={selectedId}
            canWrite={canWrite}
            canPostOrder={canPostOrder}
            canPostShipment={canPostShipment}
            onClose={() => setSelectedId(null)}
            onChanged={() => void load(false, null)}
          />
        ) : showGenerate && locationId ? (
          <GeneratePanel
            locationId={locationId}
            onClose={() => setShowGenerate(false)}
            onGenerated={(id) => {
              setShowGenerate(false);
              setSelectedId(id);
              void load(false, null);
            }}
          />
        ) : null
      }
      status={
        <span className="flex items-center gap-3">
          <span>{rows.length} loaded{hasMore ? ' of more' : ''}</span>
          {hasMore ? (
            <button type="button" className="underline" onClick={() => void load(true, cursor)} disabled={loading}>
              Load more
            </button>
          ) : null}
        </span>
      }
    />
  );
}

// Was `currency: 'USD'` — US dollars on a page about what this shop owes its suppliers.
const currency = formatCurrency;

function GeneratePanel({
  locationId,
  onClose,
  onGenerated,
}: {
  locationId: number;
  onClose: () => void;
  onGenerated: (purchaseOrderId: number) => void;
}) {
  const [suppliers, setSuppliers] = useState<SupplierRow[]>([]);
  const [supplierId, setSupplierId] = useState<number | ''>('');
  const [strategy, setStrategy] = useState<OrderQuantityStrategy>('Blank');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void mastersApi.suppliers.browse(locationId, { pageSize: 200 }).then((page) => setSuppliers(page.items));
  }, [locationId]);

  const generate = async () => {
    if (!supplierId) {
      toast({ title: 'Choose a supplier first', variant: 'destructive' });
      return;
    }

    setBusy(true);
    try {
      const order = await mastersApi.purchaseOrders.generate(locationId, supplierId, strategy);
      toast({ title: `PO-${order.poNumber} created`, description: `${order.lines.length} line(s) from ${strategy}.` });
      onGenerated(order.id);
    } catch (error) {
      toast({ title: 'Could not generate the order', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">New purchase order</h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Close
        </button>
      </div>

      <FormSection
        title="Generate"
        hint="Blank creates an empty order for manual entry. Every other method fills it from the supplier's top-ranked products, compared against each one's own reorder settings."
        actions={
          <button type="button" className="underline" disabled={busy} onClick={() => void generate()}>
            Generate
          </button>
        }
      >
        <Field label="Supplier">
          <select aria-label="Choose a supplier…" className={selectClass + ' w-full'} value={supplierId} onChange={(event) => setSupplierId(recordIdFrom(event.target.value))}>
            <option value="">Choose a supplier…</option>
            {suppliers.map((s) => (
              <option key={String(s.id)} value={s.id}>
                {s.company}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Quantity method">
          <select
            className={selectClass + ' w-full'}
            value={strategy}
            onChange={(event) => setStrategy(event.target.value as OrderQuantityStrategy)}
          >
            {orderQuantityStrategies.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </select>
        </Field>
      </FormSection>
    </div>
  );
}

function PurchaseOrderPanel({
  purchaseOrderId,
  canWrite,
  canPostOrder,
  canPostShipment,
  onClose,
  onChanged,
}: {
  purchaseOrderId: number;
  canWrite: boolean;
  canPostOrder: boolean;
  canPostShipment: boolean;
  onClose: () => void;
  onChanged: () => void;
}) {
  const confirmer = useConfirm();
  const [order, setOrder] = useState<PurchaseOrderDetail | null>(null);
  const [busy, setBusy] = useState(false);

  const reload = useCallback(async () => {
    try {
      setOrder(await mastersApi.purchaseOrders.get(purchaseOrderId));
    } catch (error) {
      toast({ title: 'Could not open the order', description: describeError(error), variant: 'destructive' });
    }
  }, [purchaseOrderId]);

  useEffect(() => {
    void reload();
  }, [reload]);

  if (!order) {
    return <p className="text-body text-ink-muted">Loading…</p>;
  }

  const isDraft = order.status === 'Draft';
  const isReceivable = order.status === 'Posted' || order.status === 'PartiallyReceived';
  const canCancel = canWrite && !order.lines.some((l) => l.qtyReceived > 0) && (isDraft || order.status === 'Posted');

  const askCancel = () => {
    confirmer.ask(
      {
        subject: `PO-${order.poNumber} · ${order.supplierCompany}`,
        consequence:
          'The order is cancelled and the stock stops being expected. Only possible while nothing '
          + 'on it has been received.',
        verb: 'Cancel order',
      },
      cancel,
    );
  };

  const post = async () => {
    setBusy(true);
    try {
      await mastersApi.purchaseOrders.post(order.id);
      toast({ title: `PO-${order.poNumber} posted`, description: 'Stock is now on order with this supplier.' });
      await reload();
      onChanged();
    } catch (error) {
      toast({ title: 'Could not post the order', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const cancel = async () => {
    setBusy(true);
    try {
      await mastersApi.purchaseOrders.cancel(order.id);
      toast({ title: `PO-${order.poNumber} cancelled` });
      await reload();
      onChanged();
    } catch (error) {
      toast({ title: 'Could not cancel the order', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">
          PO-{order.poNumber} · {order.supplierCompany} · {statusLabels[order.status]}
        </h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Close
        </button>
      </div>

      <FormSection
        title="Lines"
        actions={
          isDraft && canPostOrder && order.lines.length > 0 ? (
            <button type="button" className="underline" disabled={busy} onClick={() => void post()}>
              Post order
            </button>
          ) : null
        }
      >
        <LinesTable order={order} canEdit={isDraft && canWrite} busy={busy} setBusy={setBusy} onChanged={reload} />
        <p className="text-right text-body font-medium">Total: {currency(order.total)}</p>
      </FormSection>

      {isDraft && canWrite ? (
        <AddLinePanel purchaseOrderId={order.id} locationId={order.locationId} onAdded={reload} />
      ) : null}

      {isReceivable && canPostShipment ? <ReceivePanel order={order} onReceived={() => { void reload(); onChanged(); }} /> : null}

      {canCancel ? (
        <div className="mb-6">
          <button
            type="button"
            className="pos-button text-negative"
            disabled={busy}
            onClick={askCancel}
          >
            Cancel order
          </button>
        </div>
      ) : null}

      <ConfirmDialog
        request={confirmer.request}
        open={confirmer.open}
        onOpenChange={confirmer.setOpen}
        onConfirm={confirmer.confirm}
        busy={confirmer.busy}
      />
    </div>
  );
}

function LinesTable({
  order,
  canEdit,
  busy,
  setBusy,
  onChanged,
}: {
  order: PurchaseOrderDetail;
  canEdit: boolean;
  busy: boolean;
  setBusy: (busy: boolean) => void;
  onChanged: () => Promise<void>;
}) {
  const remove = async (lineId: number) => {
    setBusy(true);
    try {
      await mastersApi.purchaseOrders.removeLine(lineId);
      await onChanged();
    } catch (error) {
      toast({ title: 'Could not remove the line', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  if (order.lines.length === 0) {
    return <p className="text-label text-ink-muted">No lines yet.</p>;
  }

  return (
    <table className="w-full text-label">
      <thead className="text-left text-ink-muted">
        <tr>
          <th className="pb-1">Item</th>
          <th className="pb-1 text-right">Ordered</th>
          <th className="pb-1 text-right">Received</th>
          <th className="pb-1 text-right">Cost</th>
          <th className="pb-1 text-right">Extended</th>
          {canEdit ? <th /> : null}
        </tr>
      </thead>
      <tbody>
        {order.lines.map((line) => (
          <tr key={String(line.id)} className="border-t border-subtle">
            <td className="py-1">
              {line.stockCode} — {line.productName}
            </td>
            <td className="py-1 text-right pos-amount">{line.orderQty}</td>
            <td className="py-1 text-right pos-amount">{line.qtyReceived}</td>
            <td className="py-1 text-right pos-amount">{currency(line.costEach)}</td>
            <td className="py-1 text-right pos-amount">{currency(line.orderCost)}</td>
            {canEdit ? (
              <td className="py-1 text-right">
                <button type="button" className="underline" disabled={busy} onClick={() => void remove(line.id)}>
                  Remove
                </button>
              </td>
            ) : null}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function AddLinePanel({
  purchaseOrderId,
  locationId,
  onAdded,
}: {
  purchaseOrderId: number;
  locationId: number;
  onAdded: () => Promise<void>;
}) {
  const [term, setTerm] = useState('');
  const [results, setResults] = useState<Product[]>([]);
  const [selected, setSelected] = useState<Product | null>(null);
  const [orderQty, setOrderQty] = useState(1);
  const [costEach, setCostEach] = useState(0);
  const [caseQty, setCaseQty] = useState(0);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (selected || term.trim().length < 2) {
      setResults([]);
      return;
    }

    const timer = window.setTimeout(() => {
      void posApi.searchProducts(term, locationId).then(setResults).catch(() => setResults([]));
    }, 200);

    return () => window.clearTimeout(timer);
  }, [term, selected, locationId]);

  const pick = (product: Product) => {
    setSelected(product);
    setCostEach(product.lastCost || product.avgCost || 0);
    setResults([]);
    setTerm(`${product.stockCode} — ${product.name}`);
  };

  const add = async () => {
    if (!selected || orderQty <= 0) {
      toast({ title: 'Choose an item and a quantity', variant: 'destructive' });
      return;
    }

    setBusy(true);
    try {
      await mastersApi.purchaseOrders.addLine(purchaseOrderId, {
        productId: selected.id,
        orderQty,
        costEach,
        caseQty,
      });
      await onAdded();
      setSelected(null);
      setTerm('');
      setOrderQty(1);
      setCostEach(0);
      setCaseQty(0);
    } catch (error) {
      toast({ title: 'Could not add the line', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <FormSection
      title="Add a line"
      actions={
        <button type="button" className="underline" disabled={busy} onClick={() => void add()}>
          Add
        </button>
      }
    >
      <Field label="Item" hint={results.length > 0 ? undefined : 'Type a stock code or name.'}>
        <input
          className="w-full pos-input"
          value={term}
          onChange={(event) => {
            setTerm(event.target.value);
            setSelected(null);
          }}
          aria-label="Search stock" placeholder="Stock code or name"
        />
        {results.length > 0 ? (
          <ul className="mt-1 max-h-40 overflow-y-auto rounded-sm border border-subtle">
            {results.map((product) => (
              <li key={String(product.id)}>
                <button
                  type="button"
                  className="block w-full px-2 py-1 text-left text-label hover:bg-panel-hover"
                  onClick={() => pick(product)}
                >
                  {product.stockCode} — {product.name}
                </button>
              </li>
            ))}
          </ul>
        ) : null}
      </Field>
      <NumberField label="Order qty" value={orderQty} onChange={setOrderQty} step="1" />
      <NumberField label="Cost each" value={costEach} onChange={setCostEach} />
      <NumberField label="Case qty" value={caseQty} onChange={setCaseQty} step="1" />
    </FormSection>
  );
}

function ReceivePanel({ order, onReceived }: { order: PurchaseOrderDetail; onReceived: () => void }) {
  const outstanding = order.lines.filter((l) => l.qtyReceived < l.orderQty);
  const [qtys, setQtys] = useState<Record<string, number>>({});
  const [freight, setFreight] = useState(0);
  const [busy, setBusy] = useState(false);

  if (outstanding.length === 0) {
    return null;
  }

  const receive = async () => {
    const lines = outstanding
      .map((l) => ({ lineId: l.id, qtyReceived: qtys[l.id] ?? 0 }))
      .filter((l) => l.qtyReceived > 0);

    if (lines.length === 0) {
      toast({ title: 'Enter a quantity received for at least one line', variant: 'destructive' });
      return;
    }

    setBusy(true);
    try {
      await mastersApi.purchaseOrders.receive(order.id, {
        receivedOn: new Date().toISOString().slice(0, 10),
        freightTotal: freight,
        lines,
      });
      toast({ variant: 'success', title: 'Receipt recorded', description: 'Stock and average cost are updated.' });
      setQtys({});
      setFreight(0);
      onReceived();
    } catch (error) {
      toast({ title: 'Could not record the receipt', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <FormSection
      title="Receive shipment"
      hint="Freight is split across the lines you receive here, by their share of this receipt's cost."
      actions={
        <button type="button" className="underline" disabled={busy} onClick={() => void receive()}>
          Record receipt
        </button>
      }
    >
      {outstanding.map((line) => (
        <NumberField
          key={String(line.id)}
          label={`${line.stockCode} (${line.orderQty - line.qtyReceived} remaining)`}
          value={qtys[line.id] ?? 0}
          onChange={(value) => setQtys((current) => ({ ...current, [line.id]: value }))}
          step="1"
        />
      ))}
      <NumberField label="Freight total" value={freight} onChange={setFreight} />
    </FormSection>
  );
}
