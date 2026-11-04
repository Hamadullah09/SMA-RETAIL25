'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, EyeOff, FilePlus2, Link2, Link2Off, Upload, WandSparkles } from 'lucide-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { AccountSelect, LINK, StatusChip, today, useAccounts, useBankAccounts, useMoney } from '@/components/finance';
import { Button, LinkButton } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { Checkbox, DateInput, Field, Input, Select } from '@/components/ui/field';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { cx } from '@/lib/cx';
import { matchCandidates } from '@/lib/finance';
import { formatCount, formatDate } from '@/lib/format';
import { useHotkey } from '@/lib/hotkeys';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import type { LedgerLineRow, Reconciliation, StatementFormat, StatementLineRow } from '@/lib/types';

export default function ReconcilePage() {
  return (
    <Suspense>
      <Reconcile />
    </Suspense>
  );
}

/**
 * A bank account's statements against its ledger: upload the bank's CSV, let the lines match themselves (same amount,
 * dates close, reference), match the rest by hand, enter what only the bank knew (charges, profit) and leave out what
 * does not belong. When the difference is zero, the books agree with the bank.
 */
function Reconcile() {
  const params = useSearchParams();
  const router = useRouter();
  const id = params.get('id') ?? '';
  const { can } = useSession();
  const toast = useToast();
  const queryClient = useQueryClient();
  const money = useMoney();
  const banks = useBankAccounts();
  const bank = banks.data?.find((b) => b.id === id);
  const [asOf, setAsOf] = useState(today());
  const [uploading, setUploading] = useState(false);
  const [matching, setMatching] = useState<StatementLineRow | null>(null);
  const [entering, setEntering] = useState<StatementLineRow | null>(null);
  const query = useQuery({ queryKey: ['reconciliation', id, asOf], queryFn: () => api.reconciliation(id, { asOf }), enabled: !!id });
  const r = query.data;
  // Statements and matches of a company account are changed by those who change what the company shares; the server says no otherwise.
  const change = can(P.financeBank);

  useHotkey('Esc', () => router.push('/finance/bank/'));
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['reconciliation', id] });
    void queryClient.invalidateQueries({ queryKey: ['bank-accounts'] });
  };
  const fail = (e: unknown) => toast.error(e instanceof ApiError ? e.message : 'That did not work.');
  const auto = useMutation({
    mutationFn: () => api.autoMatch(id),
    onSuccess: (m) => {
      toast.ok(m.matched ? `${formatCount(m.matched)} lines matched themselves.` : 'No more lines could be matched by themselves.');
      refresh();
    },
    onError: fail,
  });
  const unmatch = useMutation({ mutationFn: (line: StatementLineRow) => api.unmatchLine(line.id), onSuccess: refresh, onError: fail });
  const ignore = useMutation({
    mutationFn: (line: StatementLineRow) => api.ignoreLine(line.id),
    onSuccess: () => {
      toast.ok('The line is left out.');
      refresh();
    },
    onError: fail,
  });

  if (!id) {
    return (
      <div className="card">
        <EmptyState title="No account chosen">Choose one on Bank and cash.</EmptyState>
      </div>
    );
  }

  return (
    <div>
      <Toolbar>
        <LinkButton href="/finance/bank/" icon={ArrowLeft} shortcut="Esc">
          Bank and cash
        </LinkButton>
        <DateInput label="As of" value={asOf} onChange={(d) => d && setAsOf(d)} />
        {change && (
          <>
            <Button variant="sun" icon={Upload} onClick={() => setUploading(true)}>
              Upload a statement
            </Button>
            <Button icon={WandSparkles} busy={auto.isPending} onClick={() => auto.mutate()} disabled={!r || r.unmatchedLines === 0}>
              Match by themselves
            </Button>
          </>
        )}
      </Toolbar>
      <PageHeader title={`Reconcile ${bank?.name ?? ''}`.trim()} aside={bank && [bank.bankName, bank.accountNumber].filter(Boolean).join(' · ')} />

      {query.error ? (
        <div className="card">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      ) : !r ? (
        <Skeleton className="h-72 rounded-card" />
      ) : (
        <>
          <Summary r={r} />
          <h2 className="section-title mb-2 mt-4">The bank’s statement</h2>
          <DataTable<StatementLineRow>
            caption="Statement lines"
            rows={r.statementLines}
            rowKey={(l) => l.id}
            maxHeight="45vh"
            empty={<EmptyState title="No statement yet">Upload the bank’s statement as a CSV file.</EmptyState>}
            columns={[
              { key: 'date', header: 'Date', cell: (l) => formatDate(l.date) },
              { key: 'what', header: 'Description', cell: (l) => l.description ?? '', className: 'wrap' },
              { key: 'ref', header: 'Reference', cell: (l) => l.reference ?? '', hideBelow: 'lg' },
              { key: 'status', header: 'State', cell: (l) => <StatusChip status={l.status} /> },
              { key: 'amount', header: 'Amount', numeric: true, cell: (l) => <span className={cx(l.amount < 0 && 'text-danger')}>{money(l.amount)}</span> },
              {
                key: 'do',
                header: <span className="sr-only">What to do</span>,
                cell: (l) =>
                  !change ? null : l.status === 'Unmatched' ? (
                    <span className="flex flex-wrap justify-end gap-2">
                      <Button icon={Link2} aria-label={`Match the line of ${formatDate(l.date)} for ${money(l.amount)}`} onClick={() => setMatching(l)}>
                        Match
                      </Button>
                      <Button icon={FilePlus2} aria-label={`Enter the line of ${formatDate(l.date)} for ${money(l.amount)}`} onClick={() => setEntering(l)}>
                        Enter
                      </Button>
                      <Button icon={EyeOff} aria-label={`Leave out the line of ${formatDate(l.date)} for ${money(l.amount)}`} busy={ignore.isPending && ignore.variables?.id === l.id} onClick={() => ignore.mutate(l)}>
                        Leave out
                      </Button>
                    </span>
                  ) : l.status === 'Matched' ? (
                    <span className="flex justify-end">
                      <Button icon={Link2Off} aria-label={`Undo the match of ${formatDate(l.date)} for ${money(l.amount)}`} busy={unmatch.isPending && unmatch.variables?.id === l.id} onClick={() => unmatch.mutate(l)}>
                        Undo
                      </Button>
                    </span>
                  ) : null,
              },
            ]}
          />
          <h2 className="section-title mb-2 mt-4">The books</h2>
          <DataTable<LedgerLineRow>
            caption="Ledger lines"
            rows={r.ledgerLines}
            rowKey={(l) => l.journalLineId}
            maxHeight="45vh"
            empty={<EmptyState title="Nothing posted">Nothing went in or out of this account in the books.</EmptyState>}
            columns={[
              { key: 'date', header: 'Date', cell: (l) => formatDate(l.date) },
              {
                key: 'entry',
                header: 'Entry',
                cell: (l) => (
                  <Link className={LINK} href={`/finance/journals/edit/?id=${encodeURIComponent(l.entryId)}`}>
                    {l.number}
                  </Link>
                ),
              },
              { key: 'memo', header: 'Description', cell: (l) => l.memo, className: 'wrap' },
              { key: 'ref', header: 'Reference', cell: (l) => l.reference ?? '', hideBelow: 'lg' },
              { key: 'state', header: 'On the statement', cell: (l) => (l.matched ? 'Yes' : 'Not yet') },
              { key: 'amount', header: 'Amount', numeric: true, cell: (l) => <span className={cx(l.amount < 0 && 'text-danger')}>{money(l.amount)}</span> },
            ]}
          />
        </>
      )}
      {uploading && <UploadDialog bankId={id} onClose={() => setUploading(false)} onDone={refresh} />}
      {matching && r && <MatchDialog line={matching} ledger={r.ledgerLines} onClose={() => setMatching(null)} onDone={refresh} />}
      {entering && <EntryDialog line={entering} onClose={() => setEntering(null)} onDone={refresh} />}
    </div>
  );
}

