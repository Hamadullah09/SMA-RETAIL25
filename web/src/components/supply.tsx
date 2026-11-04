'use client';

/**
 * What the supply chain pages share: the location picker of a store, a store's supply chain settings (each page edits
 * the ones it is about), the printed lists sorted by bin (put-away, pick, count sheet), and the small number box.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Printer, Save } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select } from '@/components/ui/field';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { formatDate, formatQuantity } from '@/lib/format';
import { parseNumber } from '@/lib/forms';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import type { BinList, BinListRow, SupplySettings } from '@/lib/types';

/** The locations of a store (none until a store is chosen). */
export function useLocations(storeId: string | null) {
  return useQuery({ queryKey: ['locations', storeId], queryFn: () => api.locations(storeId!), enabled: !!storeId, staleTime: 5 * 60_000 });
}

/** A location of the store, picked from a list; the first one until the user chooses. */
export function LocationSelect({
  storeId,
  value,
  onChange,
  id = 'location',
  className = 'w-[220px]',
}: {
  storeId: string | null;
  value: string | null;
  onChange(locationId: string): void;
  id?: string;
  className?: string;
}) {
  const locations = useLocations(storeId);
  const list = useMemo(() => locations.data ?? [], [locations.data]);
  useEffect(() => {
    if (list.length && !list.some((l) => l.id === value)) onChange(list[0].id);
  }, [list, value, onChange]);
  return (
    <>
      <label className="sr-only" htmlFor={id}>
        Location
      </label>
      <Select id={id} className={className} value={value ?? ''} onChange={(e) => onChange(e.target.value)} disabled={!list.length}>
        {list.length === 0 && <option value="">{storeId ? 'Loading locations…' : 'Choose a store'}</option>}
        {list.map((l) => (
          <option key={l.id} value={l.id}>
            {l.code} {l.name ?? ''}
          </option>
        ))}
      </Select>
    </>
  );
}

type Fields = Array<{ key: keyof SupplySettings; label: string; hint?: string; kind: 'number' | 'check'; decimals?: number }>;

/** The settings each page offers, in the words of that page. */
export const SETTINGS_FIELDS: Record<'batches' | 'matching' | 'reorder' | 'requisitions', Fields> = {
  batches: [
    { key: 'blockExpiredSales', label: 'Stop expired stock from being sold at the POS', kind: 'check' },
    { key: 'expiryWarningDays', label: 'Warn about expiry (days ahead)', kind: 'number', decimals: 0 },
  ],
  matching: [
    { key: 'quantityTolerancePercent', label: 'Quantity tolerance (%)', kind: 'number' },
    { key: 'quantityToleranceUnits', label: 'Quantity tolerance (pieces)', kind: 'number', decimals: 3 },
    { key: 'priceTolerancePercent', label: 'Price tolerance (%)', kind: 'number' },
    { key: 'priceToleranceAmount', label: 'Price tolerance (per piece)', kind: 'number', decimals: 3, hint: 'A bill line passes when it is within the larger of the percent and the amount.' },
  ],
  reorder: [
    { key: 'salesWindowDays', label: 'Average sales over (days)', kind: 'number', decimals: 0 },
    { key: 'safetyStockDays', label: 'Safety stock (days)', kind: 'number', decimals: 0 },
    { key: 'orderCoverDays', label: 'An order covers (days)', kind: 'number', decimals: 0 },
    { key: 'defaultLeadTimeDays', label: 'Lead time when none is set (days)', kind: 'number', decimals: 0 },
  ],
  requisitions: [{ key: 'approverMustDiffer', label: 'Someone else than the person who asked must approve', kind: 'check' }],
};

/**
 * A store's supply chain settings, the fields of one page. Users with `setup.store` change them; others see them.
 */
