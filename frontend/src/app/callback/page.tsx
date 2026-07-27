'use client';

import { useAuth } from 'react-oidc-context';
import { useRouter } from 'next/navigation';
import { useEffect } from 'react';

export default function CallbackPage() {
  const auth = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (auth.isAuthenticated) {
      router.replace('/pos');
    } else if (!auth.isLoading && auth.error) {
      router.replace('/');
    }
  }, [auth.isAuthenticated, auth.isLoading, auth.error, router]);

  return (
    <div className="min-h-screen flex items-center justify-center">
      <p className="text-muted-foreground">Signing in...</p>
    </div>
  );
}
