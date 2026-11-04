'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Ban, Calculator, CheckCheck, ClipboardCheck, PackageCheck, Pencil, Play, Rocket, ScanLine, Square, Trash2 } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useState } from 'react';
import { CheckAnswers, OrderDialog, StatusChip, useLocations } from '@/components/production';
import { Button, LinkButton } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select, TextArea } from '@/components/ui/field';
import { Chip, Detail, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { ApiError } from '@/lib/api';
import { formatDate, formatLocalDateTime, formatMoney, formatQuantity } from '@/lib/format';
import { numberText, parseNumber, textOrNull } from '@/lib/forms';
import { useHotkey } from '@/lib/hotkeys';
import { P } from '@/lib/nav';
import { completeAnswers, movementLabel } from '@/lib/production';
import { productionApi } from '@/lib/production-api';
import type { CheckAnswer, InspectionDisposition, LineProgress, ProductionMovement, ProductionOperation, ProductionOrderDetail, Shortage } from '@/lib/production-types';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';

export default function ProductionOrderPage() {
  return (
    <Suspense>
      <OrderPage />
    </Suspense>
  );
}

type Tab = 'materials' | 'operations' | 'output' | 'costs' | 'history';

function OrderPage() {
  const id = useSearchParams().get('id') ?? '';
  const router = useRouter();
  const { can } = useSession();
  useHotkey('Esc', () => router.push('/production/'));
  const query = useQuery({ queryKey: ['production-order', id], queryFn: () => productionApi.order(id), enabled: !!id });
  const [tab, setTab] = useState<Tab>('materials');

  if (!id) return <div className="card"><EmptyState title="No order chosen">Choose one on the board.</EmptyState></div>;
  if (query.error) return <div className="card"><ErrorState error={query.error} onRetry={() => void query.refetch()} /></div>;
  if (!query.data) return <Skeleton className="h-[60vh] rounded-card" />;
  const order = query.data;
  const tabs: Array<{ key: Tab; label: string }> = [
    { key: 'materials', label: 'Materials' },
    { key: 'operations', label: 'Operations' },
    { key: 'output', label: 'Output and quality' },
    ...(can(P.productionCosting) ? [{ key: 'costs' as const, label: 'Costs' }] : []),
    { key: 'history', label: 'History' },
  ];
  return (
    <div>
      <Toolbar>
        <LinkButton href="/production/" icon={ArrowLeft} shortcut="Esc">
          Production orders
        </LinkButton>
      </Toolbar>
      <PageHeader title={`Order ${order.number}`} aside={<StatusChip status={order.status} />} />
      <Header order={order} />
      <Actions order={order} />
      <Tabs label="Order" tabs={tabs} value={tab} onChange={setTab} className="mb-4" />
      {tab === 'materials' && <Materials order={order} />}
      {tab === 'operations' && <Operations order={order} />}
      {tab === 'output' && <Output order={order} />}
      {tab === 'costs' && <Costs order={order} />}
      {tab === 'history' && <History order={order} />}
    </div>
  );
}

function Header({ order }: { order: ProductionOrderDetail }) {
  const symbol = useStore().currencySymbol;
  const { can } = useSession();
  return (
    <div className="card mb-4 grid grid-cols-2 gap-4 p-5 md:grid-cols-4 xl:grid-cols-6">
      <Detail label="Product">
        {order.productCode} {order.productName}
      </Detail>
      <Detail label="To make">{formatQuantity(order.quantity)}</Detail>
      <Detail label="Made">{formatQuantity(order.received)}</Detail>
      <Detail label="Scrapped">{formatQuantity(order.reportedScrap + order.outputScrapped)}</Detail>
      <Detail label="Bill">Version {order.bomRevision}</Detail>
      <Detail label="Priority">{order.priority}</Detail>
      <Detail label="Materials from">{order.materialLocation}</Detail>
      <Detail label="Output to">{order.outputLocation}</Detail>
      <Detail label="Planned">{[order.plannedStart && formatDate(order.plannedStart), order.plannedEnd && formatDate(order.plannedEnd)].filter(Boolean).join(' to ')}</Detail>
      <Detail label="Made for">{order.customerOrder ?? 'Stock'}</Detail>
      {can(P.productionCosting) && <Detail label="Standard cost a unit">{order.standardUnitCost ? formatMoney(order.standardUnitCost, symbol) : ''}</Detail>}
      <Detail label="Store">{order.storeCode}</Detail>
      {order.notes && <Detail label="Notes" className="col-span-2">{order.notes}</Detail>}
    </div>
  );
}

/** The order's main actions: release, change, cancel, close and post its costs. */
function Actions({ order }: { order: ProductionOrderDetail }) {
  const { can } = useSession();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [closing, setClosing] = useState(false);
  const [shortages, setShortages] = useState<Shortage[] | null>(null);
  const refresh = (o?: ProductionOrderDetail) => {
    if (o) queryClient.setQueryData(['production-order', order.id], o);
    void queryClient.invalidateQueries({ queryKey: ['production-board'] });
  };
  const fail = (e: Error) => toast.error(e.message);
  const release = useMutation({
    mutationFn: () => productionApi.release(order.id),
    onSuccess: (r) => {
      refresh(r.order);
      if (r.shortages.length > 0) setShortages(r.shortages);
      else toast.ok(`Order ${r.order.number} is released; all its material is set aside.`);
    },
    onError: fail,
  });
  const cancel = useMutation({ mutationFn: () => productionApi.cancel(order.id, null), onSuccess: (o) => { refresh(o); setCancelling(false); toast.ok(`Order ${o.number} is cancelled.`); }, onError: fail });
  const close = useMutation({ mutationFn: () => productionApi.close(order.id), onSuccess: (o) => { refresh(o); setClosing(false); toast.ok(`Order ${o.number} is closed.`); }, onError: fail });
  const post = useMutation({ mutationFn: () => productionApi.postCosts(order.id), onSuccess: (o) => { refresh(o); toast.ok('The costs are posted to the ledger.'); }, onError: fail });
  const plan = can(P.productionPlan);
  const open = order.status === 'Planned' || order.status === 'Released' || order.status === 'InProgress';
  return (
    <Toolbar>
      {plan && order.status === 'Planned' && (
        <Button variant="sun" icon={Rocket} shortcut="F9" onClick={() => release.mutate()} busy={release.isPending}>
          Release to the floor
        </Button>
      )}
      {plan && (order.status === 'Planned' || order.status === 'Released') && (
        <Button icon={Pencil} shortcut="Ctrl+O" onClick={() => setEditing(true)}>
          Change
        </Button>
      )}
      {plan && open && (
        <Button icon={Ban} shortcut="Del" onClick={() => setCancelling(true)}>
          Cancel order
        </Button>
      )}
      {can(P.productionCosting) && order.status === 'Completed' && (
        <Button variant="sun" icon={CheckCheck} shortcut="F7" onClick={() => setClosing(true)}>
          Close order
        </Button>
      )}
      {can(P.productionCosting) && order.status !== 'Planned' && order.status !== 'Cancelled' && (
        <Button icon={Calculator} shortcut="F8" onClick={() => post.mutate()} busy={post.isPending}>
          Post costs
        </Button>
      )}
      {editing && <OrderDialog order={order} onClose={() => setEditing(false)} onSaved={(o) => { refresh(o); setEditing(false); }} />}
      <ConfirmDialog open={cancelling} title={`Cancel order ${order.number}?`} message="An order with work recorded on it cannot be cancelled; complete and close it instead." danger busy={cancel.isPending} onConfirm={() => cancel.mutate()} onClose={() => setCancelling(false)} />
      <ConfirmDialog
        open={closing}
        title={`Close order ${order.number}?`}
        message={`What is left in work in progress (${formatQuantity(order.costs?.workInProgress ?? 0)}) is posted as a variance, and nothing more can be done on the order.`}
        busy={close.isPending}
        onConfirm={() => close.mutate()}
        onClose={() => setClosing(false)}
      />
      {shortages && (
        <Dialog open title="Released, with material short" onClose={() => setShortages(null)} footer={<Button variant="sun" shortcut="Enter" onClick={() => setShortages(null)} data-autofocus>OK</Button>}>
          <p className="mb-3 text-ink">The order is released. What could not be set aside is on Planning as a suggestion to buy (or make).</p>
          <DataTable<Shortage>
            rows={shortages}
            rowKey={(s) => s.stockCode}
            caption="Material short"
            columns={[
              { key: 'code', header: 'Material', cell: (s) => <span className="font-semibold">{s.stockCode} {s.description}</span> },
              { key: 'need', header: 'Needed', numeric: true, cell: (s) => formatQuantity(s.required) },
              { key: 'have', header: 'Free in stock', numeric: true, cell: (s) => formatQuantity(s.available) },
              { key: 'short', header: 'Short', numeric: true, cell: (s) => formatQuantity(s.missing) },
            ]}
          />
        </Dialog>
      )}
    </Toolbar>
  );
}

function useOrderAction(order: ProductionOrderDetail) {
  const toast = useToast();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (run: () => Promise<ProductionOrderDetail>) => run(),
    onSuccess: (o) => {
      queryClient.setQueryData(['production-order', order.id], o);
      void queryClient.invalidateQueries({ queryKey: ['production-board'] });
    },
    onError: (e: Error) => toast.error(e instanceof ApiError ? e.message : 'That did not work.'),
  });
}