export function SupplySettingsDialog({ storeId, fields, title, onClose }: { storeId: string; fields: Fields; title: string; onClose(): void }) {
  const { can } = useSession();
  const toast = useToast();
  const queryClient = useQueryClient();
  const readOnly = !can(P.setupStore);
  const query = useQuery({ queryKey: ['supply-settings', storeId], queryFn: () => api.supplySettings(storeId) });
  const [form, setForm] = useState<Record<string, string | boolean>>({});
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!query.data) return;
    const next: Record<string, string | boolean> = {};
    for (const f of fields) next[f.key] = f.kind === 'check' ? Boolean(query.data[f.key]) : String(query.data[f.key]);
    setForm(next);
  }, [query.data, fields]);

  const save = useMutation({
    mutationFn: (body: SupplySettings) => api.saveSupplySettings(storeId, body),
    onSuccess: (saved) => {
      queryClient.setQueryData(['supply-settings', storeId], saved);
      toast.ok('The settings are saved.');
      onClose();
    },
  });

  function submit() {
    if (readOnly || !query.data) return onClose();
    const body: SupplySettings = { ...query.data };
    for (const f of fields) {
      if (f.kind === 'check') {
        (body as unknown as Record<string, unknown>)[f.key] = Boolean(form[f.key]);
        continue;
      }
      const n = parseNumber(String(form[f.key] ?? ''));
      if (n === null || n < 0) return setError(`${f.label}: enter a number, 0 or more.`);
      (body as unknown as Record<string, unknown>)[f.key] = f.decimals === 0 ? Math.round(n) : n;
    }
    setError(null);
    save.mutate(body);
  }

  return (
    <Dialog
      open
      title={title}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          {!readOnly && (
            <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending} disabled={!query.data}>
              Save
            </Button>
          )}
          <Button onClick={onClose} shortcut="Esc">
            {readOnly ? 'Close' : 'Cancel'}
          </Button>
        </>
      }
    >
      {query.error ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : !query.data ? (
        <Skeleton className="h-32" />
      ) : (
        <div className="space-y-3">
          {fields.map((f) =>
            f.kind === 'check' ? (
              <Checkbox key={f.key} label={f.label} checked={Boolean(form[f.key])} disabled={readOnly} onChange={(v) => setForm((s) => ({ ...s, [f.key]: v }))} />
            ) : (
              <Field key={f.key} label={f.label} hint={f.hint} htmlFor={`supply-${f.key}`}>
                <Input
                  id={`supply-${f.key}`}
                  inputMode="decimal"
                  className="max-w-[160px]"
                  value={String(form[f.key] ?? '')}
                  readOnly={readOnly}
                  onChange={(e) => setForm((s) => ({ ...s, [f.key]: e.target.value }))}
                />
              </Field>
            ),
          )}
          {readOnly && <p className="text-muted">These settings are changed by someone with the store setup permission.</p>}
        </div>
      )}
      <FormError error={error ?? save.error} />
    </Dialog>
  );
}

/** A list sorted by bin (put-away, pick list, count sheet) with a Print button. */
export function BinListDialog({
  list,
  loading,
  error,
  title,
  quantityHeader = 'Quantity',
  onClose,
}: {
  list: BinList | undefined;
  loading?: boolean;
  error?: unknown;
  title?: string;
  quantityHeader?: string;
  onClose(): void;
}) {
  return (
    <Dialog
      open
      width="xl"
      title={title ?? list?.title ?? 'List'}
      onClose={onClose}
      footer={
        <>
          <Button icon={Printer} shortcut="Ctrl+P" onClick={() => window.print()} disabled={!list}>
            Print
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Close
          </Button>
        </>
      }
    >
      <BinListTable rows={list?.rows} loading={loading} error={error} quantityHeader={quantityHeader} />
    </Dialog>
  );
}

export function BinListTable({ rows, loading, error, quantityHeader = 'Quantity' }: { rows: BinListRow[] | undefined; loading?: boolean; error?: unknown; quantityHeader?: string }) {
  return (
    <DataTable<BinListRow>
      rows={rows}
      loading={loading}
      error={error}
      rowKey={(r, i) => `${r.plu}-${r.batchNumber ?? ''}-${i}`}
      caption="Bin list"
      maxHeight="55vh"
      empty={<EmptyState title="Nothing on this list" />}
      columns={[
        { key: 'zone', header: 'Zone', cell: (r) => r.zone ?? '', hideBelow: 'md' },
        { key: 'bin', header: 'Bin', cell: (r) => <span className="font-semibold">{r.bin ?? 'No bin'}</span> },
        { key: 'plu', header: 'Stock code', cell: (r) => r.plu },
        { key: 'desc', header: 'Description', cell: (r) => r.description, className: 'wrap min-w-[160px]', hideBelow: 'sm' },
        { key: 'batch', header: 'Batch', cell: (r) => r.batchNumber ?? '' },
        { key: 'exp', header: 'Expires', cell: (r) => formatDate(r.expiresOn), hideBelow: 'md' },
        { key: 'qty', header: quantityHeader, numeric: true, cell: (r) => (quantityHeader === 'Counted' ? '' : formatQuantity(r.quantity)) },
        { key: 'oh', header: 'On hand', numeric: true, cell: (r) => formatQuantity(r.onHand), hideBelow: 'lg' },
        { key: 'doc', header: 'For', cell: (r) => r.document ?? '', hideBelow: 'lg' },
      ]}
    />
  );
}
