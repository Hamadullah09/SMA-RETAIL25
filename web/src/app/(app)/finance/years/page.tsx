'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CalendarCheck, CalendarPlus, Lock, LockOpen, Plus, RotateCcw, Scale, X } from 'lucide-react';
import Link from 'next/link';
import { useId, useState } from 'react';
import { AccountSelect, amountOf, LINK, MoneyInput, StatusChip, StoreSelect, today, useAccounts, useMoney } from '@/components/finance';
import { Button, IconButton } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, DateInput, Field, Input } from '@/components/ui/field';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { journalTotals, periodAction } from '@/lib/finance';
import { formatDate, formatUtc } from '@/lib/format';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { JournalDetail, PeriodInfo, YearInfo } from '@/lib/types';

/**
 * Financial years and their months: a month is locked when its figures are final (nothing more is posted in it; a change
 * goes to the next open month), a year is closed at its end (its profit goes to retained earnings) and can be opened
 * again. A company that starts its books here part way through a year enters its opening balances.
 */
export default function YearsPage() {
  const { can, canChangeShared } = useSession();
  const toast = useToast();
  const queryClient = useQueryClient();
  const years = useQuery({ queryKey: ['finance-years'], queryFn: () => api.years() });
  const [adding, setAdding] = useState(false);
  const [opening, setOpening] = useState(false);
  const [closing, setClosing] = useState<YearInfo | null>(null);
  const [reopening, setReopening] = useState<YearInfo | null>(null);
  // Years, periods and closing belong to the whole company.
  const manage = can(P.financeClose) && canChangeShared;

  const done = (data: YearInfo[], message: string) => {
    queryClient.setQueryData(['finance-years'], data);
    void queryClient.invalidateQueries({ queryKey: ['journals'] });
    void queryClient.invalidateQueries({ queryKey: ['finance-home'] });
    toast.ok(message);
  };
  const period = useMutation({
    mutationFn: ({ p, lock }: { p: PeriodInfo; lock: boolean }) => (lock ? api.lockPeriod(p.id) : api.unlockPeriod(p.id)),
    onSuccess: (data, { p, lock }) => done(data, lock ? `${p.name} is locked.` : `${p.name} is open again.`),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : 'The month did not change.'),
  });
  const close = useMutation({
    mutationFn: (y: YearInfo) => api.closeYear(y.id),
    onSuccess: (data, y) => {
      setClosing(null);
      done(data, `${y.name} is closed; its profit went to retained earnings.`);
    },
  });

  return (
    <div>
      <PageHeader title="Years and periods" />
      <Toolbar>
        {manage && (
          <Button icon={CalendarPlus} onClick={() => setAdding(true)}>
            Make a year
          </Button>
        )}
        {can(P.financePost) && (
          <Button icon={Scale} onClick={() => setOpening(true)}>
            Opening balances
          </Button>
        )}
      </Toolbar>
      {can(P.financeClose) && !canChangeShared && <p className="mb-3 px-1 text-ink">Years are the whole company’s: a super administrator locks and closes them.</p>}
      {years.error ? (
        <div className="card">
          <ErrorState error={years.error} onRetry={() => void years.refetch()} />
        </div>
      ) : !years.data ? (
        <Skeleton className="h-72 rounded-card" />
      ) : years.data.length === 0 ? (
        <div className="card">
          <EmptyState title="No financial year yet">The first posting makes its year; or make it here.</EmptyState>
        </div>
      ) : (
        <div className="space-y-4">
          {years.data.map((y) => (
            <section key={y.id} className="card p-5" aria-labelledby={`year-${y.id}`}>
              <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
                <div>
                  <h2 id={`year-${y.id}`} className="section-title">
                    {y.name} <StatusChip status={y.status} />
                  </h2>
                  <p className="text-muted">
                    {formatDate(y.startDate)} to {formatDate(y.endDate)}
                    {y.closedAtUtc && ` · closed ${formatUtc(y.closedAtUtc)}${y.closedBy ? ` by ${y.closedBy}` : ''}`}
                  </p>
                </div>
                {manage && (
                  <div className="flex flex-wrap gap-2.5">
                    {y.status === 'Closed' ? (
                      <Button icon={RotateCcw} onClick={() => setReopening(y)}>
                        Open the year again
                      </Button>
                    ) : (
                      <Button variant="sun" icon={CalendarCheck} onClick={() => setClosing(y)}>
                        Close the year
                      </Button>
                    )}
                  </div>
                )}
              </div>
              <div className="grid-card shadow-none ring-1 ring-line">
                <div className="grid-scroll">
                  <table className="grid-table" aria-label={`Months of ${y.name}`}>
                    <thead>
                      <tr>
                        <th scope="col">Month</th>
                        <th scope="col">From</th>
                        <th scope="col">To</th>
                        <th scope="col">State</th>
                        {manage && <th scope="col" aria-label="Change" />}
                      </tr>
                    </thead>
                    <tbody>
                      {y.periods.map((p) => {
                        const action = periodAction(p, y);
                        return (
                          <tr key={p.id}>
                            <td className="font-semibold">{p.name}</td>
                            <td>{formatDate(p.startDate)}</td>
                            <td>{formatDate(p.endDate)}</td>
                            <td>
                              <StatusChip status={p.status} />
                            </td>
                            {manage && (
                              <td className="w-[170px]">
                                {action && (
                                  <Button
                                    icon={action === 'lock' ? Lock : LockOpen}
                                    aria-label={`${action === 'lock' ? 'Lock' : 'Open'} ${p.name}`}
                                    busy={period.isPending && period.variables?.p.id === p.id}
                                    onClick={() => period.mutate({ p, lock: action === 'lock' })}
                                  >
                                    {action === 'lock' ? 'Lock' : 'Open'}
                                  </Button>
                                )}
                              </td>
                            )}
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              </div>
            </section>
          ))}
        </div>
      )}
      {adding && <AddYearDialog onClose={() => setAdding(false)} onDone={(data, message) => done(data, message)} />}
      {opening && <OpeningDialog onClose={() => setOpening(false)} />}
      <ConfirmDialog
        open={!!closing}
        title={`Close ${closing?.name}?`}
        message={
          <>
            Each store’s income and expenses of the year move to retained earnings in one closing entry on {closing && formatDate(closing.endDate)}, and every
            month of the year is closed. The next year is made if it is not there yet. Entries waiting for approval must be approved or deleted first.
            {close.error ? <FormError error={close.error} /> : null}
          </>
        }
        confirmLabel="Close the year"
        busy={close.isPending}
        onConfirm={() => closing && close.mutate(closing)}
        onClose={() => {
          setClosing(null);
          close.reset();
        }}
      />
      {reopening && <ReopenDialog year={reopening} onClose={() => setReopening(null)} onDone={(data, message) => done(data, message)} />}
    </div>
  );
}

function AddYearDialog({ onClose, onDone }: { onClose(): void; onDone(data: YearInfo[], message: string): void }) {
  const [date, setDate] = useState(today());
  const add = useMutation({
    mutationFn: () => api.addYear(date),
    onSuccess: (data) => {
      onDone(data, 'The year is there, with its twelve months open.');
      onClose();
    },
  });
  return (
    <Dialog
      open
      width="sm"
      title="Make a year"
      onClose={onClose}
      onSubmit={() => add.mutate()}
      footer={
        <>
          <Button type="submit" variant="sun" icon={CalendarPlus} shortcut="Enter" busy={add.isPending}>
            Make it
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-ink">The year that holds this day is made, from the month the books’ year starts in (Finance setup).</p>
        <DateInput label="A day in the year" value={date} onChange={setDate} />
        {add.error ? <FormError error={add.error} /> : null}
      </div>
    </Dialog>
  );
}

function ReopenDialog({ year, onClose, onDone }: { year: YearInfo; onClose(): void; onDone(data: YearInfo[], message: string): void }) {
  const id = useId();
  const [reason, setReason] = useState('');
  const [missing, setMissing] = useState(false);
  const reopen = useMutation({
    mutationFn: () => api.reopenYear(year.id, reason.trim()),
    onSuccess: (data) => {
      onDone(data, `${year.name} is open again; its closing entry is reversed.`);
      onClose();
    },
  });
  return (
    <Dialog
      open
      title={`Open ${year.name} again?`}
      onClose={onClose}
      onSubmit={() => (reason.trim() ? reopen.mutate() : setMissing(true))}
      footer={
        <>
          <Button type="submit" variant="danger" icon={RotateCcw} shortcut="Enter" busy={reopen.isPending}>
            Open again
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-ink">Its months open and its closing entry is reversed. Close it again when the changes are made.</p>
        <Field label="Reason" htmlFor={id} error={missing ? 'Say why.' : null}>
          <Input id={id} value={reason} maxLength={200} invalid={missing} onChange={(e) => setReason(e.target.value)} />
        </Field>
        {reopen.error ? <FormError error={reopen.error} /> : null}
      </div>
    </Dialog>
  );
}

interface OpeningLine {
  key: number;
  accountId: string | null;
  debit: string;
  credit: string;
}

let nextKey = 1;
const blank = (): OpeningLine => ({ key: nextKey++, accountId: null, debit: '', credit: '' });

/** What each account held on the day the books start here: one Opening entry, posted again when changed. */
function OpeningDialog({ onClose }: { onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const money = useMoney();
  const accounts = useAccounts();
  const { defaultStoreId, canSwitch } = useStore();
  const ids = { store: useId() };
  const [date, setDate] = useState(today());
  const [storeId, setStoreId] = useState<string | null>(canSwitch ? null : defaultStoreId);
  const [lines, setLines] = useState<OpeningLine[]>(() => [blank(), blank(), blank()]);
  const [toRetained, setToRetained] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [posted, setPosted] = useState<JournalDetail | null>(null);
  const used = lines.filter((l) => l.accountId || l.debit || l.credit);
  const totals = journalTotals(used.map((l) => ({ debit: amountOf(l.debit), credit: amountOf(l.credit) })));
  const save = useMutation({
    mutationFn: () =>
      api.openingBalances({
        date,
        storeId,
        balanceToRetainedEarnings: toRetained,
        lines: used.map((l) => ({ accountId: l.accountId!, debit: amountOf(l.debit), credit: amountOf(l.credit) })),
      }),
    onSuccess: (entry) => {
      setPosted(entry);
      toast.ok(`The opening balances are posted as entry ${entry.number}.`);
      void queryClient.invalidateQueries({ queryKey: ['finance-accounts'] });
      void queryClient.invalidateQueries({ queryKey: ['journals'] });
    },
  });
  const setLine = (key: number, change: Partial<OpeningLine>) => setLines((all) => all.map((l) => (l.key === key ? { ...l, ...change } : l)));

  function submit() {
    if (used.length === 0) return setProblem('Enter the accounts’ balances.');
    if (used.some((l) => !l.accountId)) return setProblem('Each line needs an account.');
    if (totals.badLines.length) return setProblem('Each line is either a debit or a credit.');
    if (totals.difference !== 0 && !toRetained) return setProblem(`Debits and credits differ by ${money(Math.abs(totals.difference))}. Fix them, or put the difference to retained earnings.`);
    setProblem(null);
    save.mutate();
  }

  return (
    <Dialog
      open
      width="xl"
      title="Opening balances"
      onClose={onClose}
      onSubmit={() => (posted ? onClose() : submit())}
      footer={
        <>
          {!posted && (
            <Button type="submit" variant="sun" icon={Scale} shortcut="Enter" busy={save.isPending}>
              Post the balances
            </Button>
          )}
          <Button onClick={onClose} shortcut="Esc">
            {posted ? 'Close' : 'Cancel'}
          </Button>
        </>
      }
    >
      {posted ? (
        <p className="text-ink">
          Posted as{' '}
          <Link className={LINK} href={`/finance/journals/edit/?id=${encodeURIComponent(posted.id)}`}>
            entry {posted.number}
          </Link>
          . Posting balances again for the same day and store replaces them while the month is open.
        </p>
      ) : (
        <div className="space-y-3">
          <p className="text-ink">What each account held at the start of the day the books begin here: bank and cash, stock, what suppliers are owed, capital.</p>
          <div className="flex flex-wrap items-center gap-4">
            <DateInput label="On" value={date} onChange={setDate} />
            <label htmlFor={ids.store} className="text-muted">
              For
            </label>
            <div className="w-[260px]">
              <StoreSelect id={ids.store} value={storeId} onChange={setStoreId} allowCompany />
            </div>
          </div>
          <div className="grid-card">
            <div className="grid-scroll max-h-[45vh] overflow-y-auto">
              <table className="grid-table" aria-label="Balances">
                <thead>
                  <tr>
                    <th scope="col">Account</th>
                    <th scope="col" className="num">
                      Debit
                    </th>
                    <th scope="col" className="num">
                      Credit
                    </th>
                    <th scope="col" aria-label="Remove" />
                  </tr>
                </thead>
                <tbody>
                  {lines.map((l, i) => (
                    <tr key={l.key}>
                      <td className="min-w-[260px]">
                        <AccountSelect ariaLabel={`Account of line ${i + 1}`} value={l.accountId} accounts={accounts.data} onChange={(accountId) => setLine(l.key, { accountId })} />
                      </td>
                      <td className="num w-[160px]">
                        <MoneyInput ariaLabel={`Debit of line ${i + 1}`} value={l.debit} onChange={(debit) => setLine(l.key, { debit, credit: debit ? '' : l.credit })} />
                      </td>
                      <td className="num w-[160px]">
                        <MoneyInput ariaLabel={`Credit of line ${i + 1}`} value={l.credit} onChange={(credit) => setLine(l.key, { credit, debit: credit ? '' : l.debit })} />
                      </td>
                      <td className="w-[60px]">
                        <IconButton icon={X} label={`Remove line ${i + 1}`} disabled={lines.length <= 1} onClick={() => setLines((all) => all.filter((x) => x.key !== l.key))} />
                      </td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr>
                    <td>{totals.difference === 0 ? 'Balanced' : `Off by ${money(Math.abs(totals.difference))}`}</td>
                    <td className="num">{money(totals.debit)}</td>
                    <td className="num">{money(totals.credit)}</td>
                    <td />
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>
          <Button icon={Plus} onClick={() => setLines((all) => [...all, blank()])}>
            Add a line
          </Button>
          <Checkbox
            label="Put any difference to retained earnings"
            hint="When the old books do not show every account"
            checked={toRetained}
            onChange={setToRetained}
          />
          {(problem || save.error) && (problem ? <p className="text-danger">{problem}</p> : <FormError error={save.error} />)}
        </div>
      )}
    </Dialog>
  );
}