const workable = (o: ProductionOrderDetail) => o.status === 'Released' || o.status === 'InProgress';

function Materials({ order }: { order: ProductionOrderDetail }) {
  const { can } = useSession();
  const act = useOrderAction(order);
  const [code, setCode] = useState('');
  const [quantity, setQuantity] = useState('1');
  const [scrapping, setScrapping] = useState(false);
  const floor = can(P.productionFloor) && (workable(order) || order.status === 'Completed');
  const components = order.lines.filter((l) => l.kind === 'Component');
  return (
    <div>
      {floor && (
        <form
          className="card mb-4 flex flex-wrap items-end gap-3 p-4"
          onSubmit={(e) => {
            e.preventDefault();
            const q = parseNumber(quantity);
            if (!code.trim() || !q) return;
            act.mutate(() => productionApi.issue(order.id, code.trim(), q), { onSuccess: () => { setCode(''); setQuantity('1'); } });
          }}
        >
          <label className="flex flex-col gap-1">
            <span className="text-muted">Material code or barcode</span>
            <Input value={code} onChange={(e) => setCode(e.target.value)} className="w-[260px]" aria-label="Material code or barcode" />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-muted">Quantity</span>
            <Input value={quantity} inputMode="decimal" onChange={(e) => setQuantity(e.target.value)} className="w-[120px] text-right" aria-label="Quantity" />
          </label>
          <Button type="submit" variant="sun" icon={ScanLine} shortcut="Enter" busy={act.isPending}>
            Issue
          </Button>
          <Button icon={PackageCheck} shortcut="Ctrl+I" onClick={() => act.mutate(() => productionApi.issueRemaining(order.id))} disabled={!workable(order)}>
            Issue the list
          </Button>
          <Button icon={Trash2} shortcut="F4" onClick={() => setScrapping(true)}>
            Scrap material
          </Button>
          <p className="w-full text-note text-muted">A negative quantity gives material back to stock.</p>
        </form>
      )}
      <DataTable<LineProgress>
        rows={components}
        rowKey={(l) => l.lineId}
        caption="Materials"
        empty={<EmptyState title={order.status === 'Planned' ? 'Released orders show their materials' : 'No materials'}>The bill is copied onto the order when it is released.</EmptyState>}
        columns={[
          { key: 'code', header: 'Material', cell: (l) => <span className="font-semibold">{l.stockCode}</span> },
          { key: 'name', header: 'Description', cell: (l) => l.description, hideBelow: 'md' },
          { key: 'need', header: 'Needed', numeric: true, cell: (l) => formatQuantity(l.planned) },
          { key: 'set', header: 'Set aside', numeric: true, cell: (l) => (l.reserved < l.planned ? <Chip tone="warn">{formatQuantity(l.reserved)}</Chip> : formatQuantity(l.reserved)), hideBelow: 'lg' },
          { key: 'done', header: 'Issued', numeric: true, cell: (l) => formatQuantity(l.done) },
          { key: 'how', header: 'How', cell: (l) => (l.issueMethod === 'Backflush' ? 'At the end' : 'By hand'), hideBelow: 'sm' },
          { key: 'op', header: 'At step', cell: (l) => l.operationSequence ?? '', hideBelow: 'lg' },
        ]}
      />
      {scrapping && <ScrapDialog order={order} onClose={() => setScrapping(false)} />}
    </div>
  );
}

