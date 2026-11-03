'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Check, ClipboardList, Pencil, Plus, Save, Send, Settings2, Trash2, X } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { ConflictDialog } from '@/components/masters';
import { LocationSelect, SETTINGS_FIELDS, SupplySettingsDialog } from '@/components/supply';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog, SidePanel } from '@/components/ui/dialog';
import { Field, Input, Select, TextArea } from '@/components/ui/field';
import { Chip, Detail, PageHeader, Pager, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { formatCount, formatDate, formatLocalDateTime, formatMoney, formatQuantity, formatShortDateTime } from '@/lib/format';
import { parseNumber, textOrNull } from '@/lib/forms';
import { words } from '@/lib/labels';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import { REQUISITION_STATUS, requisitionSteps, requisitionTotal } from '@/lib/supply';
import type { ApprovalLimitRow, OrderMade, RequisitionDetail, RequisitionLineRequest, RequisitionRow } from '@/lib/types';

const PAGE_SIZE = 50;

export default function RequisitionsPage() {
  return (
    <Suspense>
      <Requisitions />
    </Suspense>
  );
}

function Requisitions() {
  const { can } = useSession();
  const { storeId, defaultStoreId, currencySymbol: symbol } = useStore();
  const params = useSearchParams();
  const router = useRouter();
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<string | null>(null);
  const [editing, setEditing] = useState<RequisitionDetail | 'new' | null>(null);
  const [settings, setSettings] = useState(false);
  useEffect(() => setPage(1), [storeId, status]);
  useEffect(() => {
    if (params.get('id')) {
      setOpen(params.get('id'));
      router.replace('/requisitions/');
    }
  }, [params, router]);

  const query = useQuery({
    queryKey: ['requisitions', storeId, status, page],
    queryFn: ({ signal }) => api.requisitions({ storeId, status, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });

  return (
    <div>
      <PageHeader title="Requisitions" aside={query.data && `${formatCount(query.data.total)} requisitions`} />
      <Toolbar>
        <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')} disabled={!defaultStoreId}>
          New requisition
        </Button>
        <label className="sr-only" htmlFor="requisition-status">
          Status
        </label>
        <Select id="requisition-status" className="w-[220px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every status</option>
          {Object.entries(REQUISITION_STATUS).map(([key, s]) => (
            <option key={key} value={key}>
              {s.label}
            </option>
          ))}
        </Select>
        {defaultStoreId && (
          <Button icon={Settings2} onClick={() => setSettings(true)}>
            Approval settings
          </Button>
        )}
      </Toolbar>
      <DataTable<RequisitionRow>
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => r.id}
        onRowClick={(r) => setOpen(r.id)}
        caption="Requisitions"
        empty={<EmptyState title="No requisitions">Staff ask for goods here or at the till (Purchase Orders screen); approved requisitions become purchase orders.</EmptyState>}
        columns={[
          { key: 'n', header: 'Requisition', cell: (r) => <span className="font-semibold">{r.number}</span> },
          { key: 'status', header: 'Status', cell: (r) => <Chip tone={REQUISITION_STATUS[r.status]?.tone ?? 'plain'}>{REQUISITION_STATUS[r.status]?.label ?? words(r.status)}</Chip> },
          { key: 'where', header: 'Location', cell: (r) => `${r.storeCode} · ${r.locationCode}`, hideBelow: 'lg' },
          { key: 'by', header: 'Asked by', cell: (r) => r.requestedBy ?? '', hideBelow: 'md' },
          { key: 'made', header: 'Made', cell: (r) => formatShortDateTime(r.createdAt), hideBelow: 'md' },
          { key: 'needed', header: 'Needed by', cell: (r) => formatDate(r.neededBy), hideBelow: 'xl' },
          { key: 'lines', header: 'Lines', numeric: true, cell: (r) => formatCount(r.lines), hideBelow: 'sm' },
          { key: 'total', header: 'Total', numeric: true, cell: (r) => formatMoney(r.total, symbol) },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="requisitions" />}
      />
      {can(P.purchasingApprove) && <ApprovalLimits />}
      {open && <RequisitionPanel id={open} onClose={() => setOpen(null)} onEdit={(r) => setEditing(r)} />}
      {editing && (
        <RequisitionEditor
          requisition={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={(saved) => {
            setEditing(null);
            setOpen(saved.id);
          }}
        />
      )}
      {settings && defaultStoreId && (
        <SupplySettingsDialog storeId={defaultStoreId} fields={SETTINGS_FIELDS.requisitions} title="Approval settings" onClose={() => setSettings(false)} />
      )}
    </div>
  );
}

function RequisitionPanel({ id, onClose, onEdit }: { id: string; onClose(): void; onEdit(r: RequisitionDetail): void }) {
  const { can } = useSession();
  const symbol = useStore().currencySymbol;
  const toast = useToast();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['requisition', id], queryFn: () => api.requisition(id), staleTime: 0 });
  const [rejecting, setRejecting] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [made, setMade] = useState<OrderMade[] | null>(null);
  const [conflict, setConflict] = useState(false);
  const r = query.data;
  const steps = r ? requisitionSteps(r.status, can(P.purchasingApprove)) : null;

  function done(saved: RequisitionDetail, message: string) {
    queryClient.setQueryData(['requisition', id], saved);
    void queryClient.invalidateQueries({ queryKey: ['requisitions'] });
    toast.ok(message);
  }
  const failed = (e: Error) => (e instanceof ApiError && e.isConflict ? setConflict(true) : toast.error(e.message));

  const submit = useMutation({ mutationFn: (x: RequisitionDetail) => api.submitRequisition(x.id, { version: x.version }), onSuccess: (s) => done(s, `Requisition ${s.number} is sent for approval.`), onError: failed });
  const approve = useMutation({ mutationFn: (x: RequisitionDetail) => api.approveRequisition(x.id, { version: x.version }), onSuccess: (s) => done(s, `Requisition ${s.number} is approved.`), onError: failed });
  const order = useMutation({
    mutationFn: (x: RequisitionDetail) => api.orderRequisition(x.id, { version: x.version }),
    onSuccess: (result) => {
      setMade(result.orders);
      void query.refetch();
      void queryClient.invalidateQueries({ queryKey: ['requisitions'] });
      void queryClient.invalidateQueries({ queryKey: ['purchase-orders'] });
    },
    onError: failed,
  });
  const remove = useMutation({
    mutationFn: (x: RequisitionDetail) => api.deleteRequisition(x.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['requisitions'] });
      toast.ok('The requisition is deleted.');
      onClose();
    },
    onError: failed,
  });

  return (
    <SidePanel
      open
      wide
      title={r ? `Requisition ${r.number}` : 'Requisition'}
      subtitle={r && `${r.storeCode} · ${r.locationCode} · ${REQUISITION_STATUS[r.status]?.label ?? words(r.status)}`}
      onClose={onClose}
      footer={
        r &&
        steps && (
          <>
            {steps.edit && (
              <Button icon={Pencil} shortcut="Ctrl+O" onClick={() => onEdit(r)}>
                Change
              </Button>
            )}
            {steps.remove && (
              <Button icon={Trash2} shortcut="Del" onClick={() => setDeleting(true)}>
                Delete
              </Button>
            )}
            {steps.submit && (
              <Button variant="sun" icon={Send} shortcut="Ctrl+Enter" busy={submit.isPending} onClick={() => submit.mutate(r)}>
                Send for approval
              </Button>
            )}
            {steps.decide && (
              <>
                <Button icon={X} shortcut="Alt+R" onClick={() => setRejecting(true)}>
                  Reject
                </Button>
                <Button variant="sun" icon={Check} shortcut="Ctrl+Enter" busy={approve.isPending} onClick={() => approve.mutate(r)}>
                  Approve
                </Button>
              </>
            )}
            {steps.order && (
              <Button variant="sun" icon={ClipboardList} shortcut="Ctrl+Enter" busy={order.isPending} onClick={() => order.mutate(r)}>
                Make purchase orders
              </Button>
            )}
          </>
        )
      }
    >
      {query.error ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : !r ? (
        <Skeleton className="h-64 rounded-card" />
      ) : (
        <div className="space-y-4">
          <dl className="card grid grid-cols-2 gap-4 p-5 md:grid-cols-4">
            <Detail label="Asked by">{r.requestedBy}</Detail>
            <Detail label="Made">{formatLocalDateTime(r.createdAt)}</Detail>
            <Detail label="Needed by">{formatDate(r.neededBy)}</Detail>
            <Detail label="Total">{formatMoney(r.total, symbol)}</Detail>
            <Detail label={r.status === 'Rejected' ? 'Rejected by' : 'Approved by'}>{r.decidedBy}</Detail>
            <Detail label="Decided">{formatLocalDateTime(r.decidedAt)}</Detail>
            <Detail label="Ordered">{formatLocalDateTime(r.orderedAt)}</Detail>
            <Detail label="Note">{r.note}</Detail>
            {r.decisionNote && (
              <Detail label={r.status === 'Rejected' ? 'Why it was rejected' : 'Approver’s note'} className="col-span-2 md:col-span-4">
                {r.decisionNote}
              </Detail>
            )}
          </dl>
          {r.status === 'Submitted' && !can(P.purchasingApprove) && <p className="text-muted">Waiting for someone who may approve purchases.</p>}
          {made && (
            <div className="card p-4" role="status">
              <p className="font-semibold">Purchase orders made:</p>
              <ul className="mt-1 list-disc pl-6">
                {made.map((o) => (
                  <li key={o.id}>
                    Order {o.number}
                    {o.supplier ? ` · ${o.supplier}` : ''}
                  </li>
                ))}
              </ul>
            </div>
          )}
          <DataTable
            rows={r.lines}
            rowKey={(l) => l.id}
            caption="Requisition lines"
            showTotals
            columns={[
              { key: 'plu', header: 'Stock code', cell: (l) => l.plu },
              { key: 'desc', header: 'Description', cell: (l) => l.description, className: 'wrap min-w-[150px]', hideBelow: 'sm' },
              { key: 'qty', header: 'Pieces', numeric: true, cell: (l) => formatQuantity(l.quantity) },
              { key: 'cost', header: 'Cost each', numeric: true, cell: (l) => formatMoney(l.costEach, symbol), hideBelow: 'md' },
              { key: 'value', header: 'Value', numeric: true, cell: (l) => formatMoney(l.value, symbol), total: formatMoney(r.total, symbol) },
              { key: 'supplier', header: 'Supplier', cell: (l) => l.supplier ?? '', hideBelow: 'lg' },
              { key: 'needed', header: 'Needed by', cell: (l) => formatDate(l.neededBy ?? r.neededBy), hideBelow: 'xl' },
              { key: 'po', header: 'Order', cell: (l) => (l.purchaseOrderNumber ? String(l.purchaseOrderNumber) : ''), hideBelow: 'md' },
            ]}
          />
        </div>
      )}
      {rejecting && r && (
        <RejectDialog
          onClose={() => setRejecting(false)}
          onReject={async (note) => {
            const saved = await api.rejectRequisition(r.id, { version: r.version, note });
            setRejecting(false);
            done(saved, `Requisition ${saved.number} is rejected.`);
          }}
        />
      )}
      <ConfirmDialog
        open={deleting}
        title={`Delete requisition ${r?.number ?? ''}?`}
        confirmLabel="Delete"
        cancelLabel="Cancel"
        danger
        busy={remove.isPending}
        onConfirm={() => r && remove.mutate(r)}
        onClose={() => setDeleting(false)}
      />
      <ConflictDialog
        open={conflict}
        what="requisition"
        onReload={() => {
          setConflict(false);
          void query.refetch();
        }}
        onClose={() => setConflict(false)}
      />
    </SidePanel>
  );
}

