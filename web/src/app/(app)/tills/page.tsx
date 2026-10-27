'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ban, KeyRound, Plus, RefreshCw } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Field, Input, Select } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar, type ChipTone } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { cx } from '@/lib/cx';
import { formatAgo, formatCount, formatMoney, formatTime, formatUtc } from '@/lib/format';
import { useNow } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { EnrollmentCodeInfo, TillInfo, TillStatus } from '@/lib/types';

const STATUS: Record<TillStatus, { label: string; tone: ChipTone; bar: string }> = {
  ok: { label: 'Up to date', tone: 'ok', bar: 'bg-ok' },
  behind: { label: 'Behind', tone: 'warn', bar: 'bg-sun-dark' },
  failed: { label: 'Package failed', tone: 'danger', bar: 'bg-danger' },
  never: { label: 'Never sent', tone: 'plain', bar: 'bg-stone' },
  retired: { label: 'Retired', tone: 'plain', bar: 'bg-stone-light' },
};

const ORDER: Record<TillStatus, number> = { failed: 0, behind: 1, never: 2, ok: 3, retired: 4 };

export default function TillsPage() {
  return (
    <Suspense>
      <Tills />
    </Suspense>
  );
}

function tillName(t: Pick<TillInfo, 'storeCode' | 'number'>) {
  return `${t.storeCode} · ${String(t.number).padStart(3, '0')}`;
}

