'use client';

import { useQuery } from '@tanstack/react-query';
import { BookOpenText, Plus, ReceiptText, Wallet } from 'lucide-react';
import Link from 'next/link';
import type { ReactNode } from 'react';
import { LINK, useMoney } from '@/components/finance';
import { LinkButton } from '@/components/ui/button';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { ErrorState, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { api } from '@/lib/api';
import { cx } from '@/lib/cx';
import { label } from '@/lib/finance';
import { formatCount, formatDate } from '@/lib/format';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { BankAccountRow } from '@/lib/types';

/** The finance home: cash and bank, what is owed to suppliers and by clients, this month's profit, and what needs doing. */
export default function FinanceHomePage() {
  const { can } = useSession();
  const { storeId, store } = useStore();
  const money = useMoney();
  const home = useQuery({ queryKey: ['finance-home', storeId], queryFn: () => api.financeHome({ storeId }) });
  const h = home.data;
  const bankTotal = h?.accounts.filter((a) => a.isActive).reduce((sum, a) => sum + a.balance, 0);

  return (
    <div>
      <PageHeader title="Finance" aside={`${store ? `${store.code} ${store.name}` : 'All stores'}${h ? ` · ${formatDate(h.today)}` : ''}`} />
      <Toolbar>
        {can(P.financePayables) && (
          <LinkButton href="/finance/bills/edit/" icon={ReceiptText} variant="sun">
            New bill
          </LinkButton>
        )}
        {can(P.financePost) && (
          <LinkButton href="/finance/expenses/?new=1" icon={Wallet}>
            New expense
          </LinkButton>
        )}
        {can(P.financePost) && (
          <LinkButton href="/finance/journals/edit/" icon={BookOpenText}>
            New journal entry
          </LinkButton>
        )}
        {can(P.financePayables) && (
          <LinkButton href="/finance/payments/?run=1" icon={Plus}>
            Pay bills
          </LinkButton>
        )}
      </Toolbar>

      {home.error ? (
        <div className="card">
          <ErrorState error={home.error} onRetry={() => void home.refetch()} />
        </div>
      ) : (
        <>
          <div className="mb-4 grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Tile title="Cash in hand" value={h && money(h.cashInHand)} dark>
              Till cash not yet taken to the bank
            </Tile>
            <Tile title="Bank and cash accounts" value={h && money(bankTotal)}>
              {h && `${formatCount(h.accounts.length)} accounts`} · <Link className={LINK} href="/finance/bank/">Bank and cash</Link>
            </Tile>
            <Tile title="Payables due this week" value={h && money(h.payablesDue)}>
              {h && `${formatCount(h.billsDue)} bills, ${money(h.payablesOverdue)} overdue · ${money(h.payablesOpen)} owed in all`}
            </Tile>
            <Tile title="Receivables" value={h && money(h.receivables)}>
              Owed by clients on account
            </Tile>
            <Tile title="This month's profit" value={h && money(h.monthProfit)} dark>
              {h && `Income ${money(h.monthIncome)} · expenses ${money(h.monthExpenses)} since ${formatDate(h.monthFrom)}`}
            </Tile>
            <Tile title="In suspense" value={h && money(h.suspense)}>
              {h && h.suspense !== 0 ? (
                <Link className={LINK} href="/finance/suspense/">
                  Fix the account mapping
                </Link>
              ) : (
                'Every amount found its account'
              )}
            </Tile>
            <Tile title="Waiting for approval" value={h && formatCount(h.draftJournals)}>
              <Link className={LINK} href="/finance/journals/?status=Draft">
                Journal entries made by hand
              </Link>
            </Tile>
            <Tile title="FBR" value={h && (h.fiscalEnabled ? `${formatCount(h.fiscalPending)} waiting` : 'Off')}>
              {h && (h.fiscalEnabled ? `${formatCount(h.fiscalFailed)} refused or unanswered` : 'Sales are not reported to FBR')}
            </Tile>
          </div>
          <DataTable<BankAccountRow>
            caption="Bank and cash accounts"
            rows={h?.accounts}
            loading={home.isLoading}
            rowKey={(a) => a.id}
            columns={[
              { key: 'name', header: 'Account', cell: (a) => <span className="font-semibold">{a.name}</span> },
              { key: 'kind', header: 'Kind', cell: (a) => label(a.kind), hideBelow: 'sm' },
              { key: 'bank', header: 'Bank', cell: (a) => a.bankName ?? '', hideBelow: 'md' },
              { key: 'store', header: 'Store', cell: (a) => a.storeCode ?? 'Company', hideBelow: 'md' },
              { key: 'currency', header: 'Currency', cell: (a) => a.currency, hideBelow: 'lg' },
              // The ledger keeps every account in the base currency.
              { key: 'balance', header: 'Balance in the books', numeric: true, cell: (a) => money(a.balance) },
            ]}
          />
        </>
      )}
    </div>
  );
}

function Tile({ title, value, dark, children }: { title: string; value: string | undefined; dark?: boolean; children?: ReactNode }) {
  return (
    <section className={cx('flex min-h-[140px] min-w-0 flex-col justify-between gap-1 rounded-card p-4 shadow-card sm:p-5', dark ? 'bg-charcoal text-white' : 'bg-card')}>
      <h2 className={cx('font-heading text-lead', dark ? 'text-white' : 'text-ink')}>{title}</h2>
      {value === undefined ? (
        <Skeleton className="my-2 h-10 w-3/4" />
      ) : (
        <p className="truncate font-heading text-[clamp(24px,2.2vw,34px)] leading-tight tabular-nums" data-testid={`tile-${title}`}>
          {value}
        </p>
      )}
      <p className={cx('text-note', dark ? 'text-cream' : 'text-muted')}>{children}</p>
    </section>
  );
}
