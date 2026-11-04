'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Check, Plus, RotateCcw, Save, Send, Trash2, X } from 'lucide-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useId, useState } from 'react';
import { AccountSelect, amountOf, Attachments, LINK, MoneyInput, ReasonDialog, StatusChip, StoreSelect, today, useAccounts, useMoney } from '@/components/finance';
import { ConflictDialog } from '@/components/masters';
import { Button, IconButton, LinkButton } from '@/components/ui/button';
import { ConfirmDialog } from '@/components/ui/dialog';
import { Field, Input } from '@/components/ui/field';
import { Detail, PageHeader, Toolbar } from '@/components/ui/page';
import { ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { journalTotals, kindLabel } from '@/lib/finance';
import { formatDate, formatUtc } from '@/lib/format';
import { numberText } from '@/lib/forms';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { JournalDetail, JournalRequest } from '@/lib/types';

export default function JournalEditPage() {
  return (
    <Suspense>
      <JournalEdit />
    </Suspense>
  );
}

interface Line {
  key: number;
  accountId: string | null;
  debit: string;
  credit: string;
  memo: string;
}

let nextKey = 1;
const blank = (): Line => ({ key: nextKey++, accountId: null, debit: '', credit: '', memo: '' });

/** A journal entry: made by hand (draft, approved by a second person), or one the program posted, to look at and reverse. */
function JournalEdit() {
  const params = useSearchParams();
  const id = params.get('id');
  const detail = useQuery({ queryKey: ['journal', id], queryFn: () => api.journal(id!), enabled: !!id, staleTime: 0 });
  if (id && detail.error) {
    return (
      <div className="card">
        <ErrorState error={detail.error} onRetry={() => void detail.refetch()} />
      </div>
    );
  }
  if (id && !detail.data) return <Skeleton className="h-64" />;
  return <Editor key={detail.data?.version ?? 'new'} entry={detail.data ?? null} />;
}

function Editor({ entry }: { entry: JournalDetail | null }) {
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { can, user } = useSession();
  const { defaultStoreId, canSwitch } = useStore();
  const accounts = useAccounts();
  const settings = useQuery({ queryKey: ['finance-settings'], queryFn: () => api.financeSettings(), staleTime: 60_000 });
  const money = useMoney();
  const ids = { date: useId(), store: useId(), memo: useId(), reference: useId() };
  const editable = !entry || (entry.status === 'Draft' && entry.kind === 'Manual' && can(P.financePost));
  const [date, setDate] = useState(entry?.date ?? today());
  const [storeId, setStoreId] = useState<string | null>(entry ? entry.storeId : canSwitch ? null : defaultStoreId);
  const [memo, setMemo] = useState(entry?.memo ?? '');
  const [reference, setReference] = useState(entry?.reference ?? '');
  const [lines, setLines] = useState<Line[]>(() =>
    entry
      ? entry.lines.map((l) => ({ key: nextKey++, accountId: l.accountId, debit: l.debit ? numberText(l.debit) : '', credit: l.credit ? numberText(l.credit) : '', memo: l.memo ?? '' }))
      : [blank(), blank()],
  );
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [reversing, setReversing] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const totals = journalTotals(lines.filter((l) => l.accountId || l.debit || l.credit).map((l) => ({ debit: amountOf(l.debit), credit: amountOf(l.credit) })));
  const approvalNeeded = settings.data?.manualEntriesNeedApproval ?? true;

  useEffect(() => {
    if (!canSwitch && !storeId && defaultStoreId) setStoreId(defaultStoreId);
  }, [canSwitch, storeId, defaultStoreId]);

  const done = (saved: JournalDetail, message: string) => {
    toast.ok(message);
    void queryClient.invalidateQueries({ queryKey: ['journals'] });
    void queryClient.invalidateQueries({ queryKey: ['finance-home'] });
    queryClient.setQueryData(['journal', saved.id], saved);
    router.replace(`/finance/journals/edit/?id=${encodeURIComponent(saved.id)}`);
  };
  const save = useMutation({
    mutationFn: (body: JournalRequest) => (entry ? api.saveJournal(entry.id, body) : api.addJournal(body)),
    onSuccess: (saved) => done(saved, saved.status === 'Posted' ? `Entry ${saved.number} is posted.` : 'The entry is saved as a draft.'),
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  const approve = useMutation({
    mutationFn: () => api.approveJournal(entry!.id),
    onSuccess: (saved) => done(saved, `Entry ${saved.number} is approved and posted.`),
  });
  const reverse = useMutation({
    mutationFn: ({ on, reason }: { on: string; reason: string }) => api.reverseJournal(entry!.id, on, reason),
    onSuccess: (saved) => {
      setReversing(false);
      void queryClient.invalidateQueries({ queryKey: ['journal', entry!.id] });
      done(saved, `Entry ${entry!.number} is reversed by entry ${saved.number}.`);
    },
  });
  const remove = useMutation({
    mutationFn: () => api.deleteJournal(entry!.id),
    onSuccess: () => {
      toast.ok('The draft is deleted.');
      void queryClient.invalidateQueries({ queryKey: ['journals'] });
      router.push('/finance/journals/');
    },
  });

  function submit(post: boolean) {
    const used = lines.filter((l) => l.accountId || l.debit || l.credit);
    if (!memo.trim()) return setProblem('Say what the entry is for.');
    if (used.some((l) => !l.accountId)) return setProblem('Each line needs an account.');
    if (!totals.balanced) return setProblem(totals.badLines.length ? 'Each line is either a debit or a credit.' : `The entry does not balance: it is ${money(Math.abs(totals.difference))} off.`);
    setProblem(null);
    save.mutate({
      date,
      storeId,
      memo: memo.trim(),
      reference: reference.trim() || null,
      lines: used.map((l) => ({ accountId: l.accountId!, debit: amountOf(l.debit), credit: amountOf(l.credit), memo: l.memo.trim() || null })),
      post,
      version: entry?.version ?? null,
    });
  }

  const setLine = (key: number, change: Partial<Line>) => setLines((all) => all.map((l) => (l.key === key ? { ...l, ...change } : l)));
  const mayApprove = entry?.status === 'Draft' && can(P.financeApprove) && !(approvalNeeded && entry.createdBy === user.userName);

  return (
    <div>
      <Toolbar>
        <LinkButton href="/finance/journals/" icon={ArrowLeft}>
          Journals
        </LinkButton>
      </Toolbar>
      <PageHeader
        title={entry ? (entry.number ? `Entry ${entry.number}` : 'Draft entry') : 'New journal entry'}
        aside={entry && <StatusChip status={entry.status} />}
      />
      <form
        className="card space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          submit(false);
        }}
      >
        {entry && (
          <dl className="grid grid-cols-2 gap-3 md:grid-cols-4">
            <Detail label="Kind">{kindLabel(entry.kind)}</Detail>
            <Detail label="Made by">{entry.createdBy ?? ''}</Detail>
            <Detail label="Posted">{entry.postedAtUtc ? `${formatUtc(entry.postedAtUtc)} by ${entry.postedBy ?? ''}` : 'Not yet'}</Detail>
            <Detail label="Source">{entry.source ?? 'By hand'}</Detail>
          </dl>
        )}
        <div className="grid grid-cols-1 gap-3 md:grid-cols-4">
          <Field label="Date" htmlFor={ids.date}>
            <Input id={ids.date} type="date" value={date} readOnly={!editable} onChange={(e) => setDate(e.target.value)} />
          </Field>
          <Field label="Store" htmlFor={ids.store}>
            <StoreSelect id={ids.store} value={storeId} onChange={setStoreId} allowCompany disabled={!editable} />
          </Field>
          <Field label="Description" htmlFor={ids.memo} className="md:col-span-2">
            <Input id={ids.memo} value={memo} maxLength={200} readOnly={!editable} onChange={(e) => setMemo(e.target.value)} />
          </Field>
          <Field label="Reference" htmlFor={ids.reference}>
            <Input id={ids.reference} value={reference} maxLength={40} readOnly={!editable} onChange={(e) => setReference(e.target.value)} />
          </Field>
        </div>

        <div className="grid-card">
          <div className="grid-scroll">
            <table className="grid-table" aria-label="Lines">
              <thead>
                <tr>
                  <th scope="col">Account</th>
                  <th scope="col" className="num">
                    Debit
                  </th>
                  <th scope="col" className="num">
                    Credit
                  </th>
                  <th scope="col">Memo</th>
                  {editable && <th scope="col" aria-label="Remove" />}
                </tr>
              </thead>
              <tbody>
                {lines.map((l, i) => (
                  <tr key={l.key}>
                    <td className="min-w-[240px]">
                      {editable ? (
                        <AccountSelect ariaLabel={`Account of line ${i + 1}`} value={l.accountId} accounts={accounts.data} onChange={(accountId) => setLine(l.key, { accountId })} />
                      ) : (
                        `${entry!.lines[i]?.code ?? ''} ${entry!.lines[i]?.name ?? ''}`
                      )}
                    </td>
                    <td className="num w-[150px]">
                      {editable ? (
                        <MoneyInput ariaLabel={`Debit of line ${i + 1}`} value={l.debit} onChange={(debit) => setLine(l.key, { debit, credit: debit ? '' : l.credit })} />
                      ) : (
                        l.debit && money(amountOf(l.debit))
                      )}
                    </td>
                    <td className="num w-[150px]">
                      {editable ? (
                        <MoneyInput ariaLabel={`Credit of line ${i + 1}`} value={l.credit} onChange={(credit) => setLine(l.key, { credit, debit: credit ? '' : l.debit })} />
                      ) : (
                        l.credit && money(amountOf(l.credit))
                      )}
                    </td>
                    <td className="min-w-[160px]">
                      {editable ? <Input aria-label={`Memo of line ${i + 1}`} value={l.memo} maxLength={200} onChange={(e) => setLine(l.key, { memo: e.target.value })} /> : l.memo}
                    </td>
                    {editable && (
                      <td className="w-[60px]">
                        <IconButton icon={X} label={`Remove line ${i + 1}`} disabled={lines.length <= 2} onClick={() => setLines((all) => all.filter((x) => x.key !== l.key))} />
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td>{totals.difference === 0 ? 'Balanced' : `Off by ${money(Math.abs(totals.difference))}`}</td>
                  <td className="num" data-testid="journal-debits">
                    {money(totals.debit)}
                  </td>
                  <td className="num" data-testid="journal-credits">
                    {money(totals.credit)}
                  </td>
                  <td colSpan={editable ? 2 : 1} />
                </tr>
              </tfoot>
            </table>
          </div>
        </div>
        {editable && (
          <Button icon={Plus} onClick={() => setLines((all) => [...all, blank()])}>
            Add a line
          </Button>
        )}

        {(problem || save.error || approve.error) && (problem ? <p className="text-danger">{problem}</p> : <FormError error={save.error ?? approve.error} />)}
        <div className="flex flex-wrap gap-2.5">
          {editable && (
            <>
              <Button type="submit" variant={approvalNeeded ? 'sun' : 'plain'} icon={Save} shortcut="Ctrl+S" busy={save.isPending}>
                {approvalNeeded ? 'Save for approval' : 'Save as draft'}
              </Button>
              {!approvalNeeded && (
                <Button variant="sun" icon={Send} onClick={() => submit(true)} busy={save.isPending}>
                  Save and post
                </Button>
              )}
            </>
          )}
          {entry?.status === 'Draft' && can(P.financeApprove) && (
            <Button variant="sun" icon={Check} disabled={!mayApprove} onClick={() => approve.mutate()} busy={approve.isPending}>
              Approve and post
            </Button>
          )}
          {entry?.status === 'Draft' && can(P.financePost) && (
            <Button variant="danger" icon={Trash2} onClick={() => setDeleting(true)}>
              Delete draft
            </Button>
          )}
          {entry?.status === 'Posted' && entry.kind !== 'Reversal' && can(P.financePost) && (
            <Button variant="danger" icon={RotateCcw} onClick={() => setReversing(true)}>
              Reverse
            </Button>
          )}
        </div>
        {entry?.status === 'Draft' && approvalNeeded && (
          <p className="text-muted">Someone other than {entry.createdBy ?? 'the person who made it'} approves this entry; until then it is in no balance.</p>
        )}
        {entry?.reversedById && (
          <p className="text-ink">
            Reversed by{' '}
            <Link className={LINK} href={`/finance/journals/edit/?id=${encodeURIComponent(entry.reversedById)}`}>
              its reversing entry
            </Link>
            .
          </p>
        )}
        {entry && <p className="text-note text-muted">Dated {formatDate(entry.date)}{entry.storeCode ? ` · store ${entry.storeCode}` : ' · the company'}</p>}
      </form>

      {entry && (
        <div className="card mt-4">
          <Attachments owner="journals" ownerId={entry.id} files={entry.attachments} canChange={can(P.financePost)} queryKey={['journal', entry.id]} />
        </div>
      )}

      {reversing && (
        <ReasonDialog
          title={`Reverse entry ${entry!.number}?`}
          message="A mirror entry cancels it on the date you give (an open period)."
          confirmLabel="Reverse"
          busy={reverse.isPending}
          error={reverse.error}
          onConfirm={(on, reason) => reverse.mutate({ on, reason })}
          onClose={() => setReversing(false)}
        />
      )}
      <ConfirmDialog
        open={deleting}
        title="Delete this draft?"
        confirmLabel="Delete"
        danger
        busy={remove.isPending}
        onConfirm={() => remove.mutate()}
        onClose={() => setDeleting(false)}
      />
      <ConflictDialog
        open={conflict}
        what="entry"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['journal', entry?.id] });
        }}
        onClose={() => setConflict(false)}
      />
    </div>
  );
}
