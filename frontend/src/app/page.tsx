'use client';

import { Suspense, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { AuthShell } from '@/components/auth/auth-shell';
import { useAuth } from '@/lib/auth-config';

/**
 * The entry point, which is now only a fork in the road.
 *
 * This used to be a screen: a "Continue to sign in" button, a note explaining that the next page
 * would look different because it was served by an authorization server on another origin, and a
 * "Create one" link that asked the server whether self-registration was even switched on.
 *
 * All of that existed to soften a redirect that no longer happens. Sign-in is an ordinary page in
 * this application now, so the warning is untrue, and the click is a second screen that says nothing
 * the first one did not. What is left is the decision — signed in, or not — and a held shape while
 * the session check answers.
 */
export default function LandingPage() {
  return (
    <Suspense fallback={<Waiting />}>
      <Redirect />
    </Suspense>
  );
}

function Redirect() {
  const { isAuthenticated, isLoading } = useAuth();
  const router = useRouter();

  useEffect(() => {
    // Nothing to decide until the session check has answered. Redirecting on an unknown state sends
    // somebody who is signed in to the sign-in form for a moment on every cold load.
    if (isLoading) return;

    router.replace(isAuthenticated ? '/dashboard' : '/sign-in');
  }, [isAuthenticated, isLoading, router]);

  return <Waiting />;
}

/**
 * The shell, with nothing in it.
 *
 * A blank page for the half-second before the redirect reads as a broken load; the same frame the
 * next screen uses reads as the application starting.
 */
function Waiting() {
  return (
    <AuthShell title="SMA Retail" lead="Opening…">
      <span className="sr-only" role="status">
        Checking whether you are signed in.
      </span>
    </AuthShell>
  );
}