function Summary({ r }: { r: Reconciliation }) {
  const money = useMoney();
  const agreed = r.difference === 0;
  return (
    <div className="mb-2 grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-5">
      <Figure title="The bank says" value={r.statementBalance === null ? 'No statement' : money(r.statementBalance)}>
        On {formatDate(r.asOf)}
      </Figure>
      <Figure title="The books say" value={money(r.ledgerBalance)}>
        All posted up to that day
      </Figure>
      <Figure title="Not yet at the bank" value={money(r.uncleared)}>
        In the books, not on a statement
      </Figure>
      <Figure title="Not yet in the books" value={money(r.unmatchedStatement)}>
        {formatCount(r.unmatchedLines)} statement lines to match or enter
      </Figure>
      <Figure title="Difference" value={r.difference === null ? '—' : money(r.difference)} tone={r.difference === null ? undefined : agreed ? 'ok' : 'danger'}>
        {r.difference === null ? 'Upload a statement with balances' : agreed ? 'The books agree with the bank' : 'Still to find'}
      </Figure>
    </div>
  );
}

function Figure({ title, value, tone, children }: { title: string; value: string; tone?: 'ok' | 'danger'; children: ReactNode }) {
  return (
    <section className={cx('card p-4', tone === 'ok' && 'ring-2 ring-ok', tone === 'danger' && 'ring-2 ring-danger')} aria-label={title}>
      <h3 className="text-muted">{title}</h3>
      <p className="font-heading text-[26px] leading-tight tabular-nums" data-testid={`figure-${title}`}>
        {value}
      </p>
      <p className="text-note text-muted">{children}</p>
    </section>
  );
}

