'use client';

/** Pieces the manufacturing pages share: the status chip, the store and location pickers, the order form and check answers. */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Save } from 'lucide-react';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select, TextArea } from '@/components/ui/field';
import { Chip } from '@/components/ui/page';
import { FormError } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { numberText, parseNumber, textOrNull } from '@/lib/forms';
import { formatDate, formatQuantity } from '@/lib/format';
import { ORDER_STATUS, rangeText } from '@/lib/production';
import { productionApi } from '@/lib/production-api';
import type { CheckAnswer, ProductionOrderDetail, ProductionOrderStatus, QualityCheck } from '@/lib/production-types';
import { useStore } from '@/lib/store';

export function StatusChip({ status }: { status: ProductionOrderStatus }) {
  const s = ORDER_STATUS[status];
  return <Chip tone={s.tone}>{s.label}</Chip>;
}

/** The store a page works on: the one in the header, else the user's, else the first. */
export function useWorkingStore(): string | null {
  const { storeId, defaultStoreId } = useStore();
  return storeId ?? defaultStoreId;
}

/** A store's locations (cached). */
export function useLocations(storeId: string | null) {
  return useQuery({ queryKey: ['locations', storeId], queryFn: () => api.locations(storeId!), enabled: !!storeId, staleTime: 5 * 60_000 });
}

/** The store picker of a form, for users who see every store and have "All stores" in the header. */
export function StoreField({ value, onChange, id }: { value: string | null; onChange(id: string): void; id: string }) {
  const { stores, storeId } = useStore();
  if (storeId || stores.length <= 1) return null;
  return (
    <Field label="Store" htmlFor={id}>
      <Select id={id} value={value ?? ''} onChange={(e) => onChange(e.target.value)}>
        {stores.map((s) => (
          <option key={s.id} value={s.id}>
            {s.code} · {s.name}
          </option>
        ))}
      </Select>
    </Field>
  );
}

export function LocationSelect({ storeId, value, onChange, id, label }: { storeId: string | null; value: string; onChange(id: string): void; id: string; label: string }) {
  const locations = useLocations(storeId);
  return (
    <Field label={label} htmlFor={id}>
      <Select id={id} value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">Choose…</option>
        {(locations.data ?? []).map((l) => (
          <option key={l.id} value={l.id}>
            {l.code}
            {l.name ? ` · ${l.name}` : ''}
          </option>
        ))}
      </Select>
    </Field>
  );
}

