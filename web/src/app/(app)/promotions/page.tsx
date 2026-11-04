'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Save, Trash2, X } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useMemo, useState } from 'react';
import { ConflictDialog } from '@/components/masters';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { DateRangeBar, useDateRange } from '@/components/ui/date-range';
import { Checkbox, Field, Input, Select, TextArea } from '@/components/ui/field';
import { Chip, PageHeader, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { cx } from '@/lib/cx';
import { formatCount, formatDate, formatMoney, formatQuantity } from '@/lib/format';
import { numberText, parseNumber } from '@/lib/forms';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { blankPromotion, checkPromotion, describePromotion, kindLabel, PROMOTION_KINDS, SCOPES, WEEK_DAYS } from '@/lib/promotions';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { PromotionDetail, PromotionKind, PromotionReportRow, PromotionRequest, PromotionRow, PromotionScope, PromotionTarget } from '@/lib/types';

type Tab = 'list' | 'report';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'list', label: 'Promotions' },
  { key: 'report', label: 'Report' },
];

export default function PromotionsPage() {
  return (
    <Suspense>
      <Promotions />
    </Suspense>
  );
}

/**
 * A promotion for one's own store alone may be changed by that store's users; one for every store or several needs
 * the right to change shared records.
 */
function useMayChange() {
  const { can, canChangeShared, user } = useSession();
  return (p: Pick<PromotionRow, 'allStores' | 'storeIds'> | null) =>
    can(P.promotions) && (canChangeShared || (p ? !p.allStores && p.storeIds.length === 1 && p.storeIds[0] === user.storeId : !!user.storeId));
}

