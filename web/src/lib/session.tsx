'use client';

/**
 * Who is signed in. The session lives in the server's httpOnly cookie; the app only ever asks `/api/auth/me` who that
 * is. A 401 from any request goes to the sign-in page (see api.ts).
 */
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useMemo, type ReactNode } from 'react';
import { api } from './api';
import { P } from './nav';
import type { UserInfo } from './types';

interface Session {
  user: UserInfo;
  permissions: ReadonlySet<string>;
  can(permission: string): boolean;
  /** Sees every store (super administrator). */
  allStores: boolean;
  signOut(): Promise<void>;
}

const SessionContext = createContext<Session | null>(null);

export const ME_KEY = ['me'] as const;

/** The `/api/auth/me` query; the shell waits for it before showing a page. */
export function useMe() {
  return useQuery({ queryKey: ME_KEY, queryFn: () => api.me(), staleTime: 5 * 60_000 });
}

export function SessionProvider({ user, children }: { user: UserInfo; children: ReactNode }) {
  const queryClient = useQueryClient();

  const signOut = useCallback(async () => {
    try {
      await api.signOut();
    } catch {
      // Signed out already, or the server is away: the cookie goes with it either way.
    }
    queryClient.clear();
    window.location.assign('/sign-in/');
  }, [queryClient]);

  const value = useMemo<Session>(() => {
    const permissions = new Set(user.permissions);
    return {
      user,
      permissions,
      can: (permission) => permissions.has(permission),
      allStores: permissions.has(P.storesAll),
      signOut,
    };
  }, [user, signOut]);

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): Session {
  const session = useContext(SessionContext);
  if (!session) throw new Error('useSession outside SessionProvider');
  return session;
}
