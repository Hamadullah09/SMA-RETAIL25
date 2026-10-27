'use client';

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { KeyRound, Save } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Field, Input } from '@/components/ui/field';
import { PageHeader } from '@/components/ui/page';
import { FormError } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api } from '@/lib/api';
import { homeFor } from '@/lib/nav';
import { ME_KEY, useSession } from '@/lib/session';
import type { UserInfo } from '@/lib/types';

/** Change password. A user whose password was reset (or brought in) lands here first and cannot go on until it is done. */
export default function AccountPage() {
  const { user, permissions } = useSession();
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [again, setAgain] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const forced = user.mustChangePassword;

  const change = useMutation({
    mutationFn: () => api.changePassword({ currentPassword: current, newPassword: next }),
    onSuccess: () => {
      toast.ok('Your password is changed.');
      setCurrent('');
      setNext('');
      setAgain('');
      queryClient.setQueryData<UserInfo>(ME_KEY, (u) => (u ? { ...u, mustChangePassword: false } : u));
      void queryClient.invalidateQueries({ queryKey: ME_KEY });
      if (forced) router.replace(homeFor(permissions));
    },
  });

  return (
    <div className="max-w-3xl">
      <PageHeader title="Change password" aside={user.fullName || user.userName} />
      {forced && (
        <p className="mb-4 flex items-start gap-3 rounded-card bg-sun-light px-5 py-4 font-semibold text-ink" role="status">
          <KeyRound className="mt-0.5 h-5 w-5 shrink-0" />
          Choose a new password before you go on. The one you signed in with was temporary.
        </p>
      )}
      <form
        className="card space-y-3 p-5 sm:p-6"
        noValidate
        onSubmit={(e) => {
          e.preventDefault();
          const text = !current
            ? 'Type your current password.'
            : next.length < 8
              ? 'A password has at least 8 characters.'
              : next !== again
                ? 'The two new passwords are not the same.'
                : next === current
                  ? 'The new password is the same as the current one.'
                  : null;
          setProblem(text);
          if (!text) change.mutate();
        }}
      >
        <input type="text" name="username" autoComplete="username" value={user.userName} readOnly hidden />
        <Field label="Current password" htmlFor="pw-current">
          <Input id="pw-current" type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} autoFocus />
        </Field>
        <Field label="New password" htmlFor="pw-new" hint="At least 8 characters. A short sentence is easy to remember and hard to guess.">
          <Input id="pw-new" type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} />
        </Field>
        <Field label="New password again" htmlFor="pw-again">
          <Input id="pw-again" type="password" autoComplete="new-password" value={again} onChange={(e) => setAgain(e.target.value)} />
        </Field>
        <FormError error={problem ?? change.error} />
        <div className="flex justify-end pt-2">
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={change.isPending}>
            Change password
          </Button>
        </div>
      </form>
      <p className="mt-3 px-1 text-ink">The new password works here and, after the next download, at the tills.</p>
    </div>
  );
}
