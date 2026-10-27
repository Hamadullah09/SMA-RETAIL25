'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Save, Trash2, X } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useMemo, useState } from 'react';
import { Button } from '@/components/ui/button';
import { ConfirmDialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select, TextArea } from '@/components/ui/field';
import { Toolbar } from '@/components/ui/page';
import { ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { checkBarcode, checkNumber, numberText, parseNumber, textOrNull, type Errors } from '@/lib/forms';
import { formatMoney, formatPercent, formatQuantity } from '@/lib/format';
import { useHotkey } from '@/lib/hotkeys';
import { useStore } from '@/lib/store';
import type { ItemDetail, ItemStock } from '@/lib/types';

export default function ItemEditorPage() {
  return (
    <Suspense>
      <ItemEditor />
    </Suspense>
  );
}

type Tab = 'detail' | 'pricing' | 'stock';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'detail', label: 'Detail' },
  { key: 'pricing', label: 'Pricing' },
  { key: 'stock', label: 'Stock' },
];

interface Form {
  plu: string;
  barcode: string;
  name: string;
  description: string;
  departmentId: string;
  categoryId: string;
  supplierId: string;
  price1: string;
  price2: string;
  price3: string;
  lastCost: string;
  averageCost: string;
  taxable1: boolean;
  taxable2: boolean;
  taxable3: boolean;
  reorderPoint: string;
  reorderQuantity: string;
  unitsPerCase: string;
  isActive: boolean;
  notes: string;
}

type Key = keyof Form;

const EMPTY: Form = {
  plu: '',
  barcode: '',
  name: '',
  description: '',
  departmentId: '',
  categoryId: '',
  supplierId: '',
  price1: '',
  price2: '',
  price3: '',
  lastCost: '',
  averageCost: '',
  taxable1: true,
  taxable2: false,
  taxable3: false,
  reorderPoint: '0',
  reorderQuantity: '0',
  unitsPerCase: '1',
  isActive: true,
  notes: '',
};

function toForm(item: ItemDetail): Form {
  return {
    plu: item.plu ?? '',
    barcode: item.barcode ?? '',
    name: item.name ?? '',
    description: item.description ?? '',
    departmentId: item.departmentId ?? '',
    categoryId: item.categoryId ?? '',
    supplierId: item.supplierId ?? '',
    price1: numberText(item.price1),
    price2: numberText(item.price2),
    price3: numberText(item.price3),
    lastCost: numberText(item.lastCost),
    averageCost: numberText(item.averageCost),
    taxable1: item.taxable1,
    taxable2: item.taxable2,
    taxable3: item.taxable3,
    reorderPoint: numberText(item.reorderPoint),
    reorderQuantity: numberText(item.reorderQuantity),
    unitsPerCase: numberText(item.unitsPerCase),
    isActive: item.isActive,
    notes: item.notes ?? '',
  };
}

/** Which tab each box is on, to open the right one when a box is wrong. */
const TAB_OF: Partial<Record<Key, Tab>> = {
  price1: 'pricing',
  price2: 'pricing',
  price3: 'pricing',
  lastCost: 'pricing',
  averageCost: 'pricing',
  reorderPoint: 'stock',
  reorderQuantity: 'stock',
  unitsPerCase: 'stock',
};

