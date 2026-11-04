'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, ChevronDown, ChevronRight, Copy, Plus, Save, Trash2, X } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useLocations, useWorkingStore } from '@/components/production';
import { OperationsTable, type Text } from '@/components/routing-table';
import { Button, IconButton, LinkButton } from '@/components/ui/button';
import { ConfirmDialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select, TextArea } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { ApiError } from '@/lib/api';
import { formatMoney, formatPercent, formatQuantity } from '@/lib/format';
import { numberText, parseNumber, textOrNull } from '@/lib/forms';
import { useHotkey } from '@/lib/hotkeys';
import { P } from '@/lib/nav';
import { checkBom, newBom, nextSequence, treeRows, visibleTreeRows, type BomErrors } from '@/lib/production';
import { productionApi } from '@/lib/production-api';
import type { BomDetail, BomDifference, IssueMethod } from '@/lib/production-types';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';

export default function BomEditorPage() {
  return (
    <Suspense>
      <BomEditor />
    </Suspense>
  );
}

type Tab = 'components' | 'byproducts' | 'routing' | 'tree' | 'cost' | 'versions';

function BomEditor() {
  const id = useSearchParams().get('id');
  const query = useQuery({ queryKey: ['bom', id], queryFn: () => productionApi.bom(id!), enabled: !!id });
  if (id && query.error) return <div className="card"><ErrorState error={query.error} onRetry={() => void query.refetch()} /></div>;
  if (id && !query.data) return <Skeleton className="h-[60vh] rounded-card" />;
  return <Editor key={query.data?.version ?? 'new'} initial={query.data ?? newBom()} />;
}


