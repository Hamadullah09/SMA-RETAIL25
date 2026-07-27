'use client';

import { create } from 'zustand';
import type { Cart, CartLine, PaymentMethod } from '@/types';
import { apiClient } from '@/lib/api-client';

interface CartState {
  cart: Cart | null;
  isLoading: boolean;
  error: string | null;
  createCart: (locationId: string, terminalId: string, staffId: string) => Promise<void>;
  addItem: (identifier: string, quantity?: number) => Promise<void>;
  removeItem: (lineId: string) => Promise<void>;
  voidCart: () => void;
  clearError: () => void;
}

export const useCartStore = create<CartState>((set, get) => ({
  cart: null,
  isLoading: false,
  error: null,

  createCart: async (locationId, terminalId, staffId) => {
    set({ isLoading: true, error: null });
    try {
      const { data } = await apiClient.post<Cart>('/carts', { locationId, terminalId, staffId });
      set({ cart: data, isLoading: false });
    } catch (err: any) {
      set({ error: err.response?.data?.error ?? 'Failed to create cart', isLoading: false });
    }
  },

  addItem: async (identifier, quantity = 1) => {
    const { cart } = get();
    if (!cart) return;
    set({ isLoading: true, error: null });
    try {
      const { data } = await apiClient.post<Cart>(`/carts/${cart.id}/lines`, {
        identifier,
        quantity,
      });
      set({ cart: data, isLoading: false });
    } catch (err: any) {
      set({ error: err.response?.data?.error ?? 'Failed to add item', isLoading: false });
    }
  },

  removeItem: async (lineId) => {
    const { cart } = get();
    if (!cart) return;
    set({ isLoading: true, error: null });
    try {
      await apiClient.delete(`/carts/${cart.id}/lines/${lineId}`);
      set((state) => ({
        cart: state.cart
          ? { ...state.cart, lines: state.cart.lines.filter((l) => l.id !== lineId) }
          : null,
        isLoading: false,
      }));
    } catch (err: any) {
      set({ error: err.response?.data?.error ?? 'Failed to remove item', isLoading: false });
    }
  },

  voidCart: () => set({ cart: null, error: null }),
  clearError: () => set({ error: null }),
}));
