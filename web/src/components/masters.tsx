'use client';

/**
 * What the Clients and Suppliers pages share: the list toolbar (search, show deleted, New, Edit, Delete, Undelete),
 * the confirm dialogs for deleting and bringing back, and the "someone else changed it" question.
 */
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Trash2, Undo2 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Button } from './ui/button';
import { ConfirmDialog } from './ui/dialog';
import { SearchBox, Toolbar } from './ui/page';
import { useToast } from './ui/toast';

export function MasterToolbar({
  text,
  onText,
  searchPlaceholder,
  deleted,
  onDeleted,
  canEdit,
  hasSelection,
  newLabel,
  onNew,
  onEdit,
  onDelete,
  onUndelete,
}: {
  text: string;
  onText(text: string): void;
  searchPlaceholder: string;
  deleted: boolean;
  onDeleted(deleted: boolean): void;
  canEdit: boolean;
  hasSelection: boolean;
  newLabel: string;
  onNew(): void;
  onEdit(): void;
  onDelete(): void;
  onUndelete(): void;
}) {
  return (
    <>
      <Toolbar>
        <SearchBox value={text} onChange={onText} placeholder={searchPlaceholder} />
        <label className="flex cursor-pointer items-center gap-2.5 px-1">
          <input type="checkbox" className="check" checked={deleted} onChange={(e) => onDeleted(e.target.checked)} />
          Show deleted
        </label>
      </Toolbar>
      <Toolbar>
        {canEdit && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={onNew}>
            {newLabel}
          </Button>
        )}
        <Button icon={Pencil} shortcut="Ctrl+O" disabled={!hasSelection || deleted} onClick={onEdit}>
          {canEdit ? 'Edit' : 'Open'}
        </Button>
        {canEdit && (
          <>
            <Button icon={Trash2} shortcut="Del" disabled={!hasSelection || deleted} onClick={onDelete}>
              Delete
            </Button>
            <Button icon={Undo2} shortcut="Ctrl+U" disabled={!hasSelection || !deleted} onClick={onUndelete}>
              Undelete
            </Button>
          </>
        )}
      </Toolbar>
    </>
  );
}

/** Delete or undelete a record after asking; refreshes the list query afterwards. */
export function useDeleteUndelete({
  listKey,
  remove,
  restore,
}: {
  listKey: string;
  remove(id: string): Promise<unknown>;
  restore(id: string): Promise<unknown>;
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [target, setTarget] = useState<{ id: string; name: string; mode: 'delete' | 'undelete' } | null>(null);
  const mutation = useMutation({
    mutationFn: (t: { id: string; mode: 'delete' | 'undelete' }) => (t.mode === 'delete' ? remove(t.id) : restore(t.id)),
    onSuccess: (_, t) => {
      toast.ok(t.mode === 'delete' ? `${target?.name ?? 'It'} is deleted. Show deleted brings it back.` : `${target?.name ?? 'It'} is back.`);
      setTarget(null);
      void queryClient.invalidateQueries({ queryKey: [listKey] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const dialog: ReactNode = (
    <ConfirmDialog
      open={!!target}
      title={target?.mode === 'delete' ? `Delete ${target.name}?` : `Bring back ${target?.name ?? ''}?`}
      message={
        target?.mode === 'delete'
          ? 'It leaves the list and the tills with the next download. Its history stays, and Undelete brings it back.'
          : 'It returns to the list and goes down to the tills with the next download.'
      }
      confirmLabel={target?.mode === 'delete' ? 'Delete' : 'Undelete'}
      cancelLabel="Cancel"
      danger={target?.mode === 'delete'}
      busy={mutation.isPending}
      onConfirm={() => target && mutation.mutate(target)}
      onClose={() => setTarget(null)}
    />
  );

  return {
    askDelete: (id: string, name: string) => setTarget({ id, name, mode: 'delete' }),
    askUndelete: (id: string, name: string) => setTarget({ id, name, mode: 'undelete' }),
    dialog,
  };
}

/** The question a 409 asks. */
export function ConflictDialog({ open, what, onReload, onClose }: { open: boolean; what: string; onReload(): void; onClose(): void }) {
  return (
    <ConfirmDialog
      open={open}
      title={`Someone else changed this ${what}. Reload?`}
      message="Reloading brings in their changes and drops yours. Choose No to keep your changes on screen and copy them first."
      confirmLabel="Reload"
      cancelLabel="No"
      onConfirm={onReload}
      onClose={onClose}
    />
  );
}
