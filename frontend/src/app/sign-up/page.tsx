'use client';

import { useState } from 'react';
import { PasswordInput } from '@/components/auth/password-input';
import { useRouter } from 'next/navigation';
import { AuthField, AuthLink, AuthNotice, AuthShell } from '@/components/auth/auth-shell';
import { postAccount, type AccountProblem } from '@/lib/account-api';
import { PasswordRequirements } from '@/components/auth/password-requirements';
import { MIN_PASSWORD, identityParts, meetsPasswordPolicy } from '@/lib/password-policy';


export default function SignUpPage() {
  const router = useRouter();

  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [problem, setProblem] = useState<AccountProblem | null>(null);
  const [busy, setBusy] = useState(false);

  // Checked here as well as on the server because it is the one error the server cannot see: it
  // receives one password and has no idea what the second box said.
  const mismatch = confirm.length > 0 && confirm !== password;

  // The same parts the server refuses to see inside a password: the email's local part and the
  // words of the display name. Both are typed on this very form, so the check can be live here.
  const identity = identityParts(email, displayName);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();

    if (mismatch || !meetsPasswordPolicy(password, identity)) return;

    setBusy(true);
    setProblem(null);

    const result = await postAccount('register', { email, displayName, password });

    setBusy(false);

    if (result.ok) {
      router.push('/sign-up/done');
      return;
    }

    setProblem(result.problem);
  };

  return (
    <AuthShell
      title="Create an account"
      lead="New accounts start in training mode — practise at the till, and nothing you ring up is real until an administrator says otherwise."
      footer={
        <>
          Already have one? <AuthLink href="/">Sign in</AuthLink>
        </>
      }
    >
      {problem ? <AuthNotice tone="error">{describe(problem)}</AuthNotice> : null}

      <form onSubmit={submit} noValidate>
        <AuthField id="displayName" label="Your name">
          <input
            id="displayName"
            className="pos-input w-full"
            value={displayName}
            onChange={(event) => setDisplayName(event.target.value)}
            autoComplete="name"
            required
            autoFocus
          />
        </AuthField>

        <AuthField id="email" label="Email">
          <input
            id="email"
            type="email"
            className="pos-input w-full"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            autoComplete="email"
            required
          />
        </AuthField>

        <AuthField
          id="password"
          label="Password"
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
          />
          <PasswordRequirements id="password-rules" password={password} identity={identity} />
        </AuthField>

        <AuthField id="confirm" label="Password again">
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
          {busy ? 'Creating the account…' : 'Create account'}
        </button>
      </form>
    </AuthShell>
  );
}

function describe(problem: AccountProblem): string {
  if (problem.title === 'registration.disabled') {
    return problem.detail ?? 'This system does not accept self sign-up.';
  }

  // Identity's password rules come back as a list against the "password" key. They are worth showing
  // in full: "does not meet requirements" leaves someone guessing which requirement.
  const passwordErrors = problem.errors?.password;

  if (passwordErrors?.length) {
    return passwordErrors.join(' ');
  }

  return problem.detail ?? 'Something went wrong. Try again.';
}
