'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Copy, Divide, Save, Scale } from 'lucide-react';
import { useEffect, useId, useMemo, useState } from 'react';
import { amountOf, MoneyInput, useAccounts, useMoney } from '@/components/finance';
import { Button, IconButton, LinkButton } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select } from '@/components/ui/field';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, TableSkeleton } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { budgetChanges, cellKey, monthLabel, roundMoney, spread } from '@/lib/finance';
import { formatCount } from '@/lib/format';
import { numberText, parseNumber } from '@/lib/forms';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { BudgetInfo, FinanceAccount, YearInfo } from '@/lib/types';

const CREDIT_TYPES = new Set(['Income', 'Liability', 'Equity']);

/**
 * Budgets per account and month, for the store chosen in the header or (all stores) the company's own: typed in, spread
 * from a year's amount, or copied from last year. Income is typed as a positive amount, like the profit and loss shows it.
 */
export default function BudgetsPage() {
  const { can } = useSession();
  const { storeId, store } = useStore();
  const ids = { year: useId() };
  const years = useQuery({ queryKey: ['finance-years'], queryFn: () => api.years() });
  const [yearId, setYearId] = useState<string | null>(null);
  useEffect(() => {
    if (yearId || !years.data?.length) return;
    // The year today is in, else the newest.
    const now = new Date().toISOString().slice(0, 10);
    setYearId((years.data.find((y) => y.startDate <= now && now <= y.endDate) ?? years.data[0]).id);
  }, [yearId, years.data]);
  const year = years.data?.find((y) => y.id === yearId) ?? null;
  const budget = useQuery({ queryKey: ['budget', yearId, storeId], queryFn: () => api.budget(yearId!, storeId), enabled: !!yearId });
  const accounts = useAccounts();

  return (
    <div>
      <PageHeader title="Budgets" aside={store ? `${store.code} ${store.name}` : 'The company’s own budget'} />
      <Toolbar>
        <label htmlFor={ids.year} className="text-muted">
          Year
        </label>
        <Select id={ids.year} className="w-[200px]" value={yearId ?? ''} onChange={(e) => setYearId(e.target.value || null)} disabled={!years.data?.length}>
          {(years.data ?? []).map((y) => (
            <option key={y.id} value={y.id}>
              {y.name}
            </option>
          ))}
        </Select>
        {year && (
          <LinkButton href={`/finance/reports/view/?key=budget-vs-actual&from=${year.startDate}&to=${year.endDate}`} icon={Scale}>
            Budget against actual
          </LinkButton>
        )}
      </Toolbar>
      {!store && <p className="mb-3 px-1 text-ink">With All stores chosen this is the company’s own budget; each store’s is kept apart. Choose a store at the top for its budget.</p>}
      {years.error ? (
        <div className="card">
          <ErrorState error={years.error} onRetry={() => void years.refetch()} />
        </div>
      ) : years.data && years.data.length === 0 ? (
        <div className="card">
          <EmptyState title="No financial year yet">Make the year on Years and periods first.</EmptyState>
        </div>
      ) : budget.error ? (
        <div className="card">
          <ErrorState error={budget.error} onRetry={() => void budget.refetch()} />
        </div>
      ) : accounts.error ? (
        <div className="card">
          <ErrorState error={accounts.error} onRetry={() => void accounts.refetch()} />
        </div>
      ) : !budget.data || !year || !accounts.data ? (
        <div className="grid-card">
          <TableSkeleton rows={8} columns={8} />
        </div>
      ) : (
        <BudgetGrid
          key={`${yearId}-${storeId}-${budget.dataUpdatedAt}`}
          info={budget.data}
          year={year}
          accounts={accounts.data}
          canChange={can(P.financeBudget)}
        />
      )}
    </div>
  );
}