function RejectDialog({ onClose, onReject }: { onClose(): void; onReject(note: string): Promise<void> }) {
  const [note, setNote] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  return (
    <Dialog
      open
      width="sm"
      title="Reject the requisition"
      onClose={onClose}
      onSubmit={async () => {
        if (!note.trim()) return setError(new Error('Say why it is rejected.'));
        setBusy(true);
        try {
          await onReject(note.trim());
        } catch (e) {
          setError(e);
        } finally {
          setBusy(false);
        }
      }}
      footer={
        <>
          <Button type="submit" variant="danger" icon={X} shortcut="Enter" busy={busy}>
            Reject
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <Field label="Why" htmlFor="reject-note" stacked>
        <TextArea id="reject-note" value={note} onChange={(e) => setNote(e.target.value)} maxLength={200} />
      </Field>
      <FormError error={error} />
    </Dialog>
  );
}

interface LineForm {
  key: number;
  itemId: string | null;
  plu: string;
  quantity: string;
  neededBy: string;
  supplierId: string;
  costEach: string;
  note: string;
}

let lineKeys = 0;

function toLines(r: RequisitionDetail | null): LineForm[] {
  if (!r || r.lines.length === 0) return [{ key: ++lineKeys, itemId: null, plu: '', quantity: '1', neededBy: '', supplierId: '', costEach: '', note: '' }];
  return r.lines.map((l) => ({
    key: ++lineKeys,
    itemId: l.itemId,
    plu: l.plu,
    quantity: String(l.quantity),
    neededBy: l.neededBy ?? '',
    supplierId: l.supplierId ?? '',
    costEach: String(l.costEach),
    note: l.note ?? '',
  }));
}

/** A new requisition, or a draft (or rejected one) changed: the location, when it is needed, and the lines. */
function RequisitionEditor({ requisition, onClose, onSaved }: { requisition: RequisitionDetail | null; onClose(): void; onSaved(r: RequisitionDetail): void }) {
  const { defaultStoreId, currencySymbol: symbol } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [locationId, setLocationId] = useState<string | null>(requisition?.locationId ?? null);
  const [neededBy, setNeededBy] = useState(requisition?.neededBy ?? '');
  const [note, setNote] = useState(requisition?.note ?? '');
  const [lines, setLines] = useState<LineForm[]>(() => toLines(requisition));
  const [error, setError] = useState<unknown>(null);
  const [conflict, setConflict] = useState(false);
  const suppliers = useQuery({ queryKey: ['suppliers', 'all'], queryFn: () => api.suppliers({ pageSize: 500 }), staleTime: 5 * 60_000 });

  const save = useMutation({
    mutationFn: (body: { lines: RequisitionLineRequest[] }) => {
      const request = { version: requisition?.version ?? null, locationId, neededBy: neededBy || null, note: textOrNull(note), lines: body.lines };
      return requisition ? api.saveRequisition(requisition.id, request) : api.addRequisition(request, defaultStoreId);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ['requisitions'] });
      queryClient.setQueryData(['requisition', saved.id], saved);
      toast.ok(`Requisition ${saved.number} is saved.`);
      onSaved(saved);
    },
    onError: (e) => (e instanceof ApiError && e.isConflict ? setConflict(true) : setError(e)),
  });

  function set(key: number, change: Partial<LineForm>) {
    setLines((list) => list.map((l) => (l.key === key ? { ...l, ...change } : l)));
  }

  function submit() {
    const filled = lines.filter((l) => l.plu.trim() || l.itemId);
    if (filled.length === 0) return setError(new Error('Add at least one item.'));
    const body: RequisitionLineRequest[] = [];
    for (const l of filled) {
      const quantity = parseNumber(l.quantity);
      if (quantity === null || quantity <= 0) return setError(new Error(`${l.plu || 'A line'}: enter the pieces needed (above zero).`));
      const cost = l.costEach.trim() ? parseNumber(l.costEach) : null;
      if (l.costEach.trim() && (cost === null || cost < 0)) return setError(new Error(`${l.plu}: the cost is not a number.`));
      body.push({ itemId: l.itemId, plu: l.itemId ? null : l.plu.trim(), quantity, neededBy: l.neededBy || null, supplierId: l.supplierId || null, costEach: cost, note: textOrNull(l.note) });
    }
    setError(null);
    save.mutate({ lines: body });
  }

  const estimate = requisitionTotal(lines.map((l) => ({ quantity: parseNumber(l.quantity) ?? 0, costEach: parseNumber(l.costEach) ?? 0 })));

  return (
    <Dialog
      open
      width="xl"
      title={requisition ? `Requisition ${requisition.number}` : 'New requisition'}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Save
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="mb-4 grid grid-cols-1 gap-3 md:grid-cols-2">
        {!requisition && (
          <Field label="For location" htmlFor="requisition-location">
            <LocationSelect storeId={defaultStoreId} value={locationId} onChange={setLocationId} id="requisition-location" className="w-full" />
          </Field>
        )}
        <Field label="Needed by" htmlFor="requisition-needed">
          <input id="requisition-needed" type="date" className="input w-[180px]" value={neededBy} onChange={(e) => setNeededBy(e.target.value)} />
        </Field>
        <Field label="Note" htmlFor="requisition-note" className="md:col-span-2">
          <Input id="requisition-note" maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} />
        </Field>
      </div>
      <div className="space-y-2">
        {lines.map((l, i) => (
          <div key={l.key} className="flex flex-wrap items-end gap-2.5 rounded-control border border-line p-3">
            <label className="flex flex-col gap-1">
              <span className="text-note text-muted">Stock code</span>
              <Input aria-label={`Stock code of line ${i + 1}`} className="w-[150px]" value={l.plu} onChange={(e) => set(l.key, { plu: e.target.value, itemId: null })} />
            </label>
            <label className="flex flex-col gap-1">
              <span className="text-note text-muted">Pieces</span>
              <Input aria-label={`Pieces of line ${i + 1}`} inputMode="decimal" className="w-[100px]" value={l.quantity} onChange={(e) => set(l.key, { quantity: e.target.value })} />
            </label>
            <label className="flex flex-col gap-1">
              <span className="text-note text-muted">Supplier</span>
              <Select aria-label={`Supplier of line ${i + 1}`} className="w-[200px]" value={l.supplierId} onChange={(e) => set(l.key, { supplierId: e.target.value })}>
                <option value="">The item’s own</option>
                {(suppliers.data?.items ?? []).map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.company}
                  </option>
                ))}
              </Select>
            </label>
            <label className="flex flex-col gap-1">
              <span className="text-note text-muted">Cost each</span>
              <Input aria-label={`Cost each of line ${i + 1}`} inputMode="decimal" className="w-[110px]" placeholder="Last cost" value={l.costEach} onChange={(e) => set(l.key, { costEach: e.target.value })} />
            </label>
            <label className="flex flex-col gap-1">
              <span className="text-note text-muted">Needed by</span>
              <input type="date" aria-label={`Needed by of line ${i + 1}`} className="input w-[170px]" value={l.neededBy} onChange={(e) => set(l.key, { neededBy: e.target.value })} />
            </label>
            <button
              type="button"
              className="btn btn-icon h-11 min-h-0 w-11"
              aria-label={`Remove line ${i + 1}`}
              onClick={() => setLines((list) => (list.length > 1 ? list.filter((x) => x.key !== l.key) : list))}
            >
              <Trash2 className="h-5 w-5" />
            </button>
          </div>
        ))}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <Button icon={Plus} onClick={() => setLines((list) => [...list, { key: ++lineKeys, itemId: null, plu: '', quantity: '1', neededBy: '', supplierId: '', costEach: '', note: '' }])}>
            Another line
          </Button>
          {estimate > 0 && <p className="text-muted">About {formatMoney(estimate, symbol)} at the costs typed (the server uses each item’s last cost for the others).</p>}
        </div>
      </div>
      <FormError error={error} />
      <ConflictDialog open={conflict} what="requisition" onReload={onClose} onClose={() => setConflict(false)} />
    </Dialog>
  );
}

