import { apiClient } from '@/lib/api-client';
import type {
  Cart,
  CartTotals,
  CompleteSaleResult,
  DrawerTotals,
  PosGridPage,
  ProblemDetails,
  ProductVariant,
  SerializedUnit,
  StationPolicy,
  SuspendedCart,
  TenderRequest,
  TenderType,
} from '@/types/pos';
import type { Product } from '@/types';

/**
 * The till's view of the API.
 *
 * Every call funnels errors through {@link toProblem} so callers always get the server's
 * machine-readable `code`. That code is what lets the UI tell "the tag is already sold" apart from
 * "you may not discount" — a generic "request failed" would leave the cashier with no next step.
 */

export class PosApiError extends Error {
  constructor(readonly problem: ProblemDetails) {
    super(problem.detail || problem.title);
    this.name = 'PosApiError';
  }

  get code(): string {
    return this.problem.code;
  }

  /** 428: refusable now, but a supervisor could approve it (doc 05 error taxonomy). */
  get needsSupervisor(): boolean {
    return this.problem.status === 428 || this.problem.code === 'sale.requires_supervisor';
  }
}

function toProblem(error: unknown): never {
  const response = (error as { response?: { status?: number; data?: Partial<ProblemDetails> } })?.response;

  throw new PosApiError({
    status: response?.status ?? 0,
    title: response?.data?.title ?? 'Request failed',
    detail: response?.data?.detail ?? 'The till could not reach the server.',
    code: response?.data?.code ?? 'network.unreachable',
    arguments: response?.data?.arguments,
  });
}

async function call<T>(fn: () => Promise<{ data: T }>): Promise<T> {
  try {
    const { data } = await fn();
    return data;
  } catch (error) {
    return toProblem(error);
  }
}

