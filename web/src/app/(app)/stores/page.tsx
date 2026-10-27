'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Save } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { Button, LinkButton } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { Field, Input } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { formatAgo } from '@/lib/format';
import { useNow } from '@/lib/hooks';
import { useSession } from '@/lib/session';
import type { StationInfo, StoreInfo } from '@/lib/types';

/** Time zones as Windows names them, which is what the server checks on Windows. */
const ZONES = [
  'Eastern Standard Time',
  'Central Standard Time',
  'Mountain Standard Time',
  'Pacific Standard Time',
  'Atlantic Standard Time',
  'Newfoundland Standard Time',
  'GMT Standard Time',
  'Greenwich Standard Time',
  'W. Europe Standard Time',
  'Romance Standard Time',
  'W. Central Africa Standard Time',
  'South Africa Standard Time',
  'E. Africa Standard Time',
  'Arabian Standard Time',
  'India Standard Time',
  'China Standard Time',
  'Tokyo Standard Time',
  'AUS Eastern Standard Time',
  'UTC',
];

export default function StoresPage() {
  return (
    <Suspense>
      <Stores />
    </Suspense>
  );
}

function Stores() {
  const { allStores } = useSession();
  const params = useSearchParams();
  const router = useRouter();
  const [editing, setEditing] = useState<StoreInfo | 'new' | null>(null);
  const stores = useQuery({ queryKey: ['stores'], queryFn: () => api.stores() });

  useEffect(() => {
    if (params.get('new') === '1' && allStores) {
      setEditing('new');
      router.replace('/stores/');
    }
  }, [params, allStores, router]);

  return (
    <div>
      <PageHeader title="Stores" aside={stores.data && `${stores.data.length} stores`} />
      <Toolbar>
        {allStores && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            Add store
          </Button>
        )}
        <LinkButton href="/tills/?enrol=1" icon={Plus}>
          Enrol a till
        </LinkButton>
        {!allStores && <span className="text-ink">Only a super administrator adds stores.</span>}
      </Toolbar>
      {stores.error ? (
        <div className="card">
          <ErrorState error={stores.error} onRetry={() => void stores.refetch()} />
        </div>
      ) : !stores.data ? (
        <div className="space-y-4">
          <Skeleton className="h-48 rounded-card" />
          <Skeleton className="h-48 rounded-card" />
        </div>
      ) : stores.data.length === 0 ? (
        <div className="card">
          <EmptyState title="No stores yet" />
        </div>
      ) : (
        <div className="space-y-4">
          {stores.data.map((s) => (
            <StoreCard key={s.id} store={s} onEdit={() => setEditing(s)} />
          ))}
        </div>
      )}
      {editing && <StoreDialog store={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function StoreCard({ store, onEdit }: { store: StoreInfo; onEdit(): void }) {
  const now = useNow(60_000);
  const stations = useQuery({ queryKey: ['stations', store.id], queryFn: () => api.stations(store.id) });
  return (
    <section className="rounded-card bg-card shadow-card" aria-label={store.name}>
      <div className="flex flex-wrap items-center justify-between gap-3 px-5 pb-2 pt-4">
        <div>
          <h2 className="font-heading text-[26px] leading-8">
            {store.code} · {store.name}
          </h2>
          <p className="text-muted">{store.timeZoneId ?? 'The server’s time zone'}</p>
        </div>
        <Button icon={Pencil} onClick={onEdit}>
          Change
        </Button>
      </div>
      <DataTable<StationInfo>
        className="rounded-t-none shadow-none"
        rows={stations.data}
        loading={stations.isLoading}
        error={stations.error}
        onRetry={() => void stations.refetch()}
        rowKey={(t) => t.id}
        caption={`Stations of ${store.name}`}
        empty={<p className="px-5 pb-5 text-muted">No stations yet. Enrol a till to add one.</p>}
        columns={[
          { key: 'n', header: 'Station', cell: (t) => <span className="font-semibold">{String(t.number).padStart(3, '0')}</span> },
          { key: 'name', header: 'Name', cell: (t) => t.name ?? '' },
          {
            key: 'state',
            header: 'State',
            cell: (t) =>
              t.retired ? (
                <Chip>Retired</Chip>
              ) : t.enrolled ? (
                t.failedBatches > 0 ? <Chip tone="danger">Package failed</Chip> : t.behind ? <Chip tone="warn">Behind</Chip> : <Chip tone="ok">Joined</Chip>
              ) : (
                <Chip tone="warn">Waiting to join</Chip>
              ),
          },
          { key: 'up', header: 'Last upload', cell: (t) => (now ? formatAgo(t.lastUploadAtUtc, now) : ''), hideBelow: 'sm' },
          { key: 'ver', header: 'Version', cell: (t) => t.appVersion ?? '', hideBelow: 'md' },
        ]}
      />
    </section>
  );
}

function StoreDialog({ store, onClose }: { store: StoreInfo | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [code, setCode] = useState(store?.code ?? '');
  const [name, setName] = useState(store?.name ?? '');
  const [zone, setZone] = useState(store?.timeZoneId ?? '');
  const [problem, setProblem] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () => {
      const body = { code: code.trim().toUpperCase(), name: name.trim(), timeZoneId: zone.trim() || null };
      return store ? api.saveStore(store.id, body) : api.addStore(body);
    },
    onSuccess: (saved) => {
      toast.ok(`${saved?.name ?? name} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['stores'] });
      onClose();
    },
  });

  return (
    <Dialog
      open
      title={store ? `Change ${store.name}` : 'Add store'}
      onClose={onClose}
      onSubmit={() => {
        const text = !code.trim() || code.trim().length > 10 ? 'A store code has 1 to 10 characters.' : !name.trim() || name.trim().length > 50 ? 'A store name has 1 to 50 characters.' : null;
        setProblem(text);
        if (!text) save.mutate();
      }}
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
      {!store && <p className="mb-4 text-ink">A new store starts with its settings and a POS location. Then enrol its tills.</p>}
      <div className="space-y-3">
        <Field label="Code" htmlFor="store-code" hint="Short, on reports and receipts: MAIN, WEST.">
          <Input id="store-code" value={code} maxLength={10} className="max-w-[200px] uppercase" onChange={(e) => setCode(e.target.value)} />
        </Field>
        <Field label="Name" htmlFor="store-name">
          <Input id="store-name" value={name} maxLength={50} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="Time zone" htmlFor="store-zone" hint="Its business day: when today starts and ends. Empty for the server's.">
          <Input id="store-zone" list="zones" value={zone} onChange={(e) => setZone(e.target.value)} placeholder="Eastern Standard Time" />
          <datalist id="zones">
            {ZONES.map((z) => (
              <option key={z} value={z} />
            ))}
          </datalist>
        </Field>
      </div>
      <FormError error={problem ?? save.error} />
    </Dialog>
  );
}
