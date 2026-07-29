'use client';

import { Suspense, useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useAuth } from '@/lib/auth-config';

/**
 * The entry point.
 *
 * "Sign in" is a link to the BFF, not a JavaScript flow: the redirect, the PKCE verifier and the code
 * exchange all happen server-side, so this page has no credential to hold and nothing to leak
 * (doc 07 §Topology).
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
    <Suspense fallback={<Centered>Loading…</Centered>}>
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
      router.replace('/pos');
    }
  }, [isAuthenticated, router]);

  if (isLoading) {
    return <Centered>Loading…</Centered>;
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-[rgb(var(--surface))] p-4">
      <div className="pos-panel w-full max-w-sm p-6 text-center">
        <h1 className="text-lg font-semibold">Retail25</h1>
        <p className="mb-6 mt-1 text-sm text-[rgb(var(--text-muted))]">Point of sale, inventory and accounts</p>

        {error ? (
          <p
            role="alert"
            className="mb-4 rounded-sm px-2 py-1.5 text-sm"
            style={{ backgroundColor: 'rgb(var(--negative) / 0.1)', color: 'rgb(var(--negative))' }}
          >
            {AUTH_ERRORS[error] ?? 'Sign-in failed. Try again.'}
          </p>
        ) : null}

        <button type="button" onClick={() => signIn('/pos')} className="pos-button-primary w-full text-base">
          Sign in
        </button>
      </div>
    </div>
  );
}

function Centered({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen items-center justify-center">
      <p className="text-sm text-[rgb(var(--text-muted))]">{children}</p>
    </div>
  );
}
