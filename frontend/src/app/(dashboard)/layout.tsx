'use client';

import { useEffect } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { useAuth } from '@/lib/auth-config';
import { Sidebar, Header } from '@/components/layout/sidebar';
import { BrandingProvider, Watermark } from '@/components/layout/branding';
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
    return (
      <BrandingProvider>
        <div className="relative min-h-screen bg-surface">
          <Watermark />
          <div className="relative z-10">{children}</div>
        </div>
      </BrandingProvider>
    );
  }

  return (
    <BrandingProvider>
      <div className="relative min-h-screen bg-surface">
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

        {/*
          Behind the chrome and the content, above the page's own background.

          Drawn under rather than over, which is the opposite of what a watermark usually means. A
          mark at 20% over body text costs real contrast on every screen, and this application is
          read all day by people who did not choose the logo. Underneath it still reads across the
          gutters and the empty half of a till's item list — which at a counter is most of the
          screen — and it costs nothing anyone has to squint through.
        */}
        <Watermark />

        <div className="relative z-10">
          <Sidebar />

          {/*
            The offset matches the rail's width by name, so the two cannot drift apart — and only
            from `lg` up, because below that the rail is off-canvas. Reserving 240px for a menu that
            is not on screen would push every page off the right of a phone.
          */}
          <div
            className={cn(
              'transition-[margin] duration-200',
              sidebarOpen ? 'lg:ml-sidebar' : 'lg:ml-sidebar-collapsed',
            )}
          >
            <Header />

            {/*
              No padding here. Every page owns its own, because a browse screen sits flush against
              the chrome and a settings page wants air — and the old uniform p-6 gave the dense
              screens a gutter they then fought with p-2 on the inside.
            */}
            <main id="main" tabIndex={-1}>
              {children}
            </main>
          </div>
        </div>
      </div>
    </BrandingProvider>
  );
}