function BudgetGrid({ info, year, accounts, canChange }: { info: BudgetInfo; year: YearInfo; accounts: FinanceAccount[]; canChange: boolean }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const money = useMoney();
  const [cells, setCells] = useState<Map<string, string>>(() => {
    const map = new Map<string, string>();
    const types = new Map(accounts.map((a) => [a.id, a.type]));
    for (const a of info.amounts) map.set(cellKey(a.accountId, a.month), numberText(CREDIT_TYPES.has(types.get(a.accountId) ?? '') ? -a.amount : a.amount));
    return map;
  });
  const [spreading, setSpreading] = useState<FinanceAccount | null>(null);
  const [copying, setCopying] = useState(false);

  // Income and expense accounts that are posted to, and any other account the budget has.
  const rows = useMemo(() => {
    const budgeted = new Set(info.amounts.map((a) => a.accountId));
    return accounts
      .filter((a) => a.isPosting && ((a.isActive && (a.type === 'Income' || a.type === 'Expense')) || budgeted.has(a.id)))
      .sort((a, b) => (a.type === b.type ? a.code.localeCompare(b.code, undefined, { numeric: true }) : a.type === 'Income' ? -1 : b.type === 'Income' ? 1 : a.type.localeCompare(b.type)));
  }, [accounts, info.amounts]);
  const credit = useMemo(() => new Set(accounts.filter((a) => CREDIT_TYPES.has(a.type)).map((a) => a.id)), [accounts]);
  const changes = budgetChanges(info.amounts, cells, credit, parseNumber);
  const value = (accountId: string, month: string) => parseNumber(cells.get(cellKey(accountId, month)) ?? '') ?? 0;
  const rowTotal = (accountId: string) => roundMoney(info.months.reduce((sum, m) => sum + value(accountId, m), 0));
  const monthTotal = (month: string, type: string) => roundMoney(rows.filter((a) => a.type === type).reduce((sum, a) => sum + value(a.id, month), 0));

  const save = useMutation({
    mutationFn: () => api.saveBudget(info.yearId, info.storeId, changes.amounts),
    onSuccess: (saved) => {
      toast.ok(`The budget for ${saved.yearName} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['budget'] });
    },
  });
  const setCell = (accountId: string, month: string, text: string) => setCells((m) => new Map(m).set(cellKey(accountId, month), text));

  return (
    <>
      {canChange && (
        <Toolbar>
          <Button variant="sun" icon={Save} shortcut="Ctrl+S" busy={save.isPending} disabled={changes.amounts.length === 0 || changes.bad.length > 0} onClick={() => save.mutate()}>
            Save
          </Button>
          <Button icon={Copy} onClick={() => setCopying(true)}>
            Copy from last year
          </Button>
          <span className="text-muted" role="status">
            {changes.bad.length > 0 ? 'Some boxes do not hold a number.' : changes.amounts.length > 0 ? `${formatCount(changes.amounts.length)} changes not saved` : 'Everything is saved.'}
          </span>
        </Toolbar>
      )}
      {save.error ? (
        <div className="mb-3">
          <FormError error={save.error} />
        </div>
      ) : null}
      {rows.length === 0 ? (
        <div className="card">
          <EmptyState title="No income or expense accounts">Add them to the chart of accounts first.</EmptyState>
        </div>
      ) : (
        <div className="grid-card">
          <div className="grid-scroll max-h-[calc(100dvh-300px)] overflow-y-auto">
            <table className="grid-table" aria-label={`Budget ${info.yearName}`}>
              <thead>
                <tr>
                  <th scope="col" className="sticky left-0 z-[2]">
                    Account
                  </th>
                  {info.months.map((m) => (
                    <th key={m} scope="col" className="num">
                      {monthLabel(m)}
                    </th>
                  ))}
                  <th scope="col" className="num">
                    Year
                  </th>
                </tr>
              </thead>
              <tbody>
                {rows.map((a) => (
                  <tr key={a.id}>
                    <td className="sticky left-0 z-[1] min-w-[230px] bg-card">
                      <span className="flex items-center gap-2">
                        <span className="flex-1">
                          {a.code} {a.name}
                          <span className="block text-note text-muted">{a.type}</span>
                        </span>
                        {canChange && <IconButton icon={Divide} label={`Spread a year’s amount for ${a.name}`} onClick={() => setSpreading(a)} />}
                      </span>
                    </td>
                    {info.months.map((m) => (
                      <td key={m} className="num w-[120px] min-w-[110px]">
                        {canChange ? (
                          <MoneyInput
                            ariaLabel={`${a.name}, ${monthLabel(m)}`}
                            value={cells.get(cellKey(a.id, m)) ?? ''}
                            invalid={changes.bad.includes(cellKey(a.id, m))}
                            onChange={(text) => setCell(a.id, m, text)}
                          />
                        ) : (
                          value(a.id, m) !== 0 && money(value(a.id, m))
                        )}
                      </td>
                    ))}
                    <td className="num font-semibold" data-testid={`year-${a.code}`}>
                      {money(rowTotal(a.id))}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td className="sticky left-0">Profit</td>
                  {info.months.map((m) => (
                    <td key={m} className="num">
                      {money(monthTotal(m, 'Income') - monthTotal(m, 'Expense'))}
                    </td>
                  ))}
                  <td className="num" data-testid="budget-profit">
                    {money(roundMoney(info.months.reduce((sum, m) => sum + monthTotal(m, 'Income') - monthTotal(m, 'Expense'), 0)))}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>
        </div>
      )}
      {spreading && (
        <SpreadDialog
          account={spreading}
          months={info.months.length}
          onClose={() => setSpreading(null)}
          onSpread={(total) => {
            const parts = spread(total, info.months.length);
            setCells((map) => {
              const next = new Map(map);
              info.months.forEach((m, i) => next.set(cellKey(spreading.id, m), numberText(parts[i])));
              return next;
            });
            setSpreading(null);
          }}
        />
      )}
      {copying && <CopyDialog info={info} year={year} onClose={() => setCopying(false)} />}
    </>
  );
}

function SpreadDialog({ account, months, onSpread, onClose }: { account: FinanceAccount; months: number; onSpread(total: number): void; onClose(): void }) {
  const id = useId();
  const [text, setText] = useState('');
  const total = parseNumber(text);
  return (
    <Dialog
      open
      width="sm"
      title={`Spread ${account.name}`}
      onClose={onClose}
      onSubmit={() => total !== null && onSpread(total)}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Divide} shortcut="Enter" disabled={total === null}>
            Spread
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="For the year" htmlFor={id}>
          <MoneyInput id={id} value={text} onChange={setText} />
        </Field>
        <p className="text-muted">It is shared evenly over the {months} months; the cents left over go on the last one. Save to keep it.</p>
      </div>
    </Dialog>
  );
}

function CopyDialog({ info, year, onClose }: { info: BudgetInfo; year: YearInfo; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const id = useId();
  const [fromBudget, setFromBudget] = useState(false);
  const [percent, setPercent] = useState('0');
  const copy = useMutation({
    mutationFn: () => api.copyBudget(info.yearId, info.storeId, fromBudget, amountOf(percent)),
    onSuccess: (r) => {
      toast.ok(`${formatCount(r.copied)} amounts were copied into ${year.name}.`);
      void queryClient.invalidateQueries({ queryKey: ['budget'] });
      onClose();
    },
  });
  return (
    <Dialog
      open
      title={`Copy last year into ${year.name}`}
      onClose={onClose}
      onSubmit={() => copy.mutate()}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Copy} shortcut="Enter" busy={copy.isPending}>
            Copy
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Checkbox
          label="Copy last year’s budget"
          hint="Otherwise what was really earned and spent last year, month by month"
          checked={fromBudget}
          onChange={setFromBudget}
        />
        <Field label="Raise by (%)" htmlFor={id} hint="0 copies it as it was; 10 adds a tenth">
          <Input id={id} inputMode="decimal" className="w-[120px] text-right" value={percent} onChange={(e) => setPercent(e.target.value)} />
        </Field>
        <p className="text-ink">What {year.name} has now is replaced.</p>
        {copy.error ? <FormError error={copy.error} /> : null}
      </div>
    </Dialog>
  );
}
