'use client';

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { AuthProvider } from '@/lib/auth-config';
import { CommandPalette } from '@/components/shell/command-palette';
import { HelpProvider } from '@/components/help/help-overlay';
import { HotkeyProvider } from '@/lib/hotkeys';
import { Toaster } from '@/components/ui/toaster';
import { useState } from 'react';

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            retry: 1,
            refetchOnWindowFocus: false,
          },
        },
      }),
  );

  return (
    <AuthProvider>
      <QueryClientProvider client={queryClient}>
        {/*
          The hotkey registry used to live inside the till screen, which meant the till was the only
          screen that had shortcuts at all — and a `global` scope that reached exactly one route.
          Hoisted here it is what it always claimed to be: one registry, so Ctrl+H answers on the
          purchase order screen the same way it answers at the counter.

          It is inert where nothing is bound. The listener matches a binding before it calls
          preventDefault, so a back-office page that registers no shortcuts leaves every key alone.
        */}
        <HotkeyProvider>
          {/* Inside the registry, because it binds Ctrl+H; outside `children`, because the panel
              renders over whatever screen is asking for it. */}
          <HelpProvider>
            {children}

            {/* Global: Ctrl+K works from any screen, including the till (doc 08). */}
            <CommandPalette />
            <Toaster />
            <ReactQueryDevtools initialIsOpen={false} />
          </HelpProvider>
        </HotkeyProvider>
      </QueryClientProvider>
    </AuthProvider>
  );
}
