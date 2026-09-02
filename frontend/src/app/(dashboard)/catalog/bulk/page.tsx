'use client';

import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { formatCurrency , recordIdFrom} from '@/lib/utils';
import { productTypes } from '@/types/masters';
import type {
  BulkAdjustMethod,
  BulkFilter,
  BulkPricePreview,
  BulkPriceTarget,
  PriceRounding,
  ProductType,
} from '@/types/masters';

const inputClass =
  'pos-input';

const methodLabel: Record<BulkAdjustMethod, string> = {
  Percentage: 'Up or down by a percentage',
  FixedAmount: 'Up or down by an amount',
  SetTo: 'Set everything to',
  MarkupOnCost: 'Price from average cost, plus a margin',
};

const roundingLabel: Record<PriceRounding, string> = {
  NearestCent: 'To the penny',
  EndsIn99: 'Ending .99',
  EndsIn95: 'Ending .95',
  WholeNumber: 'Whole units',
  None: 'No rounding',
};

/** What the amount box means depends on the method, so the suffix changes with it. */
const amountSuffix: Record<BulkAdjustMethod, string> = {
  Percentage: '%',
  FixedAmount: 'currency',
  SetTo: 'currency',
  MarkupOnCost: '% margin',
};

/**
 * Batch price and tax changes (guide p.45).
 *
 * The preview is not optional. This is the most destructive screen in the back office — there is no
 * undo short of a restore from backup — so nothing is written until someone has seen the numbers.
 */
