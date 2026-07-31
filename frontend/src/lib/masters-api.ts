import { apiClient } from '@/lib/api-client';
import { PosApiError } from '@/lib/pos-api';
import type {
  AuditAction,
  AuditLogPage,
  AuditLogRow,
  CursorPage,
  CustomerForm,
  CustomerRow,
  CustomerSort,
  DeletedEntityKind,
  DeletedRow,
  ProductForm,
  ProductRow,
  ProductSort,
  ProductType,
  Matrix,
  MatrixDimension,
  ReferenceRow,
  ExternalMapRow,
  PreflightReport,
  SyncEntityName,
  SyncLogDetail,
  SyncLogPage,
  SyncRunOptions,
  SyncRunResult,
  OnOrderRow,
  RewardPointsResult,
  SalesAnalysisFilters,
  SalesAnalysisResult,
  StockPositionKind,
  StockPositionRow,
  StockReceivedPage,
  StockValuationDetailPage,
  StockValuationResult,
  TaxReportResult,
  SaleDetail,
  SalesLogPage,
  SettingsSnapshot,
  SupplierForm,
  SupplierRow,
  SupplierSort,
  OrderQuantityStrategy,
  PurchaseOrderDetail,
  PurchaseOrderRow,
  PurchaseOrderStatus,
  StockLevelRow,
  CustomerAccountRow,
  CustomerStatement,
  ReceivablesAgingRow,
  InvoiceRow,
  GiftCard,
  LoyaltyPolicy,
  LoyaltyBalance,
  LoyaltyLedgerEntryRow,
  CustomerOrder,
  CustomerOrderStatus,
  Layaway,
  LayawayStatus,
  PriceQuote,
  PriceQuoteStatus,
  LabelStock,
  LabelStockOption,
  PrintLabelsRequest,
  BulkAdjustMethod,
  BulkFilter,
  BulkPricePreview,
  BulkPriceTarget,
  CountImportResult,
  PriceRounding,
  StockCount,
  StockCountRow,
  StockCountStatus,
  Transfer,
  TransferDestination,
  TransferRow,
  TransferStatus,
} from '@/types/masters';

/**
 * The back-office view of the API.
 *
 * Errors surface as {@link PosApiError} — the same type the till uses — so a screen can react to the
 * server's machine-readable `code`. "This item still has stock on hand" and "that stock code is
 * taken" need different words and different next steps; a generic failure gives the user neither.
 */

