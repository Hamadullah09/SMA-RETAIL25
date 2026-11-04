'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ClipboardCheck, Plus, Save, Trash2, X } from 'lucide-react';
import { useState } from 'react';
import { CheckAnswers } from '@/components/production';
import { Button, IconButton } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select, TextArea } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { formatCount, formatLocalDateTime, formatQuantity } from '@/lib/format';
import { numberText, parseNumber, textOrNull } from '@/lib/forms';
import { P } from '@/lib/nav';
import { completeAnswers, rangeText } from '@/lib/production';
import { productionApi } from '@/lib/production-api';
import type { CheckAnswer, Inspection, QualityCheck, QualityCheckKind, QualityPlan, ReceivedLine, ReceivedOrder } from '@/lib/production-types';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';

type Tab = 'plans' | 'inspections' | 'incoming';

/** Quality: the checks of each product, inspections of what was made, and of what suppliers delivered. */
export default function QualityPage() {
  const [tab, setTab] = useState<Tab>('plans');
  return (
    <div>
      <PageHeader title="Quality" />
      <Tabs
        label="Quality"
        value={tab}
        onChange={setTab}
        className="mb-4"
        tabs={[
          { key: 'plans', label: 'Quality plans' },
          { key: 'inspections', label: 'Inspections' },
          { key: 'incoming', label: 'Goods received' },
        ]}
      />
      {tab === 'plans' && <Plans />}
      {tab === 'inspections' && <Inspections />}
      {tab === 'incoming' && <Incoming />}
    </div>
  );
}

const KINDS: Record<QualityCheckKind, string> = { PassFail: 'Pass or fail', Number: 'A measurement', Text: 'A note' };

