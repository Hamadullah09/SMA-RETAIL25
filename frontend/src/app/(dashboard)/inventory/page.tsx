'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { BrowseFormShell, Field, FormSection, NumberField, TextField, LiveBadge } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { useLiveGrid } from '@/lib/inventory-hub';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import type { StockLevelRow } from '@/types/masters';
import { describeError } from '@/lib/errors';

/** Stock levels, receiving, adjustments and case-break (guide p.20–22, p.43). */
export default function InventoryPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canReceive = auth.can('inventory.receive');
  const canAdjust = auth.can('inventory.adjust');

  const [search, setSearch] = useState('');
  const [belowReorderOnly, setBelowReorderOnly] = useState(false);
  const [rows, setRows] = useState<StockLevelRow[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);

  const load = useCallback(
    async (append: boolean, from: string | null) => {
      if (!locationId) return;
      setLoading(true);

      try {
        const page = await mastersApi.inventory.stockLevels(locationId, {
          search,
          belowReorderOnly,
          cursor: from ?? undefined,
          pageSize: 100,
        });

        setRows((current) => (append ? [...current, ...page.items] : page.items));
        setCursor(page.nextCursor);
        setHasMore(page.hasMore);
      } catch (error) {
        toast({ title: 'Could not load stock levels', description: describeError(error), variant: 'destructive' });
      } finally {
        setLoading(false);
      }
    },
    [locationId, search, belowReorderOnly],
  );

  useEffect(() => {
    const timer = window.setTimeout(() => void load(false, null), 200);
    return () => window.clearTimeout(timer);
  }, [load]);

  const { connected, hasEverConnected, changed } = useLiveGrid('stock_level', locationId, setRows);

  const columns = useMemo<DataGridColumn<StockLevelRow>[]>(
    () => [
      { key: 'stockCode', header: 'Stock code', width: 110, render: (r) => r.stockCode, sortValue: (r) => r.stockCode },
      { key: 'name', header: 'Product', width: 240, render: (r) => r.productName, sortValue: (r) => r.productName },
      { key: 'onHand', header: 'On hand', width: 90, numeric: true, render: (r) => r.onHand, sortValue: (r) => r.onHand },
      { key: 'committed', header: 'Committed', width: 100, numeric: true, render: (r) => r.committed },
      { key: 'available', header: 'Available', width: 100, numeric: true, render: (r) => r.available },
      { key: 'onOrder', header: 'On order', width: 90, numeric: true, render: (r) => r.onOrder },
      { key: 'reorderPoint', header: 'Reorder pt', width: 90, numeric: true, render: (r) => r.reorderPoint },
    ],
    [],
  );

  return (
    <BrowseFormShell
      title="Stock"
      toolbar={
        <>
          {/* One badge, one vocabulary — this screen used to invent its own wording, and said it in
              red from the moment the page opened. */}
          <LiveBadge connected={connected} hasEverConnected={hasEverConnected} />

          {auth.can('inventory.transfer') ? (
            <Link className="pos-button" href="/inventory/transfers">
              Transfers
            </Link>
          ) : null}

          {auth.can('inventory.count') ? (
            <Link className="pos-button" href="/inventory/counts">
              Stock counts
            </Link>
          ) : null}
        </>
      }
      filters={
        <>
          <input
            className="pos-input w-64"
            aria-label="Search stock" placeholder="Stock code or name"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
          <label className="flex items-center gap-1 text-label">
            <input
              type="checkbox"
              checked={belowReorderOnly}
              onChange={(event) => setBelowReorderOnly(event.target.checked)}
            />
            At or below reorder point
          </label>
        </>
      }
      grid={
        <DataGrid
          gridId="stock-levels"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          recentlyChanged={changed}
          onRowActivate={(row) => setSelectedId(row.id)}
          loading={loading}
          emptyMessage="No items match these filters."
        />
      }
      form={
        selectedId !== null && locationId ? (
          <StockActionsPanel
            key={String(selectedId)}
            productId={selectedId}
            locationId={locationId}
            row={rows.find((r) => r.id === selectedId) ?? null}
            canReceive={canReceive}
            canAdjust={canAdjust}
            onClose={() => setSelectedId(null)}
            onChanged={() => void load(false, null)}
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

function StockActionsPanel({
  productId,
  locationId,
  row,
  canReceive,
  canAdjust,
  onClose,
  onChanged,
}: {
  productId: number;
  locationId: number;
  row: StockLevelRow | null;
  canReceive: boolean;
  canAdjust: boolean;
  onClose: () => void;
  onChanged: () => void;
}) {
  const [receiveQty, setReceiveQty] = useState(0);
  const [receiveCost, setReceiveCost] = useState(row?.avgCost ?? 0);
  const [adjustDelta, setAdjustDelta] = useState(0);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);

  const receive = async () => {
    if (receiveQty <= 0) {
      toast({ title: 'Enter a quantity greater than zero', variant: 'destructive' });
      return;
    }

    setBusy(true);
    try {
      await mastersApi.inventory.receive({ productId, locationId, quantity: receiveQty, unitCost: receiveCost });
      toast({ variant: 'success', title: 'Stock received' });
      setReceiveQty(0);
      onChanged();
    } catch (error) {
      toast({ title: 'Could not receive stock', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const adjust = async () => {
    if (adjustDelta === 0) {
      toast({ title: 'Enter a non-zero adjustment', variant: 'destructive' });
      return;
    }

    if (!reason.trim()) {
      toast({ title: 'A reason is required', variant: 'destructive' });
      return;
    }

    setBusy(true);
    try {
      await mastersApi.inventory.adjust({ productId, locationId, quantityDelta: adjustDelta, reason: reason.trim() });
      toast({ variant: 'success', title: 'Stock adjusted' });
      setAdjustDelta(0);
      setReason('');
      onChanged();
    } catch (error) {
      toast({ title: 'Could not adjust stock', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">{row ? `${row.stockCode} — ${row.productName}` : 'Selected item'}</h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Close
        </button>
      </div>

      {canReceive ? (
        <FormSection
          title="Receive stock"
          hint="Manual, item-by-item — for a shipment with no purchase order behind it. Use Purchasing to receive against a PO."
          actions={
            <button type="button" className="underline" disabled={busy} onClick={() => void receive()}>
              Receive
            </button>
          }
        >
          <NumberField label="Quantity" value={receiveQty} onChange={setReceiveQty} step="1" />
          <NumberField label="Unit cost" value={receiveCost} onChange={setReceiveCost} />
        </FormSection>
      ) : null}

      {canAdjust ? (
        <FormSection
          title="Adjust on hand"
          hint="A signed correction — found stock, shrinkage, damage. Cost is not affected."
          actions={
            <button type="button" className="underline" disabled={busy} onClick={() => void adjust()}>
              Adjust
            </button>
          }
        >
          <NumberField label="Quantity change" value={adjustDelta} onChange={setAdjustDelta} step="1" />
          <TextField label="Reason" value={reason} onChange={setReason} placeholder="e.g. Shrinkage — cycle count" />
        </FormSection>
      ) : null}
    </div>
  );
}
