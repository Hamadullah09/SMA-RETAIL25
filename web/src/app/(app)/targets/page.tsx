'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ChevronLeft, ChevronRight, Pencil, Plus, Save, Trash2 } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { StoreSelect } from '@/components/crm';
import { ConflictDialog } from '@/components/masters';
import { TargetBar } from '@/components/target-progress';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Field, Input, Select } from '@/components/ui/field';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { formatMoney, formatPercent } from '@/lib/format';
import { checkNumber, numberText, parseNumber } from '@/lib/forms';
import { staffName, useStaff } from '@/lib/staff';
import { useStore } from '@/lib/store';
import { expectedPercent, monthLabel, shiftMonth } from '@/lib/targets';
import type { SalesTargetRow } from '@/lib/types';

export default function TargetsPage() {
  return (
    <Suspense>
      <Targets />
    </Suspense>
  );
}

/** Where sales should be by now for a month: all of it for a month gone by, none for one to come. */
function expectedFor(year: number, month: number, now = new Date()): number {
  const here = now.getFullYear() * 12 + now.getMonth();
  const that = year * 12 + month - 1;
  if (that < here) return 100;
  if (that > here) return 0;
  return expectedPercent(now.getDate(), new Date(year, month, 0).getDate());
}

function Targets() {
  const params = useSearchParams();
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { storeId, currencySymbol: symbol } = useStore();
  const now = new Date();
  const [{ year, month }, setMonth] = useState({ year: now.getFullYear(), month: now.getMonth() + 1 });
  const [selected, setSelected] = useState<SalesTargetRow | null>(null);
  const [editing, setEditing] = useState<SalesTargetRow | 'new' | null>(null);
  const [deleting, setDeleting] = useState<SalesTargetRow | null>(null);
  useEffect(() => {
    if (params.get('new') === '1') {
      setEditing('new');
      router.replace('/targets/');
    }
  }, [params, router]);
  const query = useQuery({ queryKey: ['targets', { storeId, year, month }], queryFn: () => api.targets({ storeId, year, month }) });
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteTarget(id),
    onSuccess: () => {
      toast.ok('The target is deleted.');
      setDeleting(null);
      setSelected(null);
      void queryClient.invalidateQueries({ queryKey: ['targets'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  const expected = expectedFor(year, month);
  const money = (n: number) => formatMoney(n, symbol);
  const go = (by: number) => {
    setMonth(shiftMonth(year, month, by));
    setSelected(null);
  };

  return (
    <div>
      <PageHeader title="Targets" aside={`Net sales against each month's target · ${monthLabel(year, month)}`} />
      <Toolbar>
        <div className="flex items-center gap-2" role="group" aria-label="Month">
          <button type="button" className="btn btn-icon h-12 w-12" aria-label="Previous month" onClick={() => go(-1)}>
            <ChevronLeft className="h-5 w-5" />
          </button>
          <span className="min-w-[160px] text-center font-heading text-section" data-testid="target-month">
            {monthLabel(year, month)}
          </span>
          <button type="button" className="btn btn-icon h-12 w-12" aria-label="Next month" onClick={() => go(1)}>
            <ChevronRight className="h-5 w-5" />
          </button>
        </div>
        <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
          New target
        </Button>
        <Button icon={Pencil} shortcut="Ctrl+O" disabled={!selected} onClick={() => selected && setEditing(selected)}>
          Edit
        </Button>
        <Button icon={Trash2} shortcut="Del" disabled={!selected} onClick={() => selected && setDeleting(selected)}>
          Delete
        </Button>
      </Toolbar>
      <DataTable<SalesTargetRow>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(t) => t.id}
        selectedKey={selected?.id}
        onRowClick={(t) => (selected?.id === t.id ? setEditing(t) : setSelected(t))}
        caption="Sales targets"
        empty={<EmptyState title={`No targets for ${monthLabel(year, month)}`}>New target sets one for a store or a salesperson.</EmptyState>}
        columns={[
          { key: 'store', header: 'Store', cell: (t) => t.storeCode },
          { key: 'who', header: 'For', cell: (t) => (t.userId ? t.userName ?? 'A salesperson' : <span className="font-semibold">The whole store</span>) },
          { key: 'target', header: 'Target', numeric: true, cell: (t) => money(t.amount) },
          { key: 'actual', header: 'Net sales', numeric: true, cell: (t) => money(t.actual), hideBelow: 'sm' },
          {
            key: 'bar',
            header: 'Reached',
            className: 'w-[34%]',
            cell: (t) => <TargetBar percent={t.percent} expected={expected} label={`${t.userName ?? t.storeCode} reached`} />,
          },
        ]}
      />
      <p className="mt-2 px-1 text-ink">
        {expected > 0 && expected < 100 ? `By today the month is ${formatPercent(expected)} gone: the line on each bar. ` : ''}A salesperson&apos;s sales are counted at the
        store of the target.
      </p>
      {editing && <TargetDialog target={editing === 'new' ? null : editing} year={year} month={month} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title="Delete this target?"
        message={deleting ? `${deleting.userName ?? 'The whole store'} at ${deleting.storeCode}, ${monthLabel(deleting.year, deleting.month)}: ${money(deleting.amount)}.` : undefined}
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </div>
  );
}

function TargetDialog({ target, year, month, onClose }: { target: SalesTargetRow | null; year: number; month: number; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { defaultStoreId, stores } = useStore();
  const [storeId, setStoreId] = useState(target?.storeId ?? defaultStoreId ?? '');
  const [userId, setUserId] = useState(target?.userId ?? '');
  const [amount, setAmount] = useState(numberText(target?.amount ?? 0));
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const staff = useStaff(storeId || null);
  const save = useMutation({
    mutationFn: () => {
      const value = parseNumber(amount) ?? 0;
      return target ? api.saveTarget(target.id, { amount: value, version: target.version }) : api.addTarget({ storeId, userId: userId || null, year, month, amount: value });
    },
    onSuccess: () => {
      toast.ok('The target is saved.');
      void queryClient.invalidateQueries({ queryKey: ['targets'] });
      void queryClient.invalidateQueries({ queryKey: ['target-progress'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });

  return (
    <Dialog
      open
      title={target ? `Target for ${monthLabel(target.year, target.month)}` : `New target for ${monthLabel(year, month)}`}
      onClose={onClose}
      onSubmit={() => {
        const found = !storeId ? 'Choose the store.' : checkNumber(amount, { required: true, label: 'The target' }) ?? ((parseNumber(amount) ?? 0) <= 0 ? 'The target is more than 0.' : null);
        setProblem(found);
        if (!found) save.mutate();
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
      <div className="space-y-3">
        {stores.length > 1 && (
          <Field label="Store" htmlFor="target-store">
            <StoreSelect id="target-store" value={storeId} onChange={setStoreId} disabled={!!target} />
          </Field>
        )}
        <Field label="For" htmlFor="target-user">
          <Select id="target-user" value={userId} disabled={!!target} onChange={(e) => setUserId(e.target.value)}>
            <option value="">The whole store</option>
            {target?.userId && <option value={target.userId}>{target.userName}</option>}
            {(staff.data ?? [])
              .filter((s) => s.id !== target?.userId)
              .map((s) => (
                <option key={s.id} value={s.id}>
                  {staffName(s)}
                </option>
              ))}
          </Select>
        </Field>
        <Field label="Net sales target" htmlFor="target-amount">
          <Input id="target-amount" inputMode="decimal" className="max-w-[200px]" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
      <ConflictDialog
        open={conflict}
        what="target"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['targets'] });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}
