'use client';

import { useState } from 'react';
import { AuthField, AuthLink, AuthNotice, AuthShell } from '@/components/auth/auth-shell';
import { postAccount, type AccountProblem } from '@/lib/account-api';

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [sent, setSent] = useState(false);
  const [problem, setProblem] = useState<AccountProblem | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();

    setBusy(true);
    setProblem(null);

    const result = await postAccount('forgot-password', { email });

    setBusy(false);

    if (result.ok) {
      setSent(true);
      return;
    }

    setProblem(result.problem);
  };

  if (sent) {
    return (
      <AuthShell
        title="Check your email"
        lead="If that address has an account, a reset link is on its way."
        footer={<AuthLink href="/">Back to sign in</AuthLink>}
      >
        <div className="space-y-3.5 text-body leading-relaxed text-ink-muted">
          {/*
            Phrased as a conditional on purpose, and the server answers the same way whether or not
            the address exists. "No account with that email" is a free list of who works here.
          */}
          <p>
            The link works once and expires shortly. If it has not arrived in a few minutes, check the
            junk folder before asking for another.
          </p>
          <p>
            Still nothing? Your system administrator can set a password directly — that is faster than
            waiting on mail that may not be configured yet.
          </p>
        </div>
      </AuthShell>
    );
  }

  return (
    <AuthShell
      title="Reset your password"
      lead="Tell us the address on the account and we will send a link to set a new password."
      footer={
        <>
          Remembered it? <AuthLink href="/">Sign in</AuthLink>
        </>
      }
    >
      {problem ? (
        <AuthNotice tone="error">
          {problem.title === 'mail.unavailable'
            ? 'This system cannot send mail yet, so password reset is unavailable. Ask your administrator to set your password directly.'
            : (problem.detail ?? 'Something went wrong. Try again.')}
        </AuthNotice>
      ) : null}

      <form onSubmit={submit} noValidate>
        <AuthField id="email" label="Email">
          <input
            id="email"
            type="email"
            className="pos-input w-full"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            autoComplete="email"
            required
            autoFocus
          />
        </AuthField>

        <button type="submit" className="pos-button-primary mt-1 w-full" disabled={busy || email.length === 0}>
          {busy ? 'Sending…' : 'Send the link'}
        </button>
      </form>
    </AuthShell>
  );
}
