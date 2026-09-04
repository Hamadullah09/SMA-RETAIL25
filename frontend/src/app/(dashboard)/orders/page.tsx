'use client';
import { formatCurrency, recordIdFrom } from '@/lib/utils';
import { Button } from '@/components/ui/button';

import { useCallback, useEffect, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { BrowseFormShell, Field, FormSection, NumberField } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { posApi, PosApiError } from '@/lib/pos-api';
import type {
  CustomerOrder,
  Layaway,
  PriceQuote,
  TenderSettings,
} from '@/types/masters';
import type { Product } from '@/types';
import { describeError } from '@/lib/errors';
import { DomainStatusBadge } from '@/components/ui/status-badge';
import { ConfirmDialog, useConfirm } from '@/components/ui/confirm-dialog';

type Tab = 'customerOrders' | 'layaways' | 'priceQuotes';

/** Customer orders / back orders, layaways and price quotes (guide p.9, p.16). */
export default function OrdersPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId ?? 0;
  const [tab, setTab] = useState<Tab>('customerOrders');
  const [tenders, setTenders] = useState<TenderSettings[]>([]);

  useEffect(() => {
    if (!locationId) return;
    void mastersApi.settings.get(locationId).then((snapshot) => setTenders(snapshot.tenders));
  }, [locationId]);

  return (
    <div className="flex h-below-header min-h-0 flex-col gap-2">
      <div className="flex items-center justify-between">
        <h1 className="text-h3 font-semibold">Orders &amp; Layaways</h1>
        <div className="flex gap-2">
          <button type="button" className={tabClass(tab === 'customerOrders')} onClick={() => setTab('customerOrders')}>
            Customer orders
          </button>
          <button type="button" className={tabClass(tab === 'layaways')} onClick={() => setTab('layaways')}>
            Layaways
          </button>
          <button type="button" className={tabClass(tab === 'priceQuotes')} onClick={() => setTab('priceQuotes')}>
            Price quotes
          </button>
        </div>
      </div>

      <div className="min-h-0 flex-1">
        {tab === 'customerOrders' ? <CustomerOrdersTab locationId={locationId} /> : null}
        {tab === 'layaways' ? <LayawaysTab locationId={locationId} tenders={tenders} /> : null}
        {tab === 'priceQuotes' ? <PriceQuotesTab locationId={locationId} /> : null}
      </div>
    </div>
  );
}

function tabClass(active: boolean): string {
  return active ? 'pos-button-primary' : 'pos-button';
}

// Was `currency: 'USD'`, which printed US dollars on a page a shop reads its own takings from.
const currency = formatCurrency;

