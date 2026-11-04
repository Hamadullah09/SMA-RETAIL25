'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CheckCheck, Play, Save, X } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { LocationSelect, StoreField, useWorkingStore } from '@/components/production';
import { Button } from '@/components/ui/button';
import { Field, Input } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { formatCount, formatDate, formatQuantity, formatUtc } from '@/lib/format';
import { numberText, parseNumber } from '@/lib/forms';
import { P } from '@/lib/nav';
import { productionApi } from '@/lib/production-api';
import type { LeadTime, Suggestion, SuggestionKind } from '@/lib/production-types';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';

export default function PlanningPage() {
  return (
    <Suspense>
      <Planning />
    </Suspense>
  );
}

type Tab = 'suggestions' | 'lead-times';

/** Planning (MRP): run it for a store, then firm what it suggests into production and purchase orders. */
function Planning() {
  const [tab, setTab] = useState<Tab>('suggestions');
  return (
    <div>
      <PageHeader title="Planning" />
      <Tabs label="Planning" value={tab} onChange={setTab} className="mb-4" tabs={[{ key: 'suggestions', label: 'Run and suggestions' }, { key: 'lead-times', label: 'Supplier lead times' }]} />
      {tab === 'suggestions' ? <Suggestions /> : <LeadTimes />}
    </div>
  );
}

