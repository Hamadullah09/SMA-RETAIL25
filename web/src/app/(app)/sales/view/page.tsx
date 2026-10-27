'use client';

import { useQuery } from '@tanstack/react-query';
import { ArrowLeft, Printer } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense } from 'react';
import { Button, LinkButton } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Chip, Detail, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { api } from '@/lib/api';
import { useHotkey } from '@/lib/hotkeys';
import { formatLocalDateTime, formatMoney, formatQuantity } from '@/lib/format';
import { useStore } from '@/lib/store';
import type { SaleLine, SalePayment } from '@/lib/types';

export default function SalePage() {
  return (
    <Suspense>
      <Sale />
    </Suspense>
  );
}

function Sale() {
  const id = useSearchParams().get('id');
  const router = useRouter();
  useHotkey('Esc', () => router.push('/sales/'));
  const symbol = useStore().currencySymbol;
  const query = useQuery({ queryKey: ['sale', id], queryFn: () => api.sale(id!), enabled: !!id });
  const sale = query.data;
  const money = (n: number) => formatMoney(n, symbol);

  const back = (
    <LinkButton href="/sales/" icon={ArrowLeft} shortcut="Esc">
      Sales Journal
    </LinkButton>
  );

  if (!id) {
    return (
      <div className="card">
        <EmptyState title="No sale chosen">Open a sale from the Sales Journal.</EmptyState>
        <div className="flex justify-center pb-8">{back}</div>
      </div>
    );
  }
  if (query.error) {
    return (
      <div>
        <Toolbar>{back}</Toolbar>
        <div className="card">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      </div>
    );
  }

  return (
    <div>
      <Toolbar>
        {back}
        <Button icon={Printer} shortcut="Ctrl+P" onClick={() => window.print()} disabled={!sale}>
          Print
        </Button>
      </Toolbar>

      <header className="mb-4 flex flex-wrap items-center gap-x-4 gap-y-2">
        {sale ? <h1 className="page-title">Sale {sale.invoiceNumber}</h1> : <Skeleton className="h-10 w-64" />}
        {sale && (sale.voided ? <Chip tone="danger">Voided</Chip> : <Chip tone="ok">Completed</Chip>)}
      </header>

      {!sale ? (
        <Skeleton className="h-[50vh] rounded-card" />
      ) : (
        <div className="space-y-4">
          <Card>
            <dl className="grid grid-cols-2 gap-x-6 gap-y-4 md:grid-cols-3 xl:grid-cols-6">
              <Detail label="Date">{formatLocalDateTime(sale.at)}</Detail>
              <Detail label="Till">{`${sale.storeCode} · ${String(sale.stationNumber).padStart(3, '0')}`}</Detail>
              <Detail label="Staff">{sale.staff}</Detail>
              <Detail label="Client">{sale.client}</Detail>
              <Detail label="Paid by">{sale.tenders}</Detail>
              <Detail label="Units">{formatQuantity(sale.units)}</Detail>
            </dl>
            {sale.voided && (
              <p className="mt-4 rounded-control bg-danger-light px-3 py-2 font-semibold text-danger-ink">
                Voided{sale.voidedBy ? ` by ${sale.voidedBy}` : ''}
                {sale.voidedAt ? ` on ${formatLocalDateTime(sale.voidedAt)}` : ''}. It does not count in the totals.
              </p>
            )}
          </Card>

          <section>
            <h2 className="section-title mb-2 px-1">Lines</h2>
            <DataTable<SaleLine>
              rows={sale.lines}
              rowKey={(l) => String(l.lineNumber)}
              caption="Lines"
              showTotals
              columns={[
                { key: 'n', header: '#', numeric: true, cell: (l) => l.lineNumber, className: 'w-12', total: '' },
                { key: 'plu', header: 'Stock code', cell: (l) => l.plu ?? '', hideBelow: 'sm' },
                { key: 'desc', header: 'Description', cell: (l) => l.description ?? '', total: 'Total' },
                { key: 'qty', header: 'Qty', numeric: true, cell: (l) => formatQuantity(l.quantity), total: formatQuantity(sale.lines.reduce((s, l) => s + l.quantity, 0)) },
                { key: 'price', header: 'Price', numeric: true, cell: (l) => money(l.price), hideBelow: 'md' },
                { key: 'disc', header: 'Discount', numeric: true, cell: (l) => (l.discount ? money(-l.discount) : ''), hideBelow: 'md', total: money(-sale.lines.reduce((s, l) => s + l.discount, 0)) },
                { key: 'tax', header: 'Tax', numeric: true, cell: (l) => money(l.tax), hideBelow: 'lg', total: money(sale.tax) },
                { key: 'total', header: 'Total', numeric: true, cell: (l) => money(l.total), total: money(sale.netSales) },
              ]}
            />
          </section>

          <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,1fr)_380px]">
            <section>
              <h2 className="section-title mb-2 px-1">Payments</h2>
              <DataTable<SalePayment>
                rows={sale.payments}
                rowKey={(p, i) => `${p.tender}-${i}`}
                caption="Payments"
                showTotals
                columns={[
                  { key: 'tender', header: 'Tender', cell: (p) => p.tender },
                  { key: 'foreign', header: 'Foreign amount', numeric: true, cell: (p) => (p.currency && p.foreignAmount !== null ? `${p.currency} ${formatMoney(p.foreignAmount)}` : ''), hideBelow: 'sm' },
                  { key: 'amount', header: 'Amount', numeric: true, cell: (p) => money(p.amount), total: money(sale.payments.reduce((s, p) => s + p.amount, 0)) },
                ]}
              />
            </section>
            <Card title="Totals" className="self-start">
              <dl className="space-y-2 text-lead">
                <div className="flex justify-between gap-4">
                  <dt className="text-muted">Subtotal</dt>
                  <dd className="tabular-nums">{money(sale.netSales)}</dd>
                </div>
                <div className="flex justify-between gap-4">
                  <dt className="text-muted">Tax</dt>
                  <dd className="tabular-nums">{money(sale.tax)}</dd>
                </div>
                <div className="flex justify-between gap-4 border-t border-line pt-2 font-heading text-value">
                  <dt>Total</dt>
                  <dd className="tabular-nums">{money(sale.total)}</dd>
                </div>
              </dl>
            </Card>
          </div>
        </div>
      )}
    </div>
  );
}