function ScrapDialog({ order, onClose }: { order: ProductionOrderDetail; onClose(): void }) {
  const act = useOrderAction(order);
  const [code, setCode] = useState(order.lines.find((l) => l.kind === 'Component')?.stockCode ?? '');
  const [quantity, setQuantity] = useState('1');
  const [reason, setReason] = useState('Spoiled in production');
  return (
    <Dialog
      open
      title="Scrap material"
      onClose={onClose}
      width="sm"
      onSubmit={() => act.mutate(() => productionApi.scrap(order.id, code, parseNumber(quantity) ?? 0, textOrNull(reason)), { onSuccess: onClose })}
      footer={
        <>
          <Button type="submit" variant="danger" shortcut="Enter" busy={act.isPending}>
            Throw away
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Material" htmlFor="scrap-code">
          <Input id="scrap-code" value={code} onChange={(e) => setCode(e.target.value)} />
        </Field>
        <Field label="Quantity" htmlFor="scrap-quantity">
          <Input id="scrap-quantity" inputMode="decimal" value={quantity} className="max-w-[140px] text-right" onChange={(e) => setQuantity(e.target.value)} />
        </Field>
        <Field label="Why" htmlFor="scrap-reason">
          <Input id="scrap-reason" value={reason} onChange={(e) => setReason(e.target.value)} />
        </Field>
      </div>
      <FormError error={act.error} />
    </Dialog>
  );
}

function Operations({ order }: { order: ProductionOrderDetail }) {
  const { can } = useSession();
  const act = useOrderAction(order);
  const [reporting, setReporting] = useState<ProductionOperation | null>(null);
  const floor = can(P.productionFloor) && workable(order);
  return (
    <div>
      <DataTable<ProductionOperation>
        rows={order.operations}
        rowKey={(o) => String(o.sequence)}
        caption="Operations"
        empty={<EmptyState title="No operations">The routing is copied onto the order when it is released.</EmptyState>}
        columns={[
          { key: 'seq', header: 'Step', cell: (o) => <span className="font-semibold">{o.sequence}</span> },
          { key: 'name', header: 'Operation', cell: (o) => <span title={o.instructions ?? undefined}>{o.name}</span> },
          { key: 'centre', header: 'Work centre', cell: (o) => o.workCentre, hideBelow: 'md' },
          { key: 'status', header: 'Status', cell: (o) => <Chip tone={o.status === 'Done' ? 'ok' : o.status === 'Started' ? 'dark' : 'plain'}>{o.status === 'Waiting' ? 'Waiting' : o.status}</Chip> },
          { key: 'std', header: 'Standard min', numeric: true, cell: (o) => formatQuantity(o.standardMinutes), hideBelow: 'lg' },
          { key: 'min', header: 'Minutes', numeric: true, cell: (o) => formatQuantity(o.minutes) },
          { key: 'good', header: 'Good', numeric: true, cell: (o) => formatQuantity(o.good) },
          { key: 'scrap', header: 'Scrap', numeric: true, cell: (o) => formatQuantity(o.scrap), hideBelow: 'sm' },
          {
            key: 'act',
            header: '',
            className: 'no-print',
            cell: (o) =>
              floor && (
                <span className="flex flex-wrap justify-end gap-2">
                  {o.status === 'Waiting' && (
                    <Button icon={Play} onClick={() => act.mutate(() => productionApi.startOperation(order.id, o.sequence))} aria-label={`Start step ${o.sequence}`}>
                      Start
                    </Button>
                  )}
                  <Button onClick={() => setReporting(o)} aria-label={`Report step ${o.sequence}`}>
                    Report
                  </Button>
                  {o.status !== 'Done' && (
                    <Button icon={Square} onClick={() => act.mutate(() => productionApi.finishOperation(order.id, o.sequence))} aria-label={`Finish step ${o.sequence}`}>
                      Finish
                    </Button>
                  )}
                </span>
              ),
          },
        ]}
      />
      {order.operations.some((o) => o.instructions) && (
        <div className="card mt-4 space-y-1 p-4">
          {order.operations.filter((o) => o.instructions).map((o) => (
            <p key={o.sequence}>
              <span className="font-semibold">Step {o.sequence}:</span> {o.instructions}
            </p>
          ))}
        </div>
      )}
      {reporting && <ReportDialog order={order} operation={reporting} onClose={() => setReporting(null)} />}
    </div>
  );
}

function ReportDialog({ order, operation, onClose }: { order: ProductionOrderDetail; operation: ProductionOperation; onClose(): void }) {
  const act = useOrderAction(order);
  const [minutes, setMinutes] = useState('');
  const [good, setGood] = useState('');
  const [scrap, setScrap] = useState('0');
  const [note, setNote] = useState('');
  return (
    <Dialog
      open
      title={`Report step ${operation.sequence}: ${operation.name}`}
      onClose={onClose}
      onSubmit={() =>
        act.mutate(
          () => productionApi.report(order.id, { sequence: operation.sequence, minutes: parseNumber(minutes) ?? 0, good: parseNumber(good) ?? 0, scrap: parseNumber(scrap) ?? 0, note: textOrNull(note) }),
          { onSuccess: onClose },
        )
      }
      footer={
        <>
          <Button type="submit" variant="sun" shortcut="Enter" busy={act.isPending}>
            Report
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Minutes worked" htmlFor="report-minutes" hint={`Standard for the order: ${formatQuantity(operation.standardMinutes)} minutes.`}>
          <Input id="report-minutes" inputMode="decimal" value={minutes} className="max-w-[140px] text-right" onChange={(e) => setMinutes(e.target.value)} />
        </Field>
        <Field label="Good quantity" htmlFor="report-good">
          <Input id="report-good" inputMode="decimal" value={good} className="max-w-[140px] text-right" onChange={(e) => setGood(e.target.value)} />
        </Field>
        <Field label="Scrapped" htmlFor="report-scrap">
          <Input id="report-scrap" inputMode="decimal" value={scrap} className="max-w-[140px] text-right" onChange={(e) => setScrap(e.target.value)} />
        </Field>
        <Field label="Note" htmlFor="report-note">
          <Input id="report-note" value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} />
        </Field>
      </div>
      <FormError error={act.error} />
    </Dialog>
  );
}

function Output({ order }: { order: ProductionOrderDetail }) {
  const { can } = useSession();
  const act = useOrderAction(order);
  const [quantity, setQuantity] = useState(numberText(Math.max(0, order.quantity - order.received)));
  const [complete, setComplete] = useState(false);
  const byLines = order.lines.filter((l) => l.kind === 'ByProduct');
  const [byQuantities, setByQuantities] = useState<Record<string, string>>(Object.fromEntries(byLines.map((l) => [l.stockCode, numberText(l.planned)])));
  const [inspecting, setInspecting] = useState(false);
  const inspections = useQuery({ queryKey: ['order-inspections', order.id], queryFn: () => productionApi.orderInspections(order.id) });
  const floor = can(P.productionFloor) && workable(order);
  return (
    <div className="space-y-4">
      {floor && (
        <form
          className="card space-y-3 p-4"
          onSubmit={(e) => {
            e.preventDefault();
            const byProducts = complete ? byLines.map((l) => ({ stockCode: l.stockCode, quantity: parseNumber(byQuantities[l.stockCode] ?? '') ?? 0 })) : null;
            act.mutate(() => productionApi.receive(order.id, parseNumber(quantity) ?? 0, complete, byProducts));
          }}
        >
          <h2 className="font-heading text-section">Receive output</h2>
          <Field label="Quantity made" htmlFor="receive-quantity" hint={`Into ${order.outputLocation}.`}>
            <Input id="receive-quantity" inputMode="decimal" value={quantity} className="max-w-[160px] text-right" onChange={(e) => setQuantity(e.target.value)} />
          </Field>
          <Checkbox label="This is the last of it: complete the order" hint="Materials issued at the end are taken from stock, and the output takes all the order's cost." checked={complete} onChange={setComplete} />
          {complete &&
            byLines.map((l) => (
              <Field key={l.stockCode} label={`${l.description} (by-product)`} htmlFor={`by-${l.stockCode}`}>
                <Input id={`by-${l.stockCode}`} inputMode="decimal" value={byQuantities[l.stockCode] ?? ''} className="max-w-[160px] text-right"
                  onChange={(e) => setByQuantities({ ...byQuantities, [l.stockCode]: e.target.value })} />
              </Field>
            ))}
          <Button type="submit" variant="sun" icon={PackageCheck} shortcut="Enter" busy={act.isPending}>
            {complete ? 'Receive and complete' : 'Receive'}
          </Button>
        </form>
      )}
      {byLines.length > 0 && (
        <DataTable<LineProgress>
          rows={byLines}
          rowKey={(l) => l.lineId}
          caption="By-products"
          columns={[
            { key: 'code', header: 'By-product', cell: (l) => <span className="font-semibold">{l.stockCode} {l.description}</span> },
            { key: 'plan', header: 'Expected', numeric: true, cell: (l) => formatQuantity(l.planned) },
            { key: 'done', header: 'Received', numeric: true, cell: (l) => formatQuantity(l.done) },
          ]}
        />
      )}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="font-heading text-section">Inspections</h2>
        {can(P.productionPlan) && order.received > 0 && order.status !== 'Closed' && (
          <Button icon={ClipboardCheck} shortcut="Ctrl+Q" onClick={() => setInspecting(true)}>
            Inspect output
          </Button>
        )}
      </div>
      <DataTable
        rows={inspections.data}
        loading={inspections.isLoading}
        error={inspections.error}
        rowKey={(i) => i.id}
        caption="Inspections of the output"
        empty={<EmptyState title="Not inspected yet" />}
        columns={[
          { key: 'at', header: 'When', cell: (i) => formatLocalDateTime(i.inspectedAt) },
          { key: 'qty', header: 'Inspected', numeric: true, cell: (i) => formatQuantity(i.quantityInspected) },
          { key: 'fail', header: 'Failed', numeric: true, cell: (i) => formatQuantity(i.quantityFailed) },
          { key: 'to', header: 'Failed went', cell: (i) => (i.disposition === 'Hold' ? `On hold at ${i.holdLocation}` : i.disposition === 'Scrap' ? 'Scrapped' : '') },
          { key: 'res', header: 'Result', cell: (i) => <Chip tone={i.passed ? 'ok' : 'danger'}>{i.passed ? 'Passed' : 'Failed'}</Chip> },
          { key: 'by', header: 'By', cell: (i) => i.inspectedBy ?? '', hideBelow: 'md' },
        ]}
      />
      {inspecting && <InspectDialog order={order} onClose={() => setInspecting(false)} />}
    </div>
  );
}

function InspectDialog({ order, onClose }: { order: ProductionOrderDetail; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const plans = useQuery({ queryKey: ['quality-plans'], queryFn: () => productionApi.qualityPlans() });
  const plan = plans.data?.find((p) => p.productCode === order.productCode && p.isActive && p.forProduction) ?? null;
  const locations = useLocations(order.storeId);
  const [inspected, setInspected] = useState(numberText(order.received));
  const [failed, setFailed] = useState('0');
  const [disposition, setDisposition] = useState<InspectionDisposition>('Hold');
  const [hold, setHold] = useState('');
  const [answers, setAnswers] = useState<CheckAnswer[]>([]);
  const [notes, setNotes] = useState('');
  const save = useMutation({
    mutationFn: () =>
      productionApi.inspectOutput(order.id, {
        quantityInspected: parseNumber(inspected) ?? 0,
        quantityFailed: parseNumber(failed) ?? 0,
        disposition,
        holdLocationId: disposition === 'Hold' ? hold || null : null,
        answers: completeAnswers(plan?.checks ?? [], answers),
        notes: textOrNull(notes),
      }),
    onSuccess: (i) => {
      toast.ok(i.passed ? 'The inspection is recorded: passed.' : 'The inspection is recorded: failed.');
      void queryClient.invalidateQueries({ queryKey: ['order-inspections', order.id] });
      void queryClient.invalidateQueries({ queryKey: ['production-order', order.id] });
      onClose();
    },
  });
  const failing = (parseNumber(failed) ?? 0) > 0;
  return (
    <Dialog
      open
      title={`Inspect order ${order.number}`}
      onClose={onClose}
      onSubmit={() => save.mutate()}
      width="lg"
      footer={
        <>
          <Button type="submit" variant="sun" shortcut="Enter" busy={save.isPending}>
            Record the inspection
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Inspected" htmlFor="inspect-quantity">
          <Input id="inspect-quantity" inputMode="decimal" value={inspected} className="max-w-[160px] text-right" onChange={(e) => setInspected(e.target.value)} />
        </Field>
        <Field label="Failed" htmlFor="inspect-failed">
          <Input id="inspect-failed" inputMode="decimal" value={failed} className="max-w-[160px] text-right" onChange={(e) => setFailed(e.target.value)} />
        </Field>
        {failing && (
          <Field label="Failed goods go" htmlFor="inspect-disposition">
            <Select id="inspect-disposition" value={disposition} className="max-w-[260px]" onChange={(e) => setDisposition(e.target.value as InspectionDisposition)}>
              <option value="Hold">To a hold location</option>
              <option value="Scrap">To scrap</option>
              <option value="None">Nowhere (left in stock)</option>
            </Select>
          </Field>
        )}
        {failing && disposition === 'Hold' && (
          <Field label="Hold location" htmlFor="inspect-hold">
            <Select id="inspect-hold" value={hold} className="max-w-[260px]" onChange={(e) => setHold(e.target.value)}>
              <option value="">Choose…</option>
              {(locations.data ?? []).filter((l) => l.id !== order.outputLocationId).map((l) => (
                <option key={l.id} value={l.id}>
                  {l.code} {l.name}
                </option>
              ))}
            </Select>
          </Field>
        )}
        <h3 className="pt-2 font-heading text-section">{plan ? plan.name : 'Checks'}</h3>
        <CheckAnswers checks={plan?.checks ?? []} answers={answers} onChange={setAnswers} />
        <Field label="Notes" htmlFor="inspect-notes">
          <TextArea id="inspect-notes" value={notes} maxLength={400} onChange={(e) => setNotes(e.target.value)} />
        </Field>
      </div>
      <FormError error={save.error} />
    </Dialog>
  );
}

function Costs({ order }: { order: ProductionOrderDetail }) {
  const symbol = useStore().currencySymbol;
  const money = (n: number) => formatMoney(n, symbol);
  const c = order.costs;
  const v = order.variances;
  if (!c || !v) return <div className="card"><EmptyState title="No costs to show" /></div>;
  const lines: Array<[string, number, number, number]> = [
    ['Material', v.standardMaterial, v.actualMaterial, v.actualMaterial - v.standardMaterial],
    ['Labour', v.standardLabour, v.actualLabour, v.labourVariance],
    ['Machine', v.standardMachine, v.actualMachine, v.machineVariance],
    ['Overhead', v.standardOverhead, v.actualOverhead, v.overheadVariance],
  ];
  return (
    <div className="space-y-4">
      <div className="card grid grid-cols-2 gap-4 p-5 md:grid-cols-5">
        <Detail label="Material issued">{money(c.material + c.outputScrap)}</Detail>
        <Detail label="Labour, machine, overhead">{money(c.conversion)}</Detail>
        <Detail label="Received into stock">{money(c.received)}</Detail>
        <Detail label="Work in progress">{money(c.workInProgress)}</Detail>
        <Detail label="Total variance">{money(v.totalVariance)}</Detail>
      </div>
      <DataTable<[string, number, number, number]>
        rows={lines}
        rowKey={(l) => l[0]}
        caption={`Standard against actual for ${formatQuantity(v.outputQuantity)} made`}
        showTotals
        columns={[
          { key: 'what', header: `For ${formatQuantity(v.outputQuantity)} made`, cell: (l) => <span className="font-semibold">{l[0]}</span> },
          { key: 'std', header: 'Standard', numeric: true, cell: (l) => money(l[1]), total: money(v.standardTotal) },
          { key: 'act', header: 'Actual', numeric: true, cell: (l) => money(l[2]), total: money(v.actualTotal) },
          { key: 'var', header: 'Variance', numeric: true, cell: (l) => money(l[3]), total: money(v.totalVariance) },
        ]}
      />
      <DataTable
        rows={v.materials}
        rowKey={(m) => m.stockCode}
        caption="Material usage and price"
        showTotals={v.materials.length > 0}
        columns={[
          { key: 'code', header: 'Material', cell: (m) => <span className="font-semibold">{m.stockCode}</span> },
          { key: 'stdq', header: 'Standard qty', numeric: true, cell: (m) => formatQuantity(m.standardQuantity) },
          { key: 'actq', header: 'Used', numeric: true, cell: (m) => formatQuantity(m.actualQuantity) },
          { key: 'usage', header: 'Usage variance', numeric: true, cell: (m) => money(m.usageVariance), total: money(v.materialUsageVariance) },
          { key: 'price', header: 'Price variance', numeric: true, cell: (m) => money(m.priceVariance), total: money(v.materialPriceVariance) },
        ]}
      />
      <DataTable
        rows={order.postings ?? []}
        rowKey={(p) => p.entryId}
        caption="Ledger entries"
        empty={<EmptyState title="Nothing posted yet">Costs are posted after each change, and from the tills after each send.</EmptyState>}
        columns={[
          { key: 'n', header: 'Entry', cell: (p) => <span className="font-semibold">{p.number}</span> },
          { key: 'step', header: 'Step', cell: (p) => p.step },
          { key: 'date', header: 'Date', cell: (p) => formatDate(p.date) },
          { key: 'memo', header: 'Memo', cell: (p) => p.memo, hideBelow: 'lg', className: 'wrap' },
          { key: 'amount', header: 'Amount', numeric: true, cell: (p) => money(p.amount) },
        ]}
      />
    </div>
  );
}

function History({ order }: { order: ProductionOrderDetail }) {
  const symbol = useStore().currencySymbol;
  const { can } = useSession();
  return (
    <div className="space-y-4">
      <DataTable<ProductionMovement>
        rows={order.movements}
        rowKey={(m, i) => `${m.at}-${i}`}
        caption="Stock movements"
        empty={<EmptyState title="Nothing moved yet" />}
        columns={[
          { key: 'at', header: 'When', cell: (m) => formatLocalDateTime(m.at) },
          { key: 'kind', header: 'What', cell: (m) => movementLabel(m.kind, m.quantity) },
          { key: 'code', header: 'Stock code', cell: (m) => m.stockCode },
          { key: 'qty', header: 'Quantity', numeric: true, cell: (m) => formatQuantity(m.quantity) },
          ...(can(P.productionCosting) ? [{ key: 'value', header: 'Value', numeric: true, cell: (m: ProductionMovement) => formatMoney(m.value, symbol), hideBelow: 'md' as const }] : []),
          { key: 'why', header: 'Note', cell: (m) => m.reason ?? '', hideBelow: 'lg' },
          { key: 'user', header: 'By', cell: (m) => m.user ?? '', hideBelow: 'lg' },
        ]}
      />
      <DataTable
        rows={order.reports}
        rowKey={(r, i) => `${r.at}-${i}`}
        caption="Work reported"
        empty={<EmptyState title="No work reported yet" />}
        columns={[
          { key: 'at', header: 'When', cell: (r) => formatLocalDateTime(r.at) },
          { key: 'op', header: 'Step', cell: (r) => r.operationSequence },
          { key: 'min', header: 'Minutes', numeric: true, cell: (r) => formatQuantity(r.minutes) },
          { key: 'good', header: 'Good', numeric: true, cell: (r) => formatQuantity(r.good) },
          { key: 'scrap', header: 'Scrap', numeric: true, cell: (r) => formatQuantity(r.scrap) },
          { key: 'by', header: 'Operator', cell: (r) => r.userName ?? '' },
          { key: 'note', header: 'Note', cell: (r) => r.note ?? '', hideBelow: 'lg' },
        ]}
      />
      <p className="text-note text-muted">
        Made {formatLocalDateTime(order.createdAt)}
        {order.createdBy ? ` by ${order.createdBy}` : ''}
        {order.releasedAt ? ` · released ${formatLocalDateTime(order.releasedAt)}${order.releasedBy ? ` by ${order.releasedBy}` : ''}` : ''}
        {order.completedAt ? ` · completed ${formatLocalDateTime(order.completedAt)}` : ''}
        {order.closedAt ? ` · closed ${formatLocalDateTime(order.closedAt)}${order.closedBy ? ` by ${order.closedBy}` : ''}` : ''}
      </p>
    </div>
  );
}
