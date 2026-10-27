'use client';

/**
 * Departments and categories, added and deleted from Inventory. Both are company-wide lists (the server keeps their
 * names in capitals), so only users who may change shared records open this.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { api } from '@/lib/api';
import { Button } from './ui/button';
import { ConfirmDialog, Dialog } from './ui/dialog';
import { Input } from './ui/field';
import { ErrorState, FormError, Skeleton } from './ui/states';
import { useToast } from './ui/toast';

type Kind = 'department' | 'category';

const TEXT: Record<Kind, { title: string; one: string; queryKey: string[]; placeholder: string }> = {
  department: { title: 'Departments', one: 'department', queryKey: ['departments'], placeholder: 'New department' },
  category: { title: 'Categories', one: 'category', queryKey: ['categories'], placeholder: 'New category' },
};

function NameList({ kind }: { kind: Kind }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const text = TEXT[kind];
  const [name, setName] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<{ id: string; name: string } | null>(null);
  const list = useQuery({
    queryKey: kind === 'department' ? ['departments'] : ['categories', ''],
    queryFn: () => (kind === 'department' ? api.departments() : api.categories(null)),
  });

  const refresh = () => void queryClient.invalidateQueries({ queryKey: text.queryKey });
  const add = useMutation({
    mutationFn: (value: string) => (kind === 'department' ? api.addDepartment(value) : api.addCategory(value)),
    onSuccess: (_, value) => {
      toast.ok(`The ${text.one} ${value.toUpperCase()} is added.`);
      setName('');
      refresh();
    },
  });
  const remove = useMutation({
    mutationFn: (id: string) => (kind === 'department' ? api.deleteDepartment(id) : api.deleteCategory(id)),
    onSuccess: () => {
      toast.ok(`The ${text.one} ${deleting?.name ?? ''} is deleted.`);
      setDeleting(null);
      refresh();
    },
    onError: (e: Error) => {
      setDeleting(null);
      toast.error(e.message);
    },
  });

  function submit() {
    const value = name.trim();
    const exists = (list.data ?? []).some((x) => x.name.toUpperCase() === value.toUpperCase());
    const message = !value ? `Type the ${text.one}'s name.` : value.length > 30 ? 'A name has at most 30 characters.' : exists ? `There is a ${text.one} ${value.toUpperCase()} already.` : null;
    setProblem(message);
    if (!message) add.mutate(value);
  }

  return (
    <section aria-label={text.title} className="min-w-0">
      <h3 className="section-title mb-2">{text.title}</h3>
      <div className="flex gap-2">
        <Input
          aria-label={text.placeholder}
          placeholder={text.placeholder}
          value={name}
          maxLength={30}
          onChange={(e) => setName(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault();
              submit();
            }
          }}
        />
        <Button icon={Plus} onClick={submit} busy={add.isPending} className="shrink-0">
          Add
        </Button>
      </div>
      <FormError error={problem ?? add.error} />
      <div className="mt-3 max-h-[46vh] overflow-y-auto rounded-control border border-line bg-card">
        {list.error ? (
          <ErrorState error={list.error} onRetry={() => void list.refetch()} />
        ) : !list.data ? (
          <div className="space-y-2 p-3">
            <Skeleton className="h-8" />
            <Skeleton className="h-8" />
          </div>
        ) : list.data.length === 0 ? (
          <p className="p-4 text-muted">None yet.</p>
        ) : (
          <ul>
            {list.data.map((x) => (
              <li key={x.id} className="flex min-h-[48px] items-center gap-2 border-b border-line/70 pl-4 pr-1 last:border-b-0">
                <span className="min-w-0 flex-1 truncate">{x.name}</span>
                <button
                  type="button"
                  className="grid h-10 w-10 place-items-center rounded-full text-muted hover:bg-danger-light hover:text-danger"
                  aria-label={`Delete ${x.name}`}
                  title={`Delete ${x.name}`}
                  onClick={() => setDeleting({ id: x.id, name: x.name })}
                >
                  <Trash2 className="h-5 w-5" />
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
      <ConfirmDialog
        open={!!deleting}
        title={`Delete the ${text.one} ${deleting?.name ?? ''}?`}
        message="It leaves the list here, and at the tills after their next download."
        confirmLabel="Delete"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </section>
  );
}

export function ListsDialog({ onClose }: { onClose(): void }) {
  return (
    <Dialog
      open
      width="lg"
      title="Departments and categories"
      onClose={onClose}
      footer={
        <Button variant="sun" onClick={onClose} shortcut="Esc">
          Done
        </Button>
      }
    >
      <p className="mb-4 text-ink">Every store shares these lists. They reach the tills with the next download.</p>
      <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
        <NameList kind="department" />
        <NameList kind="category" />
      </div>
    </Dialog>
  );
}