/** The statement CSV and how to read it; the layout is kept for the bank and offered next time. */
function UploadDialog({ bankId, onClose, onDone }: { bankId: string; onClose(): void; onDone(): void }) {
  const toast = useToast();
  const input = useRef<HTMLInputElement>(null);
  const ids = { bank: useId(), delimiter: useId(), date: useId(), format: useId(), description: useId(), reference: useId(), amount: useId(), debit: useId(), credit: useId(), balance: useId() };
  const saved = useQuery({ queryKey: ['statement-format', bankId], queryFn: () => api.statementFormat(bankId) });
  const [format, setFormat] = useState<StatementFormat | null>(null);
  const [split, setSplit] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [progress, setProgress] = useState<number | null>(null);
  useEffect(() => {
    if (saved.data && !format) {
      setFormat(saved.data);
      setSplit(!saved.data.amountColumn && !!(saved.data.debitColumn || saved.data.creditColumn));
    }
  }, [saved.data, format]);
  const upload = useMutation({
    mutationFn: () => {
      const f = format!;
      return api.importStatement(
        bankId,
        file!,
        { ...f, amountColumn: split ? null : f.amountColumn, debitColumn: split ? f.debitColumn : null, creditColumn: split ? f.creditColumn : null },
        setProgress,
      );
    },
    onSuccess: (r) => {
      toast.ok(`${formatCount(r.lines)} lines from ${formatDate(r.fromDate)} to ${formatDate(r.toDate)} are in. Match them next.`);
      onDone();
      onClose();
    },
    onSettled: () => setProgress(null),
  });
  const set = <K extends keyof StatementFormat>(key: K, value: StatementFormat[K]) => setFormat((f) => (f ? { ...f, [key]: value } : f));
  const text = (key: keyof StatementFormat, id: string, labelText: string, hint?: string) => (
    <Field label={labelText} htmlFor={id} hint={hint}>
      <Input id={id} value={(format?.[key] as string | null) ?? ''} maxLength={60} onChange={(e) => set(key, (e.target.value || null) as never)} />
    </Field>
  );
  return (
    <Dialog
      open
      width="lg"
      title="Upload a statement"
      onClose={onClose}
      onSubmit={() => file && format && upload.mutate()}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Upload} shortcut="Enter" busy={upload.isPending} disabled={!file || !format}>
            {progress !== null && progress < 1 ? `Uploading ${Math.round(progress * 100)}%` : 'Upload'}
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      {!format ? (
        <Skeleton className="h-64" />
      ) : (
        <div className="space-y-3">
          <div className="flex flex-wrap items-center gap-3">
            <input
              ref={input}
              type="file"
              accept=".csv,.txt"
              className="sr-only"
              aria-label="Statement file"
              onChange={(e) => {
                setFile(e.target.files?.[0] ?? null);
                e.target.value = '';
              }}
            />
            <Button icon={FilePlus2} onClick={() => input.current?.click()}>
              Choose the file
            </Button>
            <span className="text-ink">{file ? file.name : 'A CSV file from the bank’s website'}</span>
          </div>
          <p className="text-muted">How the bank writes its file (kept for {format.bankName}; the column names are the first row’s):</p>
          <div className="grid grid-cols-1 gap-x-8 gap-y-3 md:grid-cols-2">
            <Field label="Separator" htmlFor={ids.delimiter}>
              <Select id={ids.delimiter} value={format.delimiter} onChange={(e) => set('delimiter', e.target.value)}>
                <option value=",">Comma</option>
                <option value=";">Semicolon</option>
                <option value={'\t'}>Tab</option>
              </Select>
            </Field>
            {text('dateColumn', ids.date, 'Date column')}
            {text('dateFormat', ids.format, 'Dates written as', 'For example dd/MM/yyyy')}
            {text('descriptionColumn', ids.description, 'Description column')}
            {text('referenceColumn', ids.reference, 'Reference column')}
            {text('balanceColumn', ids.balance, 'Balance column')}
          </div>
          <Checkbox label="Money in and out are in two columns" checked={split} onChange={setSplit} />
          <div className="grid grid-cols-1 gap-x-8 gap-y-3 md:grid-cols-2">
            {split ? (
              <>
                {text('creditColumn', ids.credit, 'Money in column')}
                {text('debitColumn', ids.debit, 'Money out column')}
              </>
            ) : (
              text('amountColumn', ids.amount, 'Amount column', 'Money out written with a minus')
            )}
          </div>
          <Checkbox label="The first row holds the column names" checked={format.hasHeader} onChange={(v) => set('hasHeader', v)} />
          {upload.error ? <FormError error={upload.error} /> : null}
        </div>
      )}
    </Dialog>
  );
}

