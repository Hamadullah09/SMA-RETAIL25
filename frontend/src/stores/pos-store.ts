'use client';

import { create } from 'zustand';
import { posApi, PosApiError } from '@/lib/pos-api';
import { posHub } from '@/lib/pos-hub';
import { playScanTone } from '@/lib/scan-feedback';
import { useUIStore } from '@/stores/ui-store';
import type {
  Cart,
  DrawerTotals,
  PeripheralStatus,
  RejectedTag,
  StationPolicy,
  TenderRequest,
} from '@/types/pos';

/**
 * The till's client state.
 *
 * The server's cart is authoritative; this store holds the last copy it sent plus the local UI
 * concerns (which dialog is open, what the cashier has typed). Nothing recomputes money here — a
 * client that did its own arithmetic would eventually disagree with the receipt.
 */

export type PosDialog =
  | null
  | 'lineDetail'
  | 'payment'
  | 'credits'
  | 'client'
  | 'drawer'
  | 'special'
  | 'find'
  | 'suspended'
  | 'cheatSheet'
  | 'unknownItem'
  | 'variantPicker'
  | 'serialPicker'
  | 'staffSwitch'
  | 'supervisorApproval';

/** A step-up request raised after a command answered 428 (doc 07 §Step-up). */
export interface PendingApproval {
  id: number;
  permission: string;
  action: string;
  context: string | null;
}

/** Who the next sale is attributed to. Switched by PIN, inside the station's existing session. */
export interface ActiveStaff {
  staffId: number;
  staffCode: string;
  fullName: string;
  accessLevel: number;
  permissions: string[];
}

/**
 * The product a scan resolved to that turned out to be ambiguous — a matrix parent or a serialized
 * item. Held so the picker knows what to offer without the cashier scanning again.
 */
export interface PendingSelection {
  productId: number;
  identifier: string;
}

interface PosState {
  stationId: number | null;
  locationId: number | null;
  policy: StationPolicy | null;

  cart: Cart | null;
  drawer: DrawerTotals | null;

  connected: boolean;
  readerOnline: boolean;
  readRate: number;
  peripherals: PeripheralStatus | null;

  /** Rejected tags stay visible for ten seconds with a plain-language reason (doc 08). */
  rejectedTags: RejectedTag[];
  posMessage: string | null;

  dialog: PosDialog;
  /** Which line the detail dialog is editing, by cart position. Lines have no other identity. */
  selectedLineSequence: number | null;
  pendingSelection: PendingSelection | null;
  pendingApproval: PendingApproval | null;
  activeStaff: ActiveStaff | null;
  busy: boolean;
  error: { code: string; message: string } | null;
  lastSale: { transactionId: number; transactionNumber: number; changeGiven: number } | null;

  initialise: (stationId: number, locationId: number) => Promise<void>;
  teardown: () => Promise<void>;

  ensureCart: () => Promise<Cart | null>;
  scan: (identifier: string) => Promise<void>;
  addVariant: (variantId: number, quantity?: number) => Promise<void>;
  addUnit: (unitId: number) => Promise<void>;
  setReaderMode: (mode: 'Off' | 'OnDemand' | 'Continuous') => Promise<void>;
  switchStaff: (staffCode: string, pin: string) => Promise<void>;
  requestApproval: (permission: string, action: string, context?: string) => Promise<PendingApproval | null>;
  approveWithPin: (staffCode: string, pin: string) => Promise<void>;
  updateLine: (sequence: number, body: Parameters<typeof posApi.updateLine>[2]) => Promise<void>;
  removeLine: (sequence: number) => Promise<void>;
  removeLastLine: () => Promise<void>;
  clearLines: () => Promise<void>;
  addAdjustment: (body: Parameters<typeof posApi.addAdjustment>[1]) => Promise<void>;
  addUnknownItem: (description: string, unitPrice: number, quantity: number) => Promise<void>;
  setTaxOverride: (tax1: boolean | null, tax2: boolean | null) => Promise<void>;
  setCustomer: (customerId: number | null) => Promise<void>;
  suspend: (label?: string) => Promise<void>;
  recall: (cartId: number) => Promise<void>;
  complete: (tenders: TenderRequest[]) => Promise<boolean>;
  refreshDrawer: () => Promise<void>;

  openDialog: (dialog: PosDialog, sequence?: number | null) => void;
  closeDialog: () => void;
  clearError: () => void;
  dismissTag: (epc: string) => void;
}

function describe(error: unknown): { code: string; message: string } {
  if (error instanceof PosApiError) {
    return { code: error.code, message: error.message };
  }

  return { code: 'unexpected', message: error instanceof Error ? error.message : 'Something went wrong.' };
}

