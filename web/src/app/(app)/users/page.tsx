'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Copy, KeyRound, Pencil, Plus, Save, ShieldAlert, Trash2 } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { ConflictDialog } from '@/components/masters';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { formatAgo, formatUtc } from '@/lib/format';
import { useNow } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { RoleInfo, UserRow } from '@/lib/types';

export default function UsersPage() {
  return (
    <Suspense>
      <Users />
    </Suspense>
  );
}

function Users() {
  const { storeId } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const params = useSearchParams();
  const router = useRouter();
  const now = useNow(60_000);
  const [selected, setSelected] = useState<UserRow | null>(null);
  const [editing, setEditing] = useState<UserRow | 'new' | null>(null);
  const [pin, setPin] = useState<UserRow | null>(null);
  const [resetting, setResetting] = useState<UserRow | null>(null);
  const [temporary, setTemporary] = useState<{ user: UserRow; password: string } | null>(null);
  const [deleting, setDeleting] = useState<UserRow | null>(null);

  useEffect(() => {
    if (params.get('new') === '1') {
      setEditing('new');
      router.replace('/users/');
    }
  }, [params, router]);

  const query = useQuery({ queryKey: ['users', storeId], queryFn: () => api.users({ storeId }) });
  const rows = query.data;

  const reset = useMutation({
    mutationFn: (user: UserRow) => api.resetPassword(user.id),
    onSuccess: (result, user) => {
      setResetting(null);
      setTemporary({ user, password: result.temporaryPassword });
      void queryClient.invalidateQueries({ queryKey: ['users'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  const remove = useMutation({
    mutationFn: (user: UserRow) => api.deleteUser(user.id),
    onSuccess: (_, user) => {
      toast.ok(`${user.userName} is deleted.`);
      setDeleting(null);
      setSelected(null);
      void queryClient.invalidateQueries({ queryKey: ['users'] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <div>
      <PageHeader title="Users" aside={rows && `${rows.length} users`} />
      <Toolbar>
        <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
          Add user
        </Button>
        <Button icon={Pencil} shortcut="Ctrl+O" disabled={!selected} onClick={() => selected && setEditing(selected)}>
          Edit user
        </Button>
        <Button icon={KeyRound} disabled={!selected} onClick={() => selected && setResetting(selected)}>
          Reset password
        </Button>
        <Button disabled={!selected} onClick={() => selected && setPin(selected)}>
          Staff code and PIN
        </Button>
        <Button icon={Trash2} shortcut="Del" disabled={!selected} onClick={() => selected && setDeleting(selected)}>
          Delete user
        </Button>
      </Toolbar>
      <DataTable<UserRow>
        rows={rows}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(u) => u.id}
        selectedKey={selected?.id}
        onRowClick={(u) => (selected?.id === u.id ? setEditing(u) : setSelected(u))}
        caption="Users"
        empty={<EmptyState title="No users">Add the first one with Add user.</EmptyState>}
        columns={[
          { key: 'user', header: 'User', cell: (u) => <span className="font-semibold">{u.userName}</span> },
          { key: 'name', header: 'Full name', cell: (u) => u.fullName ?? '', hideBelow: 'sm' },
          { key: 'role', header: 'Role', cell: (u) => u.role ?? '' },
          { key: 'store', header: 'Store', cell: (u) => u.storeCode ?? 'Every store', hideBelow: 'md' },
          { key: 'code', header: 'Staff code', cell: (u) => u.staffCode ?? '', hideBelow: 'lg' },
          {
            key: 'state',
            header: 'State',
            cell: (u) => (
              <span className="flex flex-wrap gap-1.5">
                {!u.isActive && <Chip>Inactive</Chip>}
                {u.locked && <Chip tone="danger">Locked</Chip>}
                {u.mustChangePassword && <Chip tone="warn">Must set password</Chip>}
                {u.isActive && !u.locked && !u.mustChangePassword && <Chip tone="ok">Active</Chip>}
              </span>
            ),
          },
          { key: 'last', header: 'Last sign-in', cell: (u) => <span title={formatUtc(u.lastSignInAtUtc)}>{now ? formatAgo(u.lastSignInAtUtc, now) : ''}</span>, hideBelow: 'xl' },
        ]}
      />
      <p className="mt-3 max-w-4xl px-1 text-ink">
        What a user may do comes from their role (Roles page). A staff code and PIN let them sign in quickly at the till. Five wrong passwords or PINs lock the account for five
        minutes; Reset password unlocks it. Changes reach the tills with their next download.
      </p>

      {editing && <UserDialog user={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {pin && <PinDialog user={pin} onClose={() => setPin(null)} />}
      <ConfirmDialog
        open={!!resetting}
        title={`Reset ${resetting?.userName ?? ''}'s password?`}
        message="The server makes a temporary password and shows it once. The account is unlocked, and they choose a new password when they sign in."
        confirmLabel="Reset"
        cancelLabel="Cancel"
        busy={reset.isPending}
        onConfirm={() => resetting && reset.mutate(resetting)}
        onClose={() => setResetting(null)}
      />
      <TemporaryPasswordDialog value={temporary} onClose={() => setTemporary(null)} />
      <ConfirmDialog
        open={!!deleting}
        title={`Delete ${deleting?.userName ?? ''}?`}
        message="They can no longer sign in, at the tills or here. Their sales keep their name."
        confirmLabel="Delete"
        cancelLabel="Keep"
        danger
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting)}
        onClose={() => setDeleting(null)}
      />
    </div>
  );
}

/** The roles this user may hand out: without stores.all, none that see every store. */
function grantable(roles: RoleInfo[], allStores: boolean): RoleInfo[] {
  return allStores ? roles : roles.filter((r) => !r.permissions.includes(P.storesAll));
}

function UserDialog({ user, onClose }: { user: UserRow | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { allStores, user: me } = useSession();
  const { stores, storeId: currentStore } = useStore();
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api.roles() });
  const [userName, setUserName] = useState(user?.userName ?? '');
  const [fullName, setFullName] = useState(user?.fullName ?? '');
  const [roleId, setRoleId] = useState(user?.roleId ?? '');
  const [storeId, setStoreId] = useState<string>(user ? user.storeId ?? '' : allStores ? currentStore ?? '' : me.storeId ?? '');
  const [isActive, setIsActive] = useState(user?.isActive ?? true);
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  const choices = grantable(roles.data ?? [], allStores);
  useEffect(() => {
    if (!roleId && roles.data) setRoleId(choices.find((r) => r.name === 'Cashier')?.id ?? choices[0]?.id ?? '');
  }, [roleId, roles.data, choices]);

  const save = useMutation({
    mutationFn: () => {
      const body = { userName: userName.trim(), fullName: fullName.trim() || null, roleId: roleId || null, storeId: storeId || null, isActive };
      return user ? api.saveUser(user.id, { ...body, version: user.version }) : api.addUser({ ...body, password });
    },
    onSuccess: () => {
      toast.ok(`${userName.trim()} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['users'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });

  function submit() {
    const text = !userName.trim()
      ? 'Type a user name.'
      : /\s/.test(userName.trim())
        ? 'A user name has no spaces.'
        : !roleId
          ? 'Choose a role.'
          : !user && password.length < 8
            ? 'A password has at least 8 characters.'
            : null;
    setProblem(text);
    if (!text) save.mutate();
  }

  return (
    <Dialog
      open
      title={user ? `Change ${user.userName}` : 'Add user'}
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
      <div className="space-y-3">
        <Field label="User name" htmlFor="user-name">
          <Input id="user-name" value={userName} maxLength={30} autoComplete="off" autoCapitalize="none" onChange={(e) => setUserName(e.target.value)} />
        </Field>
        <Field label="Full name" htmlFor="user-full">
          <Input id="user-full" value={fullName} maxLength={60} onChange={(e) => setFullName(e.target.value)} />
        </Field>
        <Field label="Role" htmlFor="user-role">
          <Select id="user-role" value={roleId} onChange={(e) => setRoleId(e.target.value)}>
            {!roles.data && <option value="">Loading…</option>}
            {user?.roleId && roles.data && !choices.some((r) => r.id === user.roleId) && <option value={user.roleId}>{user.role}</option>}
            {choices.map((r) => (
              <option key={r.id ?? r.name} value={r.id ?? ''}>
                {r.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Home store" htmlFor="user-store" hint={allStores ? undefined : 'You add users to your own store.'}>
          <Select id="user-store" value={storeId} onChange={(e) => setStoreId(e.target.value)} disabled={!allStores}>
            {allStores && <option value="">Every store</option>}
            {stores.map((s) => (
              <option key={s.id} value={s.id}>
                {s.code} · {s.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="" htmlFor="user-active">
          <Checkbox label="Active (may sign in)" checked={isActive} onChange={setIsActive} />
        </Field>
        {!user && (
          <Field label="Password" htmlFor="user-password" hint="At least 8 characters. Give it to them; they can change it on the web (Change password) or at a till.">
            <Input id="user-password" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} />
          </Field>
        )}
      </div>
      <FormError error={problem ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
      <ConflictDialog
        open={conflict}
        what="user"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['users'] });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}

function PinDialog({ user, onClose }: { user: UserRow; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [staffCode, setStaffCode] = useState(user.staffCode ?? '');
  const [pin, setPin] = useState('');
  const [again, setAgain] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () => api.setPin(user.id, { staffCode: staffCode.trim(), pin }),
    onSuccess: () => {
      toast.ok(`${user.userName}'s staff code and PIN are set.`);
      void queryClient.invalidateQueries({ queryKey: ['users'] });
      onClose();
    },
  });
  return (
    <Dialog
      open
      width="sm"
      title="Staff code and PIN"
      onClose={onClose}
      onSubmit={() => {
        const text = !staffCode.trim()
          ? 'Type a staff code.'
          : !/^\d{4,8}$/.test(pin)
            ? 'A PIN has 4 to 8 digits.'
            : pin !== again
              ? 'The two PINs are not the same.'
              : null;
        setProblem(text);
        if (!text) save.mutate();
      }}
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
      <p className="mb-4 text-ink">For {user.fullName || user.userName}&apos;s quick sign-in at the till.</p>
      <div className="space-y-3">
        <Field label="Staff code" htmlFor="pin-code" stacked>
          <Input id="pin-code" value={staffCode} maxLength={10} autoComplete="off" onChange={(e) => setStaffCode(e.target.value)} className="max-w-[200px]" />
        </Field>
        <Field label="PIN" htmlFor="pin-pin" hint="At least 4 digits." stacked>
          <Input id="pin-pin" type="password" inputMode="numeric" autoComplete="new-password" value={pin} onChange={(e) => setPin(e.target.value.replace(/\D/g, '').slice(0, 8))} className="max-w-[200px]" />
        </Field>
        <Field label="PIN again" htmlFor="pin-again" stacked>
          <Input id="pin-again" type="password" inputMode="numeric" autoComplete="new-password" value={again} onChange={(e) => setAgain(e.target.value.replace(/\D/g, '').slice(0, 8))} className="max-w-[200px]" />
        </Field>
      </div>
      <FormError error={problem ?? save.error} />
    </Dialog>
  );
}

function TemporaryPasswordDialog({ value, onClose }: { value: { user: UserRow; password: string } | null; onClose(): void }) {
  const toast = useToast();
  if (!value) return null;
  return (
    <Dialog
      open
      title="Temporary password"
      onClose={onClose}
      footer={
        <>
          <Button
            icon={Copy}
            onClick={() =>
              void navigator.clipboard?.writeText(value.password).then(
                () => toast.ok('The password is copied.'),
                () => toast.error('The browser did not let it be copied.'),
              )
            }
          >
            Copy
          </Button>
          <Button variant="sun" onClick={onClose} shortcut="Esc" data-autofocus>
            Done
          </Button>
        </>
      }
    >
      <p className="text-ink">Give this password to {value.user.fullName || value.user.userName}:</p>
      <p className="my-5 select-all rounded-card bg-card px-4 py-6 text-center font-mono text-[34px] font-bold leading-none tracking-wide shadow-card sm:text-[40px]" data-testid="temporary-password">
        {value.password}
      </p>
      <p className="flex items-start gap-2 text-ink">
        <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0" />
        It is shown only now. They choose a new password when they sign in with it.
      </p>
    </Dialog>
  );
}
