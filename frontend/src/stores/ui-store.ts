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
    }),
    {
      name: 'r25.ui',

      // Only the desktop preference survives a reload. Listing it explicitly rather than excluding
      // the rest means a transient flag added later cannot accidentally become persistent.
      partialize: (state) => ({ sidebarOpen: state.sidebarOpen }),
    },
  ),
);
