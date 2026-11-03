'use client';

import { useMutation } from '@tanstack/react-query';
import { Check, X } from 'lucide-react';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { Field, Input } from '@/components/ui/field';
import { FormError } from '@/components/ui/states';

/** Approve or reject with an optional note. */
export function DecisionDialog({ title, approve, onClose, onDecide }: { title: string; approve: boolean; onClose(): void; onDecide(note: string | null): Promise<void> }) {
  const [note, setNote] = useState('');
  const decide = useMutation({ mutationFn: () => onDecide(note.trim() || null) });
  return (
    <Dialog
      open
      width="sm"
      title={title}
      onClose={onClose}
      onSubmit={() => decide.mutate()}
      footer={
        <>
          <Button type="submit" variant={approve ? 'sun' : 'danger'} icon={approve ? Check : X} shortcut="Enter" busy={decide.isPending}>
            {approve ? 'Approve' : 'Reject'}
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <Field label="Note" htmlFor="decision-note" stacked>
        <Input id="decision-note" value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} />
      </Field>
      <FormError error={decide.error} />
    </Dialog>
  );
}
