'use client';

import { X } from 'lucide-react';
import Image from 'next/image';
import { usePathname, useRouter } from 'next/navigation';
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { ApiError } from '@/lib/api';
import { HotkeyScope, useHotkey, useHotkeyMap } from '@/lib/hotkeys';
import { canOpen, homeFor, navFor, navKeyForPath, pageForPath } from '@/lib/nav';
import { SessionProvider, useMe, useSession } from '@/lib/session';
import { StoreProvider, useStore } from '@/lib/store';
import type { UserInfo } from '@/lib/types';
import { ErrorState, Forbidden, Skeleton } from '../ui/states';
import { CommandPalette } from './command-palette';
import { Clock, GoToButton, MenuButton, StorePicker, UserCard } from './header';
import { NavList } from './nav-panel';

/**
 * The frame every signed-in page sits in, laid out like the desktop app: a header row of white pill cards, the
 * navigation panel on the left and the page on the gradient. Below 1280 px the panel shows icons only; below 768 px it
 * becomes a drawer behind the menu button.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const me = useMe();

  if (me.data) {
    return (
      <SessionProvider user={me.data}>
        <StoreProvider>
          <Frame user={me.data}>{children}</Frame>
        </StoreProvider>
      </SessionProvider>
    );
  }
  if (me.error && !(me.error instanceof ApiError && me.error.status === 401)) {
    return (
      <div className="grid min-h-screen place-items-center p-6">
        <div className="card w-full max-w-lg">
          <ErrorState error={me.error} onRetry={() => void me.refetch()} />
        </div>
      </div>
    );
  }
  // Loading, or on the way to the sign-in page.
  return <ShellSkeleton />;
}

function ShellSkeleton() {
  return (
    <div className="flex h-[100dvh] flex-col gap-4 p-3 md:p-5" aria-busy="true" aria-label="Loading">
      <div className="flex gap-3">
        <Skeleton className="h-14 w-14 rounded-full md:w-[300px]" />
        <Skeleton className="h-14 flex-1 rounded-full md:max-w-[240px]" />
        <Skeleton className="ml-auto h-14 w-14 rounded-full md:w-[280px]" />
      </div>
      <div className="flex min-h-0 flex-1 gap-5">
        <Skeleton className="hidden h-full w-nav-icons rounded-card md:block xl:w-nav" />
        <div className="flex-1 space-y-4">
          <Skeleton className="h-10 w-64" />
          <Skeleton className="h-[50vh] w-full rounded-card" />
        </div>
      </div>
    </div>
  );
}

function Frame({ user, children }: { user: UserInfo; children: ReactNode }) {
  const pathname = usePathname() ?? '/';
  const router = useRouter();
  const { permissions } = useSession();
  const { storesLoading } = useStore();
  const [palette, setPalette] = useState(false);
  const [drawer, setDrawer] = useState(false);

  const pages = useMemo(() => navFor(permissions), [permissions]);
  const page = pageForPath(pathname);
  const current = navKeyForPath(pathname);
  const allowed = !page || canOpen(page, permissions);
  const mustChange = user.mustChangePassword && page?.key !== 'account';
  const toHome = page?.key === 'dashboard' && !allowed;

  useEffect(() => {
    if (mustChange) router.replace('/account/');
    else if (toHome && homeFor(permissions) !== '/') router.replace(homeFor(permissions));
  }, [mustChange, toHome, permissions, router]);

  useEffect(() => setDrawer(false), [pathname]);

  useEffect(() => {
    const title = page?.title ?? 'SMA RETAIL';
    document.title = page && page.key !== 'dashboard' ? `${title} · SMA RETAIL` : 'SMA RETAIL';
  }, [page]);

  useHotkey('Ctrl+K', () => setPalette(true));
  // Ctrl+1 … Ctrl+0 (and Alt+1 … Alt+0, for browsers that keep Ctrl+digits for their tabs).
  const navKeys = useMemo(() => {
    const map: Record<string, () => void> = {};
    for (const p of pages) {
      if (!p.shortcut) continue;
      map[p.shortcut] = () => router.push(p.path);
      map[p.shortcut.replace('Ctrl+', 'Alt+')] = () => router.push(p.path);
    }
    return map;
  }, [pages, router]);
  useHotkeyMap(navKeys);
  useHotkey('/', () => {
    const search = document.querySelector<HTMLInputElement>('[data-list-search]');
    if (search) {
      search.focus();
      search.select();
    }
  });

  let body: ReactNode;
  if (mustChange || toHome || storesLoading) body = <PageSkeleton />;
  else if (!allowed) body = <Forbidden />;
  else body = children;

  return (
    <div className="flex min-h-[100dvh] flex-col md:h-[100dvh] md:min-h-0 print:block print:h-auto">
      <header data-shell="header" className="flex items-center gap-2.5 px-3 pb-2 pt-3 md:gap-3 md:px-5 md:pb-4 md:pt-5">
        <MenuButton onOpen={() => setDrawer(true)} />
        <StorePicker className="flex-1 md:w-[250px] md:flex-none xl:w-nav" />
        <Clock className="max-lg:hidden" />
        <GoToButton onOpen={() => setPalette(true)} />
        <div className="flex-1 max-md:hidden" />
        <UserCard className="max-md:hidden" />
      </header>

      <div className="flex min-h-0 flex-1 gap-5 px-3 pb-3 md:px-5 md:pb-5">
        <nav
          data-shell="nav"
          aria-label="Pages"
          className="card hidden w-nav-icons shrink-0 flex-col overflow-hidden md:flex xl:w-nav"
        >
          <div className="min-h-0 flex-1 overflow-y-auto px-3 py-4 xl:px-[18px]">
            <NavList pages={pages} current={current} collapsible />
          </div>
          <p className="border-t border-line px-[22px] py-3 text-note text-muted max-xl:hidden">
            Keys are shown on every button. Ctrl+K goes anywhere; / finds in a list.
          </p>
        </nav>

        <main data-shell="main" id="main" className="min-w-0 flex-1 md:overflow-y-auto md:pr-1">
          <div className="mx-auto max-w-[1600px] pb-6">{body}</div>
        </main>
      </div>

      {drawer && <Drawer pages={pages} current={current} onClose={() => setDrawer(false)} />}
      <CommandPalette open={palette} onClose={() => setPalette(false)} />
    </div>
  );
}

function PageSkeleton() {
  return (
    <div className="space-y-4" aria-busy="true">
      <Skeleton className="h-11 w-72" />
      <Skeleton className="h-12 w-full max-w-2xl rounded-full" />
      <Skeleton className="h-[55vh] w-full rounded-card" />
    </div>
  );
}

/** The phone's navigation: a drawer from the left with the user card and the pages. */
function Drawer({ pages, current, onClose }: { pages: ReturnType<typeof navFor>; current: string | undefined; onClose(): void }) {
  useEffect(() => {
    const overflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = overflow;
    };
  }, []);
  return (
    <div data-shell="drawer" className="fixed inset-0 z-overlay flex animate-fade-in bg-ink/40 md:hidden" onClick={onClose}>
      <HotkeyScope>
        <DrawerKeys onClose={onClose} />
        <aside
          role="dialog"
          aria-modal="true"
          aria-label="Menu"
          className="flex h-full w-[min(320px,86vw)] animate-slide-in flex-col bg-page shadow-dialog"
          onClick={(e) => e.stopPropagation()}
        >
          <div className="flex items-center gap-3 px-4 pb-2 pt-4">
            <Image src="/sma-mark.png" alt="" width={40} height={40} className="h-10 w-10" />
            <div className="flex-1 leading-tight">
              <p className="font-heading text-section font-bold">SMA RETAIL</p>
              <p className="text-note text-muted">Back office</p>
            </div>
            <button type="button" onClick={onClose} className="btn btn-icon h-12 w-12 rounded-full" aria-label="Close the menu">
              <X className="h-5 w-5" />
            </button>
          </div>
          <div className="px-4 py-2">
            <UserCard />
          </div>
          <nav aria-label="Pages" className="card mx-4 mb-4 min-h-0 flex-1 overflow-y-auto p-3">
            <NavList pages={pages} current={current} onNavigate={onClose} />
          </nav>
        </aside>
      </HotkeyScope>
    </div>
  );
}

function DrawerKeys({ onClose }: { onClose(): void }) {
  useHotkey('Esc', onClose);
  return null;
}
