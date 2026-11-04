'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Copy, Pencil, Plus, RefreshCw, Save, Trash2, X } from 'lucide-react';
import { useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useId, useMemo, useState } from 'react';
import { BankSelect, StatusChip, useAccounts, useBankAccounts, useMoney } from '@/components/finance';
import { ConflictDialog } from '@/components/masters';
import { Button, IconButton } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select, TextArea } from '@/components/ui/field';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { label, postingAccounts } from '@/lib/finance';
import { formatCount, formatLocalDateTime, formatUtc } from '@/lib/format';
import { numberText, parseNumber } from '@/lib/forms';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { FinanceSettings, FiscalRow, MappingRow, StoreFinance, StoreInfo, TaxRuleSet, WithholdingKind } from '@/lib/types';

type Tab = 'books' | 'tax' | 'stores' | 'mapping' | 'fbr';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'books', label: 'The books' },
  { key: 'tax', label: 'Tax rules' },
  { key: 'stores', label: 'Stores' },
  { key: 'mapping', label: 'Account mapping' },
  { key: 'fbr', label: 'FBR' },
];
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

export default function FinanceSettingsPage() {
  return (
    <Suspense>
      <FinanceSetup />
    </Suspense>
  );
}

/** How the books are kept: their settings, the country's tax rules, each store's rules and bank, the daily journal's accounts, FBR. */
function FinanceSetup() {
  const params = useSearchParams();
  const asked = params.get('tab') as Tab | null;
  const [tab, setTab] = useState<Tab>(asked && TABS.some((t) => t.key === asked) ? asked : 'books');
  return (
    <div>
      <PageHeader title="Finance setup" />
      <Toolbar>
        <Tabs label="Finance setup" tabs={TABS} value={tab} onChange={setTab} />
      </Toolbar>
      {tab === 'books' ? <Books /> : tab === 'tax' ? <TaxRules /> : tab === 'stores' ? <Stores /> : tab === 'mapping' ? <Mapping /> : <Fbr />}
    </div>
  );
}

// The books ----------------------------------------------------------------------------------------------------------