/** A new production order, or a change to one (all of a planned order; dates, priority and notes of a released one). */
export function OrderDialog({ order, productCode, onClose, onSaved }: { order?: ProductionOrderDetail | null; productCode?: string; onClose(): void; onSaved(order: ProductionOrderDetail): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const working = useWorkingStore();
  const [store, setStore] = useState<string | null>(order?.storeId ?? working);
  const locations = useLocations(store);
  const firstLocation = locations.data?.[0]?.id ?? '';
  const [product, setProduct] = useState(order?.productCode ?? productCode ?? '');
  const [quantity, setQuantity] = useState(numberText(order?.quantity ?? 1));
  const [material, setMaterial] = useState(order?.materialLocationId ?? '');
  const [output, setOutput] = useState(order?.outputLocationId ?? '');
  const [start, setStart] = useState(order?.plannedStart ?? '');
  const [end, setEnd] = useState(order?.plannedEnd ?? '');
  const [priority, setPriority] = useState(String(order?.priority ?? 5));
  const [customerOrder, setCustomerOrder] = useState(order?.customerOrderId ?? '');
  const [notes, setNotes] = useState(order?.notes ?? '');
  const [problem, setProblem] = useState<string | null>(null);
  const planned = !order || order.status === 'Planned';
  const boms = useQuery({ queryKey: ['boms', 'active'], queryFn: () => productionApi.boms({ active: true }), staleTime: 60_000 });
  const customerOrders = useQuery({ queryKey: ['customer-orders', store], queryFn: () => productionApi.customerOrders({ storeId: store }), enabled: planned && !!store });
  const products = [...new Map((boms.data ?? []).filter((b) => b.inForce).map((b) => [b.productCode, b])).values()];

  const save = useMutation({
    mutationFn: () => {
      const body = {
        version: order?.version ?? null,
        productCode: product.trim().toUpperCase(),
        quantity: parseNumber(quantity) ?? 0,
        materialLocationId: material || firstLocation,
        outputLocationId: output || firstLocation,
        billOfMaterialsId: null,
        plannedStart: start || null,
        plannedEnd: end || null,
        priority: Number(priority) || 5,
        customerOrderId: customerOrder || null,
        notes: textOrNull(notes),
      };
      return order ? productionApi.saveOrder(order.id, body) : productionApi.addOrder(body);
    },
    onSuccess: (saved) => {
      toast.ok(order ? `Order ${saved.number} is saved.` : `Order ${saved.number} is planned.`);
      void queryClient.invalidateQueries({ queryKey: ['production-board'] });
      void queryClient.invalidateQueries({ queryKey: ['production-order', saved.id] });
      onSaved(saved);
    },
  });

  function submit() {
    const q = parseNumber(quantity);
    const text = !product.trim() ? 'Choose the product to make.' : q === null || q <= 0 ? 'The quantity to make must be above zero.' : start && end && end < start ? 'The planned end is before the start.' : null;
    setProblem(text);
    if (!text) save.mutate();
  }

  return (
    <Dialog
      open
      title={order ? `Change order ${order.number}` : 'New production order'}
      onClose={onClose}
      onSubmit={submit}
      width="lg"
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            {order ? 'Save' : 'Plan the order'}
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        {!order && <StoreField id="order-store" value={store} onChange={setStore} />}
        <Field label="Product" htmlFor="order-product" hint="A stock code with a bill of materials in force.">
          <Input id="order-product" list="order-products" value={product} disabled={!planned} className="uppercase" onChange={(e) => setProduct(e.target.value)} />
          <datalist id="order-products">
            {products.map((b) => (
              <option key={b.productCode} value={b.productCode}>
                {b.productName}
              </option>
            ))}
          </datalist>
        </Field>
        <Field label="Quantity" htmlFor="order-quantity">
          <Input id="order-quantity" inputMode="decimal" value={quantity} disabled={!planned} className="max-w-[160px] text-right" onChange={(e) => setQuantity(e.target.value)} />
        </Field>
        {planned && (
          <>
            <LocationSelect id="order-material" label="Materials from" storeId={store} value={material || firstLocation} onChange={setMaterial} />
            <LocationSelect id="order-output" label="Output to" storeId={store} value={output || firstLocation} onChange={setOutput} />
          </>
        )}
        <Field label="Planned start" htmlFor="order-start">
          <Input id="order-start" type="date" value={start} className="max-w-[200px]" onChange={(e) => setStart(e.target.value)} />
        </Field>
        <Field label="Planned end" htmlFor="order-end">
          <Input id="order-end" type="date" value={end} className="max-w-[200px]" onChange={(e) => setEnd(e.target.value)} />
        </Field>
        <Field label="Priority" htmlFor="order-priority" hint="1 is the most urgent.">
          <Select id="order-priority" value={priority} className="max-w-[160px]" onChange={(e) => setPriority(e.target.value)}>
            {[1, 2, 3, 4, 5, 6, 7, 8, 9].map((p) => (
              <option key={p} value={p}>
                {p}
              </option>
            ))}
          </Select>
        </Field>
        {planned && (
          <Field label="Made for" htmlFor="order-customer" hint="A customer order, when it is made to order.">
            <Select id="order-customer" value={customerOrder} onChange={(e) => setCustomerOrder(e.target.value)}>
              <option value="">Stock</option>
              {(customerOrders.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.invoiceNumber} · {c.name ?? ''} · {formatQuantity(c.quantity)} x {c.plu ?? c.description} {c.orderDate ? `(${formatDate(c.orderDate)})` : ''}
                </option>
              ))}
            </Select>
          </Field>
        )}
        <Field label="Notes" htmlFor="order-notes">
          <TextArea id="order-notes" value={notes} maxLength={400} onChange={(e) => setNotes(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? save.error} />
    </Dialog>
  );
}

/** The answer boxes of a plan's checks. */
export function CheckAnswers({ checks, answers, onChange }: { checks: QualityCheck[]; answers: CheckAnswer[]; onChange(answers: CheckAnswer[]): void }) {
  // Number boxes keep what was typed, so a half-typed "99." is not lost.
  const [typed, setTyped] = useState<Record<number, string>>({});
  const of = (line: number) => answers.find((a) => a.lineNumber === line) ?? { lineNumber: line };
  const set = (line: number, patch: Partial<CheckAnswer>) => onChange([...answers.filter((a) => a.lineNumber !== line), { ...of(line), ...patch }]);
  if (checks.length === 0) return <p className="text-muted">The product has no quality plan; only the quantities are recorded.</p>;
  return (
    <div className="space-y-2">
      {checks.map((c) => {
        const id = `check-${c.lineNumber}`;
        const label = `${c.name}${c.required ? '' : ' (if wanted)'}`;
        if (c.kind === 'PassFail')
          return <Checkbox key={c.lineNumber} label={label} hint="Ticked when it passes." checked={of(c.lineNumber).passFail === true} onChange={(v) => set(c.lineNumber, { passFail: v })} />;
        if (c.kind === 'Number')
          return (
            <Field key={c.lineNumber} label={label} htmlFor={id} hint={rangeText(c)}>
              <Input
                id={id}
                inputMode="decimal"
                className="max-w-[180px] text-right"
                value={typed[c.lineNumber] ?? numberText(of(c.lineNumber).number ?? null)}
                onChange={(e) => {
                  setTyped({ ...typed, [c.lineNumber]: e.target.value });
                  set(c.lineNumber, { number: parseNumber(e.target.value) });
                }}
              />
            </Field>
          );
        return (
          <Field key={c.lineNumber} label={label} htmlFor={id}>
            <Input id={id} value={of(c.lineNumber).text ?? ''} onChange={(e) => set(c.lineNumber, { text: e.target.value })} />
          </Field>
        );
      })}
    </div>
  );
}