function Tills() {
  const { can } = useSession();
  const { storeId, currencySymbol } = useStore();
  const params = useSearchParams();
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const now = useNow(10_000);
  const [enrolling, setEnrolling] = useState(false);
  const [code, setCode] = useState<EnrollmentCodeInfo | null>(null);
  const [retiring, setRetiring] = useState<TillInfo | null>(null);
  const [showRetired, setShowRetired] = useState(false);
  const manage = can(P.storesManage);

  const query = useQuery({
    queryKey: ['tills', storeId],
    queryFn: () => api.tills({ storeId }),
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
  });

  useEffect(() => {
    if (params.get('enrol') === '1' && manage) {
      setEnrolling(true);
      router.replace('/tills/');
    }
  }, [params, manage, router]);

  const newCode = useMutation({
    mutationFn: (stationId: string) => api.enrollmentCode(stationId),
    onSuccess: (info) => {
      setCode(info);
      void queryClient.invalidateQueries({ queryKey: ['tills'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const retire = useMutation({
    mutationFn: (stationId: string) => api.retireStation(stationId),
    onSuccess: () => {
      toast.ok(`${retiring ? tillName(retiring) : 'The till'} is retired. Its key stops working; its sales stay.`);
      setRetiring(null);
      void queryClient.invalidateQueries({ queryKey: ['tills'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const all = query.data ?? [];
  const tills = [...all].filter((t) => showRetired || t.status !== 'retired').sort((a, b) => ORDER[a.status] - ORDER[b.status] || a.storeCode.localeCompare(b.storeCode) || a.number - b.number);
  const selling = all.filter((t) => t.todayTransactions > 0).length;
  const attention = all.filter((t) => t.status === 'failed' || t.status === 'behind').length;
  const retiredCount = all.filter((t) => t.status === 'retired').length;

  return (
    <div>
      <PageHeader
        title="Tills"
        aside={
          query.data && (
            <span>
              {formatCount(all.length - retiredCount)} tills · {formatCount(selling)} sold today
              {attention > 0 && ` · ${formatCount(attention)} need a look`}
            </span>
          )
        }
      />
      <Toolbar>
        {manage && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEnrolling(true)}>
            Enrol a till
          </Button>
        )}
        <Button icon={RefreshCw} shortcut="F5" onClick={() => void query.refetch()} busy={query.isFetching && !query.isLoading}>
          Refresh
        </Button>
        {retiredCount > 0 && (
          <label className="flex cursor-pointer items-center gap-2.5 px-2">
            <input type="checkbox" className="check" checked={showRetired} onChange={(e) => setShowRetired(e.target.checked)} />
            Show retired tills
          </label>
        )}
        <span className="ml-auto text-ink max-sm:w-full" aria-live="polite">
          {query.dataUpdatedAt > 0 && now ? `Updated ${formatAgo(new Date(query.dataUpdatedAt).toISOString(), now)}; every 30 seconds` : ''}
        </span>
      </Toolbar>

      {query.error && !query.data ? (
        <div className="card">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      ) : query.isLoading ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 2xl:grid-cols-3">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-[248px] rounded-card" />
          ))}
        </div>
      ) : tills.length === 0 ? (
        <div className="card">
          <EmptyState title="No tills yet">
            {manage ? 'Enrol a till: you get a code to type at the till, and it joins this server.' : 'Ask an administrator to enrol the tills of this store.'}
          </EmptyState>
        </div>
      ) : (
        <ul className="grid grid-cols-1 gap-4 md:grid-cols-2 2xl:grid-cols-3">
          {tills.map((t) => (
            <TillCard
              key={t.stationId}
              till={t}
              now={now}
              symbol={currencySymbol}
              manage={manage}
              onCode={() => newCode.mutate(t.stationId)}
              codeBusy={newCode.isPending && newCode.variables === t.stationId}
              onRetire={() => setRetiring(t)}
            />
          ))}
        </ul>
      )}

      {enrolling && (
        <EnrolDialog
          onClose={() => setEnrolling(false)}
          onDone={(info) => {
            setEnrolling(false);
            setCode(info);
            void queryClient.invalidateQueries({ queryKey: ['tills'] });
          }}
        />
      )}
      <CodeDialog info={code} onClose={() => setCode(null)} />
      <ConfirmDialog
        open={!!retiring}
        title={`Retire ${retiring ? tillName(retiring) : ''}?`}
        message="Its key stops working at once, so it can no longer send or receive data. Its sales stay. A retired till cannot come back; enrol a new one instead."
        confirmLabel="Retire"
        cancelLabel="Keep it"
        danger
        busy={retire.isPending}
        onConfirm={() => retiring && retire.mutate(retiring.stationId)}
        onClose={() => setRetiring(null)}
      />
    </div>
  );
}

function TillCard({
  till: t,
  now,
  symbol,
  manage,
  onCode,
  codeBusy,
  onRetire,
}: {
  till: TillInfo;
  now: Date | null;
  symbol: string;
  manage: boolean;
  onCode(): void;
  codeBusy: boolean;
  onRetire(): void;
}) {
  const status = STATUS[t.status] ?? STATUS.never;
  return (
    <li className={cx('relative flex flex-col overflow-hidden rounded-card bg-card shadow-card', t.status === 'retired' && 'opacity-70')} data-testid="till">
      <span className={cx('absolute inset-y-0 left-0 w-2', status.bar)} aria-hidden="true" />
      <div className="flex items-start justify-between gap-3 py-4 pl-7 pr-5">
        <div className="min-w-0">
          <h2 className="font-heading text-[26px] leading-8">{tillName(t)}</h2>
          <p className="truncate text-muted">
            {t.name ?? 'Till'} · {t.storeName}
          </p>
        </div>
        <Chip tone={status.tone}>
          {status.label}
          {t.status === 'failed' && t.failedBatches > 1 ? ` (${t.failedBatches})` : ''}
        </Chip>
      </div>
      <dl className="grid grid-cols-2 gap-x-4 gap-y-3 pb-4 pl-7 pr-5">
        <div>
          <dt className="text-note text-muted">Today</dt>
          <dd className="font-heading text-value tabular-nums">{formatMoney(t.todayNetSales, symbol)}</dd>
          <dd className="text-note text-muted">
            {formatCount(t.todayTransactions)} sales{t.lastSaleAt ? ` · last at ${formatTime(t.lastSaleAt)}` : ''}
          </dd>
        </div>
        <div>
          <dt className="text-note text-muted">Last upload</dt>
          <dd className="font-semibold" title={t.lastUploadAtUtc ? formatUtc(t.lastUploadAtUtc) : undefined}>
            {now ? formatAgo(t.lastUploadAtUtc, now) : '…'}
          </dd>
          <dd className="text-note text-muted">Download {now ? formatAgo(t.lastDownloadAtUtc, now).toLowerCase() : '…'}</dd>
        </div>
        <div>
          <dt className="text-note text-muted">Drawer</dt>
          <dd className="flex items-center gap-2 font-semibold">
            <span className={cx('h-2.5 w-2.5 rounded-full', t.drawerOpen ? 'bg-sun-dark' : 'bg-stone')} aria-hidden="true" />
            {t.drawerOpen ? 'Open' : 'Closed'}
          </dd>
        </div>
        <div>
          <dt className="text-note text-muted">Version</dt>
          <dd className="font-semibold">{t.appVersion ?? '—'}</dd>
        </div>
      </dl>
      {manage && !t.retired && (
        <div className="mt-auto flex flex-wrap gap-2 border-t border-line px-5 py-3 pl-7">
          {!t.enrolled && (
            <Button icon={KeyRound} onClick={onCode} busy={codeBusy}>
              New code
            </Button>
          )}
          <Button icon={Ban} variant="danger" onClick={onRetire}>
            Retire
          </Button>
        </div>
      )}
    </li>
  );
}

function EnrolDialog({ onClose, onDone }: { onClose(): void; onDone(info: EnrollmentCodeInfo): void }) {
  const { stores, defaultStoreId } = useStore();
  const [storeId, setStoreId] = useState(defaultStoreId ?? '');
  const [number, setNumber] = useState('');
  const [name, setName] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const add = useMutation({
    mutationFn: () => api.addStation(storeId, { number: number ? Number(number) : null, name: name.trim() || null }),
    onSuccess: onDone,
  });

  return (
    <Dialog
      open
      title="Enrol a till"
      onClose={onClose}
      onSubmit={() => {
        if (!storeId) return setProblem('Choose the store.');
        if (number && !/^\d{1,3}$/.test(number)) return setProblem('A station number is 1 to 999.');
        setProblem(null);
        add.mutate();
      }}
      footer={
        <>
          <Button type="submit" variant="sun" icon={KeyRound} shortcut="Enter" busy={add.isPending}>
            Get a code
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <p className="mb-4 text-ink">The server makes a one-time code. Type it at the new till when it asks to join the server. The code works for 24 hours.</p>
      <div className="space-y-3">
        <Field label="Store" htmlFor="enrol-store">
          <Select id="enrol-store" value={storeId} onChange={(e) => setStoreId(e.target.value)}>
            {stores.map((s) => (
              <option key={s.id} value={s.id}>
                {s.code} · {s.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Station number" htmlFor="enrol-number" hint="Leave it empty for the next free number. Numbers are unique across the company.">
          <Input id="enrol-number" inputMode="numeric" value={number} onChange={(e) => setNumber(e.target.value.replace(/\D/g, '').slice(0, 3))} className="max-w-[140px]" />
        </Field>
        <Field label="Name" htmlFor="enrol-name" hint="For example Front counter.">
          <Input id="enrol-name" value={name} maxLength={50} onChange={(e) => setName(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? add.error} />
    </Dialog>
  );
}

function CodeDialog({ info, onClose }: { info: EnrollmentCodeInfo | null; onClose(): void }) {
  const toast = useToast();
  if (!info) return null;
  return (
    <Dialog
      open
      title={`Code for station ${String(info.station.number).padStart(3, '0')}`}
      onClose={onClose}
      footer={
        <>
          <Button
            onClick={() => {
              void navigator.clipboard?.writeText(info.code).then(
                () => toast.ok('The code is copied.'),
                () => toast.error('The browser did not let the code be copied.'),
              );
            }}
          >
            Copy
          </Button>
          <Button variant="sun" onClick={onClose} shortcut="Esc" data-autofocus>
            Done
          </Button>
        </>
      }
    >
      <p className="text-ink">Type this code at the till when it asks to join the server:</p>
      <p className="my-5 select-all rounded-card bg-card px-4 py-6 text-center font-mono text-[44px] font-bold leading-none tracking-[0.12em] text-ink shadow-card sm:text-[56px]" data-testid="enrolment-code">
        {info.code}
      </p>
      <p className="text-ink">
        It works once, until <strong>{formatUtc(info.expiresAtUtc)}</strong>. If it runs out, choose New code on the till&apos;s card.
      </p>
    </Dialog>
  );
}
