'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Lock, Plus, RotateCcw, ShieldCheck, Trash2, TriangleAlert, Users } from 'lucide-react';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { describeError } from '@/lib/errors';
import { cn } from '@/lib/utils';
import { PageHeader } from '@/components/shell/page-header';
import { EmptyState } from '@/components/ui/states';
import { ConfirmDialog, useConfirm } from '@/components/ui/confirm-dialog';
import type { PermissionEntry, RoleDetail } from '@/types/masters';

/**
 * Roles and what each may do.
 *
 * Authorisation was always by permission, and roles were always rows — but nothing could edit them,
 * so every shop had the same five legacy presets whether they fitted or not. A role is now a page of
 * ticks an administrator owns.
 *
 * Written for whoever runs the shop rather than for whoever built it: the permission names are the
 * plain ones the catalogue carries, grouped the way the menus are, and the risky ones are marked so
 * nobody grants a void or user management without noticing.
 */
export default function RolesPage() {
  const auth = useAuth();
  const allowed = auth.can('users.manage');

  const [roles, setRoles] = useState<RoleDetail[]>([]);
  const [catalogue, setCatalogue] = useState<PermissionEntry[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [loading, setLoading] = useState(false);
  const [creating, setCreating] = useState(false);

  const load = useCallback(async (select?: number) => {
    setLoading(true);

    try {
      const [list, permissions] = await Promise.all([mastersApi.roles.list(), mastersApi.roles.permissions()]);
      setRoles(list);
      setCatalogue(permissions);
      setSelectedId((current) => select ?? current ?? list[0]?.id ?? null);
    } catch (error) {
      toast({ title: 'Could not load roles', description: describeError(error), variant: 'destructive' });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (allowed) void load();
  }, [allowed, load]);

  const selected = roles.find((role) => role.id === selectedId) ?? null;

  if (!allowed) {
    return (
      <div className="p-4 lg:p-6">
        <PageHeader title="Roles and permissions" />
        <section className="pos-panel mt-4">
          <EmptyState
            icon={Lock}
            title="You cannot change roles"
            description="Only someone allowed to manage users can see and change what each role may do."
          />
        </section>
      </div>
    );
  }

  return (
    <div className="space-y-4 p-4 lg:p-6">
      <PageHeader
        title="Roles and permissions"
        description="A role is a list of what someone may do. Give each person one role on the Users screen. Changes reach people within 15 minutes, or straight away when they next sign in."
        actions={
          <>
            <button type="button" className="pos-button" onClick={() => void load()} disabled={loading}>
              <RotateCcw className="h-5 w-5" aria-hidden />
              Refresh
            </button>
            <button type="button" className="pos-button-primary" onClick={() => setCreating(true)}>
              <Plus className="h-5 w-5" aria-hidden />
              New role
            </button>
          </>
        }
      />

      {creating ? (
        <NewRole
          roles={roles}
          onCancel={() => setCreating(false)}
          onCreated={async (role) => {
            setCreating(false);
            await load(role.id);
          }}
        />
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[20rem_1fr]">
        <nav className="pos-panel h-fit overflow-hidden" aria-label="Roles">
          <ul>
            {roles.map((role) => (
              <li key={role.id} className="border-b border-subtle last:border-b-0">
                <button
                  type="button"
                  onClick={() => setSelectedId(role.id)}
                  aria-current={role.id === selectedId ? 'true' : undefined}
                  className={cn(
                    'flex w-full items-start gap-3 px-4 py-3 text-left transition-colors',
                    role.id === selectedId ? 'bg-accent-soft' : 'hover:bg-panel-hover',
                  )}
                >
                  <ShieldCheck
                    className={cn('mt-0.5 h-6 w-6 shrink-0', role.id === selectedId ? 'text-accent' : 'text-ink-muted')}
                    aria-hidden
                  />
                  <span className="min-w-0 flex-1">
                    <span className="flex items-center gap-2">
                      <span className="truncate text-body-lg font-semibold text-ink">{role.name}</span>
                      {role.isBuiltIn ? <span className="pos-badge shrink-0 text-ink-muted">Built in</span> : null}
                    </span>
                    <span className="mt-0.5 block text-body text-ink-muted">
                      {role.memberCount} {role.memberCount === 1 ? 'person' : 'people'} · {role.permissions.length} permissions
                    </span>
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </nav>

        {selected ? (
          <RoleEditor
            key={selected.id}
            role={selected}
            catalogue={catalogue}
            onSaved={(role) => setRoles((current) => current.map((r) => (r.id === role.id ? role : r)))}
            onDeleted={() => void load(roles.find((r) => r.id !== selected.id)?.id)}
          />
        ) : (
          <section className="pos-panel">
            <EmptyState icon={ShieldCheck} title="Pick a role" description="Choose a role on the left to see and change what it may do." />
          </section>
        )}
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------------------- new role */

function NewRole({
  roles,
  onCancel,
  onCreated,
}: {
  roles: RoleDetail[];
  onCancel: () => void;
  onCreated: (role: RoleDetail) => void | Promise<void>;
}) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [copyFrom, setCopyFrom] = useState<string>('');
  const [busy, setBusy] = useState(false);

  const create = async () => {
    setBusy(true);

    try {
      const role = await mastersApi.roles.create({
        name: name.trim(),
        description: description.trim() || null,
        copyFromRoleId: copyFrom ? Number(copyFrom) : null,
      });

      toast({ variant: 'success', title: `Role "${role.name}" created` });
      await onCreated(role);
    } catch (error) {
      toast({ title: 'Could not create the role', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="pos-panel p-4" aria-label="New role">
      <h2 className="text-h3 font-semibold text-ink">New role</h2>

      <div className="mt-3 grid gap-4 md:grid-cols-3">
        <label className="block">
          <span className="mb-1 block text-body font-semibold text-ink">Name</span>
          <input
            value={name}
            onChange={(event) => setName(event.target.value)}
            autoFocus
            className="h-12 w-full rounded-md border border-strong bg-panel px-3 text-body-lg text-ink outline-none focus:border-accent"
            placeholder="For example: Weekend staff"
          />
        </label>

        <label className="block">
          <span className="mb-1 block text-body font-semibold text-ink">What it is for (optional)</span>
          <input
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            className="h-12 w-full rounded-md border border-strong bg-panel px-3 text-body-lg text-ink outline-none focus:border-accent"
          />
        </label>

        <label className="block">
          <span className="mb-1 block text-body font-semibold text-ink">Start from</span>
          <select
            value={copyFrom}
            onChange={(event) => setCopyFrom(event.target.value)}
            className="h-12 w-full rounded-md border border-strong bg-panel px-3 text-body-lg text-ink outline-none focus:border-accent"
          >
            <option value="">Nothing — I will tick each permission</option>
            {roles.map((role) => (
              <option key={role.id} value={String(role.id)}>
                A copy of {role.name}
              </option>
            ))}
          </select>
        </label>
      </div>

      <div className="mt-4 flex justify-end gap-2">
        <button type="button" className="pos-button" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
        <button type="button" className="pos-button-primary" onClick={() => void create()} disabled={busy || !name.trim()}>
          Create role
        </button>
      </div>
    </section>
  );
}

/* ---------------------------------------------------------------------------- role editor */

function RoleEditor({
  role,
  catalogue,
  onSaved,
  onDeleted,
}: {
  role: RoleDetail;
  catalogue: PermissionEntry[];
  onSaved: (role: RoleDetail) => void;
  onDeleted: () => void;
}) {
  const [name, setName] = useState(role.name);
  const [description, setDescription] = useState(role.description ?? '');
  const [ticked, setTicked] = useState(() => new Set(role.permissions));
  const [busy, setBusy] = useState(false);
  const confirmer = useConfirm();

  const groups = useMemo(() => {
    const byGroup = new Map<string, PermissionEntry[]>();

    for (const permission of catalogue) {
      const list = byGroup.get(permission.group) ?? [];
      list.push(permission);
      byGroup.set(permission.group, list);
    }

    return [...byGroup.entries()];
  }, [catalogue]);

  const saved = useMemo(() => new Set(role.permissions), [role.permissions]);
  const added = [...ticked].filter((key) => !saved.has(key));
  const removed = [...saved].filter((key) => !ticked.has(key));
  const unsaved = added.length + removed.length;

  const detailsChanged = name.trim() !== role.name || (description.trim() || null) !== (role.description ?? null);

  const toggle = (key: string) =>
    setTicked((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });

  const setGroup = (entries: PermissionEntry[], on: boolean) =>
    setTicked((current) => {
      const next = new Set(current);
      for (const entry of entries) {
        if (on) next.add(entry.key);
        else next.delete(entry.key);
      }
      return next;
    });

  const savePermissions = async () => {
    setBusy(true);

    try {
      const updated = await mastersApi.roles.setPermissions(role.id, [...ticked]);
      onSaved(updated);
      toast({
        variant: 'success',
        title: `${role.name} saved`,
        description: 'People in this role get the change within 15 minutes, or when they next sign in.',
      });
    } catch (error) {
      toast({ title: 'Could not save permissions', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const saveDetails = async () => {
    setBusy(true);

    try {
      onSaved(await mastersApi.roles.update(role.id, { name: name.trim(), description: description.trim() || null }));
      toast({ variant: 'success', title: 'Role details saved' });
    } catch (error) {
      toast({ title: 'Could not save the role', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const askDelete = () =>
    confirmer.ask(
      {
        subject: role.name,
        consequence: 'The role and its list of permissions are removed. Nobody is in it, so nobody loses access.',
        verb: 'Delete role',
        tone: 'danger',
      },
      async () => {
        try {
          await mastersApi.roles.remove(role.id);
          toast({ variant: 'success', title: `Role "${role.name}" deleted` });
          onDeleted();
        } catch (error) {
          toast({ title: 'Could not delete the role', description: describeError(error), variant: 'destructive' });
        }
      },
    );

  return (
    <section className="pos-panel overflow-hidden" aria-label={`Role ${role.name}`}>
      <div className="border-b border-subtle p-4">
        <div className="grid gap-4 md:grid-cols-2">
          <label className="block">
            <span className="mb-1 block text-body font-semibold text-ink">Name</span>
            <input
              value={name}
              onChange={(event) => setName(event.target.value)}
              disabled={role.isBuiltIn}
              className="h-12 w-full rounded-md border border-strong bg-panel px-3 text-body-lg text-ink outline-none focus:border-accent disabled:bg-panel-sunken disabled:text-ink-muted"
            />
            {role.isBuiltIn ? (
              <span className="mt-1 block text-body text-ink-muted">
                Built-in roles keep their names. What they may do can still be changed below.
              </span>
            ) : null}
          </label>

          <label className="block">
            <span className="mb-1 block text-body font-semibold text-ink">What it is for</span>
            <input
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              className="h-12 w-full rounded-md border border-strong bg-panel px-3 text-body-lg text-ink outline-none focus:border-accent"
            />
          </label>
        </div>

        <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
          <span className="flex items-center gap-2 text-body text-ink-muted">
            <Users className="h-5 w-5" aria-hidden />
            {role.memberCount} {role.memberCount === 1 ? 'person is' : 'people are'} in this role
          </span>

          <span className="flex gap-2">
            {!role.isBuiltIn ? (
              <button
                type="button"
                className="pos-button text-negative-text"
                onClick={askDelete}
                disabled={busy || role.memberCount > 0}
                title={role.memberCount > 0 ? 'Move everyone out of this role first, on the Users screen.' : undefined}
              >
                <Trash2 className="h-5 w-5" aria-hidden />
                Delete role
              </button>
            ) : null}
            <button type="button" className="pos-button" onClick={() => void saveDetails()} disabled={busy || !detailsChanged}>
              Save name and description
            </button>
          </span>
        </div>
      </div>

      <div className="divide-y divide-subtle">
        {groups.map(([group, entries]) => {
          const on = entries.filter((entry) => ticked.has(entry.key)).length;

          return (
            <fieldset key={group} className="p-4">
              <legend className="sr-only">{group}</legend>

              <div className="mb-2 flex items-center justify-between gap-2">
                <h3 className="text-body-lg font-semibold text-ink">
                  {group} <span className="font-normal text-ink-muted">· {on} of {entries.length}</span>
                </h3>
                <span className="flex gap-2">
                  <button type="button" className="pos-button" onClick={() => setGroup(entries, true)} disabled={on === entries.length}>
                    Tick all
                  </button>
                  <button type="button" className="pos-button" onClick={() => setGroup(entries, false)} disabled={on === 0}>
                    Clear all
                  </button>
                </span>
              </div>

              <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
                {entries.map((entry) => (
                  <label
                    key={entry.key}
                    className={cn(
                      'flex min-h-[3rem] cursor-pointer items-center gap-3 rounded-md border px-3 py-2 transition-colors',
                      ticked.has(entry.key) ? 'border-accent bg-accent-soft' : 'border-subtle hover:bg-panel-hover',
                    )}
                  >
                    <input
                      type="checkbox"
                      checked={ticked.has(entry.key)}
                      onChange={() => toggle(entry.key)}
                      className="h-5 w-5 shrink-0 accent-[oklch(var(--accent))]"
                    />
                    <span className="min-w-0 flex-1 text-body text-ink">{entry.description}</span>
                    {entry.isSensitive ? (
                      <span className="flex shrink-0 items-center gap-1 rounded bg-warning-soft px-1.5 py-0.5 text-caption font-semibold text-ink">
                        <TriangleAlert className="h-3.5 w-3.5" aria-hidden />
                        Sensitive
                      </span>
                    ) : null}
                  </label>
                ))}
              </div>
            </fieldset>
          );
        })}
      </div>

      {/* Always in view: a page of ticks that has been changed and not saved is the easiest work there is to lose. */}
      <div className="sticky bottom-0 flex flex-wrap items-center justify-between gap-3 border-t border-subtle bg-panel px-4 py-3">
        <span className={cn('text-body', unsaved > 0 ? 'font-semibold text-ink' : 'text-ink-muted')}>
          {unsaved === 0
            ? 'No changes'
            : `${unsaved} change${unsaved === 1 ? '' : 's'} not saved — ${added.length} added, ${removed.length} removed`}
        </span>
        <span className="flex gap-2">
          <button type="button" className="pos-button" onClick={() => setTicked(new Set(role.permissions))} disabled={busy || unsaved === 0}>
            Undo changes
          </button>
          <button type="button" className="pos-button-primary" onClick={() => void savePermissions()} disabled={busy || unsaved === 0}>
            Save permissions
          </button>
        </span>
      </div>

      <ConfirmDialog
        request={confirmer.request}
        open={confirmer.open}
        onOpenChange={confirmer.setOpen}
        onConfirm={confirmer.confirm}
        busy={confirmer.busy}
      />
    </section>
  );
}
