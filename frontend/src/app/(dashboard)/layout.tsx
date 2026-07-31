'use client';

import { useEffect } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { useAuth } from '@/lib/auth-config';
import { Sidebar, Header } from '@/components/layout/sidebar';
import { useUIStore } from '@/stores/ui-store';
import { cn } from '@/lib/utils';

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const auth = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const { sidebarOpen } = useUIStore();

  useEffect(() => {
    if (!auth.isLoading && !auth.isAuthenticated) {
      router.replace('/');
    }
  }, [auth.isLoading, auth.isAuthenticated, router]);

  if (auth.isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <p className="text-ink-muted">Loading…</p>
      </div>
    );
  }

  if (!auth.isAuthenticated) return null;

  // The till runs full-bleed. Back-office chrome would cost the POS list two of the twelve lines it
  // has to show at 1366x768, and a cashier has no use for a sidebar mid-sale.
  if (pathname?.startsWith('/pos')) {
    return <div className="min-h-screen bg-surface">{children}</div>;
  }

  return (
    <div className="min-h-screen bg-surface">
      {/*
        First thing in the tab order, invisible until focused. Without it, reaching a page's content
        by keyboard means tabbing past ten navigation links on every single page.
      */}
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:left-3 focus:top-3 focus:z-50 focus:rounded focus:bg-accent focus:px-3 focus:py-2 focus:text-body focus:font-medium focus:text-accent-foreground"
      >
        Skip to the page
      </a>

      <Sidebar />

      {/* The offset matches the rail's width by name, so the two cannot drift apart. */}
      <div className={cn('transition-[margin] duration-200', sidebarOpen ? 'ml-sidebar' : 'ml-sidebar-collapsed')}>
        <Header />

        {/*
          No padding here. Every page owns its own, because a browse screen sits flush against the
          chrome and a settings page wants air — and the old uniform p-6 gave the dense screens a
          gutter they then fought with p-2 on the inside.
        */}
        <main id="main" tabIndex={-1}>
          {children}
        </main>
      </div>
    </div>
  );
}
