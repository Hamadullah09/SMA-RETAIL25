'use client';

import { Suspense, useState } from 'react';
import { PasswordInput } from '@/components/auth/password-input';
import { useSearchParams } from 'next/navigation';
import { AuthField, AuthLink, AuthNotice, AuthShell } from '@/components/auth/auth-shell';
import { postAccount, type AccountProblem } from '@/lib/account-api';
import { PasswordRequirements } from '@/components/auth/password-requirements';
import { MIN_PASSWORD, identityParts, meetsPasswordPolicy } from '@/lib/password-policy';

export default function ResetPasswordPage() {
  // useSearchParams needs a Suspense boundary or the whole route opts out of static rendering and
  // the build fails.
  return (
    <Suspense fallback={<AuthShell title="Set a new password" lead="Loading…">{null}</AuthShell>}>
      <ResetPasswordForm />
    </Suspense>
  );
}

function ResetPasswordForm() {
  const params = useSearchParams();

  const email = params.get('email') ?? '';
  const token = params.get('token') ?? '';

  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [done, setDone] = useState(false);
  const [problem, setProblem] = useState<AccountProblem | null>(null);
  const [busy, setBusy] = useState(false);

  const mismatch = confirm.length > 0 && confirm !== password;
  const identity = identityParts(email);

  // A link that lost its query string cannot be redeemed, and finding that out only after typing a
  // password twice is a poor way to learn it.
  if (!email || !token) {
    return (
      <AuthShell
        title="That link is incomplete"
        lead="It may have been cut short by an email client."
        footer={<AuthLink href="/forgot-password">Ask for a new link</AuthLink>}
      >
        <p className="text-body leading-relaxed text-ink-muted">
          Open the link directly from the message rather than copying part of it, or request another.
        </p>
      </AuthShell>
    );
  }

  if (done) {
    return (
      <AuthShell
        title="Password changed"
        lead="Sign in with the new one."
        footer={<AuthLink href="/">Go to sign in</AuthLink>}
      >
        <p className="text-body leading-relaxed text-ink-muted">
          Anywhere that account was still signed in has been signed out, including this browser. That
          is deliberate — if someone else had it, they no longer do.
        </p>
      </AuthShell>
    );
  }

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();

    if (mismatch || !meetsPasswordPolicy(password, identity)) return;

    setBusy(true);
    setProblem(null);

    const result = await postAccount('reset-password', { email, token, password });

    setBusy(false);

    if (result.ok) {
      setDone(true);
      return;
    }

    setProblem(result.problem);
  };

  return (
    <AuthShell
      title="Set a new password"
      lead={`For ${email}.`}
      footer={<AuthLink href="/">Back to sign in</AuthLink>}
    >
      {problem ? (
        <AuthNotice tone="error">
          {problem.errors?.password?.join(' ') ?? problem.detail ?? 'Something went wrong. Try again.'}
        </AuthNotice>
      ) : null}

      <form onSubmit={submit} noValidate>
        <AuthField
          id="password"
          label="New password"
          hint="Longer beats complicated."
        >
          <PasswordInput
            id="password"
            value={password}
            onChange={setPassword}
            autoComplete="new-password"
            describedBy="password-rules"
            minLength={MIN_PASSWORD}
            required
            autoFocus
          />
          <PasswordRequirements id="password-rules" password={password} identity={identity} />
        </AuthField>

        <AuthField id="confirm" label="New password again">
          <PasswordInput
            id="confirm"
            value={confirm}
            onChange={setConfirm}
            autoComplete="new-password"
            invalid={mismatch}
            describedBy={mismatch ? 'confirm-error' : undefined}
            required
          />
          {mismatch ? (
            <p id="confirm-error" role="alert" className="mt-1.5 text-caption text-negative">
              These two do not match.
            </p>
          ) : null}
        </AuthField>

        <button
          type="submit"
          className="pos-button-primary mt-1 w-full"
          disabled={busy || mismatch || !meetsPasswordPolicy(password, identity)}
        >
          {busy ? 'Saving…' : 'Change password'}
        </button>
      </form>
    </AuthShell>
  );
}