/** Shared customer typeahead used by all three creation forms. */
function CustomerPicker({
  locationId,
  onPick,
}: {
  locationId: number;
  onPick: (customer: { id: number; customerNumber: number; fullName: string } | null) => void;
}) {
  const [term, setTerm] = useState('');
  const [results, setResults] = useState<{ id: number; customerNumber: number; fullName: string }[]>([]);
  const [picked, setPicked] = useState(false);
  const [searched, setSearched] = useState(false);

  useEffect(() => {
    if (picked || term.trim().length < 2) {
      setResults([]);
      setSearched(false);
      return;
    }

    const timer = window.setTimeout(() => {
      void posApi
        .searchCustomers(term, locationId)
        .then((rows) => {
          setResults(rows);
          setSearched(true);
        })
        .catch(() => {
          setResults([]);
          setSearched(true);
        });
    }, 200);

    return () => window.clearTimeout(timer);
  }, [term, locationId, picked]);

  return (
    <Field label="Customer">
      <input
        className="w-full pos-input"
        value={term}
        onChange={(event) => {
          setTerm(event.target.value);

          // Typing after a pick abandons it. Holding the old id while the box shows a different
          // name is how an order gets raised against the wrong account.
          if (picked) {
            setPicked(false);
            onPick(null);
          }
        }}
        aria-label="Search customers" placeholder="Name or customer number"
      />

      {/*
        The empty result has to say so. A name typed into a box that quietly matched nothing looks
        exactly like a name that was accepted, and the only sign otherwise is a refusal at the end
        that says to choose a customer — next to a box with a customer's name already in it.
      */}
      {!picked && searched && results.length === 0 ? (
        <p className="mt-1 text-caption text-warning">
          No customer matches “{term.trim()}”. Add them under Customers first — an order has to be
          raised against an account that exists.
        </p>
      ) : null}
      {results.length > 0 ? (
        <ul className="mt-1 max-h-40 overflow-y-auto rounded-sm border border-subtle">
          {results.map((r) => (
            <li key={String(r.id)}>
              <button
                type="button"
                className="block w-full px-2 py-1 text-left text-label hover:bg-panel-hover"
                onClick={() => {
                  onPick(r);
                  setPicked(true);
                  setResults([]);
                  setTerm(`#${r.customerNumber} — ${r.fullName}`);
                }}
              >
                #{r.customerNumber} — {r.fullName}
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </Field>
  );
}

interface DraftLine {
  productId: number;
  stockCode: string;
  productName: string;
  quantity: number;
  unitPrice: number;
}

/** Shared "add lines before submitting" builder used by all three creation forms. */
function LineBuilder({
  locationId,
  lines,
  onChange,
}: {
  locationId: number;
  lines: DraftLine[];
  onChange: (lines: DraftLine[]) => void;
}) {
  const [term, setTerm] = useState('');
  const [results, setResults] = useState<Product[]>([]);
  const [selected, setSelected] = useState<Product | null>(null);
  const [quantity, setQuantity] = useState(1);
  const [unitPrice, setUnitPrice] = useState(0);

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
    setUnitPrice(product.regularPrice ?? 0);
    setResults([]);
    setTerm(`${product.stockCode} — ${product.name}`);
  };

  const add = () => {
    if (!selected || quantity <= 0) {
      toast({ title: 'Choose an item and a quantity', variant: 'destructive' });
      return;
    }

    onChange([
      ...lines,
      { productId: selected.id, stockCode: selected.stockCode, productName: selected.name, quantity, unitPrice },
    ]);
    setSelected(null);
    setTerm('');
    setQuantity(1);
    setUnitPrice(0);
  };

  return (
    <div>
      <Field label="Item">
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
      <NumberField label="Quantity" value={quantity} onChange={setQuantity} step="1" />
      <NumberField label="Unit price" value={unitPrice} onChange={setUnitPrice} />
      <button type="button" className="underline text-body" onClick={add}>
        Add line
      </button>

      {lines.length > 0 ? (
        <table className="pos-table mt-2">
          <thead>
            <tr>
              <th>Item</th>
              <th data-numeric>Qty</th>
              <th data-numeric>Price</th>
              <th><span className="sr-only">Remove</span></th>
            </tr>
          </thead>
          <tbody>
            {lines.map((line, index) => (
              <tr key={index} className="border-t border-subtle">
                <td className="py-1">{line.stockCode} — {line.productName}</td>
                <td className="py-1 text-right pos-amount">{line.quantity}</td>
                <td className="py-1 text-right pos-amount">{currency(line.unitPrice)}</td>
                <td className="py-1 text-right">
                  <Button variant="ghost" onClick={() => onChange(lines.filter((_, i) => i !== index))}>Remove</Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Customer orders
// ---------------------------------------------------------------------------

function CustomerOrdersTab({ locationId }: { locationId: number }) {
  const auth = useAuth();
  const canWrite = auth.can('customer.write');
  const [rows, setRows] = useState<CustomerOrder[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [showNew, setShowNew] = useState(false);

  const load = useCallback(async () => {
    if (!locationId) return;
    try {
      const page = await mastersApi.customerOrders.browse(locationId, { pageSize: 100 });
      setRows(page.items);
    } catch (error) {
      toast({ title: 'Could not load customer orders', description: describeError(error), variant: 'destructive' });
    }
  }, [locationId]);

  useEffect(() => {
    void load();
  }, [load]);

  const columns: DataGridColumn<CustomerOrder>[] = [
    { key: 'orderNumber', header: 'Order #', width: 90, numeric: true, render: (r) => r.orderNumber },
    { key: 'customer', header: 'Customer', width: 200, render: (r) => r.customerName },
    { key: 'status', header: 'Status', width: 150, render: (r) => <DomainStatusBadge status={r.status} /> },
    { key: 'lines', header: 'Lines', width: 70, numeric: true, render: (r) => r.lines.length },
    { key: 'orderedOn', header: 'Ordered', width: 110, render: (r) => r.orderedOn },
  ];

  return (
    <BrowseFormShell
      title="Orders & Layaways"
      toolbar={canWrite ? <button type="button" className="pos-button-primary" onClick={() => setShowNew(true)}>New order</button> : null}
      grid={
        <DataGrid
          gridId="customer-orders"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          onRowActivate={(row) => setSelectedId(row.id)}
          emptyMessage="No customer orders yet."
        />
      }
      form={
        selectedId ? (
          <CustomerOrderPanel key={String(selectedId)} id={selectedId} canWrite={canWrite} onClose={() => setSelectedId(null)} onChanged={load} />
        ) : showNew ? (
          <NewCustomerOrderPanel
            locationId={locationId}
            onClose={() => setShowNew(false)}
            onCreated={(id) => {
              setShowNew(false);
              setSelectedId(id);
              void load();
            }}
          />
        ) : null
      }
    />
  );
}

function NewCustomerOrderPanel({
  locationId,
  onClose,
  onCreated,
}: {
  locationId: number;
  onClose: () => void;
  onCreated: (id: number) => void;
}) {
  const [customerId, setCustomerId] = useState<number | null>(null);
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);

  const create = async () => {
    if (!customerId || lines.length === 0) {
      toast({ title: !customerId ? 'Choose a customer from the list' : 'Add at least one line', description: !customerId ? 'Typing a name is not enough — pick the account from the suggestions.' : 'Choose an item, set the quantity, then press Add line.', variant: 'destructive' });
      return;
    }

    setBusy(true);
    try {
      const order = await mastersApi.customerOrders.create({
        customerId,
        locationId,
        lines: lines.map((l) => ({ productId: l.productId, quantity: l.quantity, unitPrice: l.unitPrice })),
        notes: notes || undefined,
      });
      toast({ title: `Order #${order.orderNumber} created` });
      onCreated(order.id);
    } catch (error) {
      toast({ title: 'Could not create the order', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">New customer order</h2>
        <button type="button" className="pos-button" onClick={onClose}>Close</button>
      </div>
      <FormSection title="Customer">
        <CustomerPicker locationId={locationId} onPick={(c) => setCustomerId(c?.id ?? null)} />
      </FormSection>
      <FormSection title="Lines" hint="Reserved against stock the moment the order is placed.">
        <LineBuilder locationId={locationId} lines={lines} onChange={setLines} />
      </FormSection>
      <FormSection title="Notes" actions={<Button variant="default" loading={busy} onClick={() => void create()}>Create order</Button>}>
        <Field label="Notes (optional)">
          <input
            className="w-full pos-input"
            value={notes}
            onChange={(event) => setNotes(event.target.value)}
          />
        </Field>
      </FormSection>
    </div>
  );
}

function CustomerOrderPanel({
  id,
  canWrite,
  onClose,
  onChanged,
}: {
  id: number;
  canWrite: boolean;
  onClose: () => void;
  onChanged: () => Promise<void>;
}) {
  const confirmer = useConfirm();
  const [order, setOrder] = useState<CustomerOrder | null>(null);
  const [busy, setBusy] = useState(false);

  const reload = useCallback(async () => {
    try {
      setOrder(await mastersApi.customerOrders.get(id));
    } catch (error) {
      toast({ title: 'Could not open the order', description: describeError(error), variant: 'destructive' });
    }
  }, [id]);

  useEffect(() => {
    void reload();
  }, [reload]);

  if (!order) return <p className="text-body text-ink-muted">Loading…</p>;

  const fill = async () => {
    setBusy(true);
    try {
      await mastersApi.customerOrders.fill(order.id);
      toast({ title: 'Filled from available stock', description: 'Ring the filled lines into the cart at the price shown.' });
      await reload();
      await onChanged();
    } catch (error) {
      toast({ title: 'Could not fill the order', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const askCancel = () => {
    confirmer.ask(
      {
        subject: `Order ${order.orderNumber}`,
        consequence:
          "The order is cancelled. Anything already taken as a deposit stays on the customer's account.",
        verb: 'Cancel order',
      },
      cancel,
    );
  };
  const cancel = async () => {
    setBusy(true);
    try {
      await mastersApi.customerOrders.cancel(order.id);
      toast({ variant: 'success', title: 'Order cancelled' });
      await reload();
      await onChanged();
    } catch (error) {
      toast({ title: 'Could not cancel the order', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const canAct = canWrite && order.status !== 'Filled' && order.status !== 'Cancelled';

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">Order #{order.orderNumber} — {order.customerName} — {order.status}</h2>
        <button type="button" className="pos-button" onClick={onClose}>Close</button>
      </div>

      <FormSection title="Lines">
        <table className="pos-table">
          <thead className="text-left text-ink-muted">
            <tr><th className="pb-1">Item</th><th className="pb-1 text-right">Ordered</th><th className="pb-1 text-right">Filled</th><th className="pb-1 text-right">Price</th></tr>
          </thead>
          <tbody>
            {order.lines.map((line) => (
              <tr key={String(line.id)} className="border-t border-subtle">
                <td className="py-1">{line.stockCode} — {line.productName}</td>
                <td className="py-1 text-right pos-amount">{line.orderedQty}</td>
                <td className="py-1 text-right pos-amount">{line.filledQty}</td>
                <td className="py-1 text-right pos-amount">{currency(line.unitPrice)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </FormSection>

      {canAct ? (
        <div className="mb-6 flex gap-2">
          <button type="button" className="pos-button" disabled={busy} onClick={() => void fill()}>Fill from stock</button>
          <button type="button" className="pos-button text-negative" disabled={busy} onClick={askCancel}>Cancel order</button>
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

// ---------------------------------------------------------------------------
// Layaways
// ---------------------------------------------------------------------------

function LayawaysTab({ locationId, tenders }: { locationId: number; tenders: TenderSettings[] }) {
  const auth = useAuth();
  const canWrite = auth.can('customer.write');
  const [rows, setRows] = useState<Layaway[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [showNew, setShowNew] = useState(false);

  const load = useCallback(async () => {
    if (!locationId) return;
    try {
      const page = await mastersApi.layaways.browse(locationId, { pageSize: 100 });
      setRows(page.items);
    } catch (error) {
      toast({ title: 'Could not load layaways', description: describeError(error), variant: 'destructive' });
    }
  }, [locationId]);

  useEffect(() => {
    void load();
  }, [load]);

  const columns: DataGridColumn<Layaway>[] = [
    { key: 'layawayNumber', header: 'Layaway #', width: 100, numeric: true, render: (r) => r.layawayNumber },
    { key: 'customer', header: 'Customer', width: 200, render: (r) => r.customerName },
    { key: 'status', header: 'Status', width: 150, render: (r) => <DomainStatusBadge status={r.status} /> },
    { key: 'total', header: 'Total', width: 100, numeric: true, render: (r) => currency(r.total) },
    { key: 'paid', header: 'Paid', width: 100, numeric: true, render: (r) => currency(r.amountPaid) },
  ];

  return (
    <BrowseFormShell
      title=""
      toolbar={canWrite ? <button type="button" className="pos-button-primary" onClick={() => setShowNew(true)}>New layaway</button> : null}
      grid={
        <DataGrid
          gridId="layaways"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          onRowActivate={(row) => setSelectedId(row.id)}
          emptyMessage="No layaways yet."
        />
      }
      form={
        selectedId ? (
          <LayawayPanel key={String(selectedId)} id={selectedId} tenders={tenders} canWrite={canWrite} onClose={() => setSelectedId(null)} onChanged={load} />
        ) : showNew ? (
          <NewLayawayPanel
            locationId={locationId}
            onClose={() => setShowNew(false)}
            onCreated={(id) => {
              setShowNew(false);
              setSelectedId(id);
              void load();
            }}
          />
        ) : null
      }
    />
  );
}

function NewLayawayPanel({ locationId, onClose, onCreated }: { locationId: number; onClose: () => void; onCreated: (id: number) => void }) {
  const [customerId, setCustomerId] = useState<number | null>(null);
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [busy, setBusy] = useState(false);

  const create = async () => {
    if (!customerId || lines.length === 0) {
      toast({ title: !customerId ? 'Choose a customer from the list' : 'Add at least one line', description: !customerId ? 'Typing a name is not enough — pick the account from the suggestions.' : 'Choose an item, set the quantity, then press Add line.', variant: 'destructive' });
      return;
    }

    setBusy(true);
    try {
      const layaway = await mastersApi.layaways.create({
        customerId,
        locationId,
        lines: lines.map((l) => ({ productId: l.productId, quantity: l.quantity, unitPrice: l.unitPrice })),
      });
      toast({ title: `Layaway #${layaway.layawayNumber} created`, description: `Total ${currency(layaway.total)}` });
      onCreated(layaway.id);
    } catch (error) {
      toast({ title: 'Could not create the layaway', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">New layaway</h2>
        <button type="button" className="pos-button" onClick={onClose}>Close</button>
      </div>
      <FormSection title="Customer">
        <CustomerPicker locationId={locationId} onPick={(c) => setCustomerId(c?.id ?? null)} />
      </FormSection>
      <FormSection
        title="Lines"
        hint="Reserved against stock until paid in full or cancelled."
        actions={<Button variant="default" loading={busy} onClick={() => void create()}>Create layaway</Button>}
      >
        <LineBuilder locationId={locationId} lines={lines} onChange={setLines} />
      </FormSection>
    </div>
  );
}

function LayawayPanel({
  id,
  tenders,
  canWrite,
  onClose,
  onChanged,
}: {
  id: number;
  tenders: TenderSettings[];
  canWrite: boolean;
  onClose: () => void;
  onChanged: () => Promise<void>;
}) {
  const confirmer = useConfirm();
  const [layaway, setLayaway] = useState<Layaway | null>(null);
  const [amount, setAmount] = useState(0);
  const [tenderTypeId, setTenderTypeId] = useState<number | ''>('');
  const [busy, setBusy] = useState(false);

  const reload = useCallback(async () => {
    try {
      setLayaway(await mastersApi.layaways.get(id));
    } catch (error) {
      toast({ title: 'Could not open the layaway', description: describeError(error), variant: 'destructive' });
    }
  }, [id]);

  useEffect(() => {
    void reload();
  }, [reload]);

  useEffect(() => {
    if (!tenderTypeId && tenders.length > 0) setTenderTypeId(tenders[0]!.id);
  }, [tenders, tenderTypeId]);

  if (!layaway) return <p className="text-body text-ink-muted">Loading…</p>;

  const takePayment = async () => {
    if (amount <= 0 || !tenderTypeId) {
      toast({ title: 'Enter an amount and choose a tender', variant: 'destructive' });
      return;
    }

    setBusy(true);
    try {
      await mastersApi.layaways.takePayment(layaway.id, amount, tenderTypeId);
      toast({ title: 'Payment recorded' });
      setAmount(0);
      await reload();
      await onChanged();
    } catch (error) {
      toast({ title: 'Could not take the payment', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const askCancel = () => {
    confirmer.ask(
      {
        subject: `Layaway ${layaway.layawayNumber}`,
        consequence:
          "The layaway is cancelled and the goods go back into stock. Payments already made stay on "
          + "the customer's account.",
        verb: 'Cancel layaway',
      },
      cancel,
    );
  };
  const cancel = async () => {
    setBusy(true);
    try {
      await mastersApi.layaways.cancel(layaway.id);
      toast({ variant: 'success', title: 'Layaway cancelled' });
      await reload();
      await onChanged();
    } catch (error) {
      toast({ title: 'Could not cancel the layaway', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const remaining = layaway.total - layaway.amountPaid;
  const canAct = canWrite && layaway.status === 'Open';

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">Layaway #{layaway.layawayNumber} — {layaway.customerName} — {layaway.status}</h2>
        <button type="button" className="pos-button" onClick={onClose}>Close</button>
      </div>

      <FormSection title="Balance">
        <p className="pos-amount text-body">{currency(layaway.amountPaid)} of {currency(layaway.total)} paid — {currency(Math.max(0, remaining))} remaining</p>
      </FormSection>

      <FormSection title="Lines">
        <table className="pos-table">
          {/* Columns of figures with nothing naming them: the quantity and the price were two
              right-aligned numbers a reader had to work out from context. */}
          <thead>
            <tr>
              <th>Item</th>
              <th data-numeric>Qty</th>
              <th data-numeric>Price</th>
            </tr>
          </thead>
          <tbody>
            {layaway.lines.map((line) => (
              <tr key={String(line.id)} className="border-t border-subtle">
                <td className="py-1">{line.stockCode} — {line.productName}</td>
                <td className="py-1 text-right pos-amount">{line.quantity}</td>
                <td className="py-1 text-right pos-amount">{currency(line.unitPrice)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </FormSection>

      {canAct ? (
        <>
          <FormSection title="Take a deposit" actions={<Button variant="default" loading={busy} onClick={() => void takePayment()}>Apply</Button>}>
            <NumberField label="Amount" value={amount} onChange={setAmount} />
            <Field label="Tender">
              <select
                className="w-full pos-input"
                value={tenderTypeId}
                onChange={(event) => setTenderTypeId(recordIdFrom(event.target.value))}
              >
                {tenders.map((t) => <option key={String(t.id)} value={t.id}>{t.displayName}</option>)}
              </select>
            </Field>
          </FormSection>
          <div className="mb-6">
            <button type="button" className="pos-button text-negative" disabled={busy} onClick={askCancel}>Cancel layaway</button>
          </div>
        </>
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

// ---------------------------------------------------------------------------
// Price quotes
// ---------------------------------------------------------------------------

function PriceQuotesTab({ locationId }: { locationId: number }) {
  const auth = useAuth();
  const canWrite = auth.can('customer.write');
  const [rows, setRows] = useState<PriceQuote[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [showNew, setShowNew] = useState(false);

  const load = useCallback(async () => {
    if (!locationId) return;
    try {
      const page = await mastersApi.priceQuotes.browse(locationId, { pageSize: 100 });
      setRows(page.items);
    } catch (error) {
      toast({ title: 'Could not load price quotes', description: describeError(error), variant: 'destructive' });
    }
  }, [locationId]);

  useEffect(() => {
    void load();
  }, [load]);

  const columns: DataGridColumn<PriceQuote>[] = [
    { key: 'quoteNumber', header: 'Quote #', width: 90, numeric: true, render: (r) => r.quoteNumber },
    { key: 'customer', header: 'Customer', width: 200, render: (r) => r.customerName },
    { key: 'status', header: 'Status', width: 110, render: (r) => r.status },
    { key: 'total', header: 'Total', width: 100, numeric: true, render: (r) => currency(r.total) },
    { key: 'expires', header: 'Expires', width: 110, render: (r) => r.expiresOn ?? '—' },
  ];

  return (
    <BrowseFormShell
      title=""
      toolbar={canWrite ? <button type="button" className="pos-button-primary" onClick={() => setShowNew(true)}>New quote</button> : null}
      grid={
        <DataGrid
          gridId="price-quotes"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          onRowActivate={(row) => setSelectedId(row.id)}
          emptyMessage="No price quotes yet."
        />
      }
      form={
        selectedId ? (
          <PriceQuotePanel key={String(selectedId)} id={selectedId} canWrite={canWrite} onClose={() => setSelectedId(null)} onChanged={load} />
        ) : showNew ? (
          <NewPriceQuotePanel
            locationId={locationId}
            onClose={() => setShowNew(false)}
            onCreated={(id) => {
              setShowNew(false);
              setSelectedId(id);
              void load();
            }}
          />
        ) : null
      }
    />
  );
}

function NewPriceQuotePanel({ locationId, onClose, onCreated }: { locationId: number; onClose: () => void; onCreated: (id: number) => void }) {
  const [customerId, setCustomerId] = useState<number | null>(null);
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [expiresOn, setExpiresOn] = useState('');
  const [busy, setBusy] = useState(false);

  const create = async () => {
    if (!customerId || lines.length === 0) {
      toast({ title: !customerId ? 'Choose a customer from the list' : 'Add at least one line', description: !customerId ? 'Typing a name is not enough — pick the account from the suggestions.' : 'Choose an item, set the quantity, then press Add line.', variant: 'destructive' });
      return;
    }

    setBusy(true);
    try {
      const quote = await mastersApi.priceQuotes.create({
        customerId,
        locationId,
        lines: lines.map((l) => ({ productId: l.productId, quantity: l.quantity, unitPrice: l.unitPrice })),
        expiresOn: expiresOn || undefined,
      });
      toast({ title: `Quote #${quote.quoteNumber} created`, description: `Total ${currency(quote.total)}` });
      onCreated(quote.id);
    } catch (error) {
      toast({ title: 'Could not create the quote', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">New price quote</h2>
        <button type="button" className="pos-button" onClick={onClose}>Close</button>
      </div>
      <FormSection title="Customer">
        <CustomerPicker locationId={locationId} onPick={(c) => setCustomerId(c?.id ?? null)} />
      </FormSection>
      <FormSection title="Lines" actions={<Button variant="default" loading={busy} onClick={() => void create()}>Create quote</Button>}>
        <LineBuilder locationId={locationId} lines={lines} onChange={setLines} />
        <Field label="Expires on (optional)">
          <input
            type="date"
            className="w-full pos-input"
            value={expiresOn}
            onChange={(event) => setExpiresOn(event.target.value)}
          />
        </Field>
      </FormSection>
    </div>
  );
}

function PriceQuotePanel({
  id,
  canWrite,
  onClose,
  onChanged,
}: {
  id: number;
  canWrite: boolean;
  onClose: () => void;
  onChanged: () => Promise<void>;
}) {
  const confirmer = useConfirm();
  const [quote, setQuote] = useState<PriceQuote | null>(null);
  const [busy, setBusy] = useState(false);

  const reload = useCallback(async () => {
    try {
      setQuote(await mastersApi.priceQuotes.get(id));
    } catch (error) {
      toast({ title: 'Could not open the quote', description: describeError(error), variant: 'destructive' });
    }
  }, [id]);

  useEffect(() => {
    void reload();
  }, [reload]);

  if (!quote) return <p className="text-body text-ink-muted">Loading…</p>;

  const convert = async () => {
    setBusy(true);
    try {
      await mastersApi.priceQuotes.convert(quote.id);
      toast({ variant: 'success', title: 'Quote converted', description: 'Ring the lines into the cart at the quoted prices.' });
      await reload();
      await onChanged();
    } catch (error) {
      toast({ title: 'Could not convert the quote', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const askCancel = () => {
    confirmer.ask(
      {
        subject: `Quote ${quote.quoteNumber}`,
        consequence: 'The quote is cancelled. It stays on the list as cancelled rather than disappearing.',
        verb: 'Cancel quote',
      },
      cancel,
    );
  };
  const cancel = async () => {
    setBusy(true);
    try {
      await mastersApi.priceQuotes.cancel(quote.id);
      toast({ variant: 'success', title: 'Quote cancelled' });
      await reload();
      await onChanged();
    } catch (error) {
      toast({ title: 'Could not cancel the quote', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const canAct = canWrite && quote.status === 'Open';

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">Quote #{quote.quoteNumber} — {quote.customerName} — {quote.status}</h2>
        <button type="button" className="pos-button" onClick={onClose}>Close</button>
      </div>

      <FormSection title="Lines">
        <table className="pos-table">
          {/* Columns of figures with nothing naming them: the quantity and the price were two
              right-aligned numbers a reader had to work out from context. */}
          <thead>
            <tr>
              <th>Item</th>
              <th data-numeric>Qty</th>
              <th data-numeric>Price</th>
            </tr>
          </thead>
          <tbody>
            {quote.lines.map((line) => (
              <tr key={String(line.id)} className="border-t border-subtle">
                <td className="py-1">{line.stockCode} — {line.productName}</td>
                <td className="py-1 text-right pos-amount">{line.quantity}</td>
                <td className="py-1 text-right pos-amount">{currency(line.unitPrice)}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <p className="mt-2 text-right text-body font-medium">Total: {currency(quote.total)}</p>
      </FormSection>

      {canAct ? (
        <div className="mb-6 flex gap-2">
          <button type="button" className="pos-button" disabled={busy} onClick={() => void convert()}>Convert to sale</button>
          <button type="button" className="pos-button text-negative" disabled={busy} onClick={askCancel}>Cancel quote</button>
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
