'use client';

import { useEffect } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { useAuth } from '@/lib/auth-config';
import { Sidebar, Header } from '@/components/layout/sidebar';
import { BrandingProvider, Watermark } from '@/components/layout/branding';
import { useUIStore } from '@/stores/ui-store';
import { mastersApi } from '@/lib/masters-api';
import { setActiveCurrency, useCurrency } from '@/lib/currency';
import { cn } from '@/lib/utils';

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const auth = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const { sidebarOpen } = useUIStore();

  // Loaded here, once, because every screen below prints money and none of them should be deciding
  // what money looks like for themselves. Failure is silent on purpose: amounts render without a
  // symbol until it arrives, which is incomplete rather than wrong, and no back-office screen should
  // refuse to open because a currency lookup was slow.
  const locationId = auth.user?.locationId;
  const currency = useCurrency();

  useEffect(() => {
    if (!locationId) return;

    let cancelled = false;

    void mastersApi.settings
      .get(locationId)
      .then((snapshot) => {
        const base = snapshot.currencies.find((c) => c.isBaseCurrency) ?? snapshot.currencies[0];
        if (base && !cancelled) {
          setActiveCurrency({ code: base.code, symbol: base.symbol, scale: base.scale });
        }
      })
      .catch(() => {
        // Nothing to say to the user: the symbol is missing, not the money.
      });

    return () => {
      cancelled = true;
    };
  }, [locationId]);

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
          <div className="relative z-10">{children}</div>
          <Watermark layer="over" />
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
            {/*
              Keyed on the currency so the screens below re-render once it is known.

              Most of them format money by calling a function, not by reading a hook, so nothing
              tells them the symbol has arrived — the dashboard rendered its takings as a bare
              "54.83" and stayed that way, while a page opened later showed ₨54.83. Changing the key
              from "" to "PKR" remounts the subtree exactly once, on load, before anyone has typed
              anything into it.
            */}
            <main id="main" tabIndex={-1} key={currency.code}>
              {children}
            </main>
          </div>
        </div>
      </div>
    </BrandingProvider>
  );
}
