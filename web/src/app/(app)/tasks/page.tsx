'use client';

import { useQuery } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { ActivityDialog, useActivityDone, useMayTick } from '@/components/crm';
import { Button } from '@/components/ui/button';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states';
import { DUE, groupTasks, relatedTo } from '@/lib/crm';
import { cx } from '@/lib/cx';
import { formatCount, formatShortDateTime } from '@/lib/format';
import { api } from '@/lib/api';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import type { ActivityRow } from '@/lib/types';

export default function TasksPage() {
  const { can } = useSession();
  const [editing, setEditing] = useState<ActivityRow | 'new' | null>(null);
  const query = useQuery({ queryKey: ['my-tasks', 'all'], queryFn: () => api.myTasks({}), refetchInterval: 60_000 });
  const done = useActivityDone();
  const mayTick = useMayTick();
  const groups = query.data ? groupTasks(query.data.items) : [];

  return (
    <div>
      <PageHeader
        title="My tasks"
        aside={query.data && `${formatCount(query.data.overdue)} overdue · ${formatCount(query.data.dueToday)} today · ${formatCount(query.data.items.length)} open`}
      />
      {can(P.crmEdit) && (
        <Toolbar>
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            New activity
          </Button>
        </Toolbar>
      )}
      {query.error ? (
        <div className="card">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      ) : !query.data ? (
        <div className="space-y-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-20 rounded-card" />
          ))}
        </div>
      ) : groups.length === 0 ? (
        <div className="card">
          <EmptyState title="Nothing to do">Calls, visits and tasks given to you show up here, overdue ones first.</EmptyState>
        </div>
      ) : (
        <div className="space-y-5">
          {groups.map((group) => (
            <section key={group.key} aria-labelledby={`group-${group.key}`} className="rounded-card bg-card shadow-card">
              <h2 id={`group-${group.key}`} className={cx('flex items-center gap-3 px-5 pb-2 pt-4 font-heading text-section', group.key === 'overdue' && 'text-danger')}>
                {group.title}
                <Chip tone={DUE[group.key].tone}>{formatCount(group.items.length)}</Chip>
              </h2>
              <ul className="divide-y divide-line">
                {group.items.map((a) => (
                  <li key={a.id} className="flex items-start gap-3 px-5 py-3" data-testid={`task-${a.id}`}>
                    <input
                      type="checkbox"
                      className="check mt-1"
                      aria-label={`${a.subject} done`}
                      checked={a.isDone}
                      disabled={!mayTick(a) || done.isPending}
                      onChange={() => done.mutate(a)}
                    />
                    <button type="button" className="min-w-0 flex-1 text-left" onClick={() => setEditing(a)}>
                      <span className="block font-semibold hover:underline">{a.subject}</span>
                      <span className="block text-note text-muted">
                        {[a.kind, relatedTo(a), a.storeCode].filter(Boolean).join(' · ')}
                      </span>
                    </button>
                    <span className={cx('shrink-0 text-note tabular-nums', group.key === 'overdue' ? 'font-semibold text-danger' : 'text-muted')}>
                      {a.dueAt ? formatShortDateTime(a.dueAt) : ''}
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
      <p className="mt-3 px-1 text-ink">Tick one when it is done. Everything you own that is still open is here; CRM › Activities shows the rest.</p>
      {editing && <ActivityDialog activity={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </div>
  );
}
