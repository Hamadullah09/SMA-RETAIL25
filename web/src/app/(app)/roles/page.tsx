'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, Save, Trash2 } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useMemo, useState } from 'react';
import { ConflictDialog } from '@/components/masters';
import { Button } from '@/components/ui/button';
import { ConfirmDialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select } from '@/components/ui/field';
import { PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { cx } from '@/lib/cx';
import { useHotkey } from '@/lib/hotkeys';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import type { PermissionInfo, RoleInfo } from '@/lib/types';

/** The desktop's access levels (Setup › Roles). */
const LEVELS = ['0 · trainee (sales are not kept)', '1 · sales', '2 · sales and stock', '3 · supervisor', '4 · administrator'];

const NEW_ROLE: RoleInfo = { id: null, version: null, name: '', description: '', level: 1, isSystem: false, permissions: [P.webSignIn] };

export default function RolesPage() {
  return (
    <Suspense>
      <Roles />
    </Suspense>
  );
}

function Roles() {
  const { allStores, canChangeShared } = useSession();
  const toast = useToast();
  const queryClient = useQueryClient();
  const params = useSearchParams();
  const router = useRouter();
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api.roles() });
  const permissions = useQuery({ queryKey: ['permissions'], queryFn: () => api.permissions(), staleTime: 30 * 60_000 });
  const [currentId, setCurrentId] = useState<string | null>(null);
  const [draft, setDraft] = useState<RoleInfo | null>(null);
  const [dirty, setDirty] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const list = roles.data ?? [];
  const current = currentId === 'new' ? null : list.find((r) => r.id === currentId) ?? null;

  function choose(role: RoleInfo | 'new') {
    if (role === 'new') {
      setCurrentId('new');
      setDraft({ ...NEW_ROLE, permissions: [...NEW_ROLE.permissions] });
    } else {
      setCurrentId(role.id);
      setDraft({ ...role, permissions: [...role.permissions] });
    }
    setDirty(false);
    setProblem(null);
  }

  useEffect(() => {
    if (params.get('new') === '1') {
      choose('new');
      router.replace('/roles/');
    }
    // Only when the address asks.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params]);

  useEffect(() => {
    if (!draft && list.length > 0) choose(list[0]);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [list.length]);

  const groups = useMemo(() => {
    const map: Array<{ name: string; items: PermissionInfo[] }> = [];
    for (const p of permissions.data ?? []) {
      let g = map.find((x) => x.name === p.group);
      if (!g) map.push((g = { name: p.group, items: [] }));
      g.items.push(p);
    }
    return map;
  }, [permissions.data]);

  const save = useMutation({
    mutationFn: (role: RoleInfo) => {
      const body: RoleInfo = { id: role.id, version: role.version, name: role.name, description: role.description, level: role.level, isSystem: role.isSystem, permissions: role.permissions };
      return role.id ? api.saveRole(role.id, body) : api.addRole(body);
    },
    onSuccess: (saved) => {
      toast.ok(`The role ${saved?.name ?? draft?.name ?? ''} is saved.`);
      setDirty(false);
      void queryClient.invalidateQueries({ queryKey: ['roles'] });
      if (saved?.id) {
        setCurrentId(saved.id);
        setDraft({ ...saved, permissions: [...saved.permissions] });
      }
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteRole(id),
    onSuccess: () => {
      toast.ok(`The role ${draft?.name ?? ''} is deleted.`);
      setDeleting(false);
      setDraft(null);
      setCurrentId(null);
      void queryClient.invalidateQueries({ queryKey: ['roles'] });
    },
    onError: (e: Error) => {
      setDeleting(false);
      toast.error(e.message);
    },
  });

  function submit() {
    if (!draft) return;
    const text = !draft.name.trim() ? 'Give the role a name.' : null;
    setProblem(text);
    if (!text) save.mutate({ ...draft, name: draft.name.trim(), description: draft.description?.trim() || null });
  }

  useHotkey('Ctrl+S', submit, !!draft && !save.isPending && canChangeShared);

  function edit(patch: Partial<RoleInfo>) {
    setDraft((d) => (d ? { ...d, ...patch } : d));
    setDirty(true);
  }

  function toggle(key: string, on: boolean) {
    if (!draft) return;
    const set = new Set(draft.permissions);
    if (on) set.add(key);
    else set.delete(key);
    edit({ permissions: [...set] });
  }

  const canDelete = !!current && !current.isSystem && (current.userCount ?? 0) === 0;

  return (
    <div>
      <PageHeader title="Roles" aside={roles.data && `${roles.data.length} roles`} />
      <Toolbar>
        <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => choose('new')} disabled={!canChangeShared}>
          New role
        </Button>
        <Button icon={Save} shortcut="Ctrl+S" onClick={submit} disabled={!draft || !canChangeShared} busy={save.isPending}>
          Save role
        </Button>
        <Button
          icon={Trash2}
          shortcut="Del"
          disabled={!canDelete || !canChangeShared}
          onClick={() => setDeleting(true)}
          title={current?.isSystem ? 'Standard roles cannot be deleted.' : (current?.userCount ?? 0) > 0 ? 'Give its users another role first.' : undefined}
        >
          Delete role
        </Button>
        {!canChangeShared && <span className="text-ink">Roles are shared by every store: a super administrator changes them.</span>}
      </Toolbar>

      {roles.error ? (
        <div className="card">
          <ErrorState error={roles.error} onRetry={() => void roles.refetch()} />
        </div>
      ) : (
        <div className="card grid grid-cols-1 gap-5 p-4 lg:grid-cols-[380px_minmax(0,1fr)] lg:p-5">
          <ul className="flex flex-col gap-1 lg:max-h-[calc(100dvh-290px)] lg:overflow-y-auto lg:pr-2" aria-label="Roles">
            {roles.isLoading &&
              [0, 1, 2, 3, 4].map((i) => (
                <li key={i}>
                  <Skeleton className="h-12 rounded-[14px]" />
                </li>
              ))}
            {currentId === 'new' && (
              <li>
                <span className="flex min-h-[48px] items-center rounded-[14px] bg-charcoal px-4 font-semibold text-white">New role</span>
              </li>
            )}
            {list.map((r) => (
              <li key={r.id ?? r.name}>
                <button
                  type="button"
                  onClick={() => choose(r)}
                  aria-current={r.id === currentId || undefined}
                  className={cx('flex min-h-[48px] w-full items-center rounded-[14px] px-4 text-left', r.id === currentId ? 'bg-charcoal text-white' : 'hover:bg-stone-light')}
                >
                  <span className="min-w-0 truncate">
                    <span className="font-semibold">{r.name}</span> · level {r.level} · {r.userCount ?? 0} users{r.isSystem ? ' · standard' : ''}
                  </span>
                </button>
              </li>
            ))}
          </ul>

          <div className="min-w-0 lg:max-h-[calc(100dvh-290px)] lg:overflow-y-auto lg:pr-2">
            {!draft ? (
              <EmptyState title="Choose a role" />
            ) : (
              <form
                noValidate
                onSubmit={(e) => {
                  e.preventDefault();
                  submit();
                }}
              >
                {/* Read only for those who may not change shared records: a disabled fieldset turns off every box. */}
                <fieldset disabled={!canChangeShared} className="m-0 min-w-0 border-0 p-0">
                  <div className="space-y-3">
                    <Field label="Name" htmlFor="role-name" hint={draft.isSystem ? 'A standard role keeps its name.' : undefined}>
                      <Input id="role-name" value={draft.name} maxLength={40} readOnly={draft.isSystem} onChange={(e) => edit({ name: e.target.value })} />
                    </Field>
                    <Field label="Description" htmlFor="role-desc">
                      <Input id="role-desc" value={draft.description ?? ''} maxLength={120} onChange={(e) => edit({ description: e.target.value })} />
                    </Field>
                    <Field label="Access level" htmlFor="role-level" hint="For the parts of the tills that still go by level.">
                      <Select id="role-level" value={draft.level} onChange={(e) => edit({ level: Number(e.target.value) })} className="max-w-[340px]">
                        {LEVELS.map((text, level) => (
                          <option key={level} value={level}>
                            {text}
                          </option>
                        ))}
                      </Select>
                    </Field>
                  </div>
                  {permissions.isLoading && <Skeleton className="mt-5 h-64 rounded-card" />}
                  {groups.map((group) => (
                    <fieldset key={group.name} className="mt-5">
                      <legend className="mb-2 font-heading text-section">{group.name}</legend>
                      <div className="grid grid-cols-1 gap-x-6 sm:grid-cols-2">
                        {group.items.map((p) => {
                          const locked = p.key === P.storesAll && !allStores;
                          return (
                            <Checkbox
                              key={p.key}
                              label={p.description}
                              hint={locked ? 'Only a super administrator gives this.' : p.key}
                              checked={draft.permissions.includes(p.key)}
                              disabled={locked}
                              onChange={(on) => toggle(p.key, on)}
                            />
                          );
                        })}
                      </div>
                    </fieldset>
                  ))}
                </fieldset>
                <FormError error={problem ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
                {dirty && <p className="mt-4 font-semibold text-ink">Not saved yet: press Save role (Ctrl+S).</p>}
              </form>
            )}
          </div>
        </div>
      )}

      <ConflictDialog
        open={conflict}
        what="role"
        onReload={() => {
          setConflict(false);
          save.reset();
          void roles.refetch().then((r) => {
            const fresh = r.data?.find((x) => x.id === currentId);
            if (fresh) choose(fresh);
          });
        }}
        onClose={() => setConflict(false)}
      />
      <ConfirmDialog
        open={deleting}
        title={`Delete the role ${draft?.name ?? ''}?`}
        message="Nobody has it now. It cannot be brought back."
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => current?.id && remove.mutate(current.id)}
        onClose={() => setDeleting(false)}
      />
    </div>
  );
}
