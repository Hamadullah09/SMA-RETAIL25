'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Save, Trash2 } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { useAccounts, useMoney } from '@/components/finance';
import { ConflictDialog } from '@/components/masters';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select, TextArea } from '@/components/ui/field';
import { Chip, PageHeader, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { accountTree, headerChoices, label, naturalBalance, type AccountNode } from '@/lib/finance';
import { formatCount } from '@/lib/format';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import type { AccountType, CashFlowSection, FinanceAccount, FinanceAccountRequest } from '@/lib/types';

const TYPES: AccountType[] = ['Asset', 'Liability', 'Equity', 'Income', 'Expense'];

/** The chart of accounts as a tree: headers with their totals, the accounts under them, roles and balances. */
export default function AccountsPage() {
  const { can, canChangeShared } = useSession();
  const money = useMoney();
  const accounts = useAccounts();
  const [text, setText] = useState('');
  const [selected, setSelected] = useState<FinanceAccount | null>(null);
  const [editing, setEditing] = useState<FinanceAccount | 'new' | null>(null);
  const [deleting, setDeleting] = useState<FinanceAccount | null>(null);
  const edit = can(P.financeSetup) && canChangeShared;
  const toast = useToast();
  const queryClient = useQueryClient();

  const rows = useMemo(() => {
    const tree = accountTree(accounts.data ?? []);
    const q = text.trim().toLowerCase();
    return q ? tree.filter((n) => `${n.account.code} ${n.account.name} ${n.account.role ?? ''}`.toLowerCase().includes(q)) : tree;
  }, [accounts.data, text]);

  const remove = useMutation({
    mutationFn: (a: FinanceAccount) => api.deleteFinanceAccount(a.id),
    onSuccess: () => {
      toast.ok('The account is deleted.');
      setDeleting(null);
      setSelected(null);
      void queryClient.invalidateQueries({ queryKey: ['finance-accounts'] });
    },
  });

  return (
    <div>
      <PageHeader title="Chart of accounts" aside={accounts.data && `${formatCount(accounts.data.length)} accounts`} />
      <Toolbar>
        <SearchBox value={text} onChange={setText} placeholder="Code, name or role" />
        {edit && (
          <>
            <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
              New account
            </Button>
            <Button icon={Pencil} disabled={!selected} onClick={() => selected && setEditing(selected)}>
              Change
            </Button>
            <Button icon={Trash2} disabled={!selected} onClick={() => selected && setDeleting(selected)}>
              Delete
            </Button>
          </>
        )}
      </Toolbar>
      <DataTable<AccountNode>
        caption="Chart of accounts"
        rows={rows}
        loading={accounts.isLoading}
        error={accounts.error}
        onRetry={() => void accounts.refetch()}
        rowKey={(n) => n.account.id}
        selectedKey={selected?.id}
        onRowClick={(n) => (selected?.id === n.account.id && edit ? setEditing(n.account) : setSelected(n.account))}
        empty={<EmptyState title="No accounts found">{text ? 'Nothing matches the search.' : null}</EmptyState>}
        columns={[
          {
            key: 'code',
            header: 'Account',
            cell: (n) => (
              <span style={{ paddingLeft: `${n.depth * 1.5}rem` }} className={n.account.isPosting ? '' : 'font-semibold'}>
                {n.account.code} {n.account.name}
              </span>
            ),
          },
          { key: 'type', header: 'Type', cell: (n) => n.account.type, hideBelow: 'sm' },
          {
            key: 'role',
            header: 'Role',
            hideBelow: 'md',
            cell: (n) => (
              <span className="flex flex-wrap gap-1.5">
                {n.account.role && <Chip>{label(n.account.role)}</Chip>}
                {!n.account.isActive && <Chip tone="danger">Inactive</Chip>}
                {!n.account.isPosting && <Chip tone="dark">Header</Chip>}
              </span>
            ),
          },
          { key: 'balance', header: 'Balance', numeric: true, cell: (n) => money(naturalBalance(n.account.type, n.account.isPosting ? n.account.balance : n.total)) },
        ]}
      />
      {can(P.financeSetup) && !canChangeShared && <p className="mt-2 px-1 text-ink">The chart is shared by every store: a super administrator changes it.</p>}
      {editing && <AccountDialog account={editing === 'new' ? null : editing} accounts={accounts.data ?? []} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete ${deleting?.code} ${deleting?.name}?`}
        message={
          <>
            An account that was posted to cannot be deleted; make it inactive instead.
            {remove.error ? <FormError error={remove.error} /> : null}
          </>
        }
        confirmLabel="Delete"
        danger
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting)}
        onClose={() => {
          setDeleting(null);
          remove.reset();
        }}
      />
    </div>
  );
}

function AccountDialog({ account, accounts, onClose }: { account: FinanceAccount | null; accounts: FinanceAccount[]; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const roles = useQuery({ queryKey: ['ledger-roles'], queryFn: () => api.ledgerRoles(), staleTime: Infinity });
  const ids = { code: useId(), name: useId(), type: useId(), parent: useId(), role: useId(), flow: useId(), description: useId() };
  const [form, setForm] = useState<FinanceAccountRequest>({
    code: account?.code ?? '',
    name: account?.name ?? '',
    type: account?.type ?? 'Expense',
    parentId: account?.parentId ?? null,
    isPosting: account?.isPosting ?? true,
    isActive: account?.isActive ?? true,
    role: account?.role ?? null,
    description: account?.description ?? null,
    cashFlow: account?.cashFlow ?? null,
    version: account?.version ?? null,
  });
  const [conflict, setConflict] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const headers = headerChoices(accounts, account?.id);
  const taken = new Set(accounts.filter((a) => a.role && a.id !== account?.id).map((a) => a.role));
  const save = useMutation({
    mutationFn: (body: FinanceAccountRequest) => (account ? api.saveFinanceAccount(account.id, body) : api.addFinanceAccount(body)),
    onSuccess: (saved) => {
      toast.ok(`${saved.code} ${saved.name} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['finance-accounts'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  const set = <K extends keyof FinanceAccountRequest>(key: K, value: FinanceAccountRequest[K]) => setForm((f) => ({ ...f, [key]: value }));

  function submit() {
    if (!form.code.trim() || !form.name.trim()) return setProblem('An account needs a code and a name.');
    setProblem(null);
    save.mutate({ ...form, code: form.code.trim(), name: form.name.trim(), description: form.description?.trim() || null });
  }

  return (
    <Dialog
      open
      width="lg"
      title={account ? `${account.code} ${account.name}` : 'New account'}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Save
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="grid grid-cols-1 gap-x-8 gap-y-3 md:grid-cols-2">
        <Field label="Code" htmlFor={ids.code}>
          <Input id={ids.code} value={form.code} maxLength={20} onChange={(e) => set('code', e.target.value)} />
        </Field>
        <Field label="Name" htmlFor={ids.name}>
          <Input id={ids.name} value={form.name} maxLength={100} onChange={(e) => set('name', e.target.value)} />
        </Field>
        <Field label="Type" htmlFor={ids.type}>
          <Select id={ids.type} value={form.type} onChange={(e) => set('type', e.target.value as AccountType)}>
            {TYPES.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Under" htmlFor={ids.parent}>
          <Select id={ids.parent} value={form.parentId ?? ''} onChange={(e) => set('parentId', e.target.value || null)}>
            <option value="">Nothing (a top account)</option>
            {headers.map((h) => (
              <option key={h.id} value={h.id}>
                {h.code} {h.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Role" htmlFor={ids.role} hint="The account the program posts to by itself for this">
          <Select id={ids.role} value={form.role ?? ''} onChange={(e) => set('role', e.target.value || null)}>
            <option value="">None</option>
            {(roles.data ?? []).map((r) => (
              <option key={r} value={r} disabled={taken.has(r)}>
                {label(r)}
                {taken.has(r) ? ' (another account has it)' : ''}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Cash flow" htmlFor={ids.flow}>
          <Select id={ids.flow} value={form.cashFlow ?? ''} onChange={(e) => set('cashFlow', (e.target.value || null) as CashFlowSection | null)}>
            <option value="">As its type says</option>
            <option value="Operating">Operating</option>
            <option value="Investing">Investing</option>
            <option value="Financing">Financing</option>
          </Select>
        </Field>
        <div className="space-y-1 md:col-span-2">
          <Checkbox label="Posted to" hint="Off for a header, which only adds up the accounts under it" checked={form.isPosting} onChange={(v) => set('isPosting', v)} />
          <Checkbox label="Active" hint="Inactive accounts take no new postings" checked={form.isActive} onChange={(v) => set('isActive', v)} />
        </div>
        <Field label="Description" htmlFor={ids.description} className="md:col-span-2">
          <TextArea id={ids.description} value={form.description ?? ''} maxLength={400} onChange={(e) => set('description', e.target.value)} />
        </Field>
        {(problem || save.error) && (
          <div className="md:col-span-2">{problem ? <p className="text-danger">{problem}</p> : <FormError error={save.error} />}</div>
        )}
      </div>
      <ConflictDialog
        open={conflict}
        what="account"
        onReload={() => {
          void queryClient.invalidateQueries({ queryKey: ['finance-accounts'] });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}
