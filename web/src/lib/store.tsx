'use client';

/**
 * The store picker. Users with `stores.all` choose a store or "All stores"; everybody else works in their home store.
 * The choice is added as `storeId` to every list request, and remembered in this browser.
 */
import { useQuery } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api } from './api';
import { useSession } from './session';
import type { StoreInfo } from './types';

interface StoreScope {
  stores: StoreInfo[];
  storesLoading: boolean;
  /** The chosen store, or null for all the stores the user can see. */
  storeId: string | null;
  store: StoreInfo | null;
  /** Whether the user may switch stores. */
  canSwitch: boolean;
  setStoreId(storeId: string | null): void;
  /** A store to work on when a page needs exactly one (settings, enrolling a till): the chosen one, else the first. */
  defaultStoreId: string | null;
  /** The currency symbol of the chosen store (or, for all stores, of the first one that has one). */
  currencySymbol: string;
}

const StoreContext = createContext<StoreScope | null>(null);

const STORAGE_KEY = 'rp.storeId';

function readSaved(): string | null {
  try {
    return window.localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function save(storeId: string | null): void {
  try {
    if (storeId) window.localStorage.setItem(STORAGE_KEY, storeId);
    else window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Private windows may refuse storage; the choice then lasts until the page is reloaded.
  }
}

export function StoreProvider({ children }: { children: ReactNode }) {
  const { user, allStores } = useSession();
  const storesQuery = useQuery({ queryKey: ['stores'], queryFn: () => api.stores(), staleTime: 5 * 60_000 });
  const stores = useMemo(() => storesQuery.data ?? [], [storesQuery.data]);
  const [chosen, setChosen] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (allStores) setChosen(readSaved());
    setLoaded(true);
  }, [allStores]);

  let storeId: string | null;
  if (allStores) {
    // A remembered store that is gone (or not visible any more) falls back to all stores.
    storeId = chosen && (storesQuery.isLoading || stores.some((s) => s.id === chosen)) ? chosen : null;
  } else {
    storeId = user.storeId ?? (stores.length === 1 ? stores[0].id : null);
  }

  const setStoreId = useCallback(
    (id: string | null) => {
      if (!allStores) return;
      setChosen(id);
      save(id);
    },
    [allStores],
  );

  const defaultStoreId = storeId ?? user.storeId ?? stores[0]?.id ?? null;
  const symbolStore = stores.find((s) => s.id === defaultStoreId);
  const currencySymbol = symbolStore?.currencySymbol ?? stores.find((s) => s.currencySymbol)?.currencySymbol ?? '';

  const value = useMemo<StoreScope>(
    () => ({
      stores,
      storesLoading: storesQuery.isLoading || !loaded,
      storeId,
      store: stores.find((s) => s.id === storeId) ?? null,
      canSwitch: allStores && stores.length > 0,
      setStoreId,
      defaultStoreId,
      currencySymbol,
    }),
    [stores, storesQuery.isLoading, loaded, storeId, allStores, setStoreId, defaultStoreId, currencySymbol],
  );

  return <StoreContext.Provider value={value}>{children}</StoreContext.Provider>;
}

export function useStore(): StoreScope {
  const scope = useContext(StoreContext);
  if (!scope) throw new Error('useStore outside StoreProvider');
  return scope;
}

/** Money formatter bound to the store's currency symbol. */
export function useCurrency(): string {
  return useStore().currencySymbol;
}