/** Chooses the ledger line a statement line is: the same amount, nearest date first. */
function MatchDialog({ line, ledger, onClose, onDone }: { line: StatementLineRow; ledger: LedgerLineRow[]; onClose(): void; onDone(): void }) {
  const money = useMoney();
  const toast = useToast();
  const [all, setAll] = useState(false);
  const near = matchCandidates(line, ledger);
  const shown = all ? ledger.filter((l) => !l.matched) : near;
  const match = useMutation({
    mutationFn: (l: LedgerLineRow) => api.matchLine(line.id, l.journalLineId),
    onSuccess: () => {
      toast.ok('Matched.');
      onDone();
      onClose();
    },
  });
  return (
    <Dialog open width="lg" title={`Match ${money(line.amount)} of ${formatDate(line.date)}`} onClose={onClose} footer={<Button onClick={onClose} shortcut="Esc">Cancel</Button>}>
      <div className="space-y-3">
        <p className="text-ink">{line.description ?? ''}</p>
        <Checkbox label="Show every line not yet on a statement" hint="Otherwise only the same amount within ten days" checked={all} onChange={setAll} />
        {shown.length === 0 ? (
          <EmptyState title="Nothing fits">No line of the books has this amount near this day. Enter it instead, or show every line.</EmptyState>
        ) : (
          <ul className="space-y-2" aria-label="Lines of the books">
            {shown.map((l) => (
              <li key={l.journalLineId} className="flex flex-wrap items-center justify-between gap-3 rounded-[14px] border border-line px-4 py-2">
                <span className="min-w-0">
                  <span className="font-semibold">
                    {formatDate(l.date)} · entry {l.number}
                  </span>
                  <span className="block text-muted">{l.memo}</span>
                </span>
                <span className="flex items-center gap-3">
                  <span className="tabular-nums">{money(l.amount)}</span>
                  <Button icon={Link2} busy={match.isPending && match.variables?.journalLineId === l.journalLineId} onClick={() => match.mutate(l)}>
                    This one
                  </Button>
                </span>
              </li>
            ))}
          </ul>
        )}
        {match.error ? <FormError error={match.error} /> : null}
      </div>
    </Dialog>
  );
}

/** Posts what only the bank knew (charges, profit, a direct debit) and matches the line to it. */
function EntryDialog({ line, onClose, onDone }: { line: StatementLineRow; onClose(): void; onDone(): void }) {
  const money = useMoney();
  const toast = useToast();
  const accounts = useAccounts();
  const ids = { account: useId(), memo: useId() };
  const [accountId, setAccountId] = useState<string | null>(null);
  const [memo, setMemo] = useState(line.description ?? '');
  const enter = useMutation({
    mutationFn: () => api.lineEntry(line.id, accountId, memo.trim() || null),
    onSuccess: (entry) => {
      toast.ok(`Entry ${entry.number} is posted and matched.`);
      onDone();
      onClose();
    },
  });
  return (
    <Dialog
      open
      title={`Enter ${money(line.amount)} of ${formatDate(line.date)}`}
      onClose={onClose}
      onSubmit={() => enter.mutate()}
      footer={
        <>
          <Button type="submit" variant="sun" icon={FilePlus2} shortcut="Enter" busy={enter.isPending}>
            Post it
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Account" htmlFor={ids.account} hint={line.amount < 0 ? 'None: bank charges' : 'None: other income (bank profit)'}>
          <AccountSelect id={ids.account} value={accountId} accounts={accounts.data} allowEmpty emptyLabel={line.amount < 0 ? 'Bank charges' : 'Other income'} onChange={setAccountId} />
        </Field>
        <Field label="Description" htmlFor={ids.memo}>
          <Input id={ids.memo} value={memo} maxLength={200} onChange={(e) => setMemo(e.target.value)} />
        </Field>
        {enter.error ? <FormError error={enter.error} /> : null}
      </div>
    </Dialog>
  );
}
