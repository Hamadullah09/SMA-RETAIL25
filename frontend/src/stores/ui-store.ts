'use client';

import { create } from 'zustand';
import { persist } from 'zustand/middleware';

/**
 * Two separate pieces of chrome state, because they answer different questions.
 *
 * `sidebarOpen` is a desktop preference — whether the rail shows labels or just icons. Someone who
 * collapses it wants it collapsed tomorrow too, so it is persisted.
 *
 * `drawerOpen` is a transient mobile state — whether the off-canvas menu is currently slid over the
 * page. Persisting that would mean a phone reopening onto a menu covering the screen, which is not
 * a preference anyone holds.
 */
interface UIState {
  sidebarOpen: boolean;
  toggleSidebar: () => void;
  setSidebarOpen: (open: boolean) => void;

  drawerOpen: boolean;
  toggleDrawer: () => void;
  closeDrawer: () => void;

  /**
   * Whether the till makes a sound when a tag is scanned.
   *
   * On by default, because RFID takes away the beep a barcode scanner always gave and the cashier
   * is looking at the item rather than the screen at the moment it matters. Off is a real
   * preference all the same — a counter with three tills within earshot is a counter where nobody
   * can tell whose beep just went.
   */
  scanSound: boolean;
  toggleScanSound: () => void;
}

export const useUIStore = create<UIState>()(
  persist(
    (set) => ({
      sidebarOpen: true,
      toggleSidebar: () => set((s) => ({ sidebarOpen: !s.sidebarOpen })),
      setSidebarOpen: (open) => set({ sidebarOpen: open }),

      drawerOpen: false,
      toggleDrawer: () => set((s) => ({ drawerOpen: !s.drawerOpen })),
      closeDrawer: () => set({ drawerOpen: false }),

      scanSound: true,
      toggleScanSound: () => set((s) => ({ scanSound: !s.scanSound })),
    }),
    {
      name: 'r25.ui',

      // Only the durable preferences survive a reload. Listing them explicitly rather than
      // excluding the rest means a transient flag added later cannot accidentally become
      // persistent.
      partialize: (state) => ({ sidebarOpen: state.sidebarOpen, scanSound: state.scanSound }),
    },
  ),
);
