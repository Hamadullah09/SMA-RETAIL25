'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Chip, Detail, PageHeader, Pager, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { api } from '@/lib/api';
import { cx } from '@/lib/cx';
import { formatCount, formatDate, formatMoney, formatShortDateTime } from '@/lib/format';
import { useDebounced } from '@/lib/hooks';
import { GIFT_CARD_HISTORY, GIFT_CARD_KINDS, label } from '@/lib/labels';
import { useStore } from '@/lib/store';
import type { GiftCardHistoryLine, GiftCardRow } from '@/lib/types';

const PAGE_SIZE = 50;

export default function GiftCardsPage() {
  const symbol = useStore().currencySymbol;
  const [text, setText] = useState('');
  const [active, setActive] = useState(true);
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<GiftCardRow | null>(null);
  const search = useDebounced(text.trim());
  useEffect(() => setPage(1), [search, active]);

  const query = useQuery({
    queryKey: ['gift-cards', { search, active }, page],
    queryFn: ({ signal }) => api.giftCards({ search, active: active || undefined, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  const rows = query.data?.items;
  const onThem = (rows ?? []).reduce((s, g) => s + g.balance, 0);

  return (
    <div>
      <PageHeader title="Gift cards" aside={query.data && `${formatCount(query.data.total)} cards · ${formatMoney(onThem, symbol)} on those shown`} />
      <Toolbar>
        <SearchBox value={text} onChange={setText} placeholder="Card number or client" label="Card number or client" />
        <label className="flex cursor-pointer items-center gap-2.5 px-1">
          <input type="checkbox" className="check" checked={active} onChange={(e) => setActive(e.target.checked)} />
          Active only
        </label>
      </Toolbar>
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
        <DataTable<GiftCardRow>
          className="self-start"
          rows={rows}
          loading={query.isLoading}
          error={query.error}
          onRetry={() => void query.refetch()}
          rowKey={(g) => g.id}
          selectedKey={selected?.id}
          onRowClick={setSelected}
          caption="Gift cards"
          empty={<EmptyState title="No cards found">{search ? 'Nothing matches the number or client.' : 'Cards are sold at the tills.'}</EmptyState>}
          columns={[
            { key: 'n', header: 'Number', cell: (g) => <span className="whitespace-nowrap font-semibold">{g.number}</span> },
            { key: 'bal', header: 'Balance', numeric: true, cell: (g) => formatMoney(g.balance, symbol) },
            { key: 'kind', header: 'Kind', cell: (g) => label(GIFT_CARD_KINDS, g.kind), hideBelow: 'md' },
            { key: 'client', header: 'Client', cell: (g) => g.client ?? '', hideBelow: 'lg' },
            { key: 'issued', header: 'Issued', cell: (g) => formatDate(g.issuedAt.slice(0, 10)), hideBelow: 'sm' },
            { key: 'exp', header: 'Expires', cell: (g) => (g.expiresOn ? formatDate(g.expiresOn) : 'Never'), hideBelow: 'xl' },
            { key: 'active', header: 'Status', cell: (g) => (g.isActive ? <Chip tone="ok">Active</Chip> : <Chip>Inactive</Chip>), hideBelow: 'md' },
          ]}
          footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="cards" />}
        />
        <CardHistory card={selected} />
      </div>
    </div>
  );
}

function CardHistory({ card }: { card: GiftCardRow | null }) {
  const symbol = useStore().currencySymbol;
  const query = useQuery({ queryKey: ['gift-card', card?.id], queryFn: () => api.giftCard(card!.id), enabled: !!card });
  const detail = query.data;

  if (!card) {
    return (
      <div className="card self-start">
        <EmptyState title="Choose a card">Its balance and history show here: when it was sold, reloaded and spent, and at which till.</EmptyState>
      </div>
    );
  }
  return (
    <section className="self-start" aria-label="Card history">
      <div className="card mb-4 p-5">
        <p className="text-muted">Balance</p>
        <p className="font-heading text-big tabular-nums">{formatMoney(detail?.balance ?? card.balance, symbol)}</p>
        <dl className="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-4">
          <Detail label="Card">{card.number}</Detail>
          <Detail label="Kind">{label(GIFT_CARD_KINDS, card.kind)}</Detail>
          <Detail label="Sold at">{card.storeCode}</Detail>
          <Detail label="Expires">{card.expiresOn ? formatDate(card.expiresOn) : 'Never'}</Detail>
        </dl>
      </div>
      <h2 className="section-title mb-2 px-1">History</h2>
      {query.error ? (
        <div className="card">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      ) : !detail ? (
        <Skeleton className="h-48 rounded-card" />
      ) : (
        <DataTable<GiftCardHistoryLine>
          rows={detail.history}
          rowKey={(h, i) => `${h.at}-${i}`}
          caption="History"
          empty={<p className="p-4 text-muted">Nothing happened on this card yet.</p>}
          columns={[
            { key: 'at', header: 'When', cell: (h) => formatShortDateTime(h.at) },
            { key: 'kind', header: 'What', cell: (h) => label(GIFT_CARD_HISTORY, h.kind) },
            {
              key: 'amount',
              header: 'Amount',
              numeric: true,
              cell: (h) => <span className={cx('font-semibold', h.amount < 0 ? 'text-danger' : 'text-ok')}>{(h.amount > 0 ? '+' : '') + formatMoney(h.amount, symbol)}</span>,
            },
            { key: 'after', header: 'Balance', numeric: true, cell: (h) => formatMoney(h.balanceAfter, symbol), hideBelow: 'sm' },
            { key: 'where', header: 'Till', cell: (h) => (h.storeCode ? `${h.storeCode}${h.stationNumber ? ` · ${String(h.stationNumber).padStart(3, '0')}` : ''}` : ''), hideBelow: 'md' },
            { key: 'inv', header: 'Invoice', cell: (h) => h.invoiceNumber ?? '', hideBelow: 'lg' },
            { key: 'by', header: 'By', cell: (h) => h.user ?? '', hideBelow: 'lg' },
          ]}
        />
      )}
      <p className="mt-2 px-1 text-ink">The server knows every till&apos;s cards once they have uploaded, so this balance counts what was spent at any store.</p>
    </section>
  );
}
