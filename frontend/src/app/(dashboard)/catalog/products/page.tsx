'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import {
  BrowseFormShell,
  CheckField,
  FormSection,
  LiveBadge,
  NumberField,
  SelectField,
  TextField,
} from '@/components/masters/browse-form';
import { MatrixEditor } from '@/components/masters/matrix-editor';
import { RecordPicker, type PickerOption } from '@/components/masters/record-picker';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { useLiveGrid } from '@/lib/inventory-hub';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { formatCurrency } from '@/lib/utils';
import {
  productTypes,
  type LinkedProduct,
  type ProductForm,
  type ProductRow,
  type ProductSort,
  type ProductType,
} from '@/types/masters';

/**
 * Inventory Browse + Form View (guide p.23–24, p.30–44).
 *
 * The grid is keyset-paged and patched live; the form is the legacy item screen's tabs, saved a
 * section at a time so an untouched tab is never overwritten by whatever defaults the page happened
 * to be holding.
 */
export default function ProductsPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canWrite = auth.can('catalog.write');
  const canDelete = auth.can('catalog.delete');

  const [search, setSearch] = useState('');
  const [departmentId, setDepartmentId] = useState('');
  const [type, setType] = useState<ProductType | ''>('');
  const [belowReorderPoint, setBelowReorderPoint] = useState(false);
  const [sort, setSort] = useState<ProductSort>('StockCode');
  const [rows, setRows] = useState<ProductRow[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const { data: departments = [] } = useQuery({
    queryKey: ['departments', locationId],
    queryFn: () => mastersApi.departments.list(locationId!),
    enabled: Boolean(locationId),
  });

  const { data: categories = [] } = useQuery({
    queryKey: ['categories', locationId],
    queryFn: () => mastersApi.categories.list(locationId!),
    enabled: Boolean(locationId),
  });

  const load = useCallback(
    async (append: boolean, from: string | null) => {
      if (!locationId) return;

      setLoading(true);

      try {
        const page = await mastersApi.products.browse(locationId, {
          search,
          departmentId,
          type: type || undefined,
          belowReorderPoint,
          sort,
          cursor: from ?? undefined,
          pageSize: 100,
        });

        setRows((current) => (append ? [...current, ...page.items] : page.items));
        setCursor(page.nextCursor);
        setHasMore(page.hasMore);
      } catch (error) {
        toast({ title: 'Could not load items', description: describe(error), variant: 'destructive' });
      } finally {
        setLoading(false);
      }
    },
    [locationId, search, departmentId, type, belowReorderPoint, sort],
  );

  // Debounced: typing into the search box should not fire a request per keystroke.
  useEffect(() => {
    const timer = window.setTimeout(() => void load(false, null), 200);
    return () => window.clearTimeout(timer);
  }, [load]);

  const { connected, changed } = useLiveGrid('product', locationId, setRows);

  const columns = useMemo<DataGridColumn<ProductRow>[]>(
    () => [
      { key: 'stockCode', header: 'Code', width: 110, render: (r) => r.stockCode, sortValue: (r) => r.stockCode },
      { key: 'name', header: 'Description', width: 280, render: (r) => r.name, sortValue: (r) => r.name },
      { key: 'type', header: 'Type', width: 90, render: (r) => r.type },
      { key: 'department', header: 'Department', width: 130, render: (r) => r.departmentName ?? '—' },
      {
        key: 'price',
        header: 'Price',
        width: 90,
        numeric: true,
        render: (r) => formatCurrency(r.regularPrice),
        sortValue: (r) => r.regularPrice,
      },
      {
        key: 'cost',
        header: 'Avg cost',
        width: 90,
        numeric: true,
        render: (r) => formatCurrency(r.avgCost),
        sortValue: (r) => r.avgCost,
      },
      {
        key: 'margin',
        header: 'Margin',
        width: 80,
        numeric: true,
        render: (r) => `${r.grossMarginPct.toFixed(1)}%`,
        sortValue: (r) => r.grossMarginPct,
      },
      {
        key: 'onHand',
        header: 'On hand',
        width: 80,
        numeric: true,
        // Below the reorder point is called out in colour, because "what needs buying" is the
        // question this grid is opened for most often.
        render: (r) => (
          <span
            className={
              r.reorderPoint > 0 && r.onHand + r.onOrder <= r.reorderPoint ? 'text-[rgb(var(--warning))]' : undefined
            }
          >
            {r.onHand}
          </span>
        ),
        sortValue: (r) => r.onHand,
      },
      { key: 'onOrder', header: 'On order', width: 80, numeric: true, render: (r) => r.onOrder },
      { key: 'upc', header: 'Barcode', width: 130, render: (r) => r.upc ?? '—' },
      {
        key: 'tax',
        header: 'Tax',
        width: 60,
        render: (r) => `${r.tax1Applies ? '1' : '–'}${r.tax2Applies ? '2' : '–'}`,
      },
    ],
    [],
  );

  return (
    <BrowseFormShell
      title="Inventory"
      toolbar={
        <>
          <LiveBadge connected={connected} />
          {canWrite ? (
            <button type="button" className="pos-button-primary" onClick={() => setSelectedId('new')}>
              New item
            </button>
          ) : null}
        </>
      }
      filters={
        <>
          <input
            className="w-64 rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1"
            placeholder="Code, description or barcode"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />

          <select
            className="rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1"
            value={departmentId}
            onChange={(event) => setDepartmentId(event.target.value)}
          >
            <option value="">All departments</option>
            {departments.map((department) => (
              <option key={department.id} value={department.id}>
                {department.name} ({department.usageCount})
              </option>
            ))}
          </select>

          <select
            className="rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1"
            value={type}
            onChange={(event) => setType(event.target.value as ProductType | '')}
          >
            <option value="">All types</option>
            {productTypes.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>

          <select
            className="rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1"
            value={sort}
            onChange={(event) => setSort(event.target.value as ProductSort)}
          >
            <option value="StockCode">Sort: code</option>
            <option value="Name">Sort: description</option>
            <option value="OnHand">Sort: on hand</option>
            <option value="RegularPrice">Sort: price</option>
            <option value="Margin">Sort: margin</option>
          </select>

          <label className="flex items-center gap-1.5">
            <input
              type="checkbox"
              checked={belowReorderPoint}
              onChange={(event) => setBelowReorderPoint(event.target.checked)}
            />
            Below reorder point
          </label>
        </>
      }
      grid={
        <DataGrid
          gridId="inventory"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          recentlyChanged={changed}
          onRowActivate={(row) => setSelectedId(row.id)}
          emptyMessage={loading ? 'Loading…' : 'No items match these filters.'}
        />
      }
      form={
        selectedId && locationId ? (
          <ProductFormPanel
            key={selectedId}
            productId={selectedId === 'new' ? null : selectedId}
            locationId={locationId}
            departments={departments}
            categories={categories}
            canWrite={canWrite}
            canDelete={canDelete}
            onClose={() => setSelectedId(null)}
            onSaved={() => void load(false, null)}
          />
        ) : null
      }
      status={
        <span className="flex items-center gap-3">
          <span>
            {rows.length} loaded{hasMore ? ' of more' : ''}
          </span>
          {hasMore ? (
            <button type="button" className="underline" onClick={() => void load(true, cursor)} disabled={loading}>
              Load more
            </button>
          ) : null}
          <span>Double-click a row to open it.</span>
        </span>
      }
    />
  );
}

function describe(error: unknown): string {
  return error instanceof PosApiError ? error.problem.detail : 'Something went wrong.';
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

/** Replaces one element without mutating the caller's array — it is React state. */
function replaceAt<T>(items: T[], index: number, value: T): T[] {
  return items.map((item, i) => (i === index ? value : item));
}

function toPick(link: LinkedProduct | null): PickerOption | null {
  return link ? { id: link.id, code: link.stockCode, name: link.name } : null;
}

function fromPick(picked: PickerOption | null): LinkedProduct | null {
  return picked ? { id: picked.id, stockCode: picked.code, name: picked.name } : null;
}

/**
 * Searches the catalogue for a link target, excluding the item being edited — an item cannot be its
 * own substitute, and the server refuses it, so offering it is only a way to earn an error.
 */
function searchItems(locationId: string, term: string, excludeId: string): Promise<PickerOption[]> {
  return mastersApi.products
    .browse(locationId, { search: term, pageSize: 15 })
    .then((page) =>
      page.items
        .filter((item) => item.id !== excludeId)
        .map((item) => ({ id: item.id, code: item.stockCode, name: item.name })),
    );
}

const emptyForm: ProductForm = {
  id: '',
  locationId: '',
  stockCode: '',
  name: '',
  description: null,
  type: 'Standard',
  upc: null,
  tax1Applies: true,
  tax2Applies: true,
  regularPrice: 0,
  lastCost: 0,
  avgCost: 0,
  grossMarginPct: 0,
  baseStock: 0,
  reorderPoint: 0,
  reorderQty: 0,
  onHand: 0,
  onOrder: 0,
  caseQty: 0,
  shipWeight: 0,
  binLocation: null,
  posMessage: null,
  invoiceMessage: null,
  notes: null,
  departmentId: null,
  departmentName: null,
  categoryId: null,
  categoryName: null,
  substitute: null,
  tagAlong: null,
  parent: null,
  levels: [],
  breaks: [],
  sale: null,
  bonus: null,
  suppliers: [],
  kitComponents: [],
  isDeleted: false,
  createdAt: '',
  modifiedAt: null,
};

function ProductFormPanel({
  productId,
  locationId,
  departments,
  categories,
  canWrite,
  canDelete,
  onClose,
  onSaved,
}: {
  productId: string | null;
  locationId: string;
  departments: Array<{ id: string; name: string }>;
  categories: Array<{ id: string; name: string }>;
  canWrite: boolean;
  canDelete: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState<ProductForm>(emptyForm);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!productId) {
      setForm({ ...emptyForm, locationId });
      return;
    }

    void mastersApi.products
      .get(productId)
      .then(setForm)
      .catch((error) =>
        toast({ title: 'Could not open the item', description: describe(error), variant: 'destructive' }),
      );
  }, [productId, locationId]);

  const patch = (changes: Partial<ProductForm>) => setForm((current) => ({ ...current, ...changes }));

  const general = () => ({
    stockCode: form.stockCode,
    name: form.name,
    description: form.description,
    type: form.type,
    upc: form.upc,
    departmentId: form.departmentId,
    categoryId: form.categoryId,
    binLocation: form.binLocation,
  });

  const save = async (sections: Record<string, unknown>) => {
    setBusy(true);

    try {
      const saved = productId
        ? await mastersApi.products.update(productId, { productId, ...sections })
        : await mastersApi.products.create({ locationId, general: general(), regularPrice: form.regularPrice });

      setForm(saved);
      onSaved();
      toast({ title: productId ? 'Saved' : 'Item created' });
    } catch (error) {
      toast({ title: 'Not saved', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!productId) return;

    setBusy(true);

    try {
      await mastersApi.products.remove(productId);
      toast({ title: 'Deleted', description: 'It can be brought back from Undelete items.' });
      onSaved();
      onClose();
    } catch (error) {
      toast({ title: 'Not deleted', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const clone = async () => {
    if (!productId) return;

    const code = window.prompt('Stock code for the copy');
    if (!code) return;

    try {
      const created = await mastersApi.products.clone(productId, code);
      setForm(created);
      onSaved();
      toast({ title: 'Copied', description: 'Stock and barcode are not copied.' });
    } catch (error) {
      toast({ title: 'Not copied', description: describe(error), variant: 'destructive' });
    }
  };

  const restore = async () => {
    try {
      await mastersApi.products.restore(form.id);
      onSaved();
      toast({ title: 'Restored' });
    } catch (error) {
      toast({ title: 'Not restored', description: describe(error), variant: 'destructive' });
    }
  };

  const disabled = !canWrite || busy;

  const saveButton = (sections: () => Record<string, unknown>) =>
    canWrite ? (
      <button type="button" className="underline" disabled={busy} onClick={() => void save(sections())}>
        Save
      </button>
    ) : null;

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-sm font-semibold">
          {productId ? `${form.stockCode} — ${form.name}` : 'New item'}
          {form.isDeleted ? <span className="pos-badge ml-2 text-[rgb(var(--negative))]">Deleted</span> : null}
        </h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Close
        </button>
      </div>

      <FormSection title="General" actions={saveButton(() => ({ general: general() }))}>
        <TextField
          label="Stock code"
          value={form.stockCode}
          onChange={(v) => patch({ stockCode: v })}
          disabled={disabled}
          autoFocus
        />
        <TextField label="Description" value={form.name} onChange={(v) => patch({ name: v })} disabled={disabled} />
        <TextField
          label="Long description"
          value={form.description ?? ''}
          onChange={(v) => patch({ description: v })}
          disabled={disabled}
        />
        <SelectField
          label="Type"
          value={form.type}
          options={productTypes.map((t) => ({ value: t, label: t }))}
          onChange={(v) => patch({ type: (v || 'Standard') as ProductType })}
          disabled={disabled}
          hint="A matrix or kit item is given its grid or components on its own screen."
        />
        <TextField label="Barcode (UPC)" value={form.upc ?? ''} onChange={(v) => patch({ upc: v })} disabled={disabled} />
        <SelectField
          label="Department"
          value={form.departmentId ?? ''}
          options={[{ value: '', label: '— none —' }, ...departments.map((d) => ({ value: d.id, label: d.name }))]}
          onChange={(v) => patch({ departmentId: v || null })}
          disabled={disabled}
        />
        <SelectField
          label="Category"
          value={form.categoryId ?? ''}
          options={[{ value: '', label: '— none —' }, ...categories.map((c) => ({ value: c.id, label: c.name }))]}
          onChange={(v) => patch({ categoryId: v || null })}
          disabled={disabled}
        />
        <TextField
          label="Bin location"
          value={form.binLocation ?? ''}
          onChange={(v) => patch({ binLocation: v })}
          disabled={disabled}
        />
      </FormSection>

      {productId ? (
        <>
          <FormSection
            title="Taxes"
            hint="A tax is charged only if it is switched on here and on the POS tab of Setup."
            actions={saveButton(() => ({ tax: { tax1Applies: form.tax1Applies, tax2Applies: form.tax2Applies } }))}
          >
            <CheckField
              label="Tax 1 applies"
              checked={form.tax1Applies}
              onChange={(v) => patch({ tax1Applies: v })}
              disabled={disabled}
            />
            <CheckField
              label="Tax 2 applies"
              checked={form.tax2Applies}
              onChange={(v) => patch({ tax2Applies: v })}
              disabled={disabled}
            />
            {form.type === 'GiftCard' ? (
              <p className="text-xs text-[rgb(var(--warning))]">
                A gift card is never taxed here — the tax is charged when the card is spent.
              </p>
            ) : null}
          </FormSection>

          <FormSection
            title="Pricing"
            hint="Level prices, break points, the sale window and the bonus rule are saved together."
            actions={saveButton(() => ({
              pricing: {
                regularPrice: form.regularPrice,
                lastCost: form.lastCost,
                levels: form.levels,
                breaks: form.breaks,
                sale: form.sale,
                bonus: form.bonus,
              },
            }))}
          >
            <NumberField
              label="Regular price"
              value={form.regularPrice}
              onChange={(v) => patch({ regularPrice: v })}
              disabled={disabled}
            />
            <NumberField
              label="Last cost"
              value={form.lastCost}
              onChange={(v) => patch({ lastCost: v })}
              disabled={disabled}
              step="0.001"
            />
            <p className="text-xs text-[rgb(var(--text-muted))]">
              Average cost {formatCurrency(form.avgCost)} · margin {form.grossMarginPct.toFixed(1)}% — both maintained by
              the stock ledger and not editable here.
            </p>

            {[1, 2, 3, 4].map((level) => {
              const existing = form.levels.find((l) => l.level === level);

              return (
                <NumberField
                  key={level}
                  label={`Level ${level} price`}
                  value={existing?.price ?? 0}
                  disabled={disabled}
                  hint={level === 1 ? 'Zero means "no level price" — the item falls through to the regular price.' : undefined}
                  onChange={(price) =>
                    patch({
                      levels: [...form.levels.filter((l) => l.level !== level), { level, price }].sort(
                        (a, b) => a.level - b.level,
                      ),
                    })
                  }
                />
              );
            })}

            <hr className="border-[rgb(var(--border))]" />

            <p className="text-xs font-medium">Break points</p>
            <p className="text-xs text-[rgb(var(--text-muted))]">
              At or above the quantity, the item sells at that level&apos;s price. Break points outrank the sale window.
            </p>

            {form.breaks.map((row, index) => (
              <div key={`${row.level}-${index}`} className="flex items-end gap-2">
                <div className="flex-1">
                  <NumberField
                    label="From quantity"
                    value={row.minQuantity}
                    step="1"
                    disabled={disabled}
                    onChange={(minQuantity) => patch({ breaks: replaceAt(form.breaks, index, { ...row, minQuantity }) })}
                  />
                </div>
                <div className="w-28">
                  <SelectField
                    label="Level"
                    value={String(row.level)}
                    options={[2, 3, 4].map((level) => ({ value: String(level), label: `Level ${level}` }))}
                    onChange={(level) => patch({ breaks: replaceAt(form.breaks, index, { ...row, level: Number(level) || 2 }) })}
                    disabled={disabled}
                  />
                </div>
                {!disabled ? (
                  <button
                    type="button"
                    className="pos-button mb-0.5"
                    onClick={() => patch({ breaks: form.breaks.filter((_, i) => i !== index) })}
                  >
                    Remove
                  </button>
                ) : null}
              </div>
            ))}

            {!disabled ? (
              <button
                type="button"
                className="pos-button"
                onClick={() => patch({ breaks: [...form.breaks, { level: 2, minQuantity: 1 }] })}
              >
                Add a break point
              </button>
            ) : null}

            <hr className="border-[rgb(var(--border))]" />

            <CheckField
              label="On sale for a period"
              checked={form.sale !== null}
              disabled={disabled}
              onChange={(on) =>
                patch({
                  sale: on ? { discountPct: 10, startsOn: today(), endsOn: today() } : null,
                })
              }
              hint="Outside the window the item returns to its usual price by itself — nobody has to remember to change it back."
            />

            {form.sale ? (
              <>
                <NumberField
                  label="Sale discount %"
                  value={form.sale.discountPct}
                  disabled={disabled}
                  onChange={(discountPct) => patch({ sale: { ...form.sale!, discountPct } })}
                />
                <TextField
                  label="Starts on"
                  value={form.sale.startsOn}
                  disabled={disabled}
                  onChange={(startsOn) => patch({ sale: { ...form.sale!, startsOn } })}
                  hint="yyyy-mm-dd"
                />
                <TextField
                  label="Ends on"
                  value={form.sale.endsOn}
                  disabled={disabled}
                  onChange={(endsOn) => patch({ sale: { ...form.sale!, endsOn } })}
                  hint="yyyy-mm-dd. The last day the sale price applies."
                />
              </>
            ) : null}

            <hr className="border-[rgb(var(--border))]" />

            <CheckField
              label="Buy X, get Y free"
              checked={form.bonus !== null}
              disabled={disabled}
              onChange={(on) => patch({ bonus: on ? { buyQty: 3, freeQty: 1 } : null })}
            />

            {form.bonus ? (
              <>
                <NumberField
                  label="Buy quantity"
                  value={form.bonus.buyQty}
                  step="1"
                  disabled={disabled}
                  onChange={(buyQty) => patch({ bonus: { ...form.bonus!, buyQty } })}
                />
                <NumberField
                  label="Free quantity"
                  value={form.bonus.freeQty}
                  step="1"
                  disabled={disabled}
                  onChange={(freeQty) => patch({ bonus: { ...form.bonus!, freeQty } })}
                  hint="Must be less than the buy quantity, or every item would be free."
                />
              </>
            ) : null}
          </FormSection>

          <FormSection
            title="Ordering"
            actions={saveButton(() => ({
              ordering: {
                baseStock: form.baseStock,
                reorderPoint: form.reorderPoint,
                reorderQty: form.reorderQty,
                caseQty: form.caseQty,
                shipWeight: form.shipWeight,
                suppliers: form.suppliers,
              },
            }))}
          >
            <NumberField
              label="Base stock"
              value={form.baseStock}
              onChange={(v) => patch({ baseStock: v })}
              step="1"
              disabled={disabled}
            />
            <NumberField
              label="Reorder point"
              value={form.reorderPoint}
              onChange={(v) => patch({ reorderPoint: v })}
              step="1"
              disabled={disabled}
            />
            <NumberField
              label="Reorder quantity"
              value={form.reorderQty}
              onChange={(v) => patch({ reorderQty: v })}
              step="1"
              disabled={disabled}
            />
            <NumberField label="Case quantity" value={form.caseQty} onChange={(v) => patch({ caseQty: v })} disabled={disabled} />
            <NumberField
              label="Ship weight"
              value={form.shipWeight}
              onChange={(v) => patch({ shipWeight: v })}
              step="0.001"
              disabled={disabled}
            />

            <hr className="border-[rgb(var(--border))]" />

            <p className="text-xs font-medium">Suppliers</p>
            <p className="text-xs text-[rgb(var(--text-muted))]">
              Rank 1 is the preferred source, and is the one automatic reordering buys from.
            </p>

            {form.suppliers.map((supplier, index) => (
              <div key={supplier.supplierId} className="space-y-1 border-b border-[rgb(var(--border))] pb-2">
                <div className="flex items-center justify-between text-sm">
                  <span>{supplier.supplierName}</span>
                  {!disabled ? (
                    <button
                      type="button"
                      className="text-xs underline"
                      onClick={() => patch({ suppliers: form.suppliers.filter((_, i) => i !== index) })}
                    >
                      Unlink
                    </button>
                  ) : null}
                </div>

                <div className="grid grid-cols-2 gap-2">
                  <NumberField
                    label="Rank"
                    value={supplier.rank}
                    step="1"
                    disabled={disabled}
                    onChange={(rank) => patch({ suppliers: replaceAt(form.suppliers, index, { ...supplier, rank }) })}
                  />
                  <NumberField
                    label="Cost"
                    value={supplier.cost}
                    step="0.001"
                    disabled={disabled}
                    onChange={(cost) => patch({ suppliers: replaceAt(form.suppliers, index, { ...supplier, cost }) })}
                  />
                  <TextField
                    label="Their stock code"
                    value={supplier.reorderNumber ?? ''}
                    disabled={disabled}
                    onChange={(reorderNumber) =>
                      patch({ suppliers: replaceAt(form.suppliers, index, { ...supplier, reorderNumber }) })
                    }
                  />
                  <NumberField
                    label="Case quantity"
                    value={supplier.caseQty}
                    disabled={disabled}
                    onChange={(caseQty) => patch({ suppliers: replaceAt(form.suppliers, index, { ...supplier, caseQty }) })}
                  />
                </div>
              </div>
            ))}

            {form.suppliers.length === 0 ? (
              <p className="text-xs text-[rgb(var(--text-muted))]">No supplier linked yet.</p>
            ) : null}

            {!disabled ? (
              <RecordPicker
                label="Link a supplier"
                value={null}
                placeholder="Company or supplier number"
                search={(term) =>
                  mastersApi.suppliers
                    .browse(locationId, { search: term, pageSize: 15 })
                    .then((page) =>
                      page.items
                        .filter((s) => !form.suppliers.some((linked) => linked.supplierId === s.id))
                        .map((s) => ({ id: s.id, code: s.supplierNumber, name: s.company })),
                    )
                }
                onChange={(picked) => {
                  if (!picked) return;

                  patch({
                    suppliers: [
                      ...form.suppliers,
                      {
                        supplierId: picked.id,
                        supplierName: picked.name,
                        // Next rank down, so linking a second source does not silently displace the
                        // preferred one.
                        rank: form.suppliers.length + 1,
                        cost: form.lastCost,
                        reorderNumber: null,
                        caseQty: form.caseQty,
                        minimumOrderQty: 0,
                      },
                    ],
                  });
                }}
              />
            ) : null}
          </FormSection>

          <FormSection
            title="Messages and notes"
            hint="The POS message is shown to the cashier when the item is rung; the invoice message is printed."
            actions={saveButton(() => ({
              messages: {
                posMessage: form.posMessage,
                invoiceMessage: form.invoiceMessage,
                notes: form.notes,
              },
            }))}
          >
            <TextField
              label="POS message"
              value={form.posMessage ?? ''}
              onChange={(v) => patch({ posMessage: v })}
              disabled={disabled}
            />
            <TextField
              label="Invoice message"
              value={form.invoiceMessage ?? ''}
              onChange={(v) => patch({ invoiceMessage: v })}
              disabled={disabled}
            />
            <TextField label="Notes" value={form.notes ?? ''} onChange={(v) => patch({ notes: v })} disabled={disabled} />
          </FormSection>

          <FormSection
            title="Related items"
            hint="A substitute is offered when this item is out of stock; a tag-along is added with it."
            actions={saveButton(() => ({
              links: {
                substituteProductId: form.substitute?.id ?? null,
                tagAlongProductId: form.tagAlong?.id ?? null,
                parentProductId: form.parent?.id ?? null,
              },
            }))}
          >
            <RecordPicker
              label="Substitute"
              value={toPick(form.substitute)}
              disabled={disabled}
              search={(term) => searchItems(locationId, term, form.id)}
              onChange={(picked) => patch({ substitute: fromPick(picked) })}
            />
            <RecordPicker
              label="Tag-along"
              value={toPick(form.tagAlong)}
              disabled={disabled}
              search={(term) => searchItems(locationId, term, form.id)}
              onChange={(picked) => patch({ tagAlong: fromPick(picked) })}
              hint="Added to the sale automatically whenever this item is rung — a deposit or a fitting."
            />
            <RecordPicker
              label="Parent (case break)"
              value={toPick(form.parent)}
              disabled={disabled}
              search={(term) => searchItems(locationId, term, form.id)}
              onChange={(picked) => patch({ parent: fromPick(picked) })}
              hint="Set on the individual unit, pointing at the case it is broken out of."
            />
          </FormSection>

          {form.type === 'Matrix' ? <MatrixEditor productId={form.id} canWrite={canWrite} /> : null}

          <div className="mb-6 flex flex-wrap gap-2">
            {canWrite ? (
              <button type="button" className="pos-button" onClick={() => void clone()} disabled={busy}>
                Copy to a new code
              </button>
            ) : null}

            {canDelete && !form.isDeleted ? (
              <button
                type="button"
                className="pos-button text-[rgb(var(--negative))]"
                onClick={() => void remove()}
                disabled={busy}
              >
                Delete
              </button>
            ) : null}

            {canDelete && form.isDeleted ? (
              <button type="button" className="pos-button" disabled={busy} onClick={() => void restore()}>
                Restore
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="px-1 text-xs text-[rgb(var(--text-muted))]">
          Save the item first — pricing, ordering and messages open once it exists.
        </p>
      )}
    </div>
  );
}