function Books() {
  const { can, canChangeShared } = useSession();
  const toast = useToast();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['finance-settings'], queryFn: () => api.financeSettings() });
  const [form, setForm] = useState<FinanceSettings | null>(null);
  const [conflict, setConflict] = useState(false);
  const ids = { currency: useId(), month: useId(), country: useId(), days: useId(), hour: useId(), size: useId() };
  const edit = can(P.financeSetup) && canChangeShared;
  useEffect(() => {
    if (query.data && (!form || form.version !== query.data.version)) setForm(query.data);
    // The form follows the server's copy only when a newer one arrives.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query.data]);
  const save = useMutation({
    mutationFn: (body: FinanceSettings) => api.saveFinanceSettings(body),
    onSuccess: (saved) => {
      toast.ok('The finance settings are saved.');
      queryClient.setQueryData(['finance-settings'], saved);
      setForm(saved);
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  if (query.error) return <ErrorState className="card" error={query.error} onRetry={() => void query.refetch()} />;
  if (!form) return <Skeleton className="h-80 rounded-card" />;
  const set = <K extends keyof FinanceSettings>(key: K, value: FinanceSettings[K]) => setForm((f) => (f ? { ...f, [key]: value } : f));
  const whole = (text: string) => Math.max(0, Math.trunc(parseNumber(text) ?? 0));

  return (
    <form
      className="card space-y-4 p-5"
      onSubmit={(e) => {
        e.preventDefault();
        if (edit) save.mutate(form);
      }}
    >
      {!edit && <p className="text-ink">These settings are the whole company’s{can(P.financeSetup) ? ': a super administrator changes them' : ''}.</p>}
      <div className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
        <Field label="Currency of the books" htmlFor={ids.currency} hint="Three letters, for example PKR">
          <Input id={ids.currency} value={form.baseCurrency} maxLength={3} readOnly={!edit} onChange={(e) => set('baseCurrency', e.target.value.toUpperCase())} />
        </Field>
        <Field label="The year starts in" htmlFor={ids.month} hint="July in Pakistan. A year already made keeps its months">
          <Select id={ids.month} value={form.fiscalYearStartMonth} disabled={!edit} onChange={(e) => set('fiscalYearStartMonth', Number(e.target.value))}>
            {MONTHS.map((m, i) => (
              <option key={m} value={i + 1}>
                {m}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Country" htmlFor={ids.country} hint="Its tax rules apply where a store has none of its own">
          <Input id={ids.country} value={form.country} maxLength={2} readOnly={!edit} onChange={(e) => set('country', e.target.value.toUpperCase())} />
        </Field>
        <Field label="Receipts up to (KB)" htmlFor={ids.size} hint="The largest picture or PDF kept with an expense, bill or entry">
          <Input id={ids.size} inputMode="numeric" value={String(form.attachmentMaxKilobytes)} readOnly={!edit} onChange={(e) => set('attachmentMaxKilobytes', whole(e.target.value))} />
        </Field>
        <Field label="Post again each night" htmlFor={ids.days} hint="The last days of every store, in case a till sent late">
          <Input id={ids.days} inputMode="numeric" value={String(form.nightlyDays)} readOnly={!edit} onChange={(e) => set('nightlyDays', whole(e.target.value))} />
        </Field>
        <Field label="At (hour)" htmlFor={ids.hour} hint="0 to 23, the server’s time">
          <Input id={ids.hour} inputMode="numeric" value={String(form.nightlyHour)} readOnly={!edit} onChange={(e) => set('nightlyHour', Math.min(23, whole(e.target.value)))} />
        </Field>
      </div>
      <div className="space-y-1">
        <Checkbox
          label="Entries made by hand need a second person’s approval"
          hint="The one who made an entry cannot approve it"
          checked={form.manualEntriesNeedApproval}
          disabled={!edit}
          onChange={(v) => set('manualEntriesNeedApproval', v)}
        />
        <Checkbox
          label="Post a till’s days as soon as its package arrives"
          hint="Otherwise only each night, or with Post days"
          checked={form.autoPostOnSync}
          disabled={!edit}
          onChange={(v) => set('autoPostOnSync', v)}
        />
      </div>
      {save.error && !conflict ? <FormError error={save.error} /> : null}
      {edit && (
        <Button type="submit" variant="sun" icon={Save} shortcut="Ctrl+S" busy={save.isPending}>
          Save
        </Button>
      )}
      <ConflictDialog
        open={conflict}
        what="settings"
        onReload={() => {
          setConflict(false);
          setForm(null);
          void query.refetch();
        }}
        onClose={() => setConflict(false)}
      />
    </form>
  );
}

// Tax rules ----------------------------------------------------------------------------------------------------------

function TaxRules() {
  const { can, canChangeShared } = useSession();
  const toast = useToast();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['tax-rules'], queryFn: () => api.taxRules() });
  const [editing, setEditing] = useState<TaxRuleSet | null>(null);
  const [deleting, setDeleting] = useState<TaxRuleSet | null>(null);
  const edit = can(P.financeSetup) && canChangeShared;
  const remove = useMutation({
    mutationFn: (set: TaxRuleSet) => api.deleteTaxRules(set.id!),
    onSuccess: () => {
      toast.ok('The rule set is deleted.');
      setDeleting(null);
      void queryClient.invalidateQueries({ queryKey: ['tax-rules'] });
    },
  });
  const blank: TaxRuleSet = { id: null, version: null, country: 'PK', name: '', inputTaxClaimable: true, notes: null, isActive: true, salesTaxRates: [], withholdingRules: [] };
  return (
    <>
      <p className="mb-3 px-1 text-ink">
        Sales tax rates and the tax kept back from suppliers’ payments, as data per country. Check them each budget year; copy a set to make the next one.
      </p>
      {edit && (
        <Toolbar>
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing(blank)}>
            New rule set
          </Button>
        </Toolbar>
      )}
      {query.error ? (
        <ErrorState className="card" error={query.error} onRetry={() => void query.refetch()} />
      ) : !query.data ? (
        <Skeleton className="h-64 rounded-card" />
      ) : query.data.length === 0 ? (
        <div className="card">
          <EmptyState title="No tax rules" />
        </div>
      ) : (
        <div className="space-y-4">
          {query.data.map((set) => (
            <section key={set.id} className="card p-5" aria-label={set.name}>
              <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
                <h2 className="section-title">
                  {set.name} <span className="text-muted">({set.country})</span> {!set.isActive && <StatusChip status="Inactive" />}
                </h2>
                {edit && (
                  <div className="flex flex-wrap gap-2.5">
                    <Button icon={Pencil} onClick={() => setEditing(set)}>
                      Change
                    </Button>
                    <Button icon={Copy} onClick={() => setEditing({ ...set, id: null, version: null, name: `${set.name} (copy)` })}>
                      Copy
                    </Button>
                    <Button icon={Trash2} onClick={() => setDeleting(set)}>
                      Delete
                    </Button>
                  </div>
                )}
              </div>
              <p className="mb-2 text-ink">{set.inputTaxClaimable ? 'Input tax on purchases is claimed back.' : 'Input tax on purchases is a cost (not claimed).'}</p>
              {set.notes && <p className="mb-2 text-muted">{set.notes}</p>}
              <div className="grid gap-4 lg:grid-cols-2">
                <div>
                  <h3 className="mb-1 font-semibold">Sales tax</h3>
                  <ul className="space-y-0.5">
                    {set.salesTaxRates.map((r) => (
                      <li key={r.name}>
                        {r.name}: {r.rate}%{r.isDefault ? ' (usual)' : ''}
                      </li>
                    ))}
                  </ul>
                </div>
                <div>
                  <h3 className="mb-1 font-semibold">Kept back from suppliers</h3>
                  <ul className="space-y-0.5">
                    {set.withholdingRules.map((r) => (
                      <li key={r.kind}>
                        {r.kind}
                        {r.section ? ` (${r.section})` : ''}: {r.filerRate}% filers, {r.nonFilerRate}% others
                        {r.threshold ? `, above ${r.threshold.toLocaleString()}` : ''}
                      </li>
                    ))}
                  </ul>
                </div>
              </div>
            </section>
          ))}
        </div>
      )}
      {editing && <RuleSetDialog set={editing} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete ${deleting?.name}?`}
        message={
          <>
            A rule set a store uses cannot be deleted; make it inactive instead.
            {remove.error ? <FormError error={remove.error} /> : null}
          </>
        }
        confirmLabel="Delete"
        danger
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting)}
        onClose={() => {
          setDeleting(null);
          remove.reset();
        }}
      />
    </>
  );
}

const KINDS: WithholdingKind[] = ['Goods', 'Services', 'Rent', 'Other'];

function RuleSetDialog({ set, onClose }: { set: TaxRuleSet; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const ids = { country: useId(), name: useId(), notes: useId() };
  const [form, setForm] = useState<TaxRuleSet>(set);
  // Rates are typed as text: [name, rate, default] and [kind, section, filer, non-filer, threshold].
  const [rates, setRates] = useState(() => set.salesTaxRates.map((r) => ({ name: r.name, rate: numberText(r.rate), isDefault: r.isDefault })));
  const [rules, setRules] = useState(() =>
    set.withholdingRules.map((r) => ({ kind: r.kind, section: r.section ?? '', filer: numberText(r.filerRate), nonFiler: numberText(r.nonFilerRate), threshold: numberText(r.threshold) })),
  );
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const save = useMutation({
    mutationFn: (body: TaxRuleSet) => (set.id ? api.saveTaxRules(set.id, body) : api.addTaxRules(body)),
    onSuccess: (saved) => {
      toast.ok(`${saved.name} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['tax-rules'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  const n = (text: string) => parseNumber(text);

  function submit() {
    if (!form.name.trim() || !form.country.trim()) return setProblem('A rule set needs a country and a name.');
    if (rates.some((r) => !r.name.trim() || n(r.rate) === null)) return setProblem('Each sales tax rate needs a name and a rate.');
    if (rules.some((r) => n(r.filer) === null || n(r.nonFiler) === null)) return setProblem('Each withholding rule needs both rates.');
    setProblem(null);
    save.mutate({
      ...form,
      name: form.name.trim(),
      country: form.country.trim().toUpperCase(),
      notes: form.notes?.trim() || null,
      salesTaxRates: rates.map((r) => ({ name: r.name.trim(), rate: n(r.rate) ?? 0, isDefault: r.isDefault })),
      withholdingRules: rules.map((r) => ({ kind: r.kind, section: r.section.trim() || null, filerRate: n(r.filer) ?? 0, nonFilerRate: n(r.nonFiler) ?? 0, threshold: n(r.threshold) ?? 0 })),
    });
  }

  return (
    <Dialog
      open
      width="xl"
      title={set.id ? set.name : 'New rule set'}
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
      <div className="space-y-4">
        <div className="grid grid-cols-1 gap-x-8 gap-y-3 md:grid-cols-2">
          <Field label="Country" htmlFor={ids.country}>
            <Input id={ids.country} value={form.country} maxLength={2} onChange={(e) => setForm({ ...form, country: e.target.value.toUpperCase() })} />
          </Field>
          <Field label="Name" htmlFor={ids.name} hint="For example Pakistan 2026-27">
            <Input id={ids.name} value={form.name} maxLength={60} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          </Field>
        </div>
        <div className="space-y-1">
          <Checkbox label="Input tax on purchases is claimed back" checked={form.inputTaxClaimable} onChange={(v) => setForm({ ...form, inputTaxClaimable: v })} />
          <Checkbox label="Active" checked={form.isActive} onChange={(v) => setForm({ ...form, isActive: v })} />
        </div>
        <section aria-label="Sales tax rates" className="space-y-2">
          <h3 className="section-title">Sales tax</h3>
          {rates.map((r, i) => (
            <div key={i} className="flex flex-wrap items-center gap-2.5">
              <Input aria-label={`Name of rate ${i + 1}`} className="w-[240px]" value={r.name} maxLength={40} onChange={(e) => setRates(rates.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)))} />
              <Input aria-label={`Rate ${i + 1} (%)`} inputMode="decimal" className="w-[110px] text-right" value={r.rate} onChange={(e) => setRates(rates.map((x, j) => (j === i ? { ...x, rate: e.target.value } : x)))} />
              <Checkbox label="Usual" checked={r.isDefault} onChange={(v) => setRates(rates.map((x, j) => ({ ...x, isDefault: j === i ? v : v ? false : x.isDefault })))} />
              <IconButton icon={X} label={`Remove rate ${i + 1}`} onClick={() => setRates(rates.filter((_, j) => j !== i))} />
            </div>
          ))}
          <Button icon={Plus} onClick={() => setRates([...rates, { name: '', rate: '', isDefault: rates.length === 0 }])}>
            Add a rate
          </Button>
        </section>
        <section aria-label="Withholding" className="space-y-2">
          <h3 className="section-title">Kept back from suppliers’ payments</h3>
          {rules.map((r, i) => {
            const change = (c: Partial<(typeof rules)[number]>) => setRules(rules.map((x, j) => (j === i ? { ...x, ...c } : x)));
            return (
              <div key={i} className="flex flex-wrap items-center gap-2.5">
                <Select aria-label={`Kind of rule ${i + 1}`} className="w-[140px]" value={r.kind} onChange={(e) => change({ kind: e.target.value as WithholdingKind })}>
                  {KINDS.map((k) => (
                    <option key={k} value={k}>
                      {k}
                    </option>
                  ))}
                </Select>
                <Input aria-label={`Section of rule ${i + 1}`} placeholder="Section" className="w-[130px]" value={r.section} maxLength={20} onChange={(e) => change({ section: e.target.value })} />
                <Input aria-label={`Filer rate of rule ${i + 1}`} placeholder="Filers %" inputMode="decimal" className="w-[110px] text-right" value={r.filer} onChange={(e) => change({ filer: e.target.value })} />
                <Input aria-label={`Others' rate of rule ${i + 1}`} placeholder="Others %" inputMode="decimal" className="w-[110px] text-right" value={r.nonFiler} onChange={(e) => change({ nonFiler: e.target.value })} />
                <Input aria-label={`Threshold of rule ${i + 1}`} placeholder="Above" inputMode="decimal" className="w-[130px] text-right" value={r.threshold} onChange={(e) => change({ threshold: e.target.value })} />
                <IconButton icon={X} label={`Remove rule ${i + 1}`} onClick={() => setRules(rules.filter((_, j) => j !== i))} />
              </div>
            );
          })}
          <Button icon={Plus} onClick={() => setRules([...rules, { kind: 'Goods', section: '', filer: '', nonFiler: '', threshold: '0' }])}>
            Add a rule
          </Button>
        </section>
        <Field label="Notes" htmlFor={ids.notes} stacked>
          <TextArea id={ids.notes} value={form.notes ?? ''} maxLength={400} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
        </Field>
        {(problem || (save.error && !conflict)) && (problem ? <p className="text-danger">{problem}</p> : <FormError error={save.error} />)}
      </div>
      <ConflictDialog
        open={conflict}
        what="rule set"
        onReload={() => {
          void queryClient.invalidateQueries({ queryKey: ['tax-rules'] });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}

// Stores -------------------------------------------------------------------------------------------------------------

function Stores() {
  const { stores } = useStore();
  const rules = useQuery({ queryKey: ['tax-rules'], queryFn: () => api.taxRules() });
  const banks = useBankAccounts();
  return (
    <>
      <p className="mb-3 px-1 text-ink">Each store’s tax rules (else its country’s) and the bank account its till cash is taken to.</p>
      {stores.length === 0 ? (
        <Skeleton className="h-40 rounded-card" />
      ) : (
        <div className="space-y-3">
          {stores.map((s) => (
            <StoreRow key={s.id} store={s} ruleSets={rules.data ?? []} banks={banks.data} />
          ))}
        </div>
      )}
    </>
  );
}

function StoreRow({ store, ruleSets, banks }: { store: StoreInfo; ruleSets: TaxRuleSet[]; banks: ReturnType<typeof useBankAccounts>['data'] }) {
  const { can } = useSession();
  const toast = useToast();
  const queryClient = useQueryClient();
  const ids = { rules: useId(), bank: useId() };
  const query = useQuery({ queryKey: ['store-finance', store.id], queryFn: () => api.storeFinance(store.id) });
  const [form, setForm] = useState<StoreFinance | null>(null);
  useEffect(() => {
    if (query.data) setForm(query.data);
  }, [query.data]);
  const save = useMutation({
    mutationFn: (body: StoreFinance) => api.saveStoreFinance(store.id, body),
    onSuccess: (saved) => {
      toast.ok(`${store.name} is saved.`);
      queryClient.setQueryData(['store-finance', store.id], saved);
    },
  });
  const edit = can(P.financeSetup);
  const changed = !!form && !!query.data && (form.taxRuleSetId !== query.data.taxRuleSetId || form.depositBankAccountId !== query.data.depositBankAccountId);
  return (
    <section className="card p-5" aria-label={`${store.code} ${store.name}`}>
      <h2 className="section-title mb-3">
        {store.code} {store.name}
      </h2>
      {!form ? (
        query.error ? (
          <FormError error={query.error} />
        ) : (
          <Skeleton className="h-20" />
        )
      ) : (
        <div className="grid grid-cols-1 items-start gap-x-8 gap-y-3 lg:grid-cols-[1fr_1fr_auto]">
          <Field label="Tax rules" htmlFor={ids.rules} hint={form.ruleSet ? `In use: ${form.ruleSet}` : undefined}>
            <Select id={ids.rules} value={form.taxRuleSetId ?? ''} disabled={!edit} onChange={(e) => setForm({ ...form, taxRuleSetId: e.target.value || null })}>
              <option value="">The country’s</option>
              {ruleSets
                .filter((r) => r.isActive || r.id === form.taxRuleSetId)
                .map((r) => (
                  <option key={r.id} value={r.id ?? ''}>
                    {r.name}
                  </option>
                ))}
            </Select>
          </Field>
          <Field label="Till cash goes to" htmlFor={ids.bank}>
            <BankSelect id={ids.bank} value={form.depositBankAccountId} accounts={banks?.filter((b) => b.storeId === null || b.storeId === store.id)} allowEmpty emptyLabel="Ask each time" onChange={(v) => setForm({ ...form, depositBankAccountId: v })} />
          </Field>
          {edit && (
            <Button variant={changed ? 'sun' : 'plain'} icon={Save} disabled={!changed} busy={save.isPending} onClick={() => save.mutate(form)}>
              Save
            </Button>
          )}
          {save.error ? (
            <div className="lg:col-span-3">
              <FormError error={save.error} />
            </div>
          ) : null}
        </div>
      )}
    </section>
  );
}

// Account mapping ----------------------------------------------------------------------------------------------------

const MAPPING_LABELS: Record<string, string> = {
  Tender: 'Money taken as',
  Department: 'Sales of',
  Sales: 'Other sales',
  Tax1: 'Tax 1',
  Tax2: 'Tax 2',
  GiftCardLiability: 'Gift cards not yet spent',
  CostOfGoods: 'Cost of goods sold',
  Inventory: 'Stock',
  AccountsReceivable: 'Owed by clients on account',
  Discounts: 'Discounts on the whole sale',
  Rounding: 'Cash rounding',
  StockAdjustments: 'Stock found or lost',
  PurchasesClearing: 'Goods received, not yet billed',
  InventoryInTransit: 'Stock on its way to another store',
  GiftCardAdjustments: 'Gift card corrections',
};

function Mapping() {
  const { can } = useSession();
  const { storeId, defaultStoreId, stores, canSwitch } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const accounts = useAccounts();
  const storeSelect = useId();
  const [chosen, setChosen] = useState<string | null>(storeId ?? defaultStoreId);
  useEffect(() => {
    if (!chosen && (storeId ?? defaultStoreId)) setChosen(storeId ?? defaultStoreId);
  }, [chosen, storeId, defaultStoreId]);
  const query = useQuery({ queryKey: ['mapping', chosen], queryFn: () => api.mapping(chosen), enabled: !!chosen });
  const [codes, setCodes] = useState<Map<string, string>>(new Map());
  useEffect(() => setCodes(new Map()), [query.data]);
  const choices = useMemo(() => postingAccounts(accounts.data ?? []), [accounts.data]);
  const keyOf = (r: MappingRow) => `${r.kind}|${r.key}`;
  const save = useMutation({
    mutationFn: () => api.saveMapping(chosen, (query.data ?? []).map((r) => ({ ...r, accountCode: codes.has(keyOf(r)) ? codes.get(keyOf(r)) || null : r.accountCode }))),
    onSuccess: (saved) => {
      toast.ok('The account mapping is saved. Post the days again to move what waited in suspense.');
      queryClient.setQueryData(['mapping', chosen], saved);
    },
  });
  const edit = can(P.financeSetup);
  const missing = (query.data ?? []).filter((r) => !(codes.has(keyOf(r)) ? codes.get(keyOf(r)) : r.accountCode)).length;
  return (
    <>
      <p className="mb-3 px-1 text-ink">
        The account each amount of the tills’ days goes to: money by tender, sales by department, taxes, stock, gift cards. What has no account waits in
        Suspense.
      </p>
      <Toolbar>
        {canSwitch && (
          <>
            <label htmlFor={storeSelect} className="text-muted">
              Store
            </label>
            <Select id={storeSelect} className="w-[260px]" value={chosen ?? ''} onChange={(e) => setChosen(e.target.value || null)}>
              {stores.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.code} {s.name}
                </option>
              ))}
            </Select>
          </>
        )}
        {edit && (
          <Button variant="sun" icon={Save} shortcut="Ctrl+S" disabled={codes.size === 0} busy={save.isPending} onClick={() => save.mutate()}>
            Save
          </Button>
        )}
        {query.data && <span className="text-muted">{missing ? `${formatCount(missing)} without an account` : 'Every amount has its account'}</span>}
      </Toolbar>
      {save.error ? (
        <div className="mb-3">
          <FormError error={save.error} />
        </div>
      ) : null}
      <DataTable<MappingRow>
        caption="Account mapping"
        rows={query.data}
        loading={query.isLoading || !chosen}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={keyOf}
        columns={[
          { key: 'what', header: 'Amount', cell: (r) => <span className="font-semibold">{MAPPING_LABELS[r.kind] ?? label(r.kind)}</span> },
          { key: 'key', header: 'Of', cell: (r) => r.key },
          {
            key: 'account',
            header: 'Account',
            className: 'min-w-[300px]',
            cell: (r) => {
              const code = codes.has(keyOf(r)) ? codes.get(keyOf(r)) ?? '' : r.accountCode ?? '';
              return edit ? (
                <Select aria-label={`Account for ${MAPPING_LABELS[r.kind] ?? r.kind} ${r.key}`.trim()} value={code} onChange={(e) => setCodes((m) => new Map(m).set(keyOf(r), e.target.value))}>
                  <option value="">None (to suspense)</option>
                  {code && !choices.some((a) => a.code === code) && <option value={code}>{code} (not in the chart)</option>}
                  {choices.map((a) => (
                    <option key={a.id} value={a.code}>
                      {a.code} {a.name}
                    </option>
                  ))}
                </Select>
              ) : code ? (
                `${code} ${r.accountName ?? ''}`
              ) : (
                'None'
              );
            },
          },
        ]}
      />
    </>
  );
}

// FBR ----------------------------------------------------------------------------------------------------------------

function Fbr() {
  const { can } = useSession();
  const { storeId } = useStore();
  const money = useMoney();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [status, setStatus] = useState('');
  const query = useQuery({ queryKey: ['fiscal', storeId, status], queryFn: () => api.fiscalQueue({ storeId, status }) });
  const retry = useMutation({
    mutationFn: (row: FiscalRow) => api.retryFiscal(row.id),
    onSuccess: () => {
      toast.ok('It is sent again with the next run.');
      void queryClient.invalidateQueries({ queryKey: ['fiscal'] });
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : 'It was not queued again.'),
  });
  return (
    <>
      <p className="mb-3 px-1 text-ink">
        {query.data?.enabled
          ? 'Each sale is reported to FBR’s POS integration; the number FBR gives goes on the sale.'
          : 'FBR’s POS integration is off. It is switched on in the server’s settings (Fbr:Enabled) for the shops that fall under it.'}
      </p>
      <Toolbar>
        <label className="sr-only" htmlFor="fiscal-status">
          Status
        </label>
        <Select id="fiscal-status" className="w-[200px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every sale</option>
          <option value="Pending">Waiting</option>
          <option value="Failed">Refused or unanswered</option>
          <option value="Sent">Sent</option>
          <option value="Skipped">Not sent</option>
        </Select>
        <Button icon={RefreshCw} onClick={() => void query.refetch()} busy={query.isFetching}>
          Refresh
        </Button>
      </Toolbar>
      <DataTable<FiscalRow>
        caption="Sales reported to FBR"
        rows={query.data?.rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => r.id}
        empty={<EmptyState title="Nothing here">No sale is in the FBR queue.</EmptyState>}
        columns={[
          { key: 'sale', header: 'Sale', cell: (r) => `${r.storeCode} ${r.stationNumber}-${r.invoiceNumber}` },
          { key: 'at', header: 'Sold', cell: (r) => formatLocalDateTime(r.soldAt), hideBelow: 'md' },
          { key: 'status', header: 'State', cell: (r) => <StatusChip status={r.status} /> },
          { key: 'number', header: 'FBR number', cell: (r) => r.fiscalNumber ?? '', hideBelow: 'sm' },
          { key: 'error', header: 'Answer', cell: (r) => r.lastError ?? (r.sentAtUtc ? `Sent ${formatUtc(r.sentAtUtc)}` : ''), className: 'wrap', hideBelow: 'lg' },
          { key: 'total', header: 'Total', numeric: true, cell: (r) => money(r.total) },
          {
            key: 'retry',
            header: <span className="sr-only">Send again</span>,
            cell: (r) =>
              r.status === 'Failed' && can(P.financeSetup) ? (
                <Button icon={RefreshCw} aria-label={`Send ${r.invoiceNumber} again`} busy={retry.isPending && retry.variables?.id === r.id} onClick={() => retry.mutate(r)}>
                  Send again
                </Button>
              ) : null,
          },
        ]}
      />
    </>
  );
}