function Editor({ initial }: { initial: BomDetail }) {
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { can, canChangeShared } = useSession();
  const editable = can(P.productionPlan) && canChangeShared;
  const [bom, setBom] = useState<BomDetail>(initial);
  const [yieldText, setYieldText] = useState(numberText(initial.yieldQuantity));
  const [overheadText, setOverheadText] = useState(numberText(initial.overheadPercent));
  const [lines, setLines] = useState<Array<Text<BomDetail['lines'][number]>>>(initial.lines.map((l) => ({ ...l, quantity: numberText(l.quantity), scrapPercent: numberText(l.scrapPercent), operationSequence: numberText(l.operationSequence) })));
  const [byProducts, setByProducts] = useState<Array<Text<BomDetail['byProducts'][number]>>>(initial.byProducts.map((b) => ({ ...b, quantity: numberText(b.quantity), costSharePercent: numberText(b.costSharePercent) })));
  const [ops, setOps] = useState<Array<Text<BomDetail['operations'][number]>>>(initial.operations.map((o) => ({ ...o, sequence: numberText(o.sequence), setupMinutes: numberText(o.setupMinutes), runMinutesPerUnit: numberText(o.runMinutesPerUnit) })));
  const [errors, setErrors] = useState<BomErrors>({});
  const [tab, setTab] = useState<Tab>('components');
  const [deleting, setDeleting] = useState(false);
  const store = useWorkingStore();
  const centres = useQuery({ queryKey: ['work-centres', null], queryFn: () => productionApi.workCentres({}), staleTime: 60_000 });
  useHotkey('Esc', () => router.push('/boms/'));

  const assembled = (): BomDetail => ({
    ...bom,
    productCode: bom.productCode.trim().toUpperCase(),
    yieldQuantity: parseNumber(yieldText) ?? 0,
    overheadPercent: parseNumber(overheadText) ?? 0,
    notes: textOrNull(bom.notes),
    lines: lines.map((l) => ({ ...l, componentCode: l.componentCode.trim().toUpperCase(), quantity: parseNumber(l.quantity) ?? 0, scrapPercent: parseNumber(l.scrapPercent) ?? 0, operationSequence: parseNumber(l.operationSequence ?? '') })),
    byProducts: byProducts.map((b) => ({ ...b, stockCode: b.stockCode.trim().toUpperCase(), quantity: parseNumber(b.quantity) ?? 0, costSharePercent: parseNumber(b.costSharePercent) ?? 0 })),
    operations: ops.map((o) => ({ ...o, sequence: parseNumber(o.sequence) ?? 0, setupMinutes: parseNumber(o.setupMinutes) ?? 0, runMinutesPerUnit: parseNumber(o.runMinutesPerUnit) ?? 0 })),
  });

  const save = useMutation({
    mutationFn: (body: BomDetail) => (body.id ? productionApi.saveBom(body.id, body) : productionApi.addBom(body)),
    onSuccess: (saved) => {
      toast.ok(`${saved.productCode} version ${saved.revision} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['boms'] });
      queryClient.setQueryData(['bom', saved.id], saved);
      if (!bom.id) router.replace(`/boms/edit/?id=${saved.id}`);
    },
  });
  const copy = useMutation({
    mutationFn: () => productionApi.copyBom(bom.id!),
    onSuccess: (c) => {
      toast.ok(`Version ${c.revision} is a copy, inactive until you make it active.`);
      void queryClient.invalidateQueries({ queryKey: ['boms'] });
      router.push(`/boms/edit/?id=${c.id}`);
    },
    onError: (e: Error) => toast.error(e.message),
  });
  const remove = useMutation({
    mutationFn: () => productionApi.deleteBom(bom.id!),
    onSuccess: () => {
      toast.ok('The bill is deleted.');
      void queryClient.invalidateQueries({ queryKey: ['boms'] });
      router.push('/boms/');
    },
    onError: (e: Error) => toast.error(e.message),
  });

  function submit() {
    const body = assembled();
    const found = checkBom(body);
    setErrors(found);
    if (Object.keys(found).length === 0) save.mutate(body);
    else toast.error(Object.values(found)[0]!);
  }
  useHotkey('Ctrl+S', () => editable && submit());

  const sequences = ops.map((o) => o.sequence).filter(Boolean);
  const tabs: Array<{ key: Tab; label: string }> = [
    { key: 'components', label: `Components (${lines.length})` },
    { key: 'byproducts', label: `By-products (${byProducts.length})` },
    { key: 'routing', label: `Routing (${ops.length})` },
    ...(bom.id ? [{ key: 'tree' as const, label: 'Tree' }, { key: 'cost' as const, label: 'Cost' }, { key: 'versions' as const, label: 'Versions' }] : []),
  ];

  return (
    <div>
      <Toolbar>
        <LinkButton href="/boms/" icon={ArrowLeft} shortcut="Esc">
          Bills of materials
        </LinkButton>
        {editable && (
          <Button variant="sun" icon={Save} shortcut="Ctrl+S" onClick={submit} busy={save.isPending}>
            Save
          </Button>
        )}
        {editable && bom.id && (
          <Button icon={Copy} onClick={() => copy.mutate()} busy={copy.isPending}>
            Copy as a new version
          </Button>
        )}
        {editable && bom.id && (
          <Button icon={Trash2} onClick={() => setDeleting(true)}>
            Delete
          </Button>
        )}
      </Toolbar>
      <PageHeader title={bom.id ? `${bom.productCode} · version ${bom.revision}` : 'New bill of materials'} aside={bom.id && (bom.isActive ? <Chip tone="ok">Active</Chip> : <Chip>Inactive</Chip>)} />
      <div className="card mb-4 grid gap-3 p-5 lg:grid-cols-2">
        <Field label="Product" htmlFor="bom-product" error={errors.productCode} hint={bom.productName ?? 'A stock code, the same at every location.'}>
          <Input id="bom-product" value={bom.productCode} disabled={!editable || !!bom.id} className="uppercase" onChange={(e) => setBom({ ...bom, productCode: e.target.value })} />
        </Field>
        <Field label="Makes" htmlFor="bom-yield" error={errors.yieldQuantity} hint="The quantity one run of the bill makes (its yield).">
          <Input id="bom-yield" inputMode="decimal" value={yieldText} disabled={!editable} className="max-w-[160px] text-right" onChange={(e) => setYieldText(e.target.value)} />
        </Field>
        <Field label="Overhead % of material" htmlFor="bom-overhead" error={errors.overheadPercent}>
          <Input id="bom-overhead" inputMode="decimal" value={overheadText} disabled={!editable} className="max-w-[160px] text-right" onChange={(e) => setOverheadText(e.target.value)} />
        </Field>
        <Field label="In force" htmlFor="bom-from" error={errors.dates}>
          <div className="flex flex-wrap items-center gap-2">
            <Input id="bom-from" type="date" aria-label="In force from" value={bom.effectiveFrom ?? ''} disabled={!editable} className="w-[180px]" onChange={(e) => setBom({ ...bom, effectiveFrom: e.target.value || null })} />
            <span className="text-muted">to</span>
            <Input type="date" aria-label="In force to" value={bom.effectiveTo ?? ''} disabled={!editable} className="w-[180px]" onChange={(e) => setBom({ ...bom, effectiveTo: e.target.value || null })} />
          </div>
        </Field>
        <Checkbox label="Active" hint="Only an active bill in force is used for new orders and planning." checked={bom.isActive} disabled={!editable} onChange={(v) => setBom({ ...bom, isActive: v })} />
        <Field label="Notes" htmlFor="bom-notes">
          <TextArea id="bom-notes" value={bom.notes ?? ''} disabled={!editable} maxLength={400} onChange={(e) => setBom({ ...bom, notes: e.target.value })} />
        </Field>
      </div>
      <Tabs label="Bill" tabs={tabs} value={tab} onChange={setTab} className="mb-4" />
      <FormError error={save.error instanceof ApiError ? save.error : null} />

      {tab === 'components' && (
        <RowsCard error={errors.lines} onAdd={editable ? () => setLines([...lines, { componentCode: '', description: null, quantity: '1', scrapPercent: '0', issueMethod: 'Manual', operationSequence: '' }]) : undefined} addLabel="Add component">
          {lines.length === 0 ? (
            <EmptyState title="No components yet" />
          ) : (
            <table className="grid-table" aria-label="Components">
              <thead>
                <tr>
                  <th>Stock code</th>
                  <th className="num">Quantity</th>
                  <th className="num">Scrap %</th>
                  <th>Issued</th>
                  <th>At step</th>
                  <th className="max-md:hidden">Description</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {lines.map((l, i) => {
                  const set = (patch: Partial<typeof l>) => setLines(lines.map((x, j) => (j === i ? { ...x, ...patch } : x)));
                  return (
                    <tr key={i}>
                      <td><Input aria-label={`Component ${i + 1} stock code`} value={l.componentCode} disabled={!editable} className="min-w-[130px] uppercase" onChange={(e) => set({ componentCode: e.target.value })} /></td>
                      <td><Input aria-label={`Component ${i + 1} quantity`} inputMode="decimal" value={l.quantity} disabled={!editable} className="w-[110px] text-right" onChange={(e) => set({ quantity: e.target.value })} /></td>
                      <td><Input aria-label={`Component ${i + 1} scrap`} inputMode="decimal" value={l.scrapPercent} disabled={!editable} className="w-[90px] text-right" onChange={(e) => set({ scrapPercent: e.target.value })} /></td>
                      <td>
                        <Select aria-label={`Component ${i + 1} issued`} value={l.issueMethod} disabled={!editable} className="w-[150px]" onChange={(e) => set({ issueMethod: e.target.value as IssueMethod })}>
                          <option value="Manual">By hand</option>
                          <option value="Backflush">At the end</option>
                        </Select>
                      </td>
                      <td>
                        <Select aria-label={`Component ${i + 1} step`} value={l.operationSequence ?? ''} disabled={!editable} className="w-[110px]" onChange={(e) => set({ operationSequence: e.target.value })}>
                          <option value="">Start</option>
                          {sequences.map((s) => (
                            <option key={s} value={s}>
                              {s}
                            </option>
                          ))}
                        </Select>
                      </td>
                      <td className="max-md:hidden text-muted">{l.description ?? ''}</td>
                      <td>{editable && <IconButton icon={X} label={`Remove component ${i + 1}`} onClick={() => setLines(lines.filter((_, j) => j !== i))} />}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </RowsCard>
      )}

      {tab === 'byproducts' && (
        <RowsCard error={errors.byProducts} onAdd={editable ? () => setByProducts([...byProducts, { stockCode: '', description: null, quantity: '1', costSharePercent: '0' }]) : undefined} addLabel="Add by-product">
          {byProducts.length === 0 ? (
            <EmptyState title="No by-products">Something else the bill makes takes a share of the cost; the product takes the rest.</EmptyState>
          ) : (
            <table className="grid-table" aria-label="By-products">
              <thead>
                <tr>
                  <th>Stock code</th>
                  <th className="num">Quantity</th>
                  <th className="num">Cost share %</th>
                  <th className="max-md:hidden">Description</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {byProducts.map((b, i) => {
                  const set = (patch: Partial<typeof b>) => setByProducts(byProducts.map((x, j) => (j === i ? { ...x, ...patch } : x)));
                  return (
                    <tr key={i}>
                      <td><Input aria-label={`By-product ${i + 1} stock code`} value={b.stockCode} disabled={!editable} className="min-w-[130px] uppercase" onChange={(e) => set({ stockCode: e.target.value })} /></td>
                      <td><Input aria-label={`By-product ${i + 1} quantity`} inputMode="decimal" value={b.quantity} disabled={!editable} className="w-[110px] text-right" onChange={(e) => set({ quantity: e.target.value })} /></td>
                      <td><Input aria-label={`By-product ${i + 1} cost share`} inputMode="decimal" value={b.costSharePercent} disabled={!editable} className="w-[110px] text-right" onChange={(e) => set({ costSharePercent: e.target.value })} /></td>
                      <td className="max-md:hidden text-muted">{b.description ?? ''}</td>
                      <td>{editable && <IconButton icon={X} label={`Remove by-product ${i + 1}`} onClick={() => setByProducts(byProducts.filter((_, j) => j !== i))} />}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </RowsCard>
      )}

      {tab === 'routing' && (
        <RowsCard
          error={errors.operations}
          addLabel="Add operation"
          onAdd={
            editable
              ? () => {
                  const next = nextSequence(ops.map((o) => ({ ...o, sequence: parseNumber(o.sequence) ?? 0, setupMinutes: 0, runMinutesPerUnit: 0 })));
                  setOps([...ops, { sequence: String(next), workCentreId: centres.data?.[0]?.id ?? '', workCentreCode: null, name: '', setupMinutes: '0', runMinutesPerUnit: '0', instructions: null }]);
                }
              : undefined
          }
        >
          {ops.length === 0 ? (
            <EmptyState title="No operations">The routing says where the product is worked and how long it takes.</EmptyState>
          ) : (
            <OperationsTable ops={ops} setOps={setOps} editable={editable} centres={centres.data ?? []} />
          )}
        </RowsCard>
      )}

      {tab === 'tree' && bom.id && <Tree bomId={bom.id} storeId={store} />}
      {tab === 'cost' && bom.id && <Cost bomId={bom.id} yieldQuantity={bom.yieldQuantity} storeId={store} />}
      {tab === 'versions' && bom.id && <Versions bom={bom} />}

      <ConfirmDialog open={deleting} title={`Delete ${bom.productCode} version ${bom.revision}?`} message="Orders made by it keep their own copy of its lines." danger busy={remove.isPending} onConfirm={() => remove.mutate()} onClose={() => setDeleting(false)} />
    </div>
  );
}

function RowsCard({ children, onAdd, addLabel, error }: { children: ReactNode; onAdd?: () => void; addLabel: string; error?: string }) {
  return (
    <div className="card">
      <div className="grid-scroll">{children}</div>
      <div className="flex flex-wrap items-center gap-3 border-t border-line px-4 py-3">
        {onAdd && (
          <Button icon={Plus} onClick={onAdd}>
            {addLabel}
          </Button>
        )}
        {error && (
          <p className="font-semibold text-danger" role="alert">
            {error}
          </p>
        )}
      </div>
    </div>
  );
}

/** The bill exploded over every level, folding a made component's lines. */
function Tree({ bomId, storeId }: { bomId: string; storeId: string | null }) {
  const symbol = useStore().currencySymbol;
  const locations = useLocations(storeId);
  const [location, setLocation] = useState('');
  const query = useQuery({ queryKey: ['bom-tree', bomId, location], queryFn: () => productionApi.explodeBom(bomId, { locationId: location || null }) });
  const rows = useMemo(() => treeRows(query.data ?? []), [query.data]);
  const [folded, setFolded] = useState<Set<string>>(new Set());
  const shown = visibleTreeRows(rows, folded);
  return (
    <div className="space-y-3">
      <Toolbar>
        <label className="flex items-center gap-2">
          <span className="text-muted">Costs at</span>
          <Select aria-label="Costs at" value={location} className="w-[240px]" onChange={(e) => setLocation(e.target.value)}>
            <option value="">Every location</option>
            {(locations.data ?? []).map((l) => (
              <option key={l.id} value={l.id}>
                {l.code} {l.name}
              </option>
            ))}
          </Select>
        </label>
      </Toolbar>
      <DataTable
        rows={query.data ? shown : undefined}
        loading={query.isLoading}
        error={query.error}
        rowKey={(r) => r.path}
        caption="Exploded bill"
        empty={<EmptyState title="No components" />}
        columns={[
          {
            key: 'code',
            header: 'Component',
            cell: (r) => (
              <span className="flex items-center gap-1" style={{ paddingLeft: `${(r.level - 1) * 22}px` }}>
                {r.hasChildren ? (
                  <button
                    type="button"
                    className="rounded p-0.5 hover:bg-stone-light"
                    aria-label={folded.has(r.path) ? `Show the lines under ${r.stockCode}` : `Hide the lines under ${r.stockCode}`}
                    aria-expanded={!folded.has(r.path)}
                    onClick={() => {
                      const next = new Set(folded);
                      if (next.has(r.path)) next.delete(r.path);
                      else next.add(r.path);
                      setFolded(next);
                    }}
                  >
                    {folded.has(r.path) ? <ChevronRight className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
                  </button>
                ) : (
                  <span className="inline-block w-5" />
                )}
                <span className="font-semibold">{r.stockCode}</span>
                {r.isMade && <Chip>Made</Chip>}
              </span>
            ),
          },
          { key: 'desc', header: 'Description', cell: (r) => r.description, hideBelow: 'md' },
          { key: 'qty', header: 'For one', numeric: true, cell: (r) => formatQuantity(r.quantityPerUnit) },
          { key: 'scrap', header: 'Scrap', numeric: true, cell: (r) => (r.scrapPercent ? formatPercent(r.scrapPercent) : ''), hideBelow: 'lg' },
          { key: 'unit', header: 'Unit cost', numeric: true, cell: (r) => formatMoney(r.unitCost, symbol), hideBelow: 'sm' },
          { key: 'cost', header: 'Cost', numeric: true, cell: (r) => formatMoney(r.cost, symbol) },
        ]}
      />
    </div>
  );
}

/** The cost rolled up for a lot: materials, labour, machine, overhead, and the product's share. */
function Cost({ bomId, yieldQuantity, storeId }: { bomId: string; yieldQuantity: number; storeId: string | null }) {
  const symbol = useStore().currencySymbol;
  const money = (n: number) => formatMoney(n, symbol);
  const locations = useLocations(storeId);
  const [location, setLocation] = useState('');
  const [lotText, setLotText] = useState(numberText(yieldQuantity));
  const lot = parseNumber(lotText);
  const query = useQuery({ queryKey: ['bom-cost', bomId, location, lot], queryFn: () => productionApi.bomCost(bomId, { locationId: location || null, quantity: lot && lot > 0 ? lot : null }) });
  const c = query.data;
  return (
    <div className="space-y-4">
      <Toolbar>
        <label className="flex items-center gap-2">
          <span className="text-muted">For a lot of</span>
          <Input aria-label="Lot quantity" inputMode="decimal" value={lotText} className="w-[120px] text-right" onChange={(e) => setLotText(e.target.value)} />
        </label>
        <label className="flex items-center gap-2">
          <span className="text-muted">Costs at</span>
          <Select aria-label="Costs at" value={location} className="w-[240px]" onChange={(e) => setLocation(e.target.value)}>
            <option value="">Every location</option>
            {(locations.data ?? []).map((l) => (
              <option key={l.id} value={l.id}>
                {l.code} {l.name}
              </option>
            ))}
          </Select>
        </label>
      </Toolbar>
      {query.error ? (
        <div className="card"><ErrorState error={query.error} /></div>
      ) : !c ? (
        <Skeleton className="h-40 rounded-card" />
      ) : (
        <>
          <div className="card grid grid-cols-2 gap-4 p-5 md:grid-cols-6">
            <Figure label="Material" value={money(c.material)} />
            <Figure label="Labour" value={money(c.labour)} />
            <Figure label="Machine" value={money(c.machine)} />
            <Figure label="Overhead" value={money(c.overhead)} />
            <Figure label={`Total for ${formatQuantity(c.lotQuantity)}`} value={money(c.total)} />
            <Figure label={`One unit (${formatPercent(c.productSharePercent, undefined, 0)} of the cost)`} value={money(c.unitCost)} strong />
          </div>
          <DataTable
            rows={c.lines}
            rowKey={(l) => l.stockCode}
            caption="Materials"
            showTotals
            columns={[
              { key: 'code', header: 'Material', cell: (l) => <span className="font-semibold">{l.stockCode}{l.isMade ? ' (made)' : ''}</span> },
              { key: 'qty', header: 'Quantity', numeric: true, cell: (l) => formatQuantity(l.quantity) },
              { key: 'unit', header: 'Unit cost', numeric: true, cell: (l) => money(l.unitCost) },
              { key: 'cost', header: 'Cost', numeric: true, cell: (l) => money(l.cost), total: money(c.material) },
            ]}
          />
          {c.operations.length > 0 && (
            <DataTable
              rows={c.operations}
              rowKey={(o) => String(o.sequence)}
              caption="Operations"
              columns={[
                { key: 'seq', header: 'Step', cell: (o) => <span className="font-semibold">{o.sequence} {o.name}</span> },
                { key: 'centre', header: 'Work centre', cell: (o) => o.workCentre },
                { key: 'min', header: 'Minutes', numeric: true, cell: (o) => formatQuantity(o.minutes) },
                { key: 'lab', header: 'Labour', numeric: true, cell: (o) => money(o.labour) },
                { key: 'mac', header: 'Machine', numeric: true, cell: (o) => money(o.machine) },
                { key: 'ovh', header: 'Overhead', numeric: true, cell: (o) => money(o.overhead) },
              ]}
            />
          )}
        </>
      )}
    </div>
  );
}

function Figure({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div>
      <p className="text-note text-muted">{label}</p>
      <p className={strong ? 'font-heading text-section text-ink' : 'font-semibold text-ink'}>{value}</p>
    </div>
  );
}

/** The product's other versions, and what changed against one of them. */
function Versions({ bom }: { bom: BomDetail }) {
  const router = useRouter();
  const versions = useQuery({ queryKey: ['boms', bom.productCode, false], queryFn: () => productionApi.boms({ search: bom.productCode }) });
  const others = (versions.data ?? []).filter((b) => b.productCode === bom.productCode && b.id !== bom.id);
  const [other, setOther] = useState('');
  useEffect(() => {
    if (!other && others[0]) setOther(others[0].id);
  }, [other, others]);
  const diff = useQuery({ queryKey: ['bom-compare', other, bom.id], queryFn: () => productionApi.compareBoms(other, bom.id!), enabled: !!other });
  if (versions.data && others.length === 0) return <div className="card"><EmptyState title="This is the only version">Copy it as a new version to prepare a change.</EmptyState></div>;
  return (
    <div className="space-y-3">
      <Toolbar>
        <label className="flex items-center gap-2">
          <span className="text-muted">Compare with</span>
          <Select aria-label="Compare with" value={other} className="w-[220px]" onChange={(e) => setOther(e.target.value)}>
            {others.map((b) => (
              <option key={b.id} value={b.id}>
                Version {b.revision}
                {b.inForce ? ' (in force)' : ''}
              </option>
            ))}
          </Select>
        </label>
        {other && (
          <Button onClick={() => router.push(`/boms/edit/?id=${other}`)}>Open that version</Button>
        )}
      </Toolbar>
      <DataTable<BomDifference>
        rows={diff.data}
        loading={diff.isLoading}
        error={diff.error}
        rowKey={(d, i) => `${d.part}-${d.key}-${i}`}
        caption="Differences"
        empty={<EmptyState title="The two versions are the same" />}
        columns={[
          { key: 'part', header: 'Part', cell: (d) => d.part },
          { key: 'key', header: 'What', cell: (d) => <span className="font-semibold">{d.key}</span> },
          { key: 'change', header: 'Change', cell: (d) => <Chip tone={d.change === 'Added' ? 'ok' : d.change === 'Removed' ? 'danger' : 'warn'}>{d.change}</Chip> },
          { key: 'before', header: 'Before', cell: (d) => d.before ?? '', className: 'wrap' },
          { key: 'after', header: 'Now', cell: (d) => d.after ?? '', className: 'wrap' },
        ]}
      />
    </div>
  );
}
