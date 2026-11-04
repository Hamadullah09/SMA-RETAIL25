'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, Save } from 'lucide-react';
import { useState } from 'react';
import { OperationsTable, routingText, type OpText } from '@/components/routing-table';
import { Button } from '@/components/ui/button';
import { Chip, PageHeader, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { formatCount, formatQuantity } from '@/lib/format';
import { parseNumber } from '@/lib/forms';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { checkRouting, nextSequence } from '@/lib/production';
import { productionApi } from '@/lib/production-api';
import type { BomDetail, BomListRow, RoutingOperation } from '@/lib/production-types';
import { useSession } from '@/lib/session';

/** The routings: each bill's operations, where they are done and how long they take. */
export default function RoutingsPage() {
  const [search, setSearch] = useState('');
  const text = useDebounced(search.trim());
  const [chosen, setChosen] = useState<string | null>(null);
  const boms = useQuery({ queryKey: ['boms', text, false], queryFn: () => productionApi.boms({ search: text }) });
  return (
    <div>
      <PageHeader title="Routings" aside={boms.data && `${formatCount(boms.data.length)} bills`} />
      <Toolbar>
        <SearchBox value={search} onChange={setSearch} placeholder="Product code or name" label="Find a bill" />
      </Toolbar>
      <div className="grid gap-4 xl:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
        <DataTable<BomListRow>
          rows={boms.data}
          loading={boms.isLoading}
          error={boms.error}
          rowKey={(b) => b.id}
          caption="Bills"
          selectedKey={chosen}
          onRowClick={(b) => setChosen(b.id)}
          empty={<EmptyState title="No bills of materials yet" />}
          columns={[
            { key: 'code', header: 'Product', cell: (b) => <span className="font-semibold">{b.productCode}</span> },
            { key: 'rev', header: 'Version', numeric: true, cell: (b) => b.revision },
            { key: 'ops', header: 'Operations', numeric: true, cell: (b) => b.operations },
            { key: 'state', header: '', cell: (b) => (b.inForce ? <Chip tone="ok">In force</Chip> : '') },
          ]}
        />
        {chosen ? <Routing key={chosen} bomId={chosen} /> : <div className="card"><EmptyState title="Choose a bill">Its routing shows here.</EmptyState></div>}
      </div>
    </div>
  );
}

function Routing({ bomId }: { bomId: string }) {
  const bom = useQuery({ queryKey: ['bom', bomId], queryFn: () => productionApi.bom(bomId) });
  if (bom.error) return <div className="card"><ErrorState error={bom.error} /></div>;
  if (!bom.data) return <Skeleton className="h-64 rounded-card" />;
  return <RoutingEditor key={bom.data.version} bom={bom.data} />;
}

function RoutingEditor({ bom }: { bom: BomDetail }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { can, canChangeShared } = useSession();
  const editable = can(P.productionPlan) && canChangeShared;
  const [ops, setOps] = useState<OpText[]>(routingText(bom.operations));
  const [problem, setProblem] = useState<string | null>(null);
  const centres = useQuery({ queryKey: ['work-centres', null], queryFn: () => productionApi.workCentres({}), staleTime: 60_000 });
  const numbers = (): RoutingOperation[] =>
    ops.map((o) => ({ ...o, sequence: parseNumber(o.sequence) ?? 0, setupMinutes: parseNumber(o.setupMinutes) ?? 0, runMinutesPerUnit: parseNumber(o.runMinutesPerUnit) ?? 0 }));
  const save = useMutation({
    mutationFn: () => productionApi.saveRouting(bom.id!, bom.version, numbers()),
    onSuccess: (saved) => {
      toast.ok(`The routing of ${saved.productCode} version ${saved.revision} is saved.`);
      queryClient.setQueryData(['bom', saved.id], saved);
      void queryClient.invalidateQueries({ queryKey: ['boms'] });
    },
  });
  const minutes = numbers().reduce((s, o) => s + o.setupMinutes + o.runMinutesPerUnit * bom.yieldQuantity, 0);
  return (
    <section className="card" aria-label={`Routing of ${bom.productCode}`}>
      <div className="flex flex-wrap items-center justify-between gap-3 px-5 pb-2 pt-4">
        <div>
          <h2 className="font-heading text-section">
            {bom.productCode} {bom.productName} · version {bom.revision}
          </h2>
          <p className="text-muted">{formatQuantity(minutes)} minutes for a run of {formatQuantity(bom.yieldQuantity)}</p>
        </div>
        {editable && (
          <div className="flex gap-2">
            <Button
              icon={Plus}
              onClick={() => setOps([...ops, { sequence: String(nextSequence(numbers())), workCentreId: centres.data?.[0]?.id ?? '', workCentreCode: null, name: '', setupMinutes: '0', runMinutesPerUnit: '0', instructions: null }])}
            >
              Add operation
            </Button>
            <Button
              variant="sun"
              icon={Save}
              shortcut="Ctrl+S"
              busy={save.isPending}
              onClick={() => {
                const text = checkRouting(numbers()) ?? null;
                setProblem(text);
                if (!text) save.mutate();
              }}
            >
              Save routing
            </Button>
          </div>
        )}
      </div>
      <div className="grid-scroll">
        {ops.length === 0 ? <EmptyState title="No operations yet" /> : <OperationsTable ops={ops} setOps={setOps} editable={editable} centres={centres.data ?? []} />}
      </div>
      <div className="px-5 pb-4">
        <FormError error={problem ?? save.error} />
      </div>
    </section>
  );
}