/** The approval limit of each role that may approve purchases; changed by those who manage users and roles. */
function ApprovalLimits() {
  const { can, canChangeShared } = useSession();
  const symbol = useStore().currencySymbol;
  const toast = useToast();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['approval-limits'], queryFn: () => api.approvalLimits() });
  const [editing, setEditing] = useState<ApprovalLimitRow | null>(null);
  const [text, setText] = useState('');
  const edit = can(P.setupUsers) && canChangeShared;
  const save = useMutation({
    mutationFn: ({ roleId, maxAmount }: { roleId: string; maxAmount: number | null }) => api.setApprovalLimit(roleId, maxAmount),
    onSuccess: (rows) => {
      queryClient.setQueryData(['approval-limits'], rows);
      toast.ok('The limit is saved.');
      setEditing(null);
    },
  });
  const rows = (query.data ?? []).filter((r) => r.mayApprove);

  return (
    <section className="mt-6" aria-label="Approval limits">
      <h2 className="mb-2 font-heading text-section">Approval limits</h2>
      <p className="mb-3 text-muted">The largest requisition each role may approve. A role without a limit approves any amount.</p>
      <DataTable<ApprovalLimitRow>
        rows={query.data ? rows : undefined}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => r.roleId}
        onRowClick={
          edit
            ? (r) => {
                setEditing(r);
                setText(r.maxAmount === null ? '' : String(r.maxAmount));
              }
            : undefined
        }
        caption="Approval limits"
        empty={<EmptyState title="No role may approve purchases" />}
        columns={[
          { key: 'role', header: 'Role', cell: (r) => <span className="font-semibold">{r.role}</span> },
          { key: 'limit', header: 'Up to', numeric: true, cell: (r) => (r.maxAmount === null ? 'No limit' : formatMoney(r.maxAmount, symbol)) },
        ]}
      />
      {editing && (
        <Dialog
          open
          width="sm"
          title={`${editing.role}: approval limit`}
          onClose={() => setEditing(null)}
          onSubmit={() => {
            const n = text.trim() ? parseNumber(text) : null;
            if (text.trim() && (n === null || n < 0)) return toast.error('Enter an amount, or leave it empty for no limit.');
            save.mutate({ roleId: editing.roleId, maxAmount: n });
          }}
          footer={
            <>
              <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
                Save
              </Button>
              <Button onClick={() => setEditing(null)} shortcut="Esc">
                Cancel
              </Button>
            </>
          }
        >
          <Field label="Up to" hint="Empty for no limit." htmlFor="limit-amount">
            <Input id="limit-amount" inputMode="decimal" className="max-w-[180px]" value={text} onChange={(e) => setText(e.target.value)} />
          </Field>
          <FormError error={save.error} />
        </Dialog>
      )}
    </section>
  );
}
