'use client';

import { Suspense, useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { LogIn } from 'lucide-react';
import { AuthField, AuthLink, AuthNotice, AuthShell } from '@/components/auth/auth-shell';
import { PasswordInput } from '@/components/auth/password-input';
import { selfRegistrationEnabled } from '@/lib/account-api';

/**
 * Signing in.
 *
 * This page is the reason the authorization-code flow went. Sign-in used to be rendered by the API
 * in hand-written inline CSS, because it sat behind a redirect on a different origin and could not
 * reach the design system — so the one screen every single person sees first was the one screen
 * that looked like a different product, and every change to it had to be matched by hand and
 * re-hashed for the content-security policy. It is an ordinary page now.
 */
export default function SignInPage() {
  return (
    <Suspense fallback={<AuthShell title="Sign in" lead="Loading…">{null}</AuthShell>}>
      <SignInForm />
    </Suspense>
  );
}

function SignInForm() {
  const params = useSearchParams();

  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  /**
   * Whether to offer creating an account.
   *
   * There is a whole sign-up page, a BFF route and an API endpoint behind this, and nothing in the
   * application linked to any of it — the only way to reach it was to type the URL. The link used
   * to live on the old landing page, which asked the server whether self-registration was switched
   * on before showing it; when that page was collapsed into a redirect the link went with it and
   * was never re-added here. The sign-up page still links back to this one, so the round trip was
   * one-sided.
   *
   * Starts false and appears when the server says yes, rather than the reverse. A link that shows
   * for a moment and then vanishes is worse than one that arrives a moment late, and on a
   * deployment with registration off — the default — it must never appear at all.
   */
  const [canRegister, setCanRegister] = useState(false);

  useEffect(() => {
    let cancelled = false;

    void selfRegistrationEnabled().then((enabled) => {
      if (!cancelled) setCanRegister(enabled);
    });

    return () => {
      cancelled = true;
    };
  }, []);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();

    if (busy || !username.trim() || !password) return;

    setBusy(true);
    setProblem(null);

    let response: Response;

    try {
      response = await fetch('/api/auth/sign-in', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: username.trim(), password }),
      });
    } catch {
      setBusy(false);
      setProblem('Cannot reach the server. Check the connection and try again.');
      return;
    }

    if (!response.ok) {
      const body = (await response.json().catch(() => null)) as { message?: string } | null;

      setBusy(false);
      setPassword('');
      setProblem(body?.message ?? 'That username or password is not right.');
      return;
    }

    // Only ever a path on this app: an open redirect here would let somebody be bounced anywhere
    // after a sign-in that looked entirely legitimate.
    const requested = params.get('returnTo');
    const target = requested && requested.startsWith('/') && !requested.startsWith('//')
      ? requested
      : '/dashboard';

    // A full load, not a client-side navigation.
    //
    // The session provider read "signed out" when this page mounted and holds that answer; a
    // router.replace leaves it holding it, so the dashboard's guard turns straight round and sends
    // us back here — a sign-in that succeeds, sets its cookie, and appears to do nothing. Reloading
    // makes the whole tree read the session it just established. It costs one navigation on an
    // action somebody performs once a shift.
    window.location.assign(target);
  };

  return (
    <AuthShell
      title="Sign in"
      lead="Use the account your manager set up for you."
      footer={
        <>
          <AuthLink href="/forgot-password">Forgotten your password?</AuthLink>

          {canRegister ? (
            <>
              <span className="mx-2 text-ink-faint" aria-hidden>
                ·
              </span>
              No account yet? <AuthLink href="/sign-up">Create one</AuthLink>
            </>
          ) : null}
        </>
      }
    >
      {problem ? <AuthNotice tone="error">{problem}</AuthNotice> : null}

      <form onSubmit={submit} noValidate>
        <AuthField id="username" label="Username or email">
          <input
            id="username"
            name="username"
            className="pos-input w-full"
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            autoComplete="username"
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            required
            autoFocus
          />
        </AuthField>

        <AuthField id="password" label="Password">
          <PasswordInput
            id="password"
            value={password}
            onChange={setPassword}
            autoComplete="current-password"
            required
          />
        </AuthField>

        <button
          type="submit"
          className="pos-button-primary mt-1 w-full"
          disabled={busy || !username.trim() || !password}
        >
          <LogIn className="h-5 w-5" aria-hidden />
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </AuthShell>
  );
}
