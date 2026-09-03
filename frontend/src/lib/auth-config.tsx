'use client';

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

/**
 * The client's view of the session.
 *
 * It knows who is signed in and what they may do — and nothing else. There is no token here, no
 * refresh timer and no silent-renew iframe, because all of that lives on the BFF (doc 07). This
 * replaced a `react-oidc-context` provider that kept tokens in localStorage, which the brief forbids.
 */

export interface SessionUser {
  sub: string;
  name: string;
  email?: string;
  staffId?: number;
  locationId?: number;
  accessLevel?: number;
  roles: string[];
  permissions: string[];
}

interface AuthState {
  user: SessionUser | null;
  isLoading: boolean;
  isAuthenticated: boolean;
  /** Convenience for hiding affordances. Never a security control — the server checks every command. */
  can: (permission: string) => boolean;
  signIn: (returnTo?: string) => void;
  signOut: () => Promise<void>;
  refresh: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const refresh = useCallback(async () => {
    try {
      const response = await fetch('/api/auth/session', { cache: 'no-store' });
      const payload = (await response.json()) as { authenticated: boolean; user?: SessionUser };

      setUser(payload.authenticated && payload.user ? payload.user : null);
    } catch {
      setUser(null);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const signIn = useCallback((returnTo?: string) => {
    const target = returnTo ?? window.location.pathname + window.location.search;
    window.location.href = `/sign-in?returnTo=${encodeURIComponent(target)}`;
  }, []);

  const signOut = useCallback(async () => {
    const response = await fetch('/api/auth/logout', { method: 'POST' });
    const payload = (await response.json()) as { endSessionUrl?: string };

    setUser(null);

    // The local cookie is already gone; this ends the session at the identity provider too, so
    // signing back in asks for credentials rather than silently resuming.
    window.location.href = payload.endSessionUrl ?? '/';
  }, []);

  const permissions = useMemo(() => new Set(user?.permissions ?? []), [user?.permissions]);

  const value = useMemo<AuthState>(
    () => ({
      user,
      isLoading,
      isAuthenticated: user !== null,
      can: (permission: string) => permissions.has(permission),
      signIn,
      signOut,
      refresh,
    }),
    [user, isLoading, permissions, signIn, signOut, refresh],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext);

  if (!context) {
    throw new Error('useAuth must be used inside an AuthProvider.');
  }

  return context;
}
