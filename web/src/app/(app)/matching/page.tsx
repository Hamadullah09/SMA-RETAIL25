'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Check, RefreshCw, Settings2, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { SETTINGS_FIELDS, SupplySettingsDialog } from '@/components/supply';
import { Button } from '@/components/ui/button';
import { Dialog, SidePanel } from '@/components/ui/dialog';
import { Field, Select, TextArea } from '@/components/ui/field';
import { Chip, Detail, PageHeader, Pager, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { cx } from '@/lib/cx';
import { formatCount, formatDate, formatMoney, formatQuantity, formatShortDateTime, formatUtc } from '@/lib/format';
import { words } from '@/lib/labels';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import { MATCH_STATUS } from '@/lib/supply';
import type { MatchReportRow, MatchRow } from '@/lib/types';

type Tab = 'lines' | 'not-billed' | 'not-received';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'lines', label: 'Bill lines' },
  { key: 'not-billed', label: 'Received, not billed' },
  { key: 'not-received', label: 'Billed, not received' },
];

const PAGE_SIZE = 50;

export default function MatchingPage() {
  const { storeId, defaultStoreId } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [tab, setTab] = useState<Tab>('lines');
  const [settings, setSettings] = useState(false);
  const run = useMutation({
    mutationFn: () => api.runMatch(storeId),
    onSuccess: (r) => {
      toast.ok(`${r.lines} bill lines checked: ${r.matched} matched, ${r.held} held${r.posted ? `, ${r.posted} price differences posted` : ''}.`);
      void queryClient.invalidateQueries({ queryKey: ['matches'] });
      void queryClient.invalidateQueries({ queryKey: ['match-report'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <div>
      <PageHeader title="Matching" />
      <p className="mb-3 px-1 text-ink">
        Each supplier bill line is checked against what was ordered and received. Lines outside the tolerances are held until someone who may approve purchases accepts or rejects them.
      </p>
      <Toolbar>
        <Button icon={RefreshCw} shortcut="F5" busy={run.isPending} onClick={() => run.mutate()}>
          Check the bills again
        </Button>
        {defaultStoreId && (
          <Button icon={Settings2} onClick={() => setSettings(true)}>
            Tolerances
          </Button>
        )}
      </Toolbar>
      <Tabs label="Matching" tabs={TABS} value={tab} onChange={setTab} className="mb-4" />
      {tab === 'lines' && <BillLines />}
      {tab !== 'lines' && <Report billed={tab === 'not-received'} />}
      {settings && defaultStoreId && <SupplySettingsDialog storeId={defaultStoreId} fields={SETTINGS_FIELDS.matching} title="Match tolerances" onClose={() => setSettings(false)} />}
    </div>
  );
}

function BillLines() {
  const { storeId, currencySymbol: symbol } = useStore();
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<MatchRow | null>(null);
  useEffect(() => setPage(1), [storeId, status]);
  const query = useQuery({
    queryKey: ['matches', storeId, status, page],
    queryFn: ({ signal }) => api.matches({ storeId, status, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  return (
    <>
      <Toolbar>
        <label className="sr-only" htmlFor="match-status">
          Status
        </label>
        <Select id="match-status" className="w-[200px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every bill line</option>
          {Object.entries(MATCH_STATUS).map(([key, s]) => (
            <option key={key} value={key}>
              {s.label}
            </option>
          ))}
        </Select>
      </Toolbar>
      <DataTable<MatchRow>
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(m) => m.id}
        onRowClick={setOpen}
        caption="Bill lines"
        empty={<EmptyState title="No bill lines checked yet">Supplier bills that name a purchase order are checked here as they are entered.</EmptyState>}
        columns={[
          { key: 'status', header: 'Status', cell: (m) => <Chip tone={MATCH_STATUS[m.status]?.tone ?? 'plain'}>{MATCH_STATUS[m.status]?.label ?? words(m.status)}</Chip> },
          { key: 'bill', header: 'Bill', cell: (m) => <span className="font-semibold">{m.billNumber ?? ''}</span> },
          { key: 'po', header: 'Order', cell: (m) => String(m.purchaseOrderNumber), hideBelow: 'md' },
          { key: 'supplier', header: 'Supplier', cell: (m) => m.supplier ?? '', hideBelow: 'xl' },
          { key: 'item', header: 'Item', cell: (m) => `${m.plu} ${m.description}`, className: 'wrap min-w-[160px]' },
          { key: 'rcv', header: 'Received', numeric: true, cell: (m) => formatQuantity(m.quantityReceived), hideBelow: 'lg' },
          { key: 'billed', header: 'Billed', numeric: true, cell: (m) => formatQuantity(m.quantityBilled) },
          { key: 'ordered-cost', header: 'Ordered at', numeric: true, cell: (m) => formatMoney(m.orderedCost, symbol), hideBelow: 'lg' },
          { key: 'billed-cost', header: 'Billed at', numeric: true, cell: (m) => formatMoney(m.billedCost, symbol), hideBelow: 'sm' },
          {
            key: 'diff',
            header: 'Difference',
            numeric: true,
            cell: (m) => <span className={cx(m.priceDifference > 0 && 'font-semibold text-danger', m.priceDifference < 0 && 'font-semibold text-ok')}>{formatMoney(m.priceDifference, symbol)}</span>,
            hideBelow: 'md',
          },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="bill lines" />}
      />
      {open && <MatchPanel row={open} onClose={() => setOpen(null)} />}
    </>
  );
}

function MatchPanel({ row, onClose }: { row: MatchRow; onClose(): void }) {
  const { can } = useSession();
  const symbol = useStore().currencySymbol;
  const toast = useToast();
  const queryClient = useQueryClient();
  const [current, setCurrent] = useState(row);
  const [deciding, setDeciding] = useState<'accept' | 'reject' | null>(null);
  const mayDecide = can(P.purchasingApprove) && current.status === 'Held';
  const m = current;
  return (
    <SidePanel
      open
      title={`Bill ${m.billNumber ?? ''}, ${m.plu}`}
      subtitle={`Order ${m.purchaseOrderNumber} · ${m.storeCode}${m.supplier ? ' · ' + m.supplier : ''}`}
      onClose={onClose}
      footer={
        mayDecide && (
          <>
            <Button icon={X} shortcut="Alt+R" onClick={() => setDeciding('reject')}>
              Reject
            </Button>
            <Button variant="sun" icon={Check} shortcut="Ctrl+Enter" onClick={() => setDeciding('accept')}>
              Accept
            </Button>
          </>
        )
      }
    >
      <div className="space-y-4">
        <p>
          <Chip tone={MATCH_STATUS[m.status]?.tone ?? 'plain'}>{MATCH_STATUS[m.status]?.label ?? words(m.status)}</Chip>
        </p>
        {m.reason && (
          <p className="rounded-control bg-danger-light px-3 py-2 font-semibold text-danger-ink" role="note">
            {m.reason}
          </p>
        )}
        <dl className="card grid grid-cols-2 gap-4 p-5 md:grid-cols-3">
          <Detail label="Bill date">{formatDate(m.billDate)}</Detail>
          <Detail label="Ordered">{formatQuantity(m.quantityOrdered)}</Detail>
          <Detail label="Received">{formatQuantity(m.quantityReceived)}</Detail>
          <Detail label="Billed">{formatQuantity(m.quantityBilled)}</Detail>
          <Detail label="Billed and received">{formatQuantity(m.quantityMatched)}</Detail>
          <Detail label="Ordered at">{formatMoney(m.orderedCost, symbol)}</Detail>
          <Detail label="Received at">{formatMoney(m.receivedCost, symbol)}</Detail>
          <Detail label="Billed at">{formatMoney(m.billedCost, symbol)}</Detail>
          <Detail label="Price difference">{formatMoney(m.priceDifference, symbol)}</Detail>
          <Detail label="Checked">{formatUtc(m.checkedAtUtc)}</Detail>
          <Detail label="Decided by">{m.decidedBy}</Detail>
          <Detail label="Decided">{formatUtc(m.decidedAtUtc)}</Detail>
          {m.decisionNote && (
            <Detail label="Note" className="col-span-2 md:col-span-3">
              {m.decisionNote}
            </Detail>
          )}
        </dl>
        {m.status === 'Held' && !can(P.purchasingApprove) && <p className="text-muted">Someone who may approve purchases accepts or rejects a held line.</p>}
        <p className="text-muted">An accepted price difference goes to the cost of the stock still on hand, and to cost of goods for what was sold.</p>
      </div>
      {deciding && (
        <DecideDialog
          accept={deciding === 'accept'}
          onClose={() => setDeciding(null)}
          onDecide={async (note) => {
            const saved = deciding === 'accept' ? await api.acceptMatch(m.id, { version: m.version, note }) : await api.rejectMatch(m.id, { version: m.version, note });
            setCurrent(saved);
            setDeciding(null);
            void queryClient.invalidateQueries({ queryKey: ['matches'] });
            toast.ok(deciding === 'accept' ? 'The bill line is accepted.' : 'The bill line is rejected: the supplier should correct the bill.');
          }}
        />
      )}
    </SidePanel>
  );
}

function DecideDialog({ accept, onClose, onDecide }: { accept: boolean; onClose(): void; onDecide(note: string | null): Promise<void> }) {
  const [note, setNote] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  return (
    <Dialog
      open
      width="sm"
      title={accept ? 'Accept the bill line' : 'Reject the bill line'}
      onClose={onClose}
      onSubmit={async () => {
        if (!accept && !note.trim()) return setError(new Error('Say why it is rejected.'));
        setBusy(true);
        try {
          await onDecide(note.trim() || null);
        } catch (e) {
          setError(e instanceof ApiError && e.isConflict ? new Error('Someone else decided on this line. Close and look again.') : e);
        } finally {
          setBusy(false);
        }
      }}
      footer={
        <>
          <Button type="submit" variant={accept ? 'sun' : 'danger'} icon={accept ? Check : X} shortcut="Enter" busy={busy}>
            {accept ? 'Accept' : 'Reject'}
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <Field label={accept ? 'Note (optional)' : 'Why'} htmlFor="decide-note" stacked>
        <TextArea id="decide-note" maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} />
      </Field>
      <FormError error={error} />
    </Dialog>
  );
}

function Report({ billed }: { billed: boolean }) {
  const { storeId, currencySymbol: symbol } = useStore();
  const query = useQuery({
    queryKey: ['match-report', billed, storeId],
    queryFn: () => (billed ? api.billedNotReceived({ storeId }) : api.receivedNotBilled({ storeId })),
  });
  const rows = query.data;
  return (
    <>
      {rows && (
        <p className="mb-3 px-1 text-ink" role="status">
          {formatCount(rows.length)} order lines, worth {formatMoney(rows.reduce((s, r) => s + r.value, 0), symbol)}.
        </p>
      )}
      <DataTable<MatchReportRow>
        rows={rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(r) => r.purchaseOrderLineId}
        caption={billed ? 'Billed but not received' : 'Received but not billed'}
        showTotals={!!rows && rows.length > 1}
        empty={<EmptyState title={billed ? 'Nothing is billed before it arrived' : 'Everything received is billed'} />}
        columns={[
          { key: 'po', header: 'Order', cell: (r) => <span className="font-semibold">{r.purchaseOrderNumber}</span> },
          { key: 'store', header: 'Store', cell: (r) => r.storeCode, hideBelow: 'xl' },
          { key: 'supplier', header: 'Supplier', cell: (r) => r.supplier ?? '', hideBelow: 'lg' },
          { key: 'item', header: 'Item', cell: (r) => `${r.plu} ${r.description}`, className: 'wrap min-w-[160px]' },
          { key: 'received-at', header: 'Received', cell: (r) => formatShortDateTime(r.receivedAt), hideBelow: 'xl' },
          { key: 'ordered', header: 'Ordered', numeric: true, cell: (r) => formatQuantity(r.ordered), hideBelow: 'md' },
          { key: 'received', header: 'Received', numeric: true, cell: (r) => formatQuantity(r.received) },
          { key: 'billed', header: 'Billed', numeric: true, cell: (r) => formatQuantity(r.billed) },
          { key: 'value', header: 'Value', numeric: true, cell: (r) => formatMoney(r.value, symbol), total: formatMoney((rows ?? []).reduce((s, r) => s + r.value, 0), symbol) },
        ]}
      />
    </>
  );
}