function Promotions() {
  const params = useSearchParams();
  const router = useRouter();
  const mayChange = useMayChange();
  const [tab, setTab] = useState<Tab>('list');
  const [editing, setEditing] = useState<string | 'new' | null>(null);
  useEffect(() => {
    if (params.get('new') === '1') {
      if (mayChange(null)) setEditing('new');
      setTab('list');
      router.replace('/promotions/');
    }
  }, [params, router, mayChange]);
  return (
    <div>
      <PageHeader title="Promotions" />
      <Tabs label="Promotions" tabs={TABS} value={tab} onChange={setTab} className="mb-4" />
      {tab === 'list' ? <PromotionList onEdit={setEditing} /> : <Report />}
      {editing && <PromotionDialog id={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function PromotionList({ onEdit }: { onEdit(id: string | 'new'): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const mayChange = useMayChange();
  const { stores } = useStore();
  const [text, setText] = useState('');
  const [onlyOn, setOnlyOn] = useState(false);
  const [selected, setSelected] = useState<PromotionRow | null>(null);
  const [deleting, setDeleting] = useState<PromotionRow | null>(null);
  const search = useDebounced(text.trim());
  const query = useQuery({
    queryKey: ['promotions', { search, onlyOn }],
    queryFn: () => api.promotions({ search, active: onlyOn || undefined }),
    placeholderData: keepPreviousData,
  });
  const remove = useMutation({
    mutationFn: (id: string) => api.deletePromotion(id),
    onSuccess: () => {
      toast.ok(`${deleting?.name ?? 'The promotion'} is deleted.`);
      setDeleting(null);
      setSelected(null);
      void queryClient.invalidateQueries({ queryKey: ['promotions'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  const storeCodes = (p: PromotionRow) => (p.allStores ? 'Every store' : p.storeIds.map((id) => stores.find((s) => s.id === id)?.code ?? '?').join(', '));

  return (
    <>
      <Toolbar>
        <SearchBox value={text} onChange={setText} placeholder="Name or coupon code" />
        <label className="flex cursor-pointer items-center gap-2.5 px-1">
          <input type="checkbox" className="check" checked={onlyOn} onChange={(e) => setOnlyOn(e.target.checked)} />
          Switched on only
        </label>
      </Toolbar>
      <Toolbar>
        {mayChange(null) && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => onEdit('new')}>
            New promotion
          </Button>
        )}
        <Button icon={Pencil} shortcut="Ctrl+O" disabled={!selected} onClick={() => selected && onEdit(selected.id)}>
          {selected && mayChange(selected) ? 'Edit' : 'Open'}
        </Button>
        {selected && mayChange(selected) && (
          <Button icon={Trash2} shortcut="Del" onClick={() => setDeleting(selected)}>
            Delete
          </Button>
        )}
      </Toolbar>
      <DataTable<PromotionRow>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(p) => p.id}
        selectedKey={selected?.id}
        onRowClick={(p) => (selected?.id === p.id ? onEdit(p.id) : setSelected(p))}
        caption="Promotions"
        empty={<EmptyState title="No promotions">{search ? 'Nothing matches the search.' : 'A promotion takes money off at the till by itself, or with a coupon.'}</EmptyState>}
        columns={[
          { key: 'name', header: 'Name', cell: (p) => <span className="font-semibold">{p.name}</span> },
          { key: 'kind', header: 'Kind', cell: (p) => kindLabel(p.kind), hideBelow: 'md' },
          { key: 'summary', header: 'What it does', cell: (p) => p.summary, className: 'wrap', hideBelow: 'lg' },
          { key: 'dates', header: 'Dates', cell: (p) => (p.startsOn || p.endsOn ? `${formatDate(p.startsOn) || '…'} – ${formatDate(p.endsOn) || '…'}` : 'Always'), hideBelow: 'xl' },
          { key: 'stores', header: 'Stores', cell: (p) => storeCodes(p), hideBelow: 'xl' },
          {
            key: 'coupon',
            header: 'Coupon',
            cell: (p) => (p.couponCode ? `${p.couponCode} · ${formatCount(p.couponUses)}${p.couponMaxUses > 0 ? ` of ${formatCount(p.couponMaxUses)}` : ''} used` : ''),
            hideBelow: 'md',
          },
          { key: 'live', header: 'Now', cell: (p) => (p.live ? <Chip tone="ok">Live</Chip> : p.isActive ? <Chip tone="warn">Not today</Chip> : <Chip>Off</Chip>) },
        ]}
      />
      <p className="mt-2 px-1 text-ink">Promotions reach the tills with their next download. The till picks the best price for the customer.</p>
      <ConfirmDialog
        open={!!deleting}
        title={`Delete the promotion ${deleting?.name ?? ''}?`}
        message="A promotion that was used at a till is not deleted: switch it off instead."
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  );
}

// Editor ------------------------------------------------------------------------------------------------------------

type TargetType = 'department' | 'category' | 'item';
const TYPE_OF_SCOPE: Record<PromotionScope, TargetType | null> = { AllItems: null, Departments: 'department', Categories: 'category', Items: 'item' };

function typeOf(t: PromotionTarget): TargetType {
  return t.departmentId ? 'department' : t.categoryId ? 'category' : 'item';
}

function useLists() {
  const departments = useQuery({ queryKey: ['departments'], queryFn: () => api.departments(), staleTime: 5 * 60_000 });
  const categories = useQuery({ queryKey: ['categories', ''], queryFn: () => api.categories(null), staleTime: 5 * 60_000 });
  return { departments: departments.data ?? [], categories: categories.data ?? [] };
}

/** Lines naming a department, a category or a stock code; with a quantity for a bundle's parts. */
function TargetList({
  label,
  targets,
  onChange,
  fixed,
  withQuantity,
  readOnly,
}: {
  label: string;
  targets: PromotionTarget[];
  onChange(targets: PromotionTarget[]): void;
  /** Every line is of this type (the scope says so); otherwise each line chooses. */
  fixed?: TargetType;
  withQuantity?: boolean;
  readOnly?: boolean;
}) {
  const { departments, categories } = useLists();
  const set = (i: number, t: PromotionTarget) => onChange(targets.map((x, j) => (j === i ? t : x)));
  const blank = (type: TargetType): PromotionTarget => ({ departmentId: null, categoryId: null, plu: type === 'item' ? '' : null, quantity: 1, name: null });
  const first = (type: TargetType): PromotionTarget =>
    type === 'department' ? { ...blank(type), departmentId: departments[0]?.id ?? null } : type === 'category' ? { ...blank(type), categoryId: categories[0]?.id ?? null } : blank(type);

  return (
    <div className="space-y-2" role="group" aria-label={label}>
      {targets.map((t, i) => {
        const type = fixed ?? typeOf(t);
        return (
          <div key={i} className="flex flex-wrap items-center gap-2">
            {!fixed && (
              <Select aria-label={`${label} line ${i + 1} kind`} className="w-[150px]" value={type} disabled={readOnly} onChange={(e) => set(i, first(e.target.value as TargetType))}>
                <option value="item">Stock code</option>
                <option value="department">Department</option>
                <option value="category">Category</option>
              </Select>
            )}
            {type === 'department' && (
              <Select aria-label={`${label} line ${i + 1}`} className="min-w-[180px] flex-1" value={t.departmentId ?? ''} disabled={readOnly} onChange={(e) => set(i, { ...t, departmentId: e.target.value || null, name: null })}>
                <option value="">Choose…</option>
                {departments.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </Select>
            )}
            {type === 'category' && (
              <Select aria-label={`${label} line ${i + 1}`} className="min-w-[180px] flex-1" value={t.categoryId ?? ''} disabled={readOnly} onChange={(e) => set(i, { ...t, categoryId: e.target.value || null, name: null })}>
                <option value="">Choose…</option>
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            )}
            {type === 'item' && (
              <Input
                aria-label={`${label} line ${i + 1}`}
                className="min-w-[160px] flex-1 uppercase"
                placeholder="Stock code"
                value={t.plu ?? ''}
                readOnly={readOnly}
                onChange={(e) => set(i, { ...t, plu: e.target.value.toUpperCase(), name: null })}
              />
            )}
            {withQuantity && (
              <Input
                aria-label={`${label} line ${i + 1} quantity`}
                inputMode="numeric"
                className="w-[90px]"
                value={numberText(t.quantity)}
                readOnly={readOnly}
                onChange={(e) => set(i, { ...t, quantity: parseNumber(e.target.value) ?? 0 })}
              />
            )}
            {!readOnly && (
              <button type="button" className="btn btn-icon h-11 min-h-0 w-11" aria-label={`Remove ${label.toLowerCase()} line ${i + 1}`} onClick={() => onChange(targets.filter((_, j) => j !== i))}>
                <X className="h-5 w-5" />
              </button>
            )}
          </div>
        );
      })}
      {!readOnly && (
        <Button icon={Plus} onClick={() => onChange([...targets, first(fixed ?? 'item')])}>
          Add a line
        </Button>
      )}
    </div>
  );
}

function toRequest(p: PromotionDetail): PromotionRequest {
  // The read-only fields stay with the server.
  const { couponUses: _uses, summary: _summary, live: _live, ...rest } = p;
  void _uses;
  void _summary;
  void _live;
  return rest;
}

function PromotionDialog({ id, onClose }: { id: string | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const mayChange = useMayChange();
  const { canChangeShared, user } = useSession();
  const { stores, currencySymbol: symbol } = useStore();
  const lists = useLists();
  const detail = useQuery({ queryKey: ['promotion', id], queryFn: () => api.promotion(id!), enabled: !!id, staleTime: 0 });
  const [form, setForm] = useState<PromotionRequest | null>(() => {
    if (id) return null;
    const blank = blankPromotion();
    return canChangeShared || !user.storeId ? blank : { ...blank, allStores: false, storeIds: [user.storeId] };
  });
  const [texts, setTexts] = useState<Record<string, string>>({});
  const [problems, setProblems] = useState<string[]>([]);
  const [conflict, setConflict] = useState(false);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    if (detail.data && !form) setForm(toRequest(detail.data));
  }, [detail.data, form]);

  const readOnly = !!form && !!id && !mayChange(detail.data ?? null);
  const names = useMemo(
    () => ({
      department: (x: string) => lists.departments.find((d) => d.id === x)?.name,
      category: (x: string) => lists.categories.find((c) => c.id === x)?.name,
      stores: (ids: string[]) => ids.map((x) => stores.find((s) => s.id === x)?.code ?? '?').join(', '),
      symbol,
    }),
    [lists.departments, lists.categories, stores, symbol],
  );

  const save = useMutation({
    mutationFn: (body: PromotionRequest) => (id ? api.savePromotion(id, body) : api.addPromotion(body)),
    onSuccess: (saved) => {
      toast.ok(`${saved.name} is saved. The tills get it with their next download.`);
      void queryClient.invalidateQueries({ queryKey: ['promotions'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  const remove = useMutation({
    mutationFn: () => api.deletePromotion(id!),
    onSuccess: () => {
      toast.ok(`${form?.name ?? 'The promotion'} is deleted.`);
      void queryClient.invalidateQueries({ queryKey: ['promotions'] });
      onClose();
    },
    onError: (e: Error) => toast.error(e.message),
  });

  if (detail.error) {
    return (
      <Dialog open title="Promotion" onClose={onClose}>
        <ErrorState error={detail.error} onRetry={() => void detail.refetch()} />
      </Dialog>
    );
  }
  if (!form) {
    return (
      <Dialog open title="Promotion" onClose={onClose}>
        <Skeleton className="h-60" />
      </Dialog>
    );
  }

  const p = form;
  const set = (patch: Partial<PromotionRequest>) => setForm((f) => ({ ...f!, ...patch }));
  /** A number box keeps what is typed; the promotion gets the number. */
  const num = (key: 'percent' | 'amount' | 'buyQuantity' | 'getQuantity' | 'getPercent' | 'bundlePrice' | 'spendThreshold' | 'priority' | 'couponMaxUses', label: string, className = 'max-w-[160px]') => (
    <Field label={label} htmlFor={`promo-${key}`}>
      <Input
        id={`promo-${key}`}
        inputMode="decimal"
        className={className}
        readOnly={readOnly}
        value={texts[key] ?? numberText(p[key])}
        onChange={(e) => {
          setTexts((t) => ({ ...t, [key]: e.target.value }));
          set({ [key]: parseNumber(e.target.value) ?? 0 } as Partial<PromotionRequest>);
        }}
      />
    </Field>
  );
  const fixedType = TYPE_OF_SCOPE[p.scope];

  function submit() {
    if (readOnly) return onClose();
    const body: PromotionRequest = {
      ...p,
      name: p.name.trim(),
      couponCode: p.couponCode?.trim() ? p.couponCode.trim().toUpperCase() : null,
      targets: p.scope === 'AllItems' || p.kind === 'BundlePrice' ? [] : p.targets,
      rewardTargets: p.kind === 'BuyXGetY' ? p.rewardTargets : [],
      bundleParts: p.kind === 'BundlePrice' ? p.bundleParts : [],
      storeIds: p.allStores ? [] : p.storeIds,
    };
    const found = checkPromotion(body);
    setProblems(found);
    if (found.length === 0) save.mutate(body);
  }

  return (
    <Dialog
      open
      width="xl"
      title={id ? p.name || 'Promotion' : 'New promotion'}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          {id && !readOnly && (
            <Button className="mr-auto" variant="danger" icon={Trash2} onClick={() => setDeleting(true)}>
              Delete
            </Button>
          )}
          {!readOnly && (
            <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
              Save
            </Button>
          )}
          <Button onClick={onClose} shortcut="Esc">
            {readOnly ? 'Close' : 'Cancel'}
          </Button>
        </>
      }
    >
      <p className="mb-4 rounded-control bg-sun-light px-3 py-2 font-semibold text-ink" data-testid="promotion-summary">
        {describePromotion(p, names)}
      </p>
      <div className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
        <div className="space-y-3">
          <Field label="Name" htmlFor="promo-name" hint="Shown on the receipt.">
            <Input id="promo-name" value={p.name} maxLength={60} readOnly={readOnly} onChange={(e) => set({ name: e.target.value })} />
          </Field>
          <Field label="Description" htmlFor="promo-description">
            <TextArea id="promo-description" rows={2} value={p.description ?? ''} readOnly={readOnly} onChange={(e) => set({ description: e.target.value || null })} />
          </Field>
          <Field label="Kind" htmlFor="promo-kind" hint={PROMOTION_KINDS.find((k) => k.key === p.kind)?.hint}>
            <Select id="promo-kind" value={p.kind} disabled={readOnly} onChange={(e) => set({ kind: e.target.value as PromotionKind })}>
              {PROMOTION_KINDS.map((k) => (
                <option key={k.key} value={k.key}>
                  {k.label}
                </option>
              ))}
            </Select>
          </Field>
          {p.kind === 'PercentOff' && num('percent', 'Percent off')}
          {p.kind === 'AmountOff' && num('amount', 'Off each unit')}
          {p.kind === 'BuyXGetY' && (
            <>
              {num('buyQuantity', 'Buy')}
              {num('getQuantity', 'Get')}
              {num('getPercent', 'At percent off', 'max-w-[160px]')}
            </>
          )}
          {p.kind === 'BundlePrice' && num('bundlePrice', 'Bundle price')}
          {p.kind === 'SpendOver' && (
            <>
              {num('spendThreshold', 'When they spend')}
              {num('percent', 'Percent off')}
              {num('amount', 'Or amount off')}
            </>
          )}
          {p.kind !== 'BundlePrice' && (
            <>
              <Field label={p.kind === 'BuyXGetY' ? 'What they buy' : p.kind === 'SpendOver' ? 'Spend counted on' : 'Applies to'} htmlFor="promo-scope">
                <Select id="promo-scope" value={p.scope} disabled={readOnly} onChange={(e) => set({ scope: e.target.value as PromotionScope, targets: [] })}>
                  {SCOPES.map((s) => (
                    <option key={s.key} value={s.key}>
                      {s.label}
                    </option>
                  ))}
                </Select>
              </Field>
              {fixedType && (
                <Field label="">
                  <TargetList label="Applies to" targets={p.targets} fixed={fixedType} readOnly={readOnly} onChange={(targets) => set({ targets })} />
                </Field>
              )}
            </>
          )}
          {p.kind === 'BuyXGetY' && (
            <Field label="What they get" hint="No lines: more of what they buy, the cheapest given.">
              <TargetList label="What they get" targets={p.rewardTargets} readOnly={readOnly} onChange={(rewardTargets) => set({ rewardTargets })} />
            </Field>
          )}
          {p.kind === 'BundlePrice' && (
            <Field label="In the bundle" hint="Each line with how many.">
              <TargetList label="In the bundle" targets={p.bundleParts} withQuantity readOnly={readOnly} onChange={(bundleParts) => set({ bundleParts })} />
            </Field>
          )}
        </div>
        <div className="space-y-3">
          <Field label="First day" htmlFor="promo-from">
            <Input id="promo-from" type="date" className="max-w-[200px]" value={p.startsOn ?? ''} readOnly={readOnly} onChange={(e) => set({ startsOn: e.target.value || null })} />
          </Field>
          <Field label="Last day" htmlFor="promo-to">
            <Input id="promo-to" type="date" className="max-w-[200px]" value={p.endsOn ?? ''} readOnly={readOnly} onChange={(e) => set({ endsOn: e.target.value || null })} />
          </Field>
          <Field label="Days" hint="None chosen: every day.">
            <div className="flex flex-wrap gap-1.5" role="group" aria-label="Days of the week">
              {WEEK_DAYS.map((d) => {
                const on = p.days.includes(d.day);
                return (
                  <button
                    key={d.day}
                    type="button"
                    aria-pressed={on}
                    aria-label={d.long}
                    disabled={readOnly}
                    className={cx('btn min-h-[40px] px-3', on && 'btn-dark')}
                    onClick={() => set({ days: on ? p.days.filter((x) => x !== d.day) : [...p.days, d.day].sort((a, b) => a - b) })}
                  >
                    {d.short}
                  </button>
                );
              })}
            </div>
          </Field>
          <Field label="Hours" htmlFor="promo-start" hint="Empty: all day. A happy hour has both.">
            <div className="flex flex-wrap items-center gap-2">
              <Input id="promo-start" aria-label="From" type="time" className="w-[140px]" value={p.startTime ?? ''} readOnly={readOnly} onChange={(e) => set({ startTime: e.target.value || null })} />
              <span className="text-muted">to</span>
              <Input aria-label="Until" type="time" className="w-[140px]" value={p.endTime ?? ''} readOnly={readOnly} onChange={(e) => set({ endTime: e.target.value || null })} />
            </div>
          </Field>
          <Field label="Stores">
            <div className="space-y-1">
              <Checkbox
                label="Every store"
                checked={p.allStores}
                disabled={readOnly || !canChangeShared}
                hint={!canChangeShared ? 'Your promotions are for your store.' : undefined}
                onChange={(allStores) => set({ allStores, storeIds: allStores ? [] : user.storeId ? [user.storeId] : [] })}
              />
              {!p.allStores &&
                stores.map((s) => (
                  <Checkbox
                    key={s.id}
                    label={`${s.code} · ${s.name}`}
                    checked={p.storeIds.includes(s.id)}
                    disabled={readOnly || !canChangeShared}
                    onChange={(on) => set({ storeIds: on ? [...p.storeIds, s.id] : p.storeIds.filter((x) => x !== s.id) })}
                  />
                ))}
            </div>
          </Field>
          <Field label="Coupon code" htmlFor="promo-coupon" hint="Empty: no coupon needed. With a code it applies only when the code is entered or scanned.">
            <Input id="promo-coupon" className="max-w-[220px] uppercase" value={p.couponCode ?? ''} readOnly={readOnly} onChange={(e) => set({ couponCode: e.target.value.toUpperCase() || null })} />
          </Field>
          {p.couponCode && num('couponMaxUses', 'Coupon uses (0 no limit)')}
          {num('priority', 'Priority', 'max-w-[120px]')}
          <Field label="">
            <Checkbox label="May combine with other promotions" checked={p.combinable} disabled={readOnly} onChange={(combinable) => set({ combinable })} />
          </Field>
          <Field label="">
            <Checkbox label="On" checked={p.isActive} disabled={readOnly} onChange={(isActive) => set({ isActive })} />
          </Field>
        </div>
      </div>
      {problems.length > 0 && (
        <ul className="mt-3 list-disc space-y-1 rounded-control bg-danger-light px-8 py-2 font-semibold text-danger-ink" role="alert">
          {problems.map((x) => (
            <li key={x}>{x}</li>
          ))}
        </ul>
      )}
      <FormError error={save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null} />
      <ConflictDialog
        open={conflict}
        what="promotion"
        onReload={() => {
          setConflict(false);
          setForm(null);
          setTexts({});
          save.reset();
          void detail.refetch();
        }}
        onClose={() => setConflict(false)}
      />
      <ConfirmDialog
        open={deleting}
        title={`Delete ${p.name}?`}
        message="A promotion that was used at a till is not deleted: switch it off instead."
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => remove.mutate()}
        onClose={() => setDeleting(false)}
      />
    </Dialog>
  );
}

// Report ------------------------------------------------------------------------------------------------------------

function Report() {
  const { storeId, currencySymbol: symbol } = useStore();
  const dates = useDateRange('month');
  const query = useQuery({
    queryKey: ['promotion-report', storeId, dates.range.from, dates.range.to],
    queryFn: () => api.promotionReport({ storeId, from: dates.range.from, to: dates.range.to }),
    placeholderData: keepPreviousData,
  });
  const rows = query.data ?? [];
  const sum = (pick: (r: PromotionReportRow) => number) => rows.reduce((s, r) => s + pick(r), 0);
  const money = (n: number) => formatMoney(n, symbol);
  return (
    <>
      <Toolbar>
        <DateRangeBar state={dates} compact />
      </Toolbar>
      <DataTable<PromotionReportRow>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => r.promotionId}
        caption="What each promotion cost and earned"
        showTotals={rows.length > 0}
        empty={<EmptyState title="No promotion was used in these days" />}
        columns={[
          { key: 'name', header: 'Promotion', cell: (r) => <span className="font-semibold">{r.name}</span> },
          { key: 'kind', header: 'Kind', cell: (r) => kindLabel(r.kind), hideBelow: 'md' },
          { key: 'sales', header: 'Sales', numeric: true, cell: (r) => formatCount(r.sales), total: formatCount(sum((r) => r.sales)), hideBelow: 'sm' },
          { key: 'units', header: 'Units', numeric: true, cell: (r) => formatQuantity(r.units), total: formatQuantity(sum((r) => r.units)), hideBelow: 'lg' },
          { key: 'net', header: 'Earned', numeric: true, cell: (r) => money(r.netSales), total: money(sum((r) => r.netSales)) },
          { key: 'cost', header: 'Cost', numeric: true, cell: (r) => <span className="text-danger-ink">{money(r.discount)}</span>, total: money(sum((r) => r.discount)) },
          { key: 'coupons', header: 'Coupons used', numeric: true, cell: (r) => formatCount(r.couponUses), total: formatCount(sum((r) => r.couponUses)), hideBelow: 'md' },
        ]}
      />
      <p className="mt-2 px-1 text-ink">Earned is the net sales of the lines a promotion priced (of the whole sale for Spend over); cost is the discount it gave.</p>
    </>
  );
}