function validate(form: Form): Errors<Key> {
  const errors: Errors<Key> = {};
  if (!form.plu.trim()) errors.plu = 'A stock code is needed.';
  else if (form.plu.trim().length > 20) errors.plu = 'A stock code has at most 20 characters.';
  errors.barcode = checkBarcode(form.barcode);
  if (!form.name.trim()) errors.name = 'A description is needed.';
  else if (form.name.trim().length > 60) errors.name = 'A description has at most 60 characters.';
  errors.price1 = checkNumber(form.price1, { required: true, label: 'Price 1' });
  errors.price2 = checkNumber(form.price2, { label: 'Price 2' });
  errors.price3 = checkNumber(form.price3, { label: 'Price 3' });
  errors.lastCost = checkNumber(form.lastCost, { label: 'Last cost', decimals: 4 });
  errors.averageCost = checkNumber(form.averageCost, { label: 'Average cost', decimals: 4 });
  errors.reorderPoint = checkNumber(form.reorderPoint, { label: 'Reorder point', decimals: 3 });
  errors.reorderQuantity = checkNumber(form.reorderQuantity, { label: 'Reorder quantity', decimals: 3 });
  errors.unitsPerCase = checkNumber(form.unitsPerCase, { label: 'Units per case', decimals: 3, min: 1 });
  for (const key of Object.keys(errors) as Key[]) if (!errors[key]) delete errors[key];
  return errors;
}

