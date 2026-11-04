'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Save, Scale, Settings2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { ConflictDialog } from '@/components/masters';
import { SETTINGS_FIELDS, SupplySettingsDialog } from '@/components/supply';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input } from '@/components/ui/field';
import { Chip, PageHeader, Pager, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable, type Column } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { formatCount, formatDate, formatMoney, formatQuantity } from '@/lib/format';
import { parseNumber, textOrNull } from '@/lib/forms';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import { expiryText } from '@/lib/supply';
import type { BatchRow, ItemRow } from '@/lib/types';

type Tab = 'stock' | 'expiring' | 'expired';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'stock', label: 'Stock by batch' },
  { key: 'expiring', label: 'Expiring soon' },
  { key: 'expired', label: 'Expired' },
];

const PAGE_SIZE = 100;

export default function BatchesPage() {
  const { defaultStoreId } = useStore();
  const [tab, setTab] = useState<Tab>('stock');
  const [editing, setEditing] = useState<BatchRow | 'new' | null>(null);
  const [adjusting, setAdjusting] = useState<BatchRow | null>(null);
  const [settings, setSettings] = useState(false);
  const { can } = useSession();

  const open = (b: BatchRow) => setEditing(b);
  return (
    <div>
      <PageHeader title="Batches and expiry" />
      <Toolbar>
        <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
          Add a batch
        </Button>
        {defaultStoreId && (
          <Button icon={Settings2} onClick={() => setSettings(true)}>
            Expiry settings
          </Button>
        )}
      </Toolbar>
      <Tabs label="Batches" tabs={TABS} value={tab} onChange={setTab} className="mb-4" />
      {tab === 'stock' && <StockByBatch onOpen={open} onAdjust={can(P.inventoryAdjust) ? setAdjusting : undefined} />}
      {tab === 'expiring' && <Expiry expired={false} onOpen={open} onAdjust={can(P.inventoryAdjust) ? setAdjusting : undefined} />}
      {tab === 'expired' && <Expiry expired onOpen={open} onAdjust={can(P.inventoryAdjust) ? setAdjusting : undefined} />}
      {editing && <BatchDialog batch={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {adjusting && <AdjustDialog batch={adjusting} onClose={() => setAdjusting(null)} />}
      {settings && defaultStoreId && <SupplySettingsDialog storeId={defaultStoreId} fields={SETTINGS_FIELDS.batches} title="Expiry settings" onClose={() => setSettings(false)} />}
    </div>
  );
}

function columns(symbol: string, onAdjust?: (b: BatchRow) => void): Column<BatchRow>[] {
  const list: Column<BatchRow>[] = [
    { key: 'plu', header: 'Stock code', cell: (b) => <span className="font-semibold">{b.plu}</span> },
    { key: 'name', header: 'Item', cell: (b) => b.itemName, className: 'wrap min-w-[150px]', hideBelow: 'sm' },
    { key: 'batch', header: 'Batch', cell: (b) => b.batchNumber },
    { key: 'where', header: 'Location', cell: (b) => `${b.storeCode} · ${b.locationCode}`, hideBelow: 'xl' },
    { key: 'bin', header: 'Bin', cell: (b) => b.bin ?? '', hideBelow: '2xl' },
    { key: 'expires', header: 'Expires', cell: (b) => formatDate(b.expiresOn), hideBelow: 'md' },
    {
      key: 'left',
      header: 'Left',
      cell: (b) => {
        const e = expiryText(b.daysLeft);
        return <Chip tone={e.tone}>{e.text}</Chip>;
      },
    },
    { key: 'onhand', header: 'On hand', numeric: true, cell: (b) => formatQuantity(b.onHand) },
    { key: 'value', header: 'Value', numeric: true, cell: (b) => formatMoney(b.value, symbol), hideBelow: 'sm' },
    { key: 'supplier', header: 'Supplier', cell: (b) => b.supplier ?? '', hideBelow: '2xl' },
  ];
  if (onAdjust)
    list.push({
      key: 'adjust',
      header: '',
      cell: (b) => (
        <button
          type="button"
          className="btn h-10 min-h-0 px-3"
          onClick={(e) => {
            e.stopPropagation();
            onAdjust(b);
          }}
        >
          Adjust
        </button>
      ),
    });
  return list;
}

function StockByBatch({ onOpen, onAdjust }: { onOpen(b: BatchRow): void; onAdjust?: (b: BatchRow) => void }) {
  const { storeId, currencySymbol: symbol } = useStore();
  const [text, setText] = useState('');
  const [all, setAll] = useState(false);
  const [page, setPage] = useState(1);
  const search = useDebounced(text.trim());
  useEffect(() => setPage(1), [storeId, search, all]);
  const query = useQuery({
    queryKey: ['stock-batches', storeId, search, all, page],
    queryFn: ({ signal }) => api.stockBatches({ storeId, search, inStock: all ? false : undefined, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  const rows = query.data?.items;
  return (
    <>
      <Toolbar>
        <SearchBox value={text} onChange={setText} placeholder="Stock code, item or batch number" />
        <Checkbox label="Empty batches too" checked={all} onChange={setAll} />
      </Toolbar>
      <DataTable<BatchRow>
        rows={rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(b) => b.id}
        onRowClick={onOpen}
        caption="Stock by batch"
        showTotals={!!rows && rows.length > 1}
        empty={<EmptyState title="No batches">Batches are made when goods of items kept in batches are received.</EmptyState>}
        columns={columns(symbol, onAdjust).map((c) =>
          c.key === 'onhand' ? { ...c, total: formatQuantity((rows ?? []).reduce((s, b) => s + b.onHand, 0)) } : c.key === 'value' ? { ...c, total: formatMoney((rows ?? []).reduce((s, b) => s + b.value, 0), symbol) } : c,
        )}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="batches" />}
      />
    </>
  );
}

function Expiry({ expired, onOpen, onAdjust }: { expired: boolean; onOpen(b: BatchRow): void; onAdjust?: (b: BatchRow) => void }) {
  const { storeId, currencySymbol: symbol } = useStore();
  const [daysText, setDaysText] = useState('');
  const days = useDebounced(parseNumber(daysText) ?? undefined);
  const query = useQuery({
    queryKey: ['batches-expiry', expired, storeId, days],
    queryFn: () => (expired ? api.expiredBatches({ storeId }) : api.expiringBatches({ storeId, days })),
  });
  const report = query.data;
  return (
    <>
      {!expired && (
        <Toolbar>
          <label className="flex items-center gap-2.5">
            <span className="text-muted">Within</span>
            <Input aria-label="Days ahead" inputMode="numeric" className="w-[90px]" placeholder={report ? String(report.days) : '30'} value={daysText} onChange={(e) => setDaysText(e.target.value)} />
            <span className="text-muted">days</span>
          </label>
        </Toolbar>
      )}
      {report && (
        <p className="mb-3 px-1 text-ink" role="status">
          {expired
            ? `${formatCount(report.rows.length)} expired batches still hold ${formatQuantity(report.units)} pieces worth ${formatMoney(report.value, symbol)}.`
            : `${formatCount(report.rows.length)} batches expire within ${report.days} days: ${formatQuantity(report.units)} pieces worth ${formatMoney(report.value, symbol)}.`}
        </p>
      )}
      <DataTable<BatchRow>
        rows={report?.rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(b) => b.id}
        onRowClick={onOpen}
        caption={expired ? 'Expired batches' : 'Batches expiring soon'}
        showTotals={!!report && report.rows.length > 1}
        empty={<EmptyState title={expired ? 'Nothing has expired' : 'Nothing expires in these days'} />}
        columns={columns(symbol, onAdjust).map((c) =>
          c.key === 'onhand' ? { ...c, total: formatQuantity(report?.units ?? 0) } : c.key === 'value' ? { ...c, total: formatMoney(report?.value ?? 0, symbol) } : c,
        )}
      />
    </>
  );
}

/** A new batch of an item kept in batches, or a batch's dates and note changed. */
function BatchDialog({ batch, onClose }: { batch: BatchRow | null; onClose(): void }) {
  const { storeId } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [item, setItem] = useState<ItemRow | null>(null);
  const [itemText, setItemText] = useState('');
  const [number, setNumber] = useState(batch?.batchNumber ?? '');
  const [expiresOn, setExpiresOn] = useState(batch?.expiresOn ?? '');
  const [madeOn, setMadeOn] = useState(batch?.madeOn ?? '');
  const [note, setNote] = useState(batch?.note ?? '');
  const [error, setError] = useState<unknown>(null);
  const [conflict, setConflict] = useState(false);
  const itemSearch = useDebounced(itemText.trim());
  const items = useQuery({
    queryKey: ['items', 'batch-picker', storeId, itemSearch],
    queryFn: () => api.items({ storeId, search: itemSearch, pageSize: 8 }),
    enabled: !batch && itemSearch.length > 0,
  });

  const save = useMutation({
    mutationFn: () => {
      const body = { version: batch?.version ?? null, itemId: batch?.itemId ?? item?.id ?? null, batchNumber: number.trim(), expiresOn: expiresOn || null, madeOn: madeOn || null, note: textOrNull(note) };
      return batch ? api.saveBatch(batch.id, body) : api.addBatch(body);
    },
    onSuccess: (saved) => {
      toast.ok(`Batch ${saved.batchNumber} of ${saved.plu} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['stock-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['batches-expiry'] });
      onClose();
    },
    onError: (e) => (e instanceof ApiError && e.isConflict ? setConflict(true) : setError(e)),
  });

  function submit() {
    if (!batch && !item) return setError(new Error('Choose the item.'));
    if (!number.trim()) return setError(new Error('Enter the batch number.'));
    if (madeOn && expiresOn && expiresOn < madeOn) return setError(new Error('A batch cannot expire before it was made.'));
    setError(null);
    save.mutate();
  }

  return (
    <Dialog
      open
      title={batch ? `Batch ${batch.batchNumber}` : 'Add a batch'}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button type="submit" variant="sun" icon={batch ? Pencil : Save} shortcut="Enter" busy={save.isPending}>
            Save
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        {batch ? (
          <p className="text-ink">
            <span className="font-semibold">{batch.plu}</span> {batch.itemName} · {formatQuantity(batch.onHand)} on hand
          </p>
        ) : (
          <Field label="Item" htmlFor="batch-item" hint={item ? `${item.plu} ${item.name} · ${item.storeCode ?? ''}` : 'Type a stock code or name, then choose the item.'}>
            <Input id="batch-item" value={itemText} onChange={(e) => setItemText(e.target.value)} placeholder="Stock code or description" />
            {items.data && items.data.items.length > 0 && !item && (
              <ul className="mt-1 rounded-control border border-line bg-card" role="listbox" aria-label="Items found">
                {items.data.items.map((i) => (
                  <li key={i.id}>
                    <button type="button" className="w-full px-3 py-2 text-left hover:bg-stone-light" onClick={() => setItem(i)}>
                      {i.plu} · {i.name} {i.storeCode ? `· ${i.storeCode}` : ''}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </Field>
        )}
        <Field label="Batch number" htmlFor="batch-number">
          <Input id="batch-number" maxLength={30} value={number} onChange={(e) => setNumber(e.target.value)} />
        </Field>
        <Field label="Expires" htmlFor="batch-expires">
          <input id="batch-expires" type="date" className="input w-[180px]" value={expiresOn} onChange={(e) => setExpiresOn(e.target.value)} />
        </Field>
        <Field label="Made on" htmlFor="batch-made">
          <input id="batch-made" type="date" className="input w-[180px]" value={madeOn} onChange={(e) => setMadeOn(e.target.value)} />
        </Field>
        <Field label="Note" htmlFor="batch-note">
          <Input id="batch-note" maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} />
        </Field>
      </div>
      <FormError error={error} />
      <ConflictDialog open={conflict} what="batch" onReload={onClose} onClose={() => setConflict(false)} />
    </Dialog>
  );
}

/** A batch's stock changed by hand (expired goods thrown away, a count of one batch). */
function AdjustDialog({ batch, onClose }: { batch: BatchRow; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [quantity, setQuantity] = useState('');
  const [setTo, setSetTo] = useState(false);
  const [reason, setReason] = useState(batch.daysLeft !== null && batch.daysLeft < 0 ? 'Expired, thrown away' : '');
  const [error, setError] = useState<unknown>(null);
  const adjust = useMutation({
    mutationFn: (n: number) => api.adjustBatch(batch.id, { quantity: n, setTo, reason: textOrNull(reason) }),
    onSuccess: (saved) => {
      toast.ok(`Batch ${saved.batchNumber} now holds ${formatQuantity(saved.onHand)}.`);
      void queryClient.invalidateQueries({ queryKey: ['stock-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['batches-expiry'] });
      onClose();
    },
    onError: setError,
  });
  return (
    <Dialog
      open
      width="sm"
      title={`Adjust batch ${batch.batchNumber}`}
      onClose={onClose}
      onSubmit={() => {
        const n = parseNumber(quantity);
        if (n === null) return setError(new Error('Enter a quantity.'));
        adjust.mutate(n);
      }}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Scale} shortcut="Enter" busy={adjust.isPending}>
            Adjust
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <p className="mb-3 text-ink">
        {batch.plu} {batch.itemName}: {formatQuantity(batch.onHand)} on hand in this batch.
      </p>
      <div className="space-y-3">
        <Field label={setTo ? 'Set the stock to' : 'Add (or take off with -)'} htmlFor="adjust-quantity">
          <Input id="adjust-quantity" inputMode="decimal" className="max-w-[160px]" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
        </Field>
        <Checkbox label="Set to this count" checked={setTo} onChange={setSetTo} />
        <Field label="Reason" htmlFor="adjust-reason">
          <Input id="adjust-reason" maxLength={80} value={reason} onChange={(e) => setReason(e.target.value)} />
        </Field>
      </div>
      <FormError error={error} />
    </Dialog>
  );
}