export const usePosStore = create<PosState>((set, get) => ({
  stationId: null,
  locationId: null,
  policy: null,
  cart: null,
  drawer: null,
  connected: false,
  readerOnline: false,
  readRate: 0,
  peripherals: null,
  rejectedTags: [],
  posMessage: null,
  dialog: null,
  selectedLineSequence: null,
  pendingSelection: null,
  pendingApproval: null,
  activeStaff: null,
  busy: false,
  error: null,
  lastSale: null,

  initialise: async (stationId, locationId) => {
    set({ stationId, locationId });

    try {
      const policy = await posApi.stationPolicy(stationId);
      set({ policy });
    } catch (error) {
      set({ error: describe(error) });
    }

    // An existing cart at this station is picked up rather than replaced: a browser refresh must
    // not abandon a basket the customer is standing next to.
    try {
      const cart = await posApi.cartForStation(stationId);
      set({ cart });
      await posHub.joinCart(cart.id);
    } catch {
      set({ cart: null });
    }

    try {
      const drawer = await posApi.drawer.current(stationId);
      set({ drawer });
    } catch {
      set({ drawer: null });
    }

    await posHub.connect(stationId, locationId, {
      onCartUpdated: (cart) => set({ cart }),
      onTotalsChanged: (totals) => {
        const cart = get().cart;
        if (cart) set({ cart: { ...cart, totals } });
      },
      onCartLinesAdded: (lines, revision) => {
        const cart = get().cart;
        if (!cart) return;

        // A gap in the revision sequence means we missed a message; ask rather than guess.
        if (revision > cart.revision + 1) {
          void posHub.requestResync(cart.id, cart.revision);
          return;
        }

        set({ cart: { ...cart, revision, lines: [...cart.lines, ...lines] } });

        // The beep RFID takes away. One tone per batch rather than one per line: a basket of thirty
        // is one action from the cashier's point of view, and thirty blips is an alarm.
        if (lines.some((line) => line.epc) && useUIStore.getState().scanSound) {
          playScanTone('accepted');
        }
      },
      onCartLineRejected: ({ epc, reason, message }) => {
        if (useUIStore.getState().scanSound) {
          // An unrecognised tag sounds unlike a refused one. The first is usually a customer's own
          // coat and needs no action; the second is stock that will not sell until somebody
          // intervenes, and a cashier has to be able to tell those apart without reading.
          playScanTone(reason === 'epc.unknown' ? 'unknown' : 'rejected');
        }

        set((state) => ({
          rejectedTags: [{ epc, reason, message, at: Date.now() }, ...state.rejectedTags].slice(0, 20),
        }));
      },
      onTagStreamStatus: ({ readerOnline, readRate }) => set({ readerOnline, readRate }),
      onPeripheralStatus: (peripherals) => set({ peripherals, readerOnline: peripherals.readerOnline }),
      onDrawerStateChanged: (drawer) => set({ drawer }),
      onPosMessage: ({ message }) => set({ posMessage: message }),
      onConnectionChanged: (connected) => set({ connected }),
      onResyncRequired: async ({ cartId }) => {
        try {
          set({ cart: await posApi.getCart(cartId) });
        } catch {
          /* the next mutation will return authoritative state anyway */
        }
      },
    });
  },

  teardown: async () => {
    await posHub.disconnect();
    set({ connected: false });
  },

  ensureCart: async () => {
    const { cart, stationId } = get();
    if (cart && cart.status === 'Active') return cart;
    if (!stationId) return null;

    try {
      const created = await posApi.createCart(stationId);
      set({ cart: created });
      await posHub.joinCart(created.id);
      return created;
    } catch (error) {
      set({ error: describe(error) });
      return null;
    }
  },

  scan: async (identifier) => {
    const trimmed = identifier.trim();
    if (!trimmed) return;

    const cart = await get().ensureCart();
    if (!cart) return;

    set({ busy: true, error: null });

    try {
      set({ cart: await posApi.addLine(cart.id, { identifier: trimmed }) });
    } catch (error) {
      // A matrix parent or a serialized item is a question, not a failure. The server says which
      // kind of question it is, and the right picker opens rather than the cashier seeing an error
      // and scanning the same barcode again.
      if (error instanceof PosApiError) {
        const productId = error.problem.arguments?.productId as number | undefined;

        if (error.code === 'variant.selection_required' && productId) {
          set({ dialog: 'variantPicker', pendingSelection: { productId, identifier: trimmed }, busy: false });
          return;
        }

        if (error.code === 'serial.selection_required' && productId) {
          set({ dialog: 'serialPicker', pendingSelection: { productId, identifier: trimmed }, busy: false });
          return;
        }
      }

      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  addVariant: async (variantId, quantity = 1) => {
    const cart = await get().ensureCart();
    if (!cart) return;

    set({ busy: true, error: null });

    try {
      set({
        cart: await posApi.addVariantLine(cart.id, variantId, quantity),
        dialog: null,
        pendingSelection: null,
      });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  addUnit: async (unitId) => {
    const cart = await get().ensureCart();
    if (!cart) return;

    set({ busy: true, error: null });

    try {
      set({ cart: await posApi.addUnitLine(cart.id, unitId), dialog: null, pendingSelection: null });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  setReaderMode: async (mode) => {
    const stationId = get().stationId;
    if (!stationId) return;

    try {
      await posApi.setReaderMode(stationId, mode);
    } catch (error) {
      set({ error: describe(error) });
    }
  },

  switchStaff: async (staffCode, pin) => {
    const stationId = get().stationId;
    if (!stationId) return;

    set({ busy: true, error: null });

    try {
      const staff = await posApi.verifyStaffPin(staffCode, pin, stationId);
      set({ activeStaff: staff, dialog: null });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  requestApproval: async (permission, action, context) => {
    const stationId = get().stationId;
    if (!stationId) return null;

    set({ busy: true, error: null });

    try {
      const approval = await posApi.requestApproval(permission, action, context ?? null, stationId);

      const pending: PendingApproval = {
        id: approval.id,
        permission: approval.permission,
        action: approval.action,
        context: approval.context,
      };

      // The prompt opens here, but a supervisor at any till can answer it instead — the request was
      // broadcast when it was raised.
      set({ pendingApproval: pending, dialog: 'supervisorApproval' });
      return pending;
    } catch (error) {
      set({ error: describe(error) });
      return null;
    } finally {
      set({ busy: false });
    }
  },

  approveWithPin: async (staffCode, pin) => {
    const pending = get().pendingApproval;
    if (!pending) return;

    set({ busy: true, error: null });

    try {
      await posApi.approveWithPin(pending.id, staffCode, pin);

      // The grant now exists but is unspent; the caller retries the command carrying its id, and the
      // command consumes it. Keeping it here rather than auto-retrying means one approval can never
      // silently authorise something the cashier did not just attempt.
      set({ dialog: null });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  updateLine: async (sequence, body) => {
    const cart = get().cart;
    if (!cart) return;

    set({ busy: true, error: null });

    try {
      set({ cart: await posApi.updateLine(cart.id, sequence, body) });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  removeLine: async (sequence) => {
    const cart = get().cart;
    if (!cart) return;

    set({ busy: true, error: null });

    try {
      set({ cart: await posApi.removeLine(cart.id, sequence) });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  /** F6: delete the last line, which is what a cashier who mis-scanned reaches for (guide p.10). */
  removeLastLine: async () => {
    const cart = get().cart;
    const last = cart?.lines[cart.lines.length - 1];
    if (last) await get().removeLine(last.sequence);
  },

  clearLines: async () => {
    const cart = get().cart;
    if (!cart) return;

    set({ busy: true, error: null });

    try {
      set({ cart: await posApi.clearLines(cart.id) });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  addAdjustment: async (body) => {
    const cart = get().cart;
    if (!cart) return;

    set({ busy: true, error: null });

    try {
      set({ cart: await posApi.addAdjustment(cart.id, body), dialog: null });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  addUnknownItem: async (description, unitPrice, quantity) => {
    const cart = await get().ensureCart();
    if (!cart) return;

    set({ busy: true, error: null });

    try {
      set({ cart: await posApi.addUnknownItem(cart.id, { description, unitPrice, quantity }), dialog: null });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  setTaxOverride: async (tax1, tax2) => {
    const cart = get().cart;
    if (!cart) return;

    set({ busy: true, error: null });

    try {
      set({ cart: await posApi.setTaxOverride(cart.id, tax1, tax2), dialog: null });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  setCustomer: async (customerId) => {
    const cart = await get().ensureCart();
    if (!cart) return;

    set({ busy: true, error: null });

    try {
      set({ cart: await posApi.setCustomer(cart.id, customerId), dialog: null });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  suspend: async (label) => {
    const cart = get().cart;
    if (!cart) return;

    set({ busy: true, error: null });

    try {
      await posApi.suspend(cart.id, label);
      await posHub.leaveCart(cart.id);
      set({ cart: null, dialog: null });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  recall: async (cartId) => {
    const stationId = get().stationId;
    if (!stationId) return;

    set({ busy: true, error: null });

    try {
      const cart = await posApi.recall(cartId, stationId);
      await posHub.joinCart(cart.id);
      set({ cart, dialog: null });
    } catch (error) {
      set({ error: describe(error) });
    } finally {
      set({ busy: false });
    }
  },

  complete: async (tenders) => {
    const cart = get().cart;
    if (!cart) return false;

    set({ busy: true, error: null });

    try {
      const result = await posApi.complete(cart.id, tenders);

      await posHub.leaveCart(cart.id);
      set({
        cart: null,
        dialog: null,
        lastSale: {
          transactionId: result.transactionId,
          transactionNumber: result.transactionNumber,
          changeGiven: result.changeGiven,
        },
      });

      await get().refreshDrawer();
      return true;
    } catch (error) {
      set({ error: describe(error) });
      return false;
    } finally {
      set({ busy: false });
    }
  },

  refreshDrawer: async () => {
    const stationId = get().stationId;
    if (!stationId) return;

    try {
      set({ drawer: await posApi.drawer.current(stationId) });
    } catch {
      set({ drawer: null });
    }
  },

  openDialog: (dialog, sequence = null) =>
    set({ dialog, selectedLineSequence: sequence ?? get().selectedLineSequence }),
  closeDialog: () => set({ dialog: null, pendingSelection: null }),
  clearError: () => set({ error: null }),
  dismissTag: (epc) => set((state) => ({ rejectedTags: state.rejectedTags.filter((t) => t.epc !== epc) })),
}));