function Suggestions() {
  const { can } = useSession();
  const { storeId } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const router = useRouter();
  const params = useSearchParams();
  const working = useWorkingStore();
  const [store, setStore] = useState<string | null>(working);
  const [location, setLocation] = useState('');
  const [horizon, setHorizon] = useState('60');
  const [lead, setLead] = useState('7');
  const [kind, setKind] = useState<'all' | SuggestionKind>('all');
  const [chosen, setChosen] = useState<Set<string>>(new Set());
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const view = useQuery({ queryKey: ['mrp', storeId], queryFn: () => productionApi.suggestions({ storeId }) });
  const plan = can(P.productionPlan);
  const refresh = () => {
    setChosen(new Set());
    setQuantities({});
    void queryClient.invalidateQueries({ queryKey: ['mrp'] });
  };

  const run = useMutation({
    mutationFn: () => productionApi.runPlanning({ storeId: store, locationId: location || null, horizonDays: parseNumber(horizon) ?? 60, defaultLeadDays: parseNumber(lead) ?? 7 }),
    onSuccess: (v) => {
      toast.ok(`Planning suggests ${formatCount(v.suggestions.filter((s) => s.kind === 'Make').length)} to make and ${formatCount(v.suggestions.filter((s) => s.kind === 'Buy').length)} to buy.`);
      queryClient.setQueryData(['mrp', storeId], v);
      refresh();
    },
  });
  useEffect(() => {
    if (params.get('run') === '1' && plan && !run.isPending) {
      router.replace('/planning/');
      run.mutate();
    }
    // Only when the page is opened with ?run=1.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params]);

  const firm = useMutation({
    mutationFn: () => {
      const q: Record<string, number> = {};
      for (const [id, text] of Object.entries(quantities)) if (chosen.has(id) && parseNumber(text)) q[id] = parseNumber(text)!;
      return productionApi.firm([...chosen], q);
    },
    onSuccess: (r) => {
      if (r.made.length) toast.ok(r.made.join(' · '));
      if (r.problems.length) toast.error(r.problems.join(' '));
      refresh();
    },
    onError: (e: Error) => toast.error(e.message),
  });
  const dismiss = useMutation({
    mutationFn: () => productionApi.dismiss([...chosen]),
    onSuccess: (r) => {
      toast.ok(`${formatCount(r.dismissed)} suggestion(s) dismissed.`);
      refresh();
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const rows = (view.data?.suggestions ?? []).filter((s) => kind === 'all' || s.kind === kind);
  const toggle = (id: string) => {
    const next = new Set(chosen);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setChosen(next);
  };
  const runInfo = view.data?.run;

  return (
    <div className="space-y-4">
      {plan && (
        <form className="card grid gap-3 p-5 lg:grid-cols-2" onSubmit={(e) => { e.preventDefault(); run.mutate(); }}>
          <StoreField id="mrp-store" value={store} onChange={setStore} />
          <LocationSelect id="mrp-location" label="Make and receive at" storeId={store} value={location} onChange={setLocation} />
          <Field label="Days ahead" htmlFor="mrp-horizon" hint="Customer orders due later wait for a later run.">
            <Input id="mrp-horizon" inputMode="numeric" value={horizon} className="max-w-[120px] text-right" onChange={(e) => setHorizon(e.target.value)} />
          </Field>
          <Field label="Lead time" htmlFor="mrp-lead" hint="Days, for suppliers without a lead time of their own.">
            <Input id="mrp-lead" inputMode="numeric" value={lead} className="max-w-[120px] text-right" onChange={(e) => setLead(e.target.value)} />
          </Field>
          <div className="flex flex-wrap items-center gap-3 lg:col-span-2">
            <Button type="submit" variant="sun" icon={Play} shortcut="F5" busy={run.isPending}>
              Run planning
            </Button>
            <span className="text-muted">
              Demand: open customer orders, items below their reorder point, and production orders. Stock at every location of the store, open purchase and production orders net it.
            </span>
          </div>
          <FormError error={run.error} />
        </form>
      )}
      <Toolbar>
        <Tabs label="Kind" value={kind} onChange={setKind} tabs={[{ key: 'all', label: 'All' }, { key: 'Make', label: 'To make' }, { key: 'Buy', label: 'To buy' }]} />
        {plan && (
          <>
            <Button variant="sun" icon={CheckCheck} disabled={chosen.size === 0} busy={firm.isPending} onClick={() => firm.mutate()}>
              Firm {chosen.size > 0 ? formatCount(chosen.size) : ''}
            </Button>
            <Button icon={X} disabled={chosen.size === 0} busy={dismiss.isPending} onClick={() => dismiss.mutate()}>
              Dismiss
            </Button>
          </>
        )}
        <span className="text-muted">
          {runInfo ? `Last run ${formatUtc(runInfo.runAtUtc)}${runInfo.runBy ? ` by ${runInfo.runBy}` : ''}, ${runInfo.horizonDays} days ahead, at ${runInfo.locationCode}.` : 'Not run yet for this store.'}
        </span>
      </Toolbar>
      <DataTable<Suggestion>
        rows={view.data ? rows : undefined}
        loading={view.isLoading}
        error={view.error}
        onRetry={() => void view.refetch()}
        rowKey={(s) => s.id}
        caption="Suggestions"
        empty={<EmptyState title="Nothing to make or buy">Run planning to see what is needed.</EmptyState>}
        columns={[
          ...(plan
            ? [{ key: 'pick', header: <span className="sr-only">Choose</span>, cell: (s: Suggestion) => <input type="checkbox" className="check" aria-label={`Choose ${s.stockCode}`} checked={chosen.has(s.id)} onChange={() => toggle(s.id)} /> }]
            : []),
          { key: 'kind', header: 'Do', cell: (s) => <Chip tone={s.kind === 'Make' ? 'dark' : 'plain'}>{s.kind === 'Make' ? 'Make' : 'Buy'}</Chip> },
          { key: 'code', header: 'Stock code', cell: (s) => <span className="font-semibold">{s.stockCode}</span> },
          { key: 'desc', header: 'Description', cell: (s) => s.description, hideBelow: 'md' },
          {
            key: 'qty',
            header: 'Quantity',
            numeric: true,
            cell: (s) =>
              plan && chosen.has(s.id) ? (
                <Input aria-label={`Quantity of ${s.stockCode}`} inputMode="decimal" className="w-[110px] text-right" value={quantities[s.id] ?? numberText(s.quantity)} onChange={(e) => setQuantities({ ...quantities, [s.id]: e.target.value })} />
              ) : (
                formatQuantity(s.quantity)
              ),
          },
          { key: 'start', header: 'Order or start by', cell: (s) => (s.late ? <Chip tone="danger">{formatDate(s.startBy)}</Chip> : formatDate(s.startBy)) },
          { key: 'need', header: 'Needed by', cell: (s) => formatDate(s.needBy), hideBelow: 'sm' },
          { key: 'lead', header: 'Lead days', numeric: true, cell: (s) => s.leadDays, hideBelow: 'lg' },
          { key: 'sup', header: 'Supplier', cell: (s) => s.supplierName ?? '', hideBelow: 'lg' },
          { key: 'why', header: 'For', cell: (s) => (s.fromRelease ? `${s.reason ?? ''} (at release)` : s.reason ?? ''), hideBelow: 'xl', className: 'wrap' },
        ]}
      />
    </div>
  );
}

function LeadTimes() {
  const { can, canChangeShared } = useSession();
  const toast = useToast();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['lead-times'], queryFn: () => productionApi.leadTimes() });
  const [edits, setEdits] = useState<Record<string, string>>({});
  const edit = can(P.productionPlan) && canChangeShared;
  const save = useMutation({
    mutationFn: async () => {
      for (const [id, text] of Object.entries(edits)) await productionApi.setLeadTime(id, text.trim() === '' ? null : parseNumber(text));
    },
    onSuccess: () => {
      toast.ok('The lead times are saved.');
      setEdits({});
      void queryClient.invalidateQueries({ queryKey: ['lead-times'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  return (
    <div className="space-y-3">
      <Toolbar>
        {edit && (
          <Button variant="sun" icon={Save} shortcut="Ctrl+S" disabled={Object.keys(edits).length === 0} busy={save.isPending} onClick={() => save.mutate()}>
            Save lead times
          </Button>
        )}
        <span className="text-muted">Days from ordering to delivery. Empty uses the run’s lead time.</span>
      </Toolbar>
      <DataTable<LeadTime>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        rowKey={(t) => t.supplierId}
        caption="Supplier lead times"
        empty={<EmptyState title="No suppliers yet" />}
        columns={[
          { key: 'sup', header: 'Supplier', cell: (t) => <span className="font-semibold">{t.supplier}</span> },
          {
            key: 'days',
            header: 'Days',
            numeric: true,
            cell: (t) =>
              edit ? (
                <Input aria-label={`Lead time of ${t.supplier}`} inputMode="numeric" className="w-[100px] text-right" value={edits[t.supplierId] ?? numberText(t.days)} onChange={(e) => setEdits({ ...edits, [t.supplierId]: e.target.value })} />
              ) : (
                t.days ?? ''
              ),
          },
        ]}
      />
    </div>
  );
}
