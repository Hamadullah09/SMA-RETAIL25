'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BookCheck, Pencil, Plus, Save, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { ConflictDialog } from '@/components/masters';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog, SidePanel } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select } from '@/components/ui/field';
import { Chip, Detail, PageHeader, Pager, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { toBusinessDate } from '@/lib/dates';
import { formatCount, formatDate, formatMoney, formatQuantity, formatShortDateTime, formatUtc } from '@/lib/format';
import { parseNumber, textOrNull } from '@/lib/forms';
import { label, words } from '@/lib/labels';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import { CHARGE_KINDS, chargesTotal, LANDED_BASIS, LANDED_STATUS } from '@/lib/supply';
import type { LandedAllocation, LandedCharge, LandedCostBasis, LandedCostChargeKind, LandedCostDetail, LandedCostRow } from '@/lib/types';

const PAGE_SIZE = 50;

export default function LandedCostsPage() {
  const { can } = useSession();
  const { storeId, defaultStoreId, currencySymbol: symbol } = useStore();
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<string | null>(null);
  const [editing, setEditing] = useState<LandedCostDetail | 'new' | null>(null);
  const change = can(P.purchasingApprove);
  useEffect(() => setPage(1), [storeId, status]);
  const query = useQuery({
    queryKey: ['landed-costs', storeId, status, page],
    queryFn: ({ signal }) => api.landedCosts({ storeId, status, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });

  return (
    <div>
      <PageHeader title="Landed costs" aside={query.data && `${formatCount(query.data.total)} vouchers`} />
      <p className="mb-3 px-1 text-ink">Freight, duty, insurance and other charges for goods already received, spread over the received lines. Posting raises the cost of what is still in stock.</p>
      <Toolbar>
        {change && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" disabled={!defaultStoreId} onClick={() => setEditing('new')}>
            New voucher
          </Button>
        )}
        <label className="sr-only" htmlFor="landed-status">
          Status
        </label>
        <Select id="landed-status" className="w-[200px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every voucher</option>
          {Object.entries(LANDED_STATUS).map(([key, s]) => (
            <option key={key} value={key}>
              {s.label}
            </option>
          ))}
        </Select>
      </Toolbar>
      <DataTable<LandedCostRow>
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(v) => v.id}
        onRowClick={(v) => setOpen(v.id)}
        caption="Landed cost vouchers"
        empty={<EmptyState title="No landed cost vouchers" />}
        columns={[
          { key: 'n', header: 'Voucher', cell: (v) => <span className="font-semibold">{v.number}</span> },
          { key: 'status', header: 'Status', cell: (v) => <Chip tone={LANDED_STATUS[v.status]?.tone ?? 'plain'}>{LANDED_STATUS[v.status]?.label ?? words(v.status)}</Chip> },
          { key: 'date', header: 'Date', cell: (v) => formatDate(v.date) },
          { key: 'store', header: 'Store', cell: (v) => v.storeCode, hideBelow: 'xl' },
          { key: 'ref', header: 'Reference', cell: (v) => v.reference ?? '', hideBelow: 'md' },
          { key: 'basis', header: 'Spread', cell: (v) => label(LANDED_BASIS, v.basis), hideBelow: 'lg' },
          { key: 'receipts', header: 'Orders', numeric: true, cell: (v) => formatCount(v.receipts), hideBelow: 'sm' },
          { key: 'total', header: 'Total', numeric: true, cell: (v) => formatMoney(v.total, symbol) },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="vouchers" />}
      />
      {open && <VoucherPanel id={open} onClose={() => setOpen(null)} onEdit={setEditing} />}
      {editing && (
        <VoucherEditor
          voucher={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={(saved) => {
            setEditing(null);
            setOpen(saved.id);
          }}
        />
      )}
    </div>
  );
}

function VoucherPanel({ id, onClose, onEdit }: { id: string; onClose(): void; onEdit(v: LandedCostDetail): void }) {
  const { can } = useSession();
  const symbol = useStore().currencySymbol;
  const toast = useToast();
  const queryClient = useQueryClient();
  const [deleting, setDeleting] = useState(false);
  const [conflict, setConflict] = useState(false);
  const query = useQuery({ queryKey: ['landed-cost', id], queryFn: () => api.landedPreview(id), staleTime: 0 });
  const v = query.data;
  const change = can(P.purchasingApprove) && v?.status === 'Draft';
  const failed = (e: Error) => (e instanceof ApiError && e.isConflict ? setConflict(true) : toast.error(e.message));
  const post = useMutation({
    mutationFn: (x: LandedCostDetail) => api.postLandedCost(x.id, { version: x.version }),
    onSuccess: (saved) => {
      queryClient.setQueryData(['landed-cost', id], saved);
      void queryClient.invalidateQueries({ queryKey: ['landed-costs'] });
      toast.ok(`Voucher ${saved.number} is posted: the costs of the stock on hand are raised.`);
    },
    onError: failed,
  });
  const remove = useMutation({
    mutationFn: (x: LandedCostDetail) => api.deleteLandedCost(x.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['landed-costs'] });
      toast.ok('The voucher is deleted.');
      onClose();
    },
    onError: failed,
  });
  const allocations = v?.allocations ?? [];

  return (
    <SidePanel
      open
      wide
      title={v ? `Voucher ${v.number}` : 'Voucher'}
      subtitle={v && `${v.storeCode} · ${formatDate(v.date)} · ${label(LANDED_BASIS, v.basis)}`}
      onClose={onClose}
      footer={
        change &&
        v && (
          <>
            <Button icon={Pencil} shortcut="Ctrl+O" onClick={() => onEdit(v)}>
              Change
            </Button>
            <Button icon={Trash2} shortcut="Del" onClick={() => setDeleting(true)}>
              Delete
            </Button>
            <Button variant="sun" icon={BookCheck} shortcut="Ctrl+Enter" busy={post.isPending} onClick={() => post.mutate(v)}>
              Post
            </Button>
          </>
        )
      }
    >
      {query.error ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : !v ? (
        <Skeleton className="h-64 rounded-card" />
      ) : (
        <div className="space-y-4">
          <dl className="card grid grid-cols-2 gap-4 p-5 md:grid-cols-4">
            <Detail label="Status">{LANDED_STATUS[v.status]?.label ?? words(v.status)}</Detail>
            <Detail label="Reference">{v.reference}</Detail>
            <Detail label="Total">{formatMoney(v.total, symbol)}</Detail>
            <Detail label="Posted">{v.postedAtUtc ? `${formatUtc(v.postedAtUtc)} · ${v.postedBy ?? ''}` : ''}</Detail>
            <Detail label="Charges" className="col-span-2">
              {v.charges.map((c) => `${label(CHARGE_KINDS, c.kind)}${c.description ? ' (' + c.description + ')' : ''} ${formatMoney(c.amount, symbol)}`).join(' · ')}
            </Detail>
            <Detail label="Orders" className="col-span-2">
              {v.receipts.map((r) => `${r.number}${r.supplier ? ' ' + r.supplier : ''}`).join(' · ')}
            </Detail>
          </dl>
          <h3 className="font-heading text-section">{v.status === 'Draft' ? 'How it would be spread now' : 'How it was spread'}</h3>
          <DataTable<LandedAllocation>
            rows={allocations}
            rowKey={(a) => a.purchaseOrderLineId}
            caption="Allocations"
            showTotals={allocations.length > 0}
            empty={<EmptyState title="Nothing to spread over">Choose received orders and enter the charges.</EmptyState>}
            columns={[
              { key: 'plu', header: 'Stock code', cell: (a) => a.plu },
              { key: 'desc', header: 'Description', cell: (a) => a.description, className: 'wrap min-w-[140px]', hideBelow: 'md' },
              { key: 'qty', header: 'Pieces', numeric: true, cell: (a) => formatQuantity(a.quantity), hideBelow: 'sm' },
              { key: 'value', header: 'Value', numeric: true, cell: (a) => formatMoney(a.value, symbol), hideBelow: 'lg' },
              { key: 'weight', header: 'Kg', numeric: true, cell: (a) => formatQuantity(a.weight), hideBelow: 'xl' },
              { key: 'share', header: 'Share', numeric: true, cell: (a) => formatMoney(a.share, symbol), total: formatMoney(allocations.reduce((s, a) => s + a.share, 0), symbol) },
              { key: 'onhand', header: 'Still in stock', numeric: true, cell: (a) => formatQuantity(a.onHand), hideBelow: 'md' },
              { key: 'stock', header: 'To stock', numeric: true, cell: (a) => formatMoney(a.toInventory, symbol), total: formatMoney(allocations.reduce((s, a) => s + a.toInventory, 0), symbol) },
              { key: 'cogs', header: 'To cost of goods', numeric: true, cell: (a) => formatMoney(a.toCostOfGoods, symbol), total: formatMoney(allocations.reduce((s, a) => s + a.toCostOfGoods, 0), symbol), hideBelow: 'sm' },
            ]}
          />
        </div>
      )}
      <ConfirmDialog
        open={deleting}
        title={`Delete voucher ${v?.number ?? ''}?`}
        confirmLabel="Delete"
        cancelLabel="Cancel"
        danger
        busy={remove.isPending}
        onConfirm={() => v && remove.mutate(v)}
        onClose={() => setDeleting(false)}
      />
      <ConflictDialog
        open={conflict}
        what="voucher"
        onReload={() => {
          setConflict(false);
          void query.refetch();
        }}
        onClose={() => setConflict(false)}
      />
    </SidePanel>
  );
}

interface ChargeForm {
  key: number;
  kind: LandedCostChargeKind;
  description: string;
  amount: string;
}

let chargeKeys = 0;

function VoucherEditor({ voucher, onClose, onSaved }: { voucher: LandedCostDetail | null; onClose(): void; onSaved(v: LandedCostDetail): void }) {
  const { defaultStoreId, currencySymbol: symbol } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const storeId = voucher?.storeId ?? defaultStoreId;
  const [date, setDate] = useState(voucher?.date ?? toBusinessDate(new Date()));
  const [basis, setBasis] = useState<LandedCostBasis>(voucher?.basis ?? 'Value');
  const [reference, setReference] = useState(voucher?.reference ?? '');
  const [note, setNote] = useState(voucher?.note ?? '');
  const [charges, setCharges] = useState<ChargeForm[]>(() =>
    voucher?.charges.length
      ? voucher.charges.map((c) => ({ key: ++chargeKeys, kind: c.kind, description: c.description ?? '', amount: String(c.amount) }))
      : [{ key: ++chargeKeys, kind: 'Freight', description: '', amount: '' }],
  );
  const [orders, setOrders] = useState<Set<string>>(new Set(voucher?.receipts.map((r) => r.purchaseOrderId) ?? []));
  const [error, setError] = useState<unknown>(null);
  const [conflict, setConflict] = useState(false);
  const receipts = useQuery({ queryKey: ['landed-receipts', storeId], queryFn: () => api.landedReceipts({ storeId }), enabled: !!storeId });

  const save = useMutation({
    mutationFn: (body: LandedCharge[]) => {
      const request = { version: voucher?.version ?? null, storeId, date, basis, reference: textOrNull(reference), note: textOrNull(note), charges: body, purchaseOrderIds: [...orders] };
      return voucher ? api.saveLandedCost(voucher.id, request) : api.addLandedCost(request);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ['landed-costs'] });
      queryClient.setQueryData(['landed-cost', saved.id], saved);
      toast.ok(`Voucher ${saved.number} is saved.`);
      onSaved(saved);
    },
    onError: (e) => (e instanceof ApiError && e.isConflict ? setConflict(true) : setError(e)),
  });

  function submit() {
    const body: LandedCharge[] = [];
    for (const c of charges) {
      if (!c.amount.trim() && !c.description.trim()) continue;
      const amount = parseNumber(c.amount);
      if (amount === null || amount < 0) return setError(new Error(`${label(CHARGE_KINDS, c.kind)}: enter the amount.`));
      body.push({ kind: c.kind, description: textOrNull(c.description), amount });
    }
    if (body.length === 0) return setError(new Error('Enter at least one charge.'));
    if (orders.size === 0) return setError(new Error('Choose the received orders the charges are for.'));
    setError(null);
    save.mutate(body);
  }

  const total = chargesTotal(charges.map((c) => ({ kind: c.kind, description: null, amount: parseNumber(c.amount) ?? 0 })));
  const setCharge = (key: number, change: Partial<ChargeForm>) => setCharges((list) => list.map((c) => (c.key === key ? { ...c, ...change } : c)));

  return (
    <Dialog
      open
      width="xl"
      title={voucher ? `Voucher ${voucher.number}` : 'New landed cost voucher'}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Save and preview
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="grid grid-cols-1 gap-x-8 gap-y-3 md:grid-cols-2">
        <Field label="Date" htmlFor="landed-date">
          <input id="landed-date" type="date" className="input w-[180px]" value={date} onChange={(e) => setDate(e.target.value)} />
        </Field>
        <Field label="Spread" htmlFor="landed-basis">
          <Select id="landed-basis" className="max-w-[220px]" value={basis} onChange={(e) => setBasis(e.target.value as LandedCostBasis)}>
            {Object.entries(LANDED_BASIS).map(([key, text]) => (
              <option key={key} value={key}>
                {text}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Reference" htmlFor="landed-reference" hint="The carrier’s or agent’s bill number.">
          <Input id="landed-reference" maxLength={40} value={reference} onChange={(e) => setReference(e.target.value)} />
        </Field>
        <Field label="Note" htmlFor="landed-note">
          <Input id="landed-note" maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} />
        </Field>
      </div>
      <h3 className="mb-2 mt-5 font-heading text-section">Charges</h3>
      <div className="space-y-2">
        {charges.map((c, i) => (
          <div key={c.key} className="flex flex-wrap items-end gap-2.5">
            <Select aria-label={`Kind of charge ${i + 1}`} className="w-[160px]" value={c.kind} onChange={(e) => setCharge(c.key, { kind: e.target.value as LandedCostChargeKind })}>
              {Object.entries(CHARGE_KINDS).map(([key, text]) => (
                <option key={key} value={key}>
                  {text}
                </option>
              ))}
            </Select>
            <Input aria-label={`Description of charge ${i + 1}`} className="w-[220px]" maxLength={100} placeholder="Description" value={c.description} onChange={(e) => setCharge(c.key, { description: e.target.value })} />
            <Input aria-label={`Amount of charge ${i + 1}`} inputMode="decimal" className="w-[130px]" placeholder="Amount" value={c.amount} onChange={(e) => setCharge(c.key, { amount: e.target.value })} />
            <button type="button" className="btn btn-icon h-11 min-h-0 w-11" aria-label={`Remove charge ${i + 1}`} onClick={() => setCharges((list) => (list.length > 1 ? list.filter((x) => x.key !== c.key) : list))}>
              <Trash2 className="h-5 w-5" />
            </button>
          </div>
        ))}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <Button icon={Plus} onClick={() => setCharges((list) => [...list, { key: ++chargeKeys, kind: 'Duty', description: '', amount: '' }])}>
            Another charge
          </Button>
          <p className="font-semibold">Total {formatMoney(total, symbol)}</p>
        </div>
      </div>
      <h3 className="mb-2 mt-5 font-heading text-section">For the orders received</h3>
      {receipts.error ? (
        <ErrorState error={receipts.error} />
      ) : !receipts.data ? (
        <Skeleton className="h-24" />
      ) : receipts.data.length === 0 ? (
        <p className="text-muted">No order of this store has been received yet.</p>
      ) : (
        <ul className="max-h-[30vh] space-y-1 overflow-y-auto" aria-label="Received orders">
          {receipts.data.map((r) => (
            <li key={r.purchaseOrderId}>
              <Checkbox
                label={`Order ${r.number}${r.supplier ? ' · ' + r.supplier : ''} · ${formatShortDateTime(r.receivedAt)} · ${formatMoney(r.value, symbol)}`}
                checked={orders.has(r.purchaseOrderId)}
                onChange={(on) =>
                  setOrders((s) => {
                    const next = new Set(s);
                    if (on) next.add(r.purchaseOrderId);
                    else next.delete(r.purchaseOrderId);
                    return next;
                  })
                }
              />
            </li>
          ))}
        </ul>
      )}
      <FormError error={error} />
      <ConflictDialog open={conflict} what="voucher" onReload={onClose} onClose={() => setConflict(false)} />
    </Dialog>
  );
}
