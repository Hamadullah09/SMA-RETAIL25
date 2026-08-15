'use client';

import { useCallback, useEffect, useState } from 'react';

/**
 * Whether the dashboard's money is on screen.
 *
 * The dashboard is the screen most likely to be open on a back-office monitor that faces the shop
 * floor, or shared on a call, or looked at over somebody's shoulder while they ask a question about
 * something else entirely. Takings are the one figure on it that is nobody else's business.
 *
 * Hidden by default, and deliberately. A privacy control that starts revealed protects nothing on
 * the first load, which is exactly the load nobody was expecting — the one where somebody walks in
 * while the screen is up.
 *
 * The choice is remembered per browser rather than per account: it is a fact about *this screen in
 * this room*, not about the person. The same manager wants the numbers visible on the office
 * machine and hidden on the one by the counter, and a server-side preference would force one answer
 * onto both.
 */
const STORAGE_KEY = 'retail25.dashboard.amounts-visible';

export function useAmountVisibility(): { visible: boolean; toggle: () => void; ready: boolean } {
  // Starts hidden and stays hidden through the first paint. Reading localStorage during render
  // would differ between the server pass and the client one, and React would resolve that
  // mismatch by flashing the real figures before correcting itself — revealing exactly what this
  // is for, to anybody watching.
  const [visible, setVisible] = useState(false);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    try {
      setVisible(window.localStorage.getItem(STORAGE_KEY) === 'true');
    } catch {
      // Private browsing, or storage disabled by policy. Hidden is the safe answer.
    }

    setReady(true);
  }, []);

  const toggle = useCallback(() => {
    setVisible((current) => {
      const next = !current;

      try {
        window.localStorage.setItem(STORAGE_KEY, String(next));
      } catch {
        // Not being able to remember the choice is not a reason to refuse to make it.
      }

      return next;
    });
  }, []);

  return { visible, toggle, ready };
}

/**
 * What stands in for a figure that is hidden.
 *
 * A fixed width regardless of the real number: a placeholder that grew with the amount would leak
 * the order of magnitude, which on a day's takings is most of what somebody glancing at the screen
 * wanted to know.
 */
export const HIDDEN_AMOUNT = '••••••';
