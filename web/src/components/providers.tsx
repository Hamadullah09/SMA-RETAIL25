'use client';

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { ApiError } from '@/lib/api';
import { HotkeysProvider } from '@/lib/hotkeys';
import { ToastProvider } from './ui/toast';

/** Retries only what may work a second time: a dropped connection or a server error, once. */
function retry(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError && error.status > 0 && error.status < 500) return false;
  return failureCount < 1;
}

export function Providers({ children }: { children: ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: { retry, staleTime: 15_000, refetchOnWindowFocus: false },
          mutations: { retry: false },
        },
      }),
  );
  return (
    <QueryClientProvider client={client}>
      <HotkeysProvider>
        <ToastProvider>{children}</ToastProvider>
      </HotkeysProvider>
    </QueryClientProvider>
  );
}
