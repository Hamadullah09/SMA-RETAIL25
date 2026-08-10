'use client';

import { useSyncExternalStore } from 'react';

/**
 * The shop's money, in one place.
 *
 * Every screen that prints an amount used to decide for itself what money looks like, and they did
 * not agree: the shared helper was fixed to Australian dollars, three back-office pages formatted as
 * US dollars, and the till read the configured symbol. A shop trading in rupees saw all three.
 *
 * So there is one source now. It is a module-level store rather than React context because
 * `formatCurrency(amount)` is called from eighteen files as a plain function, several of them
 * outside a component — a context would have meant changing every call site to a hook, and a
 * refactor that large is how a formatting change becomes a rendering bug.
 *
 * `useCurrency` exists so components re-render when the currency arrives or is edited; anything
 * that only needs to format a number can keep calling the function.
 */
export interface ActiveCurrency {
  code: string;
  symbol: string;

  /** Decimal places, from the currency row. Not every currency has two. */
  scale: number;
}

/**
 * Until the real one loads.
 *
 * Deliberately not a dollar sign. A wrong symbol is read as fact — a cashier seeing $12.95 has no
 * reason to doubt it — whereas a bare number is obviously incomplete and cannot be mistaken for a
 * currency the shop does not use. This shows for one paint, before the location's currency arrives.
 */
const Unknown: ActiveCurrency = { code: '', symbol: '', scale: 2 };

let active: ActiveCurrency = Unknown;

const listeners = new Set<() => void>();

export function setActiveCurrency(currency: ActiveCurrency): void {
  if (
    currency.code === active.code &&
    currency.symbol === active.symbol &&
    currency.scale === active.scale
  ) {
    return;
  }

  active = currency;
  listeners.forEach((listener) => listener());
}

export function getActiveCurrency(): ActiveCurrency {
  return active;
}

/**
 * Formats an amount in the shop's currency.
 *
 * The symbol is placed in front and the digits grouped by the browser's locale. `Intl`'s own
 * currency mode is deliberately not used: it needs a valid ISO code to know the symbol, so an
 * unrecognised or newly-added code renders as the letters "PKR 12.95" rather than the symbol the
 * shopkeeper typed. The symbol is theirs to choose, so it is theirs to print.
 */
export function formatCurrency(amount: number): string {
  const { symbol, scale } = active;
  const sign = amount < 0 ? '-' : '';

  const digits = Math.abs(amount).toLocaleString(undefined, {
    minimumFractionDigits: scale,
    maximumFractionDigits: scale,
  });

  return `${sign}${symbol}${digits}`;
}

/** Subscribes a component to currency changes. */
export function useCurrency(): ActiveCurrency {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    getActiveCurrency,

    // The server render has no shop to ask, and must agree with the first client render or React
    // discards the tree.
    () => Unknown,
  );
}
