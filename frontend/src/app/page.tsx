'use client';

import { Suspense, useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { AuthLink, AuthNotice, AuthShell } from '@/components/auth/auth-shell';
import { useAuth } from '@/lib/auth-config';

/**
 * The entry point.
 *
 * "Sign in" is a link to the BFF, not a form: the redirect, the PKCE verifier and the code exchange
 * all happen server-side, and the password is typed on the identity provider's own origin. So this
 * page has no credential to hold and nothing to leak (doc 07 §Topology).
 *
 * The other two account screens are ordinary forms, because neither of them submits an existing
 * password — one sets a brand new one, the other submits only an email address.
 */
const AUTH_ERRORS: Record<string, string> = {
  access_denied: 'Sign-in was cancelled.',
  state_mismatch: 'That sign-in link did not match this browser. Try again.',
  token_exchange_failed: 'The server could not complete the sign-in. Try again.',
  invalid_callback: 'That sign-in link was incomplete. Try again.',
  authorization_failed: 'Sign-in failed. Try again.',
};

export default function LoginPage() {
  return (
    <Suspense fallback={<AuthShell title="Sign in" lead="Loading…">{null}</AuthShell>}>
      <LoginContent />
    </Suspense>
  );
}

function LoginContent() {
  const { isAuthenticated, isLoading, signIn } = useAuth();
  const router = useRouter();
  const params = useSearchParams();

  const error = params.get('authError');

  useEffect(() => {
    if (isAuthenticated) {
      router.replace('/dashboard');
    }
  }, [isAuthenticated, router]);

  return (
    <AuthShell
      title="Sign in"
      lead="You will be taken to the secure sign-in page to enter your password."
      footer={
        <div className="space-y-1">
          <p>
            Forgotten it? <AuthLink href="/forgot-password">Reset your password</AuthLink>
          </p>
          <p>
            No account yet? <AuthLink href="/sign-up">Create one</AuthLink>
          </p>
        </div>
      }
    >
      {error ? <AuthNotice tone="error">{AUTH_ERRORS[error] ?? 'Sign-in failed. Try again.'}</AuthNotice> : null}

      <button
        type="button"
        onClick={() => signIn('/dashboard')}
        className="pos-button-primary w-full"
        disabled={isLoading}
      >
        {isLoading ? 'Checking…' : 'Continue to sign in'}
      </button>

      {/*
        Said plainly rather than left as a surprise. Being bounced to a different-looking page at the
        moment you are asked for a password is exactly what a phishing flow looks like, so the reason
        it happens is worth one sentence.
      */}
      <p className="mt-4 text-caption text-ink-faint">
        Passwords are only ever entered on the sign-in page itself, never on this one.
      </p>
    </AuthShell>
  );
}