function toProblem(error: unknown): never {
  const response = (error as { response?: { status?: number; data?: Record<string, unknown> } })?.response;
  const data = response?.data ?? {};

  throw new PosApiError({
    status: response?.status ?? 0,
    title: (data.title as string) ?? 'Request failed',
    detail: (data.detail as string) ?? 'The server could not be reached.',
    code: (data.code as string) ?? 'network.unreachable',
    arguments: data.arguments as Record<string, unknown> | undefined,
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

/**
 * A PDF, fetched rather than linked because the request carries a body.
 *
 * The error path needs its own handling: with `responseType: 'blob'` a failure response arrives as a
 * Blob too, so {@link toProblem} would find no `title` and report "Request failed" for every problem
 * the server took the trouble to describe. Reading the blob back as text recovers it.
 */
async function callPdf(fn: () => Promise<{ data: Blob }>): Promise<Blob> {
  try {
    const { data } = await fn();
    return data;
  } catch (error) {
    const response = (error as { response?: { status?: number; data?: unknown } })?.response;
    const body = response?.data;

    if (body instanceof Blob) {
      try {
        const problem = JSON.parse(await body.text()) as Record<string, unknown>;
        throw new PosApiError({
          status: response?.status ?? 0,
          title: (problem.title as string) ?? 'Could not print',
          detail: (problem.detail as string) ?? 'The server could not produce the document.',
          code: (problem.code as string) ?? 'documents.failed',
          arguments: problem.arguments as Record<string, unknown> | undefined,
        });
      } catch (parseError) {
        if (parseError instanceof PosApiError) throw parseError;
        // Not a problem document — fall through to the generic shape below.
      }
    }

    return toProblem(error);
  }
}

function query(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    // Undefined and empty mean "no filter". Sending them as empty strings would make the server
    // filter on a blank, which matches nothing.
    if (value === undefined || value === null || value === '') continue;
    search.set(key, String(value));
  }

  return search.toString();
}

export interface ProductBrowseFilters {
  search?: string;
  departmentId?: string;
  categoryId?: string;
  type?: ProductType;
  belowReorderPoint?: boolean;
  deletedOnly?: boolean;
  sort?: ProductSort;
  descending?: boolean;
  cursor?: string;
  pageSize?: number;
}

export interface CustomerBrowseFilters {
  search?: string;
  clientType?: string;
  withBalanceOnly?: boolean;
  deletedOnly?: boolean;
  sort?: CustomerSort;
  descending?: boolean;
  cursor?: string;
  pageSize?: number;
}

export interface SupplierBrowseFilters {
  search?: string;
  deletedOnly?: boolean;
  sort?: SupplierSort;
  descending?: boolean;
  cursor?: string;
  pageSize?: number;
}

export interface CustomerAccountBrowseFilters {
  search?: string;
  withBalanceOnly?: boolean;
  cursor?: string;
  pageSize?: number;
}

export interface StockLevelBrowseFilters {
  search?: string;
  belowReorderOnly?: boolean;
  cursor?: string;
  pageSize?: number;
}

export interface PurchaseOrderBrowseFilters {
  supplierId?: string;
  status?: PurchaseOrderStatus;
  cursor?: string;
  pageSize?: number;
}

export interface SalesLogFilters {
  from?: string;
  to?: string;
  stationId?: string;
  staffId?: string;
  customerId?: string;
  includeVoided?: boolean;
  skip?: number;
  take?: number;
}

export interface AuditFilters {
  from?: string;
  to?: string;
  actorStaffId?: string;
  stationId?: string;
  entityType?: string;
  entityId?: string;
  action?: AuditAction;
  skip?: number;
  take?: number;
}

export const mastersApi = {
  products: {
    browse: (locationId: string, filters: ProductBrowseFilters = {}) =>
      call<CursorPage<ProductRow>>(() =>
        apiClient.get(`/catalog/products?${query({ locationId, ...filters })}`),
      ),

    get: (id: string) => call<ProductForm>(() => apiClient.get(`/catalog/products/${id}`)),

    create: (body: unknown) => call<ProductForm>(() => apiClient.post('/catalog/products', body)),

    update: (id: string, body: unknown) => call<ProductForm>(() => apiClient.put(`/catalog/products/${id}`, body)),

    clone: (id: string, newStockCode: string, newName?: string) =>
      call<ProductForm>(() => apiClient.post(`/catalog/products/${id}/clone`, { newStockCode, newName })),

    remove: (id: string) => call<void>(() => apiClient.delete(`/catalog/products/${id}`)),

    restore: (id: string) => call<void>(() => apiClient.post(`/catalog/products/${id}/restore`)),
  },

  departments: {
    list: (locationId: string, includeInactive = false) =>
      call<ReferenceRow[]>(() => apiClient.get(`/catalog/departments?${query({ locationId, includeInactive })}`)),

    save: (body: unknown) => call<ReferenceRow>(() => apiClient.post('/catalog/departments', body)),

    remove: (id: string) => call<void>(() => apiClient.delete(`/catalog/departments/${id}`)),
  },

  categories: {
    list: (locationId: string, includeInactive = false) =>
      call<ReferenceRow[]>(() => apiClient.get(`/catalog/categories?${query({ locationId, includeInactive })}`)),

    save: (body: unknown) => call<ReferenceRow>(() => apiClient.post('/catalog/categories', body)),

    remove: (id: string) => call<void>(() => apiClient.delete(`/catalog/categories/${id}`)),
  },

  customers: {
    browse: (locationId: string, filters: CustomerBrowseFilters = {}) =>
      call<CursorPage<CustomerRow>>(() => apiClient.get(`/customers?${query({ locationId, ...filters })}`)),

    get: (id: string) => call<CustomerForm>(() => apiClient.get(`/customers/${id}`)),

    clientTypes: (locationId: string) =>
      call<string[]>(() => apiClient.get(`/customers/client-types?${query({ locationId })}`)),

    create: (body: unknown) => call<CustomerForm>(() => apiClient.post('/customers', body)),

    update: (id: string, body: unknown) => call<CustomerForm>(() => apiClient.put(`/customers/${id}`, body)),

    remove: (id: string) => call<void>(() => apiClient.delete(`/customers/${id}`)),

    restore: (id: string) => call<void>(() => apiClient.post(`/customers/${id}/restore`)),
  },

  suppliers: {
    browse: (locationId: string, filters: SupplierBrowseFilters = {}) =>
      call<CursorPage<SupplierRow>>(() => apiClient.get(`/suppliers?${query({ locationId, ...filters })}`)),

    get: (id: string) => call<SupplierForm>(() => apiClient.get(`/suppliers/${id}`)),

    create: (body: unknown) => call<SupplierForm>(() => apiClient.post('/suppliers', body)),

    update: (id: string, body: unknown) => call<SupplierForm>(() => apiClient.put(`/suppliers/${id}`, body)),

    remove: (id: string) => call<void>(() => apiClient.delete(`/suppliers/${id}`)),

    restore: (id: string) => call<void>(() => apiClient.post(`/suppliers/${id}/restore`)),
  },

  purchaseOrders: {
    browse: (locationId: string, filters: PurchaseOrderBrowseFilters = {}) =>
      call<CursorPage<PurchaseOrderRow>>(() => apiClient.get(`/purchase-orders?${query({ locationId, ...filters })}`)),

    get: (id: string) => call<PurchaseOrderDetail>(() => apiClient.get(`/purchase-orders/${id}`)),

    generate: (locationId: string, supplierId: string, strategy: OrderQuantityStrategy) =>
      call<PurchaseOrderDetail>(() => apiClient.post('/purchase-orders/generate', { locationId, supplierId, strategy })),

    addLine: (purchaseOrderId: string, body: { productId: string; orderQty: number; costEach: number; caseQty: number }) =>
      call<PurchaseOrderDetail>(() => apiClient.post(`/purchase-orders/${purchaseOrderId}/lines`, body)),

    updateLine: (lineId: string, body: { orderQty: number; costEach: number }) =>
      call<PurchaseOrderDetail>(() => apiClient.put(`/purchase-orders/lines/${lineId}`, body)),

    removeLine: (lineId: string) => call<PurchaseOrderDetail>(() => apiClient.delete(`/purchase-orders/lines/${lineId}`)),

    post: (id: string) => call<PurchaseOrderDetail>(() => apiClient.post(`/purchase-orders/${id}/post`)),

    receive: (
      id: string,
      body: { receivedOn: string; freightTotal: number; lines: { lineId: string; qtyReceived: number }[] },
    ) => call<PurchaseOrderDetail>(() => apiClient.post(`/purchase-orders/${id}/receive`, body)),

    cancel: (id: string) => call<PurchaseOrderDetail>(() => apiClient.post(`/purchase-orders/${id}/cancel`)),
  },

  inventory: {
    stockLevels: (locationId: string, filters: StockLevelBrowseFilters = {}) =>
      call<CursorPage<StockLevelRow>>(() => apiClient.get(`/inventory/stock-levels?${query({ locationId, ...filters })}`)),

    receive: (body: { productId: string; locationId: string; quantity: number; unitCost: number }) =>
      call<StockLevelRow>(() => apiClient.post('/inventory/receive', body)),

    adjust: (body: { productId: string; locationId: string; quantityDelta: number; reason: string }) =>
      call<StockLevelRow>(() => apiClient.post('/inventory/adjust', body)),

    breakCase: (body: { parentProductId: string; locationId: string; casesToBreak: number }) =>
      call<void>(() => apiClient.post('/inventory/case-break', body)),
  },

  receivables: {
    browseAccounts: (locationId: string, filters: CustomerAccountBrowseFilters = {}) =>
      call<CursorPage<CustomerAccountRow>>(() => apiClient.get(`/receivables/accounts?${query({ locationId, ...filters })}`)),

    statement: (customerId: string) =>
      call<CustomerStatement>(() => apiClient.get(`/receivables/customers/${customerId}/statement`)),

    aging: (locationId: string) =>
      call<ReceivablesAgingRow[]>(() => apiClient.get(`/receivables/aging?${query({ locationId })}`)),

    takePayment: (body: { customerId: string; amount: number; tenderTypeId: string; reference?: string }) =>
      call<{ amountApplied: number; amountUnapplied: number }>(() => apiClient.post('/receivables/payments', body)),

    voidInvoice: (invoiceId: string, reason?: string) =>
      call<InvoiceRow>(() => apiClient.post(`/receivables/invoices/${invoiceId}/void`, { reason })),

    refundInvoice: (invoiceId: string, amount: number, reason?: string) =>
      call<InvoiceRow>(() => apiClient.post(`/receivables/invoices/${invoiceId}/refund`, { amount, reason })),
  },

  giftCards: {
    issue: (body: { value: number; serialNumber?: string; customerId?: string; expiresOn?: string }) =>
      call<GiftCard>(() => apiClient.post('/gift-cards', body)),

    balance: (serialNumber: string) => call<GiftCard>(() => apiClient.get(`/gift-cards/${encodeURIComponent(serialNumber)}`)),
  },

  loyalty: {
    getPolicy: (locationId: string) => call<LoyaltyPolicy>(() => apiClient.get(`/loyalty/policy?${query({ locationId })}`)),

    savePolicy: (policy: LoyaltyPolicy) => call<LoyaltyPolicy>(() => apiClient.put('/loyalty/policy', policy)),

    balance: (customerId: string) => call<LoyaltyBalance>(() => apiClient.get(`/loyalty/customers/${customerId}/balance`)),

    ledger: (customerId: string) => call<LoyaltyLedgerEntryRow[]>(() => apiClient.get(`/loyalty/customers/${customerId}/ledger`)),

    adjust: (customerId: string, pointsDelta: number, reason: string) =>
      call<LoyaltyBalance>(() => apiClient.post(`/loyalty/customers/${customerId}/adjust`, { pointsDelta, reason })),
  },

  customerOrders: {
    browse: (locationId: string, filters: { customerId?: string; status?: CustomerOrderStatus; cursor?: string; pageSize?: number } = {}) =>
      call<CursorPage<CustomerOrder>>(() => apiClient.get(`/customer-orders?${query({ locationId, ...filters })}`)),

    get: (id: string) => call<CustomerOrder>(() => apiClient.get(`/customer-orders/${id}`)),

    create: (body: { customerId: string; locationId: string; lines: { productId: string; quantity: number; unitPrice: number }[]; notes?: string }) =>
      call<CustomerOrder>(() => apiClient.post('/customer-orders', body)),

    fill: (id: string) => call<CustomerOrder>(() => apiClient.post(`/customer-orders/${id}/fill`)),

    cancel: (id: string) => call<CustomerOrder>(() => apiClient.post(`/customer-orders/${id}/cancel`)),
  },

  layaways: {
    browse: (locationId: string, filters: { customerId?: string; status?: LayawayStatus; cursor?: string; pageSize?: number } = {}) =>
      call<CursorPage<Layaway>>(() => apiClient.get(`/layaways?${query({ locationId, ...filters })}`)),

    get: (id: string) => call<Layaway>(() => apiClient.get(`/layaways/${id}`)),

    create: (body: { customerId: string; locationId: string; lines: { productId: string; quantity: number; unitPrice: number }[] }) =>
      call<Layaway>(() => apiClient.post('/layaways', body)),

    takePayment: (id: string, amount: number, tenderTypeId: string) =>
      call<Layaway>(() => apiClient.post(`/layaways/${id}/payments`, { amount, tenderTypeId })),

    cancel: (id: string) => call<Layaway>(() => apiClient.post(`/layaways/${id}/cancel`)),
  },

  priceQuotes: {
    browse: (locationId: string, filters: { customerId?: string; status?: PriceQuoteStatus; cursor?: string; pageSize?: number } = {}) =>
      call<CursorPage<PriceQuote>>(() => apiClient.get(`/price-quotes?${query({ locationId, ...filters })}`)),

    get: (id: string) => call<PriceQuote>(() => apiClient.get(`/price-quotes/${id}`)),

    create: (body: { customerId: string; locationId: string; lines: { productId: string; quantity: number; unitPrice: number }[]; expiresOn?: string }) =>
      call<PriceQuote>(() => apiClient.post('/price-quotes', body)),

    convert: (id: string) => call<PriceQuote>(() => apiClient.post(`/price-quotes/${id}/convert`)),

    cancel: (id: string) => call<PriceQuote>(() => apiClient.post(`/price-quotes/${id}/cancel`)),
  },

  deleted: {
    list: (locationId: string, kind?: DeletedEntityKind, search?: string) =>
      call<DeletedRow[]>(() => apiClient.get(`/catalog/deleted?${query({ locationId, kind, search })}`)),

    restore: (kind: DeletedEntityKind, id: string) =>
      call<void>(() => apiClient.post(`/catalog/deleted/${kind}/${id}/restore`)),
  },

  /** The itemized sales log (guide p.14–15) and one sale in full. */
  sales: {
    log: (locationId: string, filters: SalesLogFilters = {}) =>
      call<SalesLogPage>(() => apiClient.get(`/sales?${query({ locationId, ...filters })}`)),

    get: (transactionId: string) => call<SaleDetail>(() => apiClient.get(`/sales/${transactionId}`)),

    reprint: (transactionId: string, stationId: string, copies = 1) =>
      call<unknown>(() => apiClient.post(`/sales/${transactionId}/reprint`, { stationId, copies })),

    /** The URL the browser downloads from — the modern "Open In MS-Excel" (guide p.101). */
    exportUrl: (locationId: string, filters: SalesLogFilters = {}) =>
      `/api/proxy/sales/export?${query({ locationId, ...filters })}`,
  },

  /**
   * The analytical reports (guide p.15–27, p.56, p.83–84). Every one of them has an `…Url` twin
   * for CSV: the browser downloads those through a plain link rather than fetching a blob, so a
   * large export streams instead of being buffered in the page.
   */
  reports: {
    salesAnalysis: (filters: SalesAnalysisFilters) =>
      call<SalesAnalysisResult>(() => apiClient.get(`/reports/sales-analysis?${query({ ...filters })}`)),

    salesAnalysisExportUrl: (filters: SalesAnalysisFilters) =>
      `/api/proxy/reports/sales-analysis/export?${query({ ...filters })}`,

    margin: (filters: SalesAnalysisFilters) =>
      call<SalesAnalysisResult>(() => apiClient.get(`/reports/margin?${query({ ...filters })}`)),

    marginExportUrl: (filters: SalesAnalysisFilters) =>
      `/api/proxy/reports/margin/export?${query({ ...filters })}`,

    tax: (locationId: string, from: string, to: string, includeVoided = false) =>
      call<TaxReportResult>(() => apiClient.get(`/reports/tax?${query({ locationId, from, to, includeVoided })}`)),

    taxExportUrl: (locationId: string, from: string, to: string, includeVoided = false) =>
      `/api/proxy/reports/tax/export?${query({ locationId, from, to, includeVoided })}`,

    stockValue: (locationId: string, departmentId?: string) =>
      call<StockValuationResult>(() => apiClient.get(`/reports/stock-value?${query({ locationId, departmentId })}`)),

    stockValueDetail: (locationId: string, departmentId?: string, skip = 0, take = 200) =>
      call<StockValuationDetailPage>(() =>
        apiClient.get(`/reports/stock-value/detail?${query({ locationId, departmentId, skip, take })}`)),

    stockValueExportUrl: (locationId: string, departmentId?: string) =>
      `/api/proxy/reports/stock-value/export?${query({ locationId, departmentId })}`,

    stockPosition: (locationId: string, departmentId?: string, only?: StockPositionKind) =>
      call<StockPositionRow[]>(() =>
        apiClient.get(`/reports/stock-position?${query({ locationId, departmentId, only })}`)),

    stockPositionExportUrl: (locationId: string, departmentId?: string, only?: StockPositionKind) =>
      `/api/proxy/reports/stock-position/export?${query({ locationId, departmentId, only })}`,

    onOrder: (locationId: string, supplierId?: string, departmentId?: string) =>
      call<OnOrderRow[]>(() => apiClient.get(`/reports/on-order?${query({ locationId, supplierId, departmentId })}`)),

    onOrderExportUrl: (locationId: string, supplierId?: string, departmentId?: string) =>
      `/api/proxy/reports/on-order/export?${query({ locationId, supplierId, departmentId })}`,

    stockReceived: (locationId: string, from: string, to: string, supplierId?: string, skip = 0, take = 200) =>
      call<StockReceivedPage>(() =>
        apiClient.get(`/reports/stock-received?${query({ locationId, from, to, supplierId, skip, take })}`)),

    stockReceivedExportUrl: (locationId: string, from: string, to: string, supplierId?: string) =>
      `/api/proxy/reports/stock-received/export?${query({ locationId, from, to, supplierId })}`,

    rewardPoints: (locationId: string, from: string, to: string, customerId?: string) =>
      call<RewardPointsResult>(() =>
        apiClient.get(`/reports/reward-points?${query({ locationId, from, to, customerId })}`)),

    rewardPointsExportUrl: (locationId: string, from: string, to: string, customerId?: string) =>
      `/api/proxy/reports/reward-points/export?${query({ locationId, from, to, customerId })}`,
  },

  /**
   * Printable documents (guide App. L). Label sheets carry a body, so they are fetched as a blob
   * rather than opened as a link; the single-item and whole-catalogue prints are plain GETs and
   * open straight in the browser's PDF viewer.
   */
  documents: {
    labelStocks: () => call<LabelStockOption[]>(() => apiClient.get('/documents/labels/stocks')),

    printPriceTags: (body: PrintLabelsRequest) =>
      callPdf(() => apiClient.post('/documents/labels/price-tags', body, { responseType: 'blob' })),

    printBarcodeLabels: (body: PrintLabelsRequest) =>
      callPdf(() => apiClient.post('/documents/labels/barcodes', body, { responseType: 'blob' })),

    priceTagUrl: (productId: string, locationId: string, stock: LabelStock, copies = 1) =>
      `/api/proxy/documents/labels/price-tag/${productId}?${query({ locationId, stock, copies })}`,

    statementEnvelopeUrl: (customerId: string) =>
      `/api/proxy/documents/envelopes/statement/${customerId}`,

    catalogueUrl: (locationId: string, filters: { departmentId?: string; categoryId?: string; search?: string } = {}) =>
      `/api/proxy/documents/catalogue?${query({ locationId, ...filters })}`,
  },

  /**
   * Batch changes across a selection (guide p.45). The preview is a separate call because there is
   * no undo — a batch reprice is put in front of someone before it is written.
   */
  bulk: {
    previewPrice: (
      filter: BulkFilter,
      target: BulkPriceTarget,
      method: BulkAdjustMethod,
      amount: number,
      rounding: PriceRounding,
      take = 200,
    ) =>
      call<BulkPricePreview>(() =>
        apiClient.post('/catalog/bulk/price/preview', { filter, target, method, amount, rounding, take })),

    applyPrice: (
      filter: BulkFilter,
      target: BulkPriceTarget,
      method: BulkAdjustMethod,
      amount: number,
      rounding: PriceRounding,
    ) => call<number>(() => apiClient.post('/catalog/bulk/price', { filter, target, method, amount, rounding })),

    applyTax: (filter: BulkFilter, tax1Applies: boolean | null, tax2Applies: boolean | null) =>
      call<number>(() => apiClient.post('/catalog/bulk/tax', { filter, tax1Applies, tax2Applies })),
  },

  /** Stock moving between stores (guide p.20–21). */
  transfers: {
    destinations: (locationId: string) =>
      call<TransferDestination[]>(() => apiClient.get(`/transfers/destinations?${query({ locationId })}`)),

    browse: (locationId: string, filters: { status?: TransferStatus; includeInbound?: boolean } = {}) =>
      call<TransferRow[]>(() => apiClient.get(`/transfers?${query({ locationId, ...filters })}`)),

    get: (id: string) => call<Transfer>(() => apiClient.get(`/transfers/${id}`)),

    create: (fromLocationId: string, toLocationId: string, notes?: string) =>
      call<Transfer>(() => apiClient.post('/transfers', { fromLocationId, toLocationId, notes })),

    upsertLine: (id: string, productId: string, quantity: number) =>
      call<Transfer>(() => apiClient.post(`/transfers/${id}/lines`, { productId, quantity })),

    removeLine: (id: string, lineId: string) =>
      call<Transfer>(() => apiClient.delete(`/transfers/${id}/lines/${lineId}`)),

    ship: (id: string) => call<Transfer>(() => apiClient.post(`/transfers/${id}/ship`)),

    /** An empty line list receives everything still outstanding. */
    receive: (id: string, lines?: Array<{ lineId: string; quantity: number }>) =>
      call<Transfer>(() => apiClient.post(`/transfers/${id}/receive`, { lines: lines ?? null })),

    cancel: (id: string) => call<Transfer>(() => apiClient.post(`/transfers/${id}/cancel`)),
  },

  /** Stock counts (guide p.22): count, review the variances, then post. */
  stockCounts: {
    browse: (locationId: string, status?: StockCountStatus) =>
      call<StockCountRow[]>(() => apiClient.get(`/stock-counts?${query({ locationId, status })}`)),

    get: (id: string, varianceOnly = false, take = 500) =>
      call<StockCount>(() => apiClient.get(`/stock-counts/${id}?${query({ varianceOnly, take })}`)),

    start: (locationId: string, departmentId?: string, notes?: string) =>
      call<StockCount>(() => apiClient.post('/stock-counts', { locationId, departmentId, notes })),

    importLines: (id: string, items: Array<{ stockCode: string; countedQty: number; notes?: string }>) =>
      call<CountImportResult>(() => apiClient.post(`/stock-counts/${id}/lines`, { items })),

    importCsv: (id: string, csv: string) =>
      call<CountImportResult>(() => apiClient.post(`/stock-counts/${id}/import`, { csv })),

    removeLine: (id: string, lineId: string) =>
      call<StockCount>(() => apiClient.delete(`/stock-counts/${id}/lines/${lineId}`)),

    post: (id: string, reason?: string) =>
      call<StockCount>(() => apiClient.post(`/stock-counts/${id}/post`, { reason })),

    cancel: (id: string) => call<StockCount>(() => apiClient.post(`/stock-counts/${id}/cancel`)),

    exportUrl: (id: string, varianceOnly = true) =>
      `/api/proxy/stock-counts/${id}/export?${query({ varianceOnly })}`,
  },

  /** The accounting link (doc 09 §1) — what to post, whether it can be posted, and what happened. */
  accounting: {
    preflight: (locationId: string) =>
      call<PreflightReport>(() => apiClient.get(`/sync/accounting/preflight?${query({ locationId })}`)),

    run: (entity: SyncEntityName, locationId: string, extra: SyncRunOptions = {}) =>
      call<SyncRunResult>(() =>
        apiClient.post(`/sync/accounting/push/${entity}?${query({ locationId, ...extra })}`)),

    /** The generated file itself, downloaded rather than fetched — it is meant to be handed on. */
    exportUrl: (entity: SyncEntityName, locationId: string, extra: SyncRunOptions = {}) =>
      `/api/proxy/sync/accounting/${entity}/export?${query({ locationId, ...extra })}`,

    log: (filters: { entity?: string; status?: string; skip?: number; take?: number } = {}) =>
      call<SyncLogPage>(() => apiClient.get(`/sync/accounting/log?${query({ ...filters })}`)),

    logDetail: (id: string) => call<SyncLogDetail>(() => apiClient.get(`/sync/accounting/log/${id}`)),

    mappings: (provider = 'csv') =>
      call<ExternalMapRow[]>(() => apiClient.get(`/sync/accounting/mappings?${query({ provider })}`)),

    saveMapping: (body: {
      provider: string;
      entityType: string;
      localId?: string | null;
      localKey?: string | null;
      remoteId: string;
      remoteName?: string | null;
    }) => call<ExternalMapRow>(() => apiClient.post('/sync/accounting/mappings', body)),
  },

  audit: {
    list: (filters: AuditFilters = {}) => call<AuditLogPage>(() => apiClient.get(`/audit?${query({ ...filters })}`)),

    forRequest: (correlationId: string) =>
      call<AuditLogRow[]>(() => apiClient.get(`/audit/request/${encodeURIComponent(correlationId)}`)),
  },

  matrix: {
    get: (productId: string) => call<Matrix>(() => apiClient.get(`/products/${productId}/matrix`)),

    define: (productId: string, dimensions: MatrixDimension[]) =>
      call<Matrix>(() => apiClient.post(`/products/${productId}/matrix`, { dimensions })),
  },

  settings: {
    get: (locationId: string) => call<SettingsSnapshot>(() => apiClient.get(`/settings?${query({ locationId })}`)),

    business: (body: unknown) => call<unknown>(() => apiClient.put('/settings/business', body)),

    taxes: (body: unknown) => call<unknown>(() => apiClient.post('/settings/taxes', body)),

    pos: (body: unknown) => call<unknown>(() => apiClient.put('/settings/pos', body)),

    numbering: (body: unknown) => call<unknown>(() => apiClient.put('/settings/numbering', body)),

    pricingLadder: (body: unknown) => call<unknown>(() => apiClient.put('/settings/pricing-ladder', body)),

    station: (body: unknown) => call<unknown>(() => apiClient.post('/settings/stations', body)),

    deactivateStation: (id: string) => call<void>(() => apiClient.post(`/settings/stations/${id}/deactivate`)),

    printer: (body: unknown) => call<unknown>(() => apiClient.post('/settings/printers', body)),

    scale: (body: unknown) => call<unknown>(() => apiClient.post('/settings/scales', body)),

    poleDisplay: (body: unknown) => call<unknown>(() => apiClient.post('/settings/pole-displays', body)),

    reader: (body: unknown) => call<unknown>(() => apiClient.post('/settings/readers', body)),

    tender: (body: unknown) => call<unknown>(() => apiClient.post('/settings/tenders', body)),

    removeTender: (id: string, locationId: string) =>
      call<void>(() => apiClient.delete(`/settings/tenders/${id}?${query({ locationId })}`)),

    currency: (body: unknown) => call<unknown>(() => apiClient.post('/settings/currencies', body)),

    staff: (body: unknown) => call<unknown>(() => apiClient.post('/settings/staff', body)),
  },
};
