'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ClipboardCheck, ClipboardList, Settings2 } from 'lucide-react';
import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { LocationSelect, SETTINGS_FIELDS, SupplySettingsDialog } from '@/components/supply';
import { Button } from '@/components/ui/button';
import { Checkbox, Input, Select } from '@/components/ui/field';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { formatCount, formatMoney, formatQuantity } from '@/lib/format';
import { parseNumber } from '@/lib/forms';
import { useStore } from '@/lib/store';
import { ordersFor, reorderLines } from '@/lib/supply';
import type { OrderMade, ReorderRow } from '@/lib/types';

export default function ReorderPage() {
  const { defaultStoreId, currencySymbol: symbol } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [locationId, setLocationId] = useState<string | null>(null);
  const [supplierId, setSupplierId] = useState('');
  const [all, setAll] = useState(false);
  const [chosen, setChosen] = useState<Set<string>>(new Set());
  const [pieces, setPieces] = useState<Record<string, string>>({});
  const [submit, setSubmit] = useState(true);
  const [made, setMade] = useState<{ orders?: OrderMade[]; requisition?: { id: string; number: number } } | null>(null);
  const [settings, setSettings] = useState(false);

  const suppliers = useQuery({ queryKey: ['suppliers', 'all'], queryFn: () => api.suppliers({ pageSize: 500 }), staleTime: 5 * 60_000 });
  const query = useQuery({
    queryKey: ['reorder', locationId, supplierId, all],
    queryFn: () => api.reorder({ locationId, supplierId, all: all || undefined }),
    enabled: !!locationId,
  });
  const rows = useMemo(() => query.data?.rows ?? [], [query.data]);
  // What needs ordering is ticked to begin with.
  useEffect(() => setChosen(new Set(rows.filter((r) => r.suggestedPieces > 0).map((r) => r.itemId))), [rows]);
  useEffect(() => setPieces({}), [locationId, supplierId, all]);

  const lines = reorderLines(rows, chosen, pieces);
  const value = lines.reduce((s, l) => s + l.pieces * (rows.find((r) => r.itemId === l.itemId)?.costEach ?? 0), 0);
  const done = () => {
    void queryClient.invalidateQueries({ queryKey: ['reorder'] });
    void queryClient.invalidateQueries({ queryKey: ['purchase-orders'] });
    void queryClient.invalidateQueries({ queryKey: ['requisitions'] });
  };
  const toOrders = useMutation({
    mutationFn: () => api.reorderToOrders({ locationId, lines }, defaultStoreId),
    onSuccess: (result) => {
      setMade({ orders: result.orders });
      toast.ok(`${result.orders.length} draft purchase orders are made.`);
      done();
    },
    onError: (e: Error) => toast.error(e.message),
  });
  const toRequisition = useMutation({
    mutationFn: () => api.reorderToRequisition({ locationId, lines, submit }, defaultStoreId),
    onSuccess: (r) => {
      setMade({ requisition: { id: r.id, number: r.number } });
      toast.ok(`Requisition ${r.number} is made${submit ? ' and sent for approval' : ''}.`);
      done();
    },
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <div>
      <PageHeader title="Reorder suggestions" aside={query.data && `${formatCount(rows.filter((r) => r.suggestedPieces > 0).length)} items to order`} />
      <p className="mb-3 px-1 text-ink">
        From the average daily sales, the supplier’s lead time and the safety days, against the stock on hand and on order. An item’s reorder point is never undercut. Quantities are in whole cases.
      </p>
      <Toolbar>
        <LocationSelect storeId={defaultStoreId} value={locationId} onChange={setLocationId} />
        <label className="sr-only" htmlFor="reorder-supplier">
          Supplier
        </label>
        <Select id="reorder-supplier" className="w-[220px]" value={supplierId} onChange={(e) => setSupplierId(e.target.value)}>
          <option value="">Every supplier</option>
          {(suppliers.data?.items ?? []).map((s) => (
            <option key={s.id} value={s.id}>
              {s.company}
            </option>
          ))}
        </Select>
        <Checkbox label="Items with nothing to order too" checked={all} onChange={setAll} />
        {defaultStoreId && (
          <Button icon={Settings2} onClick={() => setSettings(true)}>
            Reorder settings
          </Button>
        )}
      </Toolbar>
      <DataTable<ReorderRow>
        rows={locationId ? query.data?.rows : []}
        loading={!!locationId && query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => r.itemId}
        caption="Reorder suggestions"
        showTotals={rows.length > 1}
        empty={<EmptyState title="Nothing needs ordering">Every item has enough on hand and on order for its lead time and safety days.</EmptyState>}
        columns={[
          {
            key: 'pick',
            header: <span className="sr-only">Order it</span>,
            cell: (r) => (
              <input
                type="checkbox"
                className="check"
                aria-label={`Order ${r.plu}`}
                checked={chosen.has(r.itemId)}
                onChange={(e) =>
                  setChosen((s) => {
                    const next = new Set(s);
                    if (e.target.checked) next.add(r.itemId);
                    else next.delete(r.itemId);
                    return next;
                  })
                }
              />
            ),
          },
          { key: 'plu', header: 'Stock code', cell: (r) => <span className="font-semibold">{r.plu}</span> },
          { key: 'name', header: 'Item', cell: (r) => r.name, className: 'wrap min-w-[140px]', hideBelow: 'sm' },
          { key: 'supplier', header: 'Supplier', cell: (r) => r.supplier ?? '', hideBelow: 'lg' },
          { key: 'daily', header: 'A day', numeric: true, cell: (r) => formatQuantity(r.averageDailySales), hideBelow: 'md' },
          { key: 'lead', header: 'Lead days', numeric: true, cell: (r) => formatCount(r.leadTimeDays), hideBelow: 'xl' },
          { key: 'onhand', header: 'On hand', numeric: true, cell: (r) => formatQuantity(r.onHand), hideBelow: 'sm' },
          { key: 'onorder', header: 'On order', numeric: true, cell: (r) => formatQuantity(r.onOrder), hideBelow: 'lg' },
          { key: 'level', header: 'Reorder at', numeric: true, cell: (r) => formatQuantity(r.reorderLevel), hideBelow: 'xl' },
          { key: 'case', header: 'Case', numeric: true, cell: (r) => formatQuantity(r.caseQuantity), hideBelow: '2xl' },
          {
            key: 'pieces',
            header: 'Pieces',
            numeric: true,
            cell: (r) => (
              <Input
                aria-label={`Pieces of ${r.plu}`}
                inputMode="decimal"
                className="ml-auto h-10 w-[90px] text-right"
                value={pieces[r.itemId] ?? String(r.suggestedPieces)}
                onChange={(e) => setPieces((p) => ({ ...p, [r.itemId]: e.target.value }))}
              />
            ),
          },
          {
            key: 'value',
            header: 'Value',
            numeric: true,
            cell: (r) => formatMoney((parseNumber(pieces[r.itemId] ?? String(r.suggestedPieces)) ?? 0) * r.costEach, symbol),
            total: formatMoney(value, symbol),
          },
        ]}
      />
      <Toolbar className="mt-4">
        <Button variant="sun" icon={ClipboardList} shortcut="Ctrl+Enter" busy={toOrders.isPending} disabled={lines.length === 0} onClick={() => toOrders.mutate()}>
          {lines.length ? `Make ${ordersFor(lines)} purchase orders` : 'Make purchase orders'}
        </Button>
        <Button icon={ClipboardCheck} shortcut="Ctrl+Shift+R" busy={toRequisition.isPending} disabled={lines.length === 0} onClick={() => toRequisition.mutate()}>
          Make a requisition
        </Button>
        <Checkbox label="Send it for approval" checked={submit} onChange={setSubmit} />
      </Toolbar>
      {made && (
        <div className="card mt-3 p-4" role="status">
          {made.orders && (
            <>
              <p className="font-semibold">Draft purchase orders made:</p>
              <ul className="mt-1 list-disc pl-6">
                {made.orders.map((o) => (
                  <li key={o.id}>
                    Order {o.number}
                    {o.supplier ? ` · ${o.supplier}` : ''}
                  </li>
                ))}
              </ul>
              <Link className="mt-2 inline-block font-semibold underline" href="/purchase-orders/">
                Open purchase orders
              </Link>
            </>
          )}
          {made.requisition && (
            <p>
              Requisition {made.requisition.number} is made.{' '}
              <Link className="font-semibold underline" href={`/requisitions/?id=${encodeURIComponent(made.requisition.id)}`}>
                Open it
              </Link>
            </p>
          )}
        </div>
      )}
      {settings && defaultStoreId && <SupplySettingsDialog storeId={defaultStoreId} fields={SETTINGS_FIELDS.reorder} title="Reorder settings" onClose={() => setSettings(false)} />}
    </div>
  );
}