export default function BulkAdjustPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canAdjust = auth.can('catalog.bulk_adjust');

  const [departmentId, setDepartmentId] = useState<number | ''>('');
  const [categoryId, setCategoryId] = useState<number | ''>('');
  const [supplierId, setSupplierId] = useState<number | ''>('');
  const [type, setType] = useState<ProductType | ''>('');
  const [search, setSearch] = useState('');

  const [target, setTarget] = useState<BulkPriceTarget>('RegularPrice');
  const [method, setMethod] = useState<BulkAdjustMethod>('Percentage');
  const [amount, setAmount] = useState(0);
  const [rounding, setRounding] = useState<PriceRounding>('NearestCent');

  const [preview, setPreview] = useState<BulkPricePreview | null>(null);
  const [busy, setBusy] = useState(false);

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

  const { data: suppliers } = useQuery({
    queryKey: ['suppliers-for-bulk', locationId],
    queryFn: () => mastersApi.suppliers.browse(locationId!, { pageSize: 200 }),
    enabled: Boolean(locationId),
  });

  const filter = (): BulkFilter => ({
    locationId: locationId!,
    departmentId: departmentId || null,
    categoryId: categoryId || null,
    supplierId: supplierId || null,
    type: type || null,
    search: search || null,
  });

  // Any change to the selection or the sum invalidates what is on screen. Applying a preview that
  // no longer matches the form is exactly the mistake this screen exists to prevent.
  const invalidate = () => setPreview(null);

  const runPreview = async () => {
    if (!locationId) return;
    setBusy(true);

    try {
      setPreview(await mastersApi.bulk.previewPrice(filter(), target, method, amount, rounding));
    } catch (error) {
      setPreview(null);
      toast({ title: 'Could not work that out', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const apply = async () => {
    if (!locationId || !preview) return;

    const confirmed = window.confirm(
      `This will change ${preview.matchedCount} item(s). There is no undo. Continue?`,
    );

    if (!confirmed) return;

    setBusy(true);

    try {
      const changed = await mastersApi.bulk.applyPrice(filter(), target, method, amount, rounding);
      setPreview(null);
      toast({ title: `${changed} item(s) changed` });
    } catch (error) {
      toast({ title: 'Nothing changed', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const applyTax = async (tax1: boolean | null, tax2: boolean | null) => {
    if (!locationId) return;

    const confirmed = window.confirm('This changes the tax flags on every matching item. Continue?');
    if (!confirmed) return;

    setBusy(true);

    try {
      const changed = await mastersApi.bulk.applyTax(filter(), tax1, tax2);
      toast({ title: `${changed} item(s) changed` });
    } catch (error) {
      toast({ title: 'Nothing changed', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  if (!canAdjust) {
    return (
      <div className="p-6">
        <h1 className="text-h1 font-semibold">Batch changes</h1>
        <p className="mt-2 text-body text-ink-muted">
          You do not have permission to make batch changes to the catalogue.
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-4 p-6">
      <header>
        <h1 className="text-h1 font-semibold">Batch changes</h1>
        <p className="text-body text-ink-muted">
          Repricing and tax flags across a selection of items. Every change is written at once and cannot be
          undone, so check the preview.
        </p>
      </header>

      <section className="pos-panel">
        <div className="pos-panel-header">
          <span>Which items</span>
        </div>
        <div className="flex flex-wrap gap-3 p-3">
          <label className="flex flex-col gap-1 text-label">
            Department
            <select
              className={inputClass}
              value={departmentId}
              onChange={(event) => {
                setDepartmentId(recordIdFrom(event.target.value));
                invalidate();
              }}
            >
              <option value="">Any</option>
              {departments.map((department) => (
                <option key={String(department.id)} value={department.id}>
                  {department.name}
                </option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1 text-label">
            Category
            <select
              className={inputClass}
              value={categoryId}
              onChange={(event) => {
                setCategoryId(recordIdFrom(event.target.value));
                invalidate();
              }}
            >
              <option value="">Any</option>
              {categories.map((category) => (
                <option key={String(category.id)} value={category.id}>
                  {category.name}
                </option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1 text-label">
            Supplier
            <select
              className={inputClass}
              value={supplierId}
              onChange={(event) => {
                setSupplierId(recordIdFrom(event.target.value));
                invalidate();
              }}
            >
              <option value="">Any</option>
              {(suppliers?.items ?? []).map((supplier) => (
                <option key={String(supplier.id)} value={supplier.id}>
                  {supplier.company}
                </option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1 text-label">
            Type
            <select
              className={inputClass}
              value={type}
              onChange={(event) => {
                setType(event.target.value as ProductType | '');
                invalidate();
              }}
            >
              <option value="">Any</option>
              {productTypes.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1 text-label">
            Code or description contains
            <input
              className={`${inputClass} w-56`}
              value={search}
              onChange={(event) => {
                setSearch(event.target.value);
                invalidate();
              }}
            />
          </label>
        </div>
      </section>

      <section className="pos-panel">
        <div className="pos-panel-header">
          <span>Reprice</span>
        </div>
        <div className="space-y-3 p-3">
          <div className="flex flex-wrap items-end gap-3">
            <label className="flex flex-col gap-1 text-label">
              Change
              <select
                className={inputClass}
                value={target}
                onChange={(event) => {
                  setTarget(event.target.value as BulkPriceTarget);
                  invalidate();
                }}
              >
                <option value="RegularPrice">The shelf price</option>
                <option value="LastCost">The buying cost</option>
              </select>
            </label>

            <label className="flex flex-col gap-1 text-label">
              How
              <select
                className={inputClass}
                value={method}
                onChange={(event) => {
                  setMethod(event.target.value as BulkAdjustMethod);
                  invalidate();
                }}
              >
                {(Object.keys(methodLabel) as BulkAdjustMethod[]).map((option) => (
                  <option key={option} value={option}>
                    {methodLabel[option]}
                  </option>
                ))}
              </select>
            </label>

            <label className="flex flex-col gap-1 text-label">
              Amount ({amountSuffix[method]})
              <input
                type="number"
                step="0.01"
                className={`${inputClass} w-32`}
                value={amount}
                onChange={(event) => {
                  setAmount(Number(event.target.value) || 0);
                  invalidate();
                }}
              />
            </label>

            <label className="flex flex-col gap-1 text-label">
              Round
              <select
                className={inputClass}
                value={rounding}
                onChange={(event) => {
                  setRounding(event.target.value as PriceRounding);
                  invalidate();
                }}
              >
                {(Object.keys(roundingLabel) as PriceRounding[]).map((option) => (
                  <option key={option} value={option}>
                    {roundingLabel[option]}
                  </option>
                ))}
              </select>
            </label>

            <button type="button" className="pos-button" disabled={busy} onClick={() => void runPreview()}>
              Preview
            </button>

            <button
              type="button"
              className="pos-button-primary"
              disabled={busy || preview === null || preview.wouldGoNegative > 0}
              onClick={() => void apply()}
            >
              Apply
            </button>
          </div>

          <p className="text-label text-ink-muted">
            A negative percentage or amount is a reduction. Pricing from cost uses the average cost — what the
            stock on the shelf actually cost — rather than the last delivery&apos;s price, which can be an outlier.
          </p>

          {preview ? (
            <>
              <p className="text-body">
                {preview.matchedCount} item(s) match, showing {preview.shownCount}.
                {preview.wouldGoNegative > 0 ? (
                  <span className="ml-2 font-semibold text-negative">
                    ⚠ {preview.wouldGoNegative} would fall below zero — this cannot be applied.
                  </span>
                ) : null}
              </p>

              <div className="max-h-80 overflow-y-auto border border-subtle">
                <table className="w-full text-body">
                  <thead className="sticky top-0 bg-panel text-label">
                    <tr>
                      <th className="px-2 py-1 text-left">Code</th>
                      <th className="px-2 py-1 text-left">Description</th>
                      <th className="px-2 py-1 text-right">Now</th>
                      <th className="px-2 py-1 text-right">Becomes</th>
                      <th className="px-2 py-1 text-right">Avg cost</th>
                      <th className="px-2 py-1 text-right">Margin after</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.rows.map((row) => (
                      <tr key={row.productId} className="border-t border-subtle">
                        <td className="px-2 py-1">{row.stockCode}</td>
                        <td className="px-2 py-1">{row.name}</td>
                        <td className="px-2 py-1 text-right">{formatCurrency(row.current)}</td>
                        <td className="px-2 py-1 text-right font-semibold">{formatCurrency(row.proposed)}</td>
                        <td className="px-2 py-1 text-right">{formatCurrency(row.avgCost)}</td>
                        <td className="px-2 py-1 text-right">{row.proposedMarginPct.toFixed(1)}%</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          ) : (
            <p className="text-label text-ink-muted">
              Preview first — Apply stays disabled until you have seen what would change.
            </p>
          )}
        </div>
      </section>

      <section className="pos-panel">
        <div className="pos-panel-header">
          <span>Tax flags</span>
        </div>
        <div className="space-y-2 p-3">
          <p className="text-label text-ink-muted">
            Applies to the same selection. Each button changes one flag and leaves the other alone. A gift card
            is never made taxable, whatever is asked for — the tax is charged when the card is spent.
          </p>
          <div className="flex flex-wrap gap-2">
            <button type="button" className="pos-button" disabled={busy} onClick={() => void applyTax(true, null)}>
              Tax 1 on
            </button>
            <button type="button" className="pos-button" disabled={busy} onClick={() => void applyTax(false, null)}>
              Tax 1 off
            </button>
            <button type="button" className="pos-button" disabled={busy} onClick={() => void applyTax(null, true)}>
              Tax 2 on
            </button>
            <button type="button" className="pos-button" disabled={busy} onClick={() => void applyTax(null, false)}>
              Tax 2 off
            </button>
            <button type="button" className="pos-button" disabled={busy} onClick={() => void applyTax(false, false)}>
              Both off (exempt)
            </button>
          </div>
        </div>
      </section>
    </div>
  );
}

function describe(error: unknown): string {
  return error instanceof PosApiError ? error.problem.detail : 'Something went wrong.';
}
