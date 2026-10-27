'use client';

import { LogOut, Menu, Search, Store } from 'lucide-react';
import Link from 'next/link';
import { useEffect, useState } from 'react';
import { cx } from '@/lib/cx';
import { initials } from '@/lib/format';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import { KeyCap } from '../ui/button';

/** The store picker: every store for `stores.all` users, the home store fixed for everyone else. */
export function StorePicker({ className }: { className?: string }) {
  const { stores, storeId, setStoreId, canSwitch, store, storesLoading } = useStore();
  const { user } = useSession();
  const fixedName = store ? `${store.code} · ${store.name}` : user.storeName ?? (stores.length === 1 ? stores[0].name : 'All stores');
  return (
    <div className={cx('card flex min-h-[56px] min-w-0 items-center gap-2.5 rounded-full px-4', className)}>
      <Store className="h-5 w-5 shrink-0 text-muted" strokeWidth={1.8} aria-hidden="true" />
      {canSwitch ? (
        <>
          <label htmlFor="store-picker" className="sr-only">
            Store
          </label>
          <select
            id="store-picker"
            className="min-w-0 flex-1 cursor-pointer truncate bg-transparent py-2 pr-1 font-semibold outline-none"
            value={storeId ?? ''}
            onChange={(e) => setStoreId(e.target.value || null)}
            disabled={storesLoading}
          >
            <option value="">All stores</option>
            {stores.map((s) => (
              <option key={s.id} value={s.id}>
                {s.code} · {s.name}
              </option>
            ))}
          </select>
        </>
      ) : (
        <span className="min-w-0 flex-1 truncate font-semibold" title="Your home store">
          {fixedName}
        </span>
      )}
    </div>
  );
}

/** The time and date, as large as the desktop's (shown after the page loads, so the build never bakes in a time). */
export function Clock({ className }: { className?: string }) {
  const [now, setNow] = useState<Date | null>(null);
  useEffect(() => {
    setNow(new Date());
    const timer = setInterval(() => setNow(new Date()), 15_000);
    return () => clearInterval(timer);
  }, []);
  return (
    <div className={cx('card flex min-h-[56px] flex-col items-end justify-center rounded-full px-5 leading-tight', className)} aria-live="off">
      <span className="font-heading text-[24px] leading-7 text-ink" suppressHydrationWarning>
        {now ? now.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' }) : ' '}
      </span>
      <span className="text-note text-muted" suppressHydrationWarning>
        {now ? now.toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }) : ' '}
      </span>
    </div>
  );
}

export function GoToButton({ onOpen, className }: { onOpen(): void; className?: string }) {
  return (
    <button type="button" onClick={onOpen} className={cx('btn min-h-[56px] gap-3 border-0 px-5 shadow-card', className)} aria-keyshortcuts="Control+K">
      <Search className="h-5 w-5" strokeWidth={1.9} aria-hidden="true" />
      <span className="max-md:sr-only">Go to</span>
      <KeyCap keys="Ctrl+K" className="max-lg:hidden" />
    </button>
  );
}

/** The signed-in user (avatar, name, role) with the dark Sign out pill, like the desktop's user card. */
export function UserCard({ className, full }: { className?: string; /** Name, role and "Sign out" at every width (the phone's menu). */ full?: boolean }) {
  const { user, signOut } = useSession();
  const [busy, setBusy] = useState(false);
  const name = user.fullName || user.userName;
  return (
    <div className={cx('card flex min-h-[56px] items-center gap-3 rounded-full p-1.5 pl-1.5', className)}>
      <Link href="/account/" className="flex min-w-0 items-center gap-3 rounded-full pr-2 hover:bg-cream" title="Change password">
        <span className="grid h-11 w-11 shrink-0 place-items-center rounded-full bg-sun font-heading text-section font-bold text-ink" aria-hidden="true">
          {initials(name)}
        </span>
        <span className={cx('min-w-0 leading-tight', !full && 'max-lg:hidden')}>
          <span className="block truncate font-semibold">{name}</span>
          <span className="block truncate text-note text-muted">{user.role ?? 'No role'}</span>
        </span>
      </Link>
      <button
        type="button"
        onClick={() => {
          setBusy(true);
          void signOut();
        }}
        disabled={busy}
        className="btn btn-dark ml-auto min-h-[44px] shrink-0 px-4"
      >
        <LogOut className="h-5 w-5" strokeWidth={1.9} aria-hidden="true" />
        <span className={cx(!full && 'max-xl:sr-only')}>Sign out</span>
      </button>
    </div>
  );
}

export function MenuButton({ onOpen }: { onOpen(): void }) {
  return (
    <button type="button" onClick={onOpen} className="btn btn-icon h-14 w-14 rounded-full border-0 shadow-card md:hidden" aria-label="Open the menu">
      <Menu className="h-6 w-6" strokeWidth={1.9} />
    </button>
  );
}
