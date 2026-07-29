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
        <p className="text-muted-foreground">Loading…</p>
      </div>
    );
  }

  if (!auth.isAuthenticated) return null;

  // The till runs full-bleed. Back-office chrome would cost the POS list two of the twelve lines it
  // has to show at 1366x768, and a cashier has no use for a sidebar mid-sale.
  if (pathname?.startsWith('/pos')) {
    return <div className="min-h-screen bg-background">{children}</div>;
  }

  return (
    <div className="min-h-screen bg-background">
      <Sidebar />
      <div className={cn('transition-all duration-200', sidebarOpen ? 'ml-64' : 'ml-16')}>
        <Header />
        <main className="p-6">{children}</main>
      </div>
    </div>
  );
}