function Plans() {
  const { can, canChangeShared } = useSession();
  const [editing, setEditing] = useState<QualityPlan | 'new' | null>(null);
  const query = useQuery({ queryKey: ['quality-plans'], queryFn: () => productionApi.qualityPlans() });
  const edit = can(P.productionPlan) && canChangeShared;
  return (
    <>
      <Toolbar>
        {edit && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            New plan
          </Button>
        )}
        <span className="text-ink">One plan per product: the checks made on what is made, what is received, or both.</span>
      </Toolbar>
      <DataTable<QualityPlan>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(p) => p.id ?? p.productCode}
        caption="Quality plans"
        onRowClick={edit ? (p) => setEditing(p) : undefined}
        empty={<EmptyState title="No quality plans yet" />}
        columns={[
          { key: 'code', header: 'Product', cell: (p) => <span className="font-semibold">{p.productCode}</span> },
          { key: 'name', header: 'Plan', cell: (p) => p.name },
          { key: 'for', header: 'Checks', cell: (p) => [p.forProduction && 'Made', p.forIncoming && 'Received'].filter(Boolean).join(' and ') },
          { key: 'n', header: 'Checks', numeric: true, cell: (p) => p.checks.length, hideBelow: 'sm' },
          { key: 'state', header: '', cell: (p) => (p.isActive ? '' : <Chip>Inactive</Chip>) },
        ]}
      />
      {editing && <PlanDialog plan={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

type CheckRow = Omit<QualityCheck, 'minimum' | 'maximum'> & { minimum: string; maximum: string };

function PlanDialog({ plan, onClose }: { plan: QualityPlan | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [code, setCode] = useState(plan?.productCode ?? '');
  const [name, setName] = useState(plan?.name ?? '');
  const [active, setActive] = useState(plan?.isActive ?? true);
  const [made, setMade] = useState(plan?.forProduction ?? true);
  const [received, setReceived] = useState(plan?.forIncoming ?? false);
  const [notes, setNotes] = useState(plan?.notes ?? '');
  const [checks, setChecks] = useState<CheckRow[]>(
    (plan?.checks ?? [{ lineNumber: 1, name: '', kind: 'PassFail' as const, minimum: null, maximum: null, unit: null, required: true }]).map((c) => ({ ...c, minimum: numberText(c.minimum), maximum: numberText(c.maximum) })),
  );
  const [problem, setProblem] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);
  const done = () => {
    void queryClient.invalidateQueries({ queryKey: ['quality-plans'] });
    onClose();
  };
  const save = useMutation({
    mutationFn: () => {
      const body: QualityPlan = {
        id: plan?.id ?? null, version: plan?.version ?? null, productCode: code.trim().toUpperCase(), name: name.trim(), isActive: active, forProduction: made, forIncoming: received,
        notes: textOrNull(notes), checks: checks.map((c, i) => ({ ...c, lineNumber: i + 1, name: c.name.trim(), minimum: parseNumber(c.minimum), maximum: parseNumber(c.maximum) })),
      };
      return plan?.id ? productionApi.saveQualityPlan(plan.id, body) : productionApi.addQualityPlan(body);
    },
    onSuccess: (p) => {
      toast.ok(`The plan for ${p.productCode} is saved.`);
      done();
    },
  });
  const remove = useMutation({ mutationFn: () => productionApi.deleteQualityPlan(plan!.id!), onSuccess: () => { toast.ok('The plan is deleted.'); done(); } });
  function submit() {
    const text = !code.trim() ? 'Enter the product\'s stock code.' : !name.trim() ? 'Give the plan a name.' : !made && !received ? 'Say whether it checks what is made, what is received, or both.'
      : checks.length === 0 || checks.some((c) => !c.name.trim()) ? 'Every check needs a name.' : null;
    setProblem(text);
    if (!text) save.mutate();
  }
  return (
    <Dialog
      open
      title={plan ? `Plan for ${plan.productCode}` : 'New quality plan'}
      onClose={onClose}
      onSubmit={submit}
      width="xl"
      footer={
        <>
          {plan && (
            <Button icon={Trash2} className="mr-auto" onClick={() => setDeleting(true)}>
              Delete
            </Button>
          )}
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Save
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Product" htmlFor="plan-code">
          <Input id="plan-code" value={code} maxLength={16} className="max-w-[220px] uppercase" onChange={(e) => setCode(e.target.value)} />
        </Field>
        <Field label="Name" htmlFor="plan-name">
          <Input id="plan-name" value={name} maxLength={60} onChange={(e) => setName(e.target.value)} />
        </Field>
        <div className="flex flex-wrap gap-6">
          <Checkbox label="Checks what is made" checked={made} onChange={setMade} />
          <Checkbox label="Checks what is received" checked={received} onChange={setReceived} />
          <Checkbox label="Active" checked={active} onChange={setActive} />
        </div>
        <div className="grid-scroll">
          <table className="grid-table" aria-label="Checks">
            <thead>
              <tr>
                <th>Check</th>
                <th>Kind</th>
                <th className="num">Minimum</th>
                <th className="num">Maximum</th>
                <th>Unit</th>
                <th>Required</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {checks.map((c, i) => {
                const set = (patch: Partial<CheckRow>) => setChecks(checks.map((x, j) => (j === i ? { ...x, ...patch } : x)));
                const number = c.kind === 'Number';
                return (
                  <tr key={i}>
                    <td><Input aria-label={`Check ${i + 1} name`} value={c.name} className="min-w-[180px]" onChange={(e) => set({ name: e.target.value })} /></td>
                    <td>
                      <Select aria-label={`Check ${i + 1} kind`} value={c.kind} className="w-[170px]" onChange={(e) => set({ kind: e.target.value as QualityCheckKind })}>
                        {Object.entries(KINDS).map(([k, label]) => (
                          <option key={k} value={k}>
                            {label}
                          </option>
                        ))}
                      </Select>
                    </td>
                    <td><Input aria-label={`Check ${i + 1} minimum`} inputMode="decimal" disabled={!number} value={c.minimum} className="w-[100px] text-right" onChange={(e) => set({ minimum: e.target.value })} /></td>
                    <td><Input aria-label={`Check ${i + 1} maximum`} inputMode="decimal" disabled={!number} value={c.maximum} className="w-[100px] text-right" onChange={(e) => set({ maximum: e.target.value })} /></td>
                    <td><Input aria-label={`Check ${i + 1} unit`} disabled={!number} value={c.unit ?? ''} maxLength={10} className="w-[80px]" onChange={(e) => set({ unit: e.target.value || null })} /></td>
                    <td><input type="checkbox" className="check" aria-label={`Check ${i + 1} required`} checked={c.required} onChange={(e) => set({ required: e.target.checked })} /></td>
                    <td><IconButton icon={X} label={`Remove check ${i + 1}`} onClick={() => setChecks(checks.filter((_, j) => j !== i))} /></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <Button icon={Plus} onClick={() => setChecks([...checks, { lineNumber: checks.length + 1, name: '', kind: 'PassFail', minimum: '', maximum: '', unit: null, required: true }])}>
          Add check
        </Button>
        <Field label="Notes" htmlFor="plan-notes">
          <TextArea id="plan-notes" value={notes} maxLength={400} onChange={(e) => setNotes(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? save.error ?? remove.error} />
      <ConfirmDialog open={deleting} title={`Delete the plan for ${plan?.productCode}?`} message="Inspections already made keep their results." danger busy={remove.isPending} onConfirm={() => remove.mutate()} onClose={() => setDeleting(false)} />
    </Dialog>
  );
}

function Inspections() {
  const { storeId } = useStore();
  const [kind, setKind] = useState('');
  const query = useQuery({ queryKey: ['inspections', storeId, kind], queryFn: () => productionApi.inspections({ storeId, kind: kind || null }) });
  const [shown, setShown] = useState<Inspection | null>(null);
  return (
    <>
      <Toolbar>
        <label className="flex items-center gap-2">
          <span className="text-muted">Of</span>
          <Select aria-label="Inspections of" value={kind} className="w-[220px]" onChange={(e) => setKind(e.target.value)}>
            <option value="">Everything</option>
            <option value="Production">What was made</option>
            <option value="Incoming">What was received</option>
          </Select>
        </label>
        {query.data && <span className="text-muted">{formatCount(query.data.length)} inspections</span>}
      </Toolbar>
      <DataTable<Inspection>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(i) => i.id}
        caption="Inspections"
        onRowClick={setShown}
        empty={<EmptyState title="No inspections yet" />}
        columns={[
          { key: 'at', header: 'When', cell: (i) => formatLocalDateTime(i.inspectedAt) },
          { key: 'of', header: 'Of', cell: (i) => (i.kind === 'Production' ? `Production order ${i.documentNumber}` : `Purchase order ${i.documentNumber}`) },
          { key: 'code', header: 'Stock code', cell: (i) => <span className="font-semibold">{i.stockCode}</span> },
          { key: 'qty', header: 'Inspected', numeric: true, cell: (i) => formatQuantity(i.quantityInspected) },
          { key: 'fail', header: 'Failed', numeric: true, cell: (i) => formatQuantity(i.quantityFailed) },
          { key: 'res', header: 'Result', cell: (i) => <Chip tone={i.passed ? 'ok' : 'danger'}>{i.passed ? 'Passed' : 'Failed'}</Chip> },
          { key: 'by', header: 'By', cell: (i) => i.inspectedBy ?? '', hideBelow: 'md' },
          { key: 'store', header: 'Store', cell: (i) => i.storeCode, hideBelow: 'lg' },
        ]}
      />
      {shown && (
        <Dialog open title={`Inspection of ${shown.stockCode}`} onClose={() => setShown(null)} width="lg" footer={<Button shortcut="Esc" onClick={() => setShown(null)}>Close</Button>}>
          <p className="mb-3 text-ink">
            {formatQuantity(shown.quantityInspected)} inspected, {formatQuantity(shown.quantityFailed)} failed
            {shown.disposition === 'Hold' ? `, held at ${shown.holdLocation}` : shown.disposition === 'Scrap' ? ', scrapped' : ''}.{shown.notes ? ` ${shown.notes}` : ''}
          </p>
          <DataTable
            rows={shown.results}
            rowKey={(r) => String(r.lineNumber)}
            caption="Results"
            empty={<EmptyState title="No checks (the product had no plan)" />}
            columns={[
              { key: 'check', header: 'Check', cell: (r) => <span className="font-semibold">{r.checkName}</span> },
              { key: 'value', header: 'Result', cell: (r) => (r.kind === 'PassFail' ? (r.passFail ? 'Pass' : 'Fail') : r.kind === 'Number' ? `${r.numberValue ?? ''} (${rangeText({ minimum: r.minimum, maximum: r.maximum, unit: null })})` : r.textValue ?? '') },
              { key: 'ok', header: '', cell: (r) => <Chip tone={r.passed ? 'ok' : 'danger'}>{r.passed ? 'Passed' : 'Failed'}</Chip> },
            ]}
          />
        </Dialog>
      )}
    </>
  );
}

function Incoming() {
  const { storeId } = useStore();
  const { can } = useSession();
  const query = useQuery({ queryKey: ['quality-receipts', storeId], queryFn: () => productionApi.receivedOrders({ storeId }) });
  const [inspecting, setInspecting] = useState<{ order: ReceivedOrder; line: ReceivedLine } | null>(null);
  const rows = (query.data ?? []).flatMap((order) => order.lines.map((line) => ({ order, line })));
  return (
    <>
      <Toolbar>
        <span className="text-ink">Purchase orders received in the last 90 days. An inspection records the results; the receipt itself does not change.</span>
      </Toolbar>
      <DataTable
        rows={query.data ? rows : undefined}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => `${r.order.id}-${r.line.plu}`}
        caption="Goods received"
        empty={<EmptyState title="Nothing received lately" />}
        columns={[
          { key: 'po', header: 'Purchase order', cell: (r) => <span className="font-semibold">{r.order.number}</span> },
          { key: 'at', header: 'Received', cell: (r) => (r.order.receivedAt ? formatLocalDateTime(r.order.receivedAt) : ''), hideBelow: 'md' },
          { key: 'code', header: 'Stock code', cell: (r) => r.line.plu },
          { key: 'desc', header: 'Description', cell: (r) => r.line.description, hideBelow: 'lg' },
          { key: 'qty', header: 'Received', numeric: true, cell: (r) => formatQuantity(r.line.received) },
          { key: 'plan', header: 'Plan', cell: (r) => (r.line.hasPlan ? <Chip tone="ok">Has checks</Chip> : '') },
          { key: 'done', header: 'Inspected', numeric: true, cell: (r) => (r.line.inspections ? formatCount(r.line.inspections) : '') },
          {
            key: 'act',
            header: '',
            cell: (r) =>
              can(P.productionPlan) && (
                <Button icon={ClipboardCheck} onClick={() => setInspecting(r)} aria-label={`Inspect ${r.line.plu} of purchase order ${r.order.number}`}>
                  Inspect
                </Button>
              ),
          },
        ]}
      />
      {inspecting && <IncomingDialog order={inspecting.order} line={inspecting.line} onClose={() => setInspecting(null)} />}
    </>
  );
}

function IncomingDialog({ order, line, onClose }: { order: ReceivedOrder; line: ReceivedLine; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const plans = useQuery({ queryKey: ['quality-plans'], queryFn: () => productionApi.qualityPlans() });
  const plan = plans.data?.find((p) => p.productCode === line.plu && p.isActive && p.forIncoming) ?? null;
  const [inspected, setInspected] = useState(numberText(line.received));
  const [failed, setFailed] = useState('0');
  const [answers, setAnswers] = useState<CheckAnswer[]>([]);
  const [notes, setNotes] = useState('');
  const save = useMutation({
    mutationFn: () =>
      productionApi.inspectReceipt(order.id, {
        stockCode: line.plu, quantityInspected: parseNumber(inspected) ?? 0, quantityFailed: parseNumber(failed) ?? 0, answers: completeAnswers(plan?.checks ?? [], answers), notes: textOrNull(notes),
      }),
    onSuccess: (i) => {
      toast.ok(i.passed ? 'Recorded: passed.' : 'Recorded: failed.');
      void queryClient.invalidateQueries({ queryKey: ['quality-receipts'] });
      void queryClient.invalidateQueries({ queryKey: ['inspections'] });
      onClose();
    },
  });
  return (
    <Dialog
      open
      title={`Inspect ${line.plu} of purchase order ${order.number}`}
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
        <Field label="Inspected" htmlFor="incoming-quantity" hint={`${formatQuantity(line.received)} received.`}>
          <Input id="incoming-quantity" inputMode="decimal" value={inspected} className="max-w-[160px] text-right" onChange={(e) => setInspected(e.target.value)} />
        </Field>
        <Field label="Failed" htmlFor="incoming-failed">
          <Input id="incoming-failed" inputMode="decimal" value={failed} className="max-w-[160px] text-right" onChange={(e) => setFailed(e.target.value)} />
        </Field>
        <CheckAnswers checks={plan?.checks ?? []} answers={answers} onChange={setAnswers} />
        <Field label="Notes" htmlFor="incoming-notes">
          <TextArea id="incoming-notes" value={notes} maxLength={400} onChange={(e) => setNotes(e.target.value)} />
        </Field>
      </div>
      <FormError error={save.error} />
    </Dialog>
  );
}
