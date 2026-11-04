'use client';

/**
 * The dashboard's E4 sections, each only for those who may see it: the signed-in user's overdue and today's
 * activities (crm.view), and this month's targets against net sales (sales.targets).
 */
import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { TargetBar } from './target-progress';
import { Card } from './ui/card';
import { Chip } from './ui/page';
import { ErrorState, Skeleton } from './ui/states';
import { api } from '@/lib/api';
import { DUE, relatedTo } from '@/lib/crm';
import { formatCount, formatMoney, formatPercent, formatShortDateTime } from '@/lib/format';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import { expectedPercent, monthLabel } from '@/lib/targets';

const linkClass = 'font-semibold text-ink underline decoration-sun-dark decoration-2 underline-offset-4';

export function DashboardExtras() {
  const { can } = useSession();
  const tasks = can(P.crmView);
  const targets = can(P.targets);
  if (!tasks && !targets) return null;
  return (
    <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
      {tasks && <MyTasksCard />}
      {targets && <TargetsCard />}
    </div>
  );
}

function MyTasksCard() {
  const query = useQuery({ queryKey: ['my-tasks', 'today'], queryFn: () => api.myTasks({ due: 'today' }), refetchInterval: 60_000 });
  const data = query.data;
  return (
    <Card
      title="My tasks"
      aside={
        <Link href="/tasks/" className={linkClass}>
          All my tasks
        </Link>
      }
    >
      {query.error ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} className="py-4" />
      ) : !data ? (
        <Skeleton className="h-24" />
      ) : (
        <>
          <p className="mb-2 text-ink" data-testid="my-tasks-summary">
            <strong className={data.overdue > 0 ? 'text-danger' : undefined}>{formatCount(data.overdue)} overdue</strong> · <strong>{formatCount(data.dueToday)} today</strong>
          </p>
          {data.items.length === 0 ? (
            <p className="text-muted">Nothing due today. Well done.</p>
          ) : (
            <ul className="divide-y divide-line">
              {data.items.slice(0, 6).map((a) => (
                <li key={a.id} className="flex items-center gap-3 py-2">
                  <Chip tone={DUE[a.due]?.tone ?? 'plain'}>{DUE[a.due]?.label ?? a.due}</Chip>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate font-semibold">{a.subject}</span>
                    <span className="block truncate text-note text-muted">{relatedTo(a) || a.kind}</span>
                  </span>
                  <span className="shrink-0 text-note tabular-nums text-muted">{a.dueAt ? formatShortDateTime(a.dueAt) : ''}</span>
                </li>
              ))}
            </ul>
          )}
          {data.items.length > 6 && <p className="mt-2 text-note text-muted">And {formatCount(data.items.length - 6)} more.</p>}
        </>
      )}
    </Card>
  );
}

function TargetsCard() {
  const { storeId, currencySymbol: symbol } = useStore();
  const query = useQuery({ queryKey: ['target-progress', storeId], queryFn: () => api.targetProgress({ storeId }), refetchInterval: 5 * 60_000 });
  const p = query.data;
  const money = (n: number) => formatMoney(n, symbol);
  const expected = p ? expectedPercent(p.day, p.daysInMonth) : 0;
  return (
    <Card
      title="Targets"
      aside={
        <Link href="/targets/" className={linkClass}>
          {p ? monthLabel(p.year, p.month) : 'This month'}
        </Link>
      }
    >
      {query.error ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} className="py-4" />
      ) : !p ? (
        <Skeleton className="h-24" />
      ) : p.target <= 0 ? (
        <p className="text-muted">
          No target is set for this month.{' '}
          <Link href="/targets/?new=1" className={linkClass}>
            Set one
          </Link>
        </p>
      ) : (
        <div className="space-y-3">
          <div>
            <p className="font-heading text-value tabular-nums" data-testid="target-percent">
              {formatPercent(p.percent)}
            </p>
            <p className="text-ink">
              {money(p.actual)} of {money(p.target)} · {formatPercent(expected)} of the month gone
            </p>
          </div>
          <TargetBar percent={p.percent} expected={expected} label="This month's target reached" />
          {(p.stores.length > 1 || p.people.length > 0) && (
            <ul className="space-y-2">
              {[...(p.stores.length > 1 ? p.stores.map((s) => ({ key: s.storeId, name: s.storeCode, ...s })) : []), ...p.people.map((x) => ({ key: x.userId, name: `${x.userName} · ${x.storeCode}`, ...x }))]
                .slice(0, 6)
                .map((row) => (
                  <li key={row.key} className="grid grid-cols-[minmax(0,140px)_minmax(0,1fr)] items-center gap-3">
                    <span className="truncate text-note">{row.name}</span>
                    <TargetBar percent={row.percent} expected={expected} label={`${row.name} reached`} />
                  </li>
                ))}
            </ul>
          )}
        </div>
      )}
    </Card>
  );
}