/** A fresh key per attempt to complete, so a retry after a timeout replays rather than double-charges. */
function newIdempotencyKey(): string {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

export const posApi = {
  stationPolicy: (stationId: number) =>
    call<StationPolicy>(() => apiClient.get(`/terminals/${stationId}/policy`)),

  tenderTypes: () => call<TenderType[]>(() => apiClient.get('/tender-types')),

  createCart: (stationId: number, staffId?: number) =>
    call<Cart>(() => apiClient.post('/carts', { stationId, staffId })),

  getCart: (cartId: number) => call<Cart>(() => apiClient.get(`/carts/${cartId}`)),

  cartForStation: (stationId: number) => call<Cart>(() => apiClient.get(`/carts/by-station/${stationId}`)),

  quote: (cartId: number) => call<CartTotals>(() => apiClient.get(`/carts/${cartId}/quote`)),

  addLine: (
    cartId: number,
    body: {
      identifier: string;
      quantity?: number;
      manualPrice?: number | null;
      manualDiscountPct?: number | null;
      priceLevel?: number | null;
      lineType?: string;
    },
  ) => call<Cart>(() => apiClient.post(`/carts/${cartId}/lines`, body)),

  updateLine: (
    cartId: number,
    lineId: number,
    body: {
      quantity?: number;
      manualPrice?: number | null;
      manualDiscountPct?: number | null;
      priceLevel?: number | null;
      tax1Override?: boolean | null;
      tax2Override?: boolean | null;
      note?: string | null;
      clear?: string[];
    },
  ) => call<Cart>(() => apiClient.patch(`/carts/${cartId}/lines/${lineId}`, body)),

  removeLine: (cartId: number, lineId: number) =>
    call<Cart>(() => apiClient.delete(`/carts/${cartId}/lines/${lineId}`)),

  clearLines: (cartId: number) => call<Cart>(() => apiClient.delete(`/carts/${cartId}/lines`)),

  addAdjustment: (
    cartId: number,
    body: { type: string; label: string; amount?: number; percent?: number; serial?: string | null },
  ) => call<Cart>(() => apiClient.post(`/carts/${cartId}/adjustments`, body)),

  removeAdjustment: (cartId: number, adjustmentId: number) =>
    call<Cart>(() => apiClient.delete(`/carts/${cartId}/adjustments/${adjustmentId}`)),

  /** Answers `variant.selection_required` with the variant the cashier picked (guide p.39–40). */
  addVariantLine: (cartId: number, variantId: number, quantity = 1) =>
    call<Cart>(() => apiClient.post(`/carts/${cartId}/lines/variant`, { variantId, quantity })),

  /** Answers `serial.selection_required` with the unit the cashier picked (guide p.42). */
  addUnitLine: (cartId: number, unitId: number) =>
    call<Cart>(() => apiClient.post(`/carts/${cartId}/lines/unit`, { unitId })),

  listVariants: (productId: number, locationId: number, inStockOnly = true) =>
    call<ProductVariant[]>(() =>
      apiClient.get(`/products/${productId}/matrix/variants?locationId=${locationId}&inStockOnly=${inStockOnly}`),
    ),

  listAvailableUnits: (productId: number, locationId: number) =>
    call<SerializedUnit[]>(() =>
      apiClient.get(`/serialized-units/available?productId=${productId}&locationId=${locationId}`),
    ),

  /** A supervisor's answer to an `epc.unknown` row in the live feed (doc 06 §1). */
  commissionTag: (epc: string, productId: number, locationId: number) =>
    call<string>(() => apiClient.post('/serialized-units/commission', { epc, productId, locationId })),

  setReaderMode: (stationId: number, mode: 'Off' | 'OnDemand' | 'Continuous') =>
    call<void>(() => apiClient.put(`/terminals/${stationId}/reader-mode`, { mode })),

  /** Ctrl+I staff switch, inside the station's existing session (doc 07). */
  verifyStaffPin: (staffCode: string, pin: string, stationId: number) =>
    call<{
      staffId: number;
      staffCode: string;
      fullName: string;
      accessLevel: number;
      permissions: string[];
    }>(() => apiClient.post('/staff/verify-pin', { staffCode, pin, stationId })),

  /** Raises a supervisor override after a command answered 428 (doc 07 §Step-up). */
  requestApproval: (permission: string, action: string, context: string | null, stationId: number) =>
    call<{ id: number; permission: string; action: string; context: string | null }>(() =>
      apiClient.post('/approvals', { permission, action, context, stationId }),
    ),

  approveWithPin: (approvalId: number, staffCode: string, pin: string) =>
    call<{ id: number; status: string }>(() =>
      apiClient.post(`/approvals/${approvalId}/approve-with-pin`, { staffCode, pin }),
    ),

  addUnknownItem: (
    cartId: number,
    body: { description: string; unitPrice: number; quantity?: number; createProduct?: boolean },
  ) => call<Cart>(() => apiClient.post(`/carts/${cartId}/unknown-item`, body)),

  setTaxOverride: (cartId: number, tax1: boolean | null, tax2: boolean | null) =>
    call<Cart>(() => apiClient.put(`/carts/${cartId}/tax-override`, { tax1, tax2 })),

  setCustomer: (cartId: number, customerId: number | null) =>
    call<Cart>(() => apiClient.put(`/carts/${cartId}/customer`, { customerId })),

  suspend: (cartId: number, label?: string) =>
    call<SuspendedCart>(() => apiClient.post(`/carts/${cartId}/suspend`, { label })),

  recall: (cartId: number, stationId: number) =>
    call<Cart>(() => apiClient.post(`/carts/${cartId}/recall`, { stationId })),

  listSuspended: (locationId: number) =>
    call<SuspendedCart[]>(() => apiClient.get(`/carts/suspended?locationId=${locationId}`)),

  complete: (cartId: number, tenders: TenderRequest[], options?: { printReceipt?: boolean; copies?: number }) =>
    call<CompleteSaleResult>(() =>
      apiClient.post(
        `/carts/${cartId}/complete`,
        { tenders, printReceipt: options?.printReceipt ?? true, copies: options?.copies ?? 1 },
        { headers: { 'Idempotency-Key': newIdempotencyKey() } },
      ),
    ),

  voidSale: (transactionId: number, reason?: string) =>
    call<{ reversalTransactionId: number; reversalNumber: number }>(() =>
      apiClient.post(
        `/sales/${transactionId}/void`,
        { reason },
        { headers: { 'Idempotency-Key': newIdempotencyKey() } },
      ),
    ),

  reprintLast: (stationId: number) =>
    call<unknown>(() => apiClient.post('/sales/reprint-last', { stationId })),

  packingSlip: (transactionId: number, stationId: number) =>
    call<unknown>(() => apiClient.post(`/sales/${transactionId}/packing-slip`, { stationId })),

  searchProducts: (term: string, locationId: number) =>
    call<Product[]>(() => apiClient.get(`/products/search?term=${encodeURIComponent(term)}&locationId=${locationId}`)),

  /** The till's product picker: one page of items plus the headings above them. */
  grid: (options: {
    locationId: number;
    departmentId?: number | null;
    categoryId?: number | null;
    search?: string;
    skip?: number;
    take?: number;
  }) => {
    const params = new URLSearchParams({ locationId: String(options.locationId) });
    if (options.departmentId) params.set('departmentId', String(options.departmentId));
    if (options.categoryId) params.set('categoryId', String(options.categoryId));
    if (options.search) params.set('search', options.search);
    if (options.skip) params.set('skip', String(options.skip));
    if (options.take) params.set('take', String(options.take));

    return call<PosGridPage>(() => apiClient.get(`/products/grid?${params.toString()}`));
  },

  // The till's own endpoint, not the back-office browse: it returns a flat list of just enough to
  // choose from, rather than a page of addresses, balances and pricing profiles a cashier never sees.
  searchCustomers: (term: string, locationId: number) =>
    call<Array<{ id: number; customerNumber: number; fullName: string }>>(() =>
      apiClient.get(`/customers/search?term=${encodeURIComponent(term)}&locationId=${locationId}`),
    ),

  drawer: {
    current: (stationId: number) =>
      call<DrawerTotals>(() => apiClient.get(`/drawer-sessions/current?stationId=${stationId}`)),
    open: (stationId: number, openingFloat: number) =>
      call<DrawerTotals>(() => apiClient.post('/drawer-sessions', { stationId, openingFloat })),
    payIn: (stationId: number, amount: number, reason: string) =>
      call<DrawerTotals>(() => apiClient.post('/drawer-sessions/pay-in', { stationId, amount, reason })),
    payOut: (stationId: number, amount: number, reason: string) =>
      call<DrawerTotals>(() => apiClient.post('/drawer-sessions/pay-out', { stationId, amount, reason })),
    pop: (stationId: number, reason?: string) =>
      call<DrawerTotals>(() => apiClient.post('/drawer-sessions/pop', { stationId, reason })),
    close: (stationId: number, countedCash: number) =>
      call<DrawerTotals>(() => apiClient.post('/drawer-sessions/close', { stationId, countedCash })),
  },
};