function ItemEditor() {
  const id = useSearchParams().get('id');
  const isNew = !id;
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { currencySymbol: symbol, stores, defaultStoreId } = useStore();
  // A new item goes to the first location of this store (the server keeps items per location).
  const [newStoreId, setNewStoreId] = useState<string>(defaultStoreId ?? '');
  const [newLocationId, setNewLocationId] = useState<string>('');
  const [tab, setTab] = useState<Tab>('detail');
  const [form, setForm] = useState<Form>(EMPTY);
  const [loadedFrom, setLoadedFrom] = useState<string | null>(null);
  const [errors, setErrors] = useState<Errors<Key>>({});
  const [conflict, setConflict] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [dirty, setDirty] = useState(false);

  const item = useQuery({ queryKey: ['item', id], queryFn: () => api.item(id!), enabled: !isNew, staleTime: 0 });
  const departments = useQuery({ queryKey: ['departments'], queryFn: () => api.departments(), staleTime: 5 * 60_000 });
  // Categories are not kept under a department, so every category is offered.
  const categories = useQuery({ queryKey: ['categories', ''], queryFn: () => api.categories(null), staleTime: 5 * 60_000 });
  const suppliers = useQuery({ queryKey: ['suppliers', 'all'], queryFn: () => api.suppliers({ pageSize: 500 }), staleTime: 5 * 60_000 });
  // A new item goes to a location of the chosen store (the model keeps items per location).
  const locations = useQuery({ queryKey: ['locations', newStoreId], queryFn: () => api.locations(newStoreId), enabled: isNew && !!newStoreId, staleTime: 5 * 60_000 });
  useEffect(() => {
    const list = locations.data ?? [];
    if (!list.some((l) => l.id === newLocationId)) setNewLocationId(list[0]?.id ?? '');
  }, [locations.data, newLocationId]);

  // Fill the boxes from the server once per version (a reload after a conflict brings the new values in).
  useEffect(() => {
    if (item.data && item.data.version !== loadedFrom) {
      setForm(toForm(item.data));
      setLoadedFrom(item.data.version);
      setDirty(false);
    }
  }, [item.data, loadedFrom]);

  useEffect(() => {
    document.title = `${isNew ? 'New item' : form.name || 'Item'} · SMA RETAIL`;
  }, [isNew, form.name]);

  function set<K extends Key>(key: K, value: Form[K]) {
    setForm((f) => ({ ...f, [key]: value }));
    setDirty(true);
    if (errors[key]) setErrors((e) => ({ ...e, [key]: undefined }));
  }

  const save = useMutation({
    mutationFn: (body: ItemDetail) => (isNew ? api.addItem(body, newStoreId || null) : api.saveItem(id!, body)),
    onSuccess: (saved) => {
      toast.ok(`${saved.name || 'The item'} is saved.`);
      setDirty(false);
      void queryClient.invalidateQueries({ queryKey: ['items'] });
      queryClient.setQueryData(['item', saved.id], saved);
      setLoadedFrom(saved.version);
      setForm(toForm(saved));
      if (isNew && saved.id) router.replace(`/items/edit/?id=${encodeURIComponent(saved.id)}`);
    },
    onError: (e) => {
      if (e instanceof ApiError && e.isConflict) setConflict(true);
    },
  });

  const remove = useMutation({
    mutationFn: () => api.deleteItem(id!),
    onSuccess: () => {
      toast.ok(`${form.name || 'The item'} is deleted. Show deleted on the list brings it back.`);
      void queryClient.invalidateQueries({ queryKey: ['items'] });
      router.push('/items/');
    },
    onError: (e: Error) => {
      setDeleting(false);
      toast.error(e.message);
    },
  });

  function submit() {
    const found = validate(form);
    setErrors(found);
    const first = (Object.keys(found) as Key[])[0];
    if (first) {
      setTab(TAB_OF[first] ?? 'detail');
      setTimeout(() => document.getElementById(`item-${first}`)?.focus(), 0);
      return;
    }
    const body: ItemDetail = {
      id: isNew ? null : id,
      ...(isNew && newLocationId ? { locationId: newLocationId } : {}),
      version: isNew ? null : item.data?.version ?? null,
      plu: form.plu.trim(),
      barcode: textOrNull(form.barcode),
      name: form.name.trim(),
      description: textOrNull(form.description),
      departmentId: form.departmentId || null,
      categoryId: form.categoryId || null,
      supplierId: form.supplierId || null,
      price1: parseNumber(form.price1) ?? 0,
      price2: parseNumber(form.price2) ?? 0,
      price3: parseNumber(form.price3) ?? 0,
      lastCost: parseNumber(form.lastCost) ?? 0,
      averageCost: parseNumber(form.averageCost) ?? 0,
      taxable1: form.taxable1,
      taxable2: form.taxable2,
      taxable3: false,
      reorderPoint: parseNumber(form.reorderPoint) ?? 0,
      reorderQuantity: parseNumber(form.reorderQuantity) ?? 0,
      unitsPerCase: parseNumber(form.unitsPerCase) ?? 1,
      isActive: form.isActive,
      notes: textOrNull(form.notes),
    };
    save.mutate(body);
  }

  useHotkey('Ctrl+S', submit, !save.isPending);
  useHotkey('Esc', () => router.push('/items/'));

  const margin = useMemo(() => {
    const price = parseNumber(form.price1);
    const cost = parseNumber(form.averageCost) ?? parseNumber(form.lastCost);
    if (!price || cost === null) return null;
    return ((price - cost) / price) * 100;
  }, [form.price1, form.averageCost, form.lastCost]);

  if (!isNew && item.error) {
    return (
      <div className="card">
        <ErrorState error={item.error} onRetry={() => void item.refetch()} />
      </div>
    );
  }
  const loading = !isNew && !item.data;

  const box = (key: Key, label: string, props: { hint?: string; inputMode?: 'decimal' | 'numeric'; maxLength?: number; className?: string; autoFocus?: boolean } = {}) => (
    <Field label={label} htmlFor={`item-${key}`} error={errors[key]} hint={props.hint}>
      <Input
        id={`item-${key}`}
        value={form[key] as string}
        onChange={(e) => set(key, e.target.value as never)}
        invalid={!!errors[key]}
        inputMode={props.inputMode}
        maxLength={props.maxLength}
        className={props.className}
        autoFocus={props.autoFocus}
      />
    </Field>
  );

  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
    >
      <header className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div className="min-w-0">
          <h1 className="page-title truncate">{isNew ? 'New item' : loading ? 'Item' : form.name || 'Item'}</h1>
          {!isNew && item.data && <p className="text-muted">Stock code {item.data.plu}</p>}
        </div>
        <Toolbar className="mb-0">
          <Button type="submit" variant="sun" icon={Save} shortcut="Ctrl+S" busy={save.isPending} disabled={loading}>
            Save
          </Button>
          <Button icon={X} shortcut="Esc" onClick={() => router.push('/items/')}>
            {dirty ? 'Cancel' : 'Close'}
          </Button>
          {!isNew && (
            <Button icon={Trash2} variant="danger" shortcut="Del" onClick={() => setDeleting(true)} disabled={loading}>
              Delete
            </Button>
          )}
        </Toolbar>
      </header>

      <Tabs label="Item" tabs={TABS} value={tab} onChange={setTab} className="mb-4" />

      <div className="card p-5 sm:p-6">
        {loading ? (
          <div className="space-y-4">
            {[0, 1, 2, 3, 4].map((i) => (
              <Skeleton key={i} className="h-11 max-w-xl" />
            ))}
          </div>
        ) : (
          <>
            <div hidden={tab !== 'detail'} className="grid grid-cols-1 gap-x-10 gap-y-3 xl:grid-cols-2">
              <div className="space-y-3">
                {isNew && stores.length > 1 && (
                  <Field label="Store" htmlFor="item-store" hint="Each store keeps its own price and stock for a stock code.">
                    <Select id="item-store" value={newStoreId} onChange={(e) => setNewStoreId(e.target.value)}>
                      {stores.map((s) => (
                        <option key={s.id} value={s.id}>
                          {s.code} · {s.name}
                        </option>
                      ))}
                    </Select>
                  </Field>
                )}
                {isNew && (locations.data?.length ?? 0) > 1 && (
                  <Field label="Location" htmlFor="item-location">
                    <Select id="item-location" value={newLocationId} onChange={(e) => setNewLocationId(e.target.value)}>
                      {(locations.data ?? []).map((l) => (
                        <option key={l.id} value={l.id}>
                          {l.code} · {l.name}
                        </option>
                      ))}
                    </Select>
                  </Field>
                )}
                {box('plu', 'Stock code', { maxLength: 20, autoFocus: isNew })}
                {box('barcode', 'Barcode', { inputMode: 'numeric', maxLength: 13, hint: 'UPC-A, EAN-13 or EAN-8, as printed on the product.' })}
                {box('name', 'Description', { maxLength: 60 })}
                <Field label="Long description" htmlFor="item-description">
                  <TextArea id="item-description" value={form.description} onChange={(e) => set('description', e.target.value)} rows={2} />
                </Field>
                <Field label="Department" htmlFor="item-departmentId">
                  <Select
                    id="item-departmentId"
                    value={form.departmentId}
                    onChange={(e) => set('departmentId', e.target.value)}
                  >
                    <option value="">None</option>
                    {(departments.data ?? []).map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.name}
                      </option>
                    ))}
                  </Select>
                </Field>
                <Field label="Category" htmlFor="item-categoryId">
                  <Select id="item-categoryId" value={form.categoryId} onChange={(e) => set('categoryId', e.target.value)}>
                    <option value="">None</option>
                    {(categories.data ?? []).map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.name}
                      </option>
                    ))}
                  </Select>
                </Field>
              </div>
              <div className="space-y-3">
                <Field label="Supplier" htmlFor="item-supplierId">
                  <Select id="item-supplierId" value={form.supplierId} onChange={(e) => set('supplierId', e.target.value)}>
                    <option value="">None</option>
                    {(suppliers.data?.items ?? []).map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.code} · {s.company}
                      </option>
                    ))}
                  </Select>
                </Field>
                <Field label="Active" htmlFor="item-active">
                  <Checkbox
                    label="Sold at the tills"
                    hint={isNew ? undefined : 'Untick and save to delete it; Show deleted on the list brings it back.'}
                    checked={form.isActive}
                    onChange={(v) => set('isActive', v)}
                  />
                </Field>
                <Field label="Message at the till" htmlFor="item-notes" hint="Shown at the POS when the item is rung up.">
                  <TextArea id="item-notes" value={form.notes} onChange={(e) => set('notes', e.target.value)} rows={3} />
                </Field>
              </div>
            </div>

            <div hidden={tab !== 'pricing'} className="grid grid-cols-1 gap-x-10 gap-y-3 xl:grid-cols-2">
              <div className="space-y-3">
                {box('price1', 'Price 1', { inputMode: 'decimal', className: 'max-w-[200px]', hint: 'The regular price.' })}
                {box('price2', 'Price 2', { inputMode: 'decimal', className: 'max-w-[200px]' })}
                {box('price3', 'Price 3', { inputMode: 'decimal', className: 'max-w-[200px]' })}
              </div>
              <div className="space-y-3">
                {box('lastCost', 'Last cost', { inputMode: 'decimal', className: 'max-w-[200px]' })}
                {box('averageCost', 'Average cost', { inputMode: 'decimal', className: 'max-w-[200px]' })}
                <Field label="Margin" htmlFor="item-margin">
                  <p id="item-margin" className="pt-2.5 font-semibold">
                    {margin === null ? '—' : `${formatPercent(margin)} at ${formatMoney(parseNumber(form.price1), symbol)}`}
                  </p>
                </Field>
                <Field label="Taxes" htmlFor="item-tax">
                  <div className="flex flex-wrap gap-x-6">
                    <Checkbox label="Tax 1" checked={form.taxable1} onChange={(v) => set('taxable1', v)} />
                    <Checkbox label="Tax 2" checked={form.taxable2} onChange={(v) => set('taxable2', v)} />
                  </div>
                </Field>
              </div>
            </div>

            <div hidden={tab !== 'stock'} className="grid grid-cols-1 gap-x-10 gap-y-3 xl:grid-cols-2">
              <div className="space-y-3">
                {box('reorderPoint', 'Reorder point', { inputMode: 'decimal', className: 'max-w-[200px]', hint: 'Order more when stock falls to this.' })}
                {box('reorderQuantity', 'Reorder quantity', { inputMode: 'decimal', className: 'max-w-[200px]' })}
                {box('unitsPerCase', 'Units per case', { inputMode: 'decimal', className: 'max-w-[200px]' })}
              </div>
              <div>
                <h2 className="section-title mb-2">On hand</h2>
                {isNew ? (
                  <p className="text-muted">A new item has no stock. Receive it at a till or count it.</p>
                ) : (
                  <DataTable<ItemStock>
                    className="border border-line shadow-none"
                    rows={item.data?.stock ?? []}
                    rowKey={(s) => `${s.storeCode}-${s.locationCode}`}
                    empty={<p className="p-4 text-muted">No stock anywhere.</p>}
                    showTotals={(item.data?.stock ?? []).length > 1}
                    columns={[
                      { key: 'store', header: 'Store', cell: (s) => s.storeCode },
                      { key: 'loc', header: 'Location', cell: (s) => s.locationCode },
                      {
                        key: 'qty',
                        header: 'On hand',
                        numeric: true,
                        cell: (s) => formatQuantity(s.onHand),
                        total: formatQuantity((item.data?.stock ?? []).reduce((t, s) => t + s.onHand, 0)),
                      },
                    ]}
                  />
                )}
              </div>
            </div>
          </>
        )}
        <FormError error={save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null} />
      </div>

      <ConfirmDialog
        open={conflict}
        title="Someone else changed this item. Reload?"
        message="Reloading brings in their changes and drops yours. Choose No to keep your changes on screen and copy them first."
        confirmLabel="Reload"
        cancelLabel="No"
        onConfirm={() => {
          setConflict(false);
          setLoadedFrom(null);
          save.reset();
          void item.refetch();
        }}
        onClose={() => setConflict(false)}
      />
      <ConfirmDialog
        open={deleting}
        title={`Delete ${form.name || 'this item'}?`}
        message="It leaves the item list and the tills with the next download. Its sales and stock history stay, and Undelete brings it back."
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => remove.mutate()}
        onClose={() => setDeleting(false)}
      />
    </form>
  );
}
