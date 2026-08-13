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
  RefundLineRequest,
  RefundResult,
  RefundTenderRequest,
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
  CommissionReportResult,
  CommissionRule,
  ArchiveRow,
  FiscalYear,
  FiscalYearCloseResult,
  AnalysisReport,
  LegacyControlTotals,
  LegacySourceKind,
  MigrationBatch,
  ReconciliationReport,
  StagingRow,
  ValidationFinding,
  HoursReportResult,
  StaffRow,
  AssignableRole,
  CreateStaffBody,
  TimeClockEntry,
  TimeClockState,
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
  departmentId?: number;
  categoryId?: number;
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
  supplierId?: number;
  status?: PurchaseOrderStatus;
  cursor?: string;
  pageSize?: number;
}

export interface SalesLogFilters {
  from?: string;
  to?: string;
  stationId?: number;
  staffId?: number;
  customerId?: number;
  includeVoided?: boolean;
  skip?: number;
  take?: number;
}

export interface AuditFilters {
  from?: string;
  to?: string;
  actorStaffId?: number;
  stationId?: number;
  entityType?: string;
  entityId?: number;
  action?: AuditAction;
  skip?: number;
  take?: number;
}

export const mastersApi = {
  products: {
    browse: (locationId: number, filters: ProductBrowseFilters = {}) =>
      call<CursorPage<ProductRow>>(() =>
        apiClient.get(`/catalog/products?${query({ locationId, ...filters })}`),
      ),

    get: (id: number) => call<ProductForm>(() => apiClient.get(`/catalog/products/${id}`)),

    create: (body: unknown) => call<ProductForm>(() => apiClient.post('/catalog/products', body)),

    update: (id: number, body: unknown) => call<ProductForm>(() => apiClient.put(`/catalog/products/${id}`, body)),

    clone: (id: number, newStockCode: string, newName?: string) =>
      call<ProductForm>(() => apiClient.post(`/catalog/products/${id}/clone`, { newStockCode, newName })),

    remove: (id: number) => call<void>(() => apiClient.delete(`/catalog/products/${id}`)),

    restore: (id: number) => call<void>(() => apiClient.post(`/catalog/products/${id}/restore`)),
  },

  departments: {
    list: (locationId: number, includeInactive = false) =>
      call<ReferenceRow[]>(() => apiClient.get(`/catalog/departments?${query({ locationId, includeInactive })}`)),

    save: (body: unknown) => call<ReferenceRow>(() => apiClient.post('/catalog/departments', body)),

    remove: (id: number) => call<void>(() => apiClient.delete(`/catalog/departments/${id}`)),
  },

  categories: {
    list: (locationId: number, includeInactive = false) =>
      call<ReferenceRow[]>(() => apiClient.get(`/catalog/categories?${query({ locationId, includeInactive })}`)),

    save: (body: unknown) => call<ReferenceRow>(() => apiClient.post('/catalog/categories', body)),

    remove: (id: number) => call<void>(() => apiClient.delete(`/catalog/categories/${id}`)),
  },

  customers: {
    browse: (locationId: number, filters: CustomerBrowseFilters = {}) =>
      call<CursorPage<CustomerRow>>(() => apiClient.get(`/customers?${query({ locationId, ...filters })}`)),

    get: (id: number) => call<CustomerForm>(() => apiClient.get(`/customers/${id}`)),

    clientTypes: (locationId: number) =>
      call<string[]>(() => apiClient.get(`/customers/client-types?${query({ locationId })}`)),

    create: (body: unknown) => call<CustomerForm>(() => apiClient.post('/customers', body)),

    update: (id: number, body: unknown) => call<CustomerForm>(() => apiClient.put(`/customers/${id}`, body)),

    remove: (id: number) => call<void>(() => apiClient.delete(`/customers/${id}`)),

    restore: (id: number) => call<void>(() => apiClient.post(`/customers/${id}/restore`)),
  },

  suppliers: {
    browse: (locationId: number, filters: SupplierBrowseFilters = {}) =>
      call<CursorPage<SupplierRow>>(() => apiClient.get(`/suppliers?${query({ locationId, ...filters })}`)),

    get: (id: number) => call<SupplierForm>(() => apiClient.get(`/suppliers/${id}`)),

    create: (body: unknown) => call<SupplierForm>(() => apiClient.post('/suppliers', body)),

    update: (id: number, body: unknown) => call<SupplierForm>(() => apiClient.put(`/suppliers/${id}`, body)),

    remove: (id: number) => call<void>(() => apiClient.delete(`/suppliers/${id}`)),

    restore: (id: number) => call<void>(() => apiClient.post(`/suppliers/${id}/restore`)),
  },

  purchaseOrders: {
    browse: (locationId: number, filters: PurchaseOrderBrowseFilters = {}) =>
      call<CursorPage<PurchaseOrderRow>>(() => apiClient.get(`/purchase-orders?${query({ locationId, ...filters })}`)),

    get: (id: number) => call<PurchaseOrderDetail>(() => apiClient.get(`/purchase-orders/${id}`)),

    generate: (locationId: number, supplierId: number, strategy: OrderQuantityStrategy) =>
      call<PurchaseOrderDetail>(() => apiClient.post('/purchase-orders/generate', { locationId, supplierId, strategy })),

    addLine: (purchaseOrderId: number, body: { productId: number; orderQty: number; costEach: number; caseQty: number }) =>
      call<PurchaseOrderDetail>(() => apiClient.post(`/purchase-orders/${purchaseOrderId}/lines`, body)),

    updateLine: (lineId: number, body: { orderQty: number; costEach: number }) =>
      call<PurchaseOrderDetail>(() => apiClient.put(`/purchase-orders/lines/${lineId}`, body)),

    removeLine: (lineId: number) => call<PurchaseOrderDetail>(() => apiClient.delete(`/purchase-orders/lines/${lineId}`)),

    post: (id: number) => call<PurchaseOrderDetail>(() => apiClient.post(`/purchase-orders/${id}/post`)),

    receive: (
      id: number,
      body: { receivedOn: string; freightTotal: number; lines: { lineId: number; qtyReceived: number }[] },
    ) => call<PurchaseOrderDetail>(() => apiClient.post(`/purchase-orders/${id}/receive`, body)),

    cancel: (id: number) => call<PurchaseOrderDetail>(() => apiClient.post(`/purchase-orders/${id}/cancel`)),
  },

  inventory: {
    stockLevels: (locationId: number, filters: StockLevelBrowseFilters = {}) =>
      call<CursorPage<StockLevelRow>>(() => apiClient.get(`/inventory/stock-levels?${query({ locationId, ...filters })}`)),

    receive: (body: { productId: number; locationId: number; quantity: number; unitCost: number }) =>
      call<StockLevelRow>(() => apiClient.post('/inventory/receive', body)),

    adjust: (body: { productId: number; locationId: number; quantityDelta: number; reason: string }) =>
      call<StockLevelRow>(() => apiClient.post('/inventory/adjust', body)),

    breakCase: (body: { parentProductId: number; locationId: number; casesToBreak: number }) =>
      call<void>(() => apiClient.post('/inventory/case-break', body)),
  },

  receivables: {
    browseAccounts: (locationId: number, filters: CustomerAccountBrowseFilters = {}) =>
      call<CursorPage<CustomerAccountRow>>(() => apiClient.get(`/receivables/accounts?${query({ locationId, ...filters })}`)),

    statement: (customerId: number) =>
      call<CustomerStatement>(() => apiClient.get(`/receivables/customers/${customerId}/statement`)),

    aging: (locationId: number) =>
      call<ReceivablesAgingRow[]>(() => apiClient.get(`/receivables/aging?${query({ locationId })}`)),

    takePayment: (body: { customerId: number; amount: number; tenderTypeId: number; reference?: string }) =>
      call<{ amountApplied: number; amountUnapplied: number }>(() => apiClient.post('/receivables/payments', body)),

    voidInvoice: (invoiceId: number, reason?: string) =>
      call<InvoiceRow>(() => apiClient.post(`/receivables/invoices/${invoiceId}/void`, { reason })),

    refundInvoice: (invoiceId: number, amount: number, reason?: string) =>
      call<InvoiceRow>(() => apiClient.post(`/receivables/invoices/${invoiceId}/refund`, { amount, reason })),
  },

  giftCards: {
    issue: (body: { value: number; serialNumber?: string; customerId?: number; expiresOn?: string }) =>
      call<GiftCard>(() => apiClient.post('/gift-cards', body)),

    balance: (serialNumber: string) => call<GiftCard>(() => apiClient.get(`/gift-cards/${encodeURIComponent(serialNumber)}`)),
  },

  loyalty: {
    getPolicy: (locationId: number) => call<LoyaltyPolicy>(() => apiClient.get(`/loyalty/policy?${query({ locationId })}`)),

    savePolicy: (policy: LoyaltyPolicy) => call<LoyaltyPolicy>(() => apiClient.put('/loyalty/policy', policy)),

    balance: (customerId: number) => call<LoyaltyBalance>(() => apiClient.get(`/loyalty/customers/${customerId}/balance`)),

    ledger: (customerId: number) => call<LoyaltyLedgerEntryRow[]>(() => apiClient.get(`/loyalty/customers/${customerId}/ledger`)),

    adjust: (customerId: number, pointsDelta: number, reason: string) =>
      call<LoyaltyBalance>(() => apiClient.post(`/loyalty/customers/${customerId}/adjust`, { pointsDelta, reason })),
  },

  customerOrders: {
    browse: (locationId: number, filters: { customerId?: number; status?: CustomerOrderStatus; cursor?: string; pageSize?: number } = {}) =>
      call<CursorPage<CustomerOrder>>(() => apiClient.get(`/customer-orders?${query({ locationId, ...filters })}`)),

    get: (id: number) => call<CustomerOrder>(() => apiClient.get(`/customer-orders/${id}`)),

    create: (body: { customerId: number; locationId: number; lines: { productId: number; quantity: number; unitPrice: number }[]; notes?: string }) =>
      call<CustomerOrder>(() => apiClient.post('/customer-orders', body)),

    fill: (id: number) => call<CustomerOrder>(() => apiClient.post(`/customer-orders/${id}/fill`)),

    cancel: (id: number) => call<CustomerOrder>(() => apiClient.post(`/customer-orders/${id}/cancel`)),
  },

  layaways: {
    browse: (locationId: number, filters: { customerId?: number; status?: LayawayStatus; cursor?: string; pageSize?: number } = {}) =>
      call<CursorPage<Layaway>>(() => apiClient.get(`/layaways?${query({ locationId, ...filters })}`)),

    get: (id: number) => call<Layaway>(() => apiClient.get(`/layaways/${id}`)),

    create: (body: { customerId: number; locationId: number; lines: { productId: number; quantity: number; unitPrice: number }[] }) =>
      call<Layaway>(() => apiClient.post('/layaways', body)),

    takePayment: (id: number, amount: number, tenderTypeId: number) =>
      call<Layaway>(() => apiClient.post(`/layaways/${id}/payments`, { amount, tenderTypeId })),

    cancel: (id: number) => call<Layaway>(() => apiClient.post(`/layaways/${id}/cancel`)),
  },

  priceQuotes: {
    browse: (locationId: number, filters: { customerId?: number; status?: PriceQuoteStatus; cursor?: string; pageSize?: number } = {}) =>
      call<CursorPage<PriceQuote>>(() => apiClient.get(`/price-quotes?${query({ locationId, ...filters })}`)),

    get: (id: number) => call<PriceQuote>(() => apiClient.get(`/price-quotes/${id}`)),

    create: (body: { customerId: number; locationId: number; lines: { productId: number; quantity: number; unitPrice: number }[]; expiresOn?: string }) =>
      call<PriceQuote>(() => apiClient.post('/price-quotes', body)),

    convert: (id: number) => call<PriceQuote>(() => apiClient.post(`/price-quotes/${id}/convert`)),

    cancel: (id: number) => call<PriceQuote>(() => apiClient.post(`/price-quotes/${id}/cancel`)),
  },

  deleted: {
    list: (locationId: number, kind?: DeletedEntityKind, search?: string) =>
      call<DeletedRow[]>(() => apiClient.get(`/catalog/deleted?${query({ locationId, kind, search })}`)),

    restore: (kind: DeletedEntityKind, id: number) =>
      call<void>(() => apiClient.post(`/catalog/deleted/${kind}/${id}/restore`)),
  },

  /** The itemized sales log (guide p.14–15) and one sale in full. */
  sales: {
    log: (locationId: number, filters: SalesLogFilters = {}) =>
      call<SalesLogPage>(() => apiClient.get(`/sales?${query({ locationId, ...filters })}`)),

    get: (transactionId: number) => call<SaleDetail>(() => apiClient.get(`/sales/${transactionId}`)),

    /** Station omitted means "wherever this sale was rung", which the server already knows. */
    reprint: (transactionId: number, stationId?: number, copies = 1) =>
      call<unknown>(() => apiClient.post(`/sales/${transactionId}/reprint`, { stationId, copies })),

    /**
     * Gives part of a sale back. Carries an idempotency key for the reason paying does: a retried
     * refund must hand back the first one rather than pay the customer twice.
     */
    refund: (
      transactionId: number,
      lines: RefundLineRequest[],
      tenders: RefundTenderRequest[],
      reason?: string,
    ) =>
      call<RefundResult>(() =>
        apiClient.post(
          `/sales/${transactionId}/refund`,
          { lines, tenders, reason },
          { headers: { 'Idempotency-Key': crypto.randomUUID() } },
        ),
      ),

    /** The URL the browser downloads from — the modern "Open In MS-Excel" (guide p.101). */
    exportUrl: (locationId: number, filters: SalesLogFilters = {}) =>
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

    tax: (locationId: number, from: string, to: string, includeVoided = false) =>
      call<TaxReportResult>(() => apiClient.get(`/reports/tax?${query({ locationId, from, to, includeVoided })}`)),

    taxExportUrl: (locationId: number, from: string, to: string, includeVoided = false) =>
      `/api/proxy/reports/tax/export?${query({ locationId, from, to, includeVoided })}`,

    stockValue: (locationId: number, departmentId?: number) =>
      call<StockValuationResult>(() => apiClient.get(`/reports/stock-value?${query({ locationId, departmentId })}`)),

    stockValueDetail: (locationId: number, departmentId?: number, skip = 0, take = 200) =>
      call<StockValuationDetailPage>(() =>
        apiClient.get(`/reports/stock-value/detail?${query({ locationId, departmentId, skip, take })}`)),

    stockValueExportUrl: (locationId: number, departmentId?: number) =>
      `/api/proxy/reports/stock-value/export?${query({ locationId, departmentId })}`,

    stockPosition: (locationId: number, departmentId?: number, only?: StockPositionKind) =>
      call<StockPositionRow[]>(() =>
        apiClient.get(`/reports/stock-position?${query({ locationId, departmentId, only })}`)),

    stockPositionExportUrl: (locationId: number, departmentId?: number, only?: StockPositionKind) =>
      `/api/proxy/reports/stock-position/export?${query({ locationId, departmentId, only })}`,

    onOrder: (locationId: number, supplierId?: number, departmentId?: number) =>
      call<OnOrderRow[]>(() => apiClient.get(`/reports/on-order?${query({ locationId, supplierId, departmentId })}`)),

    onOrderExportUrl: (locationId: number, supplierId?: number, departmentId?: number) =>
      `/api/proxy/reports/on-order/export?${query({ locationId, supplierId, departmentId })}`,

    stockReceived: (locationId: number, from: string, to: string, supplierId?: number, skip = 0, take = 200) =>
      call<StockReceivedPage>(() =>
        apiClient.get(`/reports/stock-received?${query({ locationId, from, to, supplierId, skip, take })}`)),

    stockReceivedExportUrl: (locationId: number, from: string, to: string, supplierId?: number) =>
      `/api/proxy/reports/stock-received/export?${query({ locationId, from, to, supplierId })}`,

    rewardPoints: (locationId: number, from: string, to: string, customerId?: number) =>
      call<RewardPointsResult>(() =>
        apiClient.get(`/reports/reward-points?${query({ locationId, from, to, customerId })}`)),

    rewardPointsExportUrl: (locationId: number, from: string, to: string, customerId?: number) =>
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

    priceTagUrl: (productId: number, locationId: number, stock: LabelStock, copies = 1) =>
      `/api/proxy/documents/labels/price-tag/${productId}?${query({ locationId, stock, copies })}`,

    statementEnvelopeUrl: (customerId: number) =>
      `/api/proxy/documents/envelopes/statement/${customerId}`,

    catalogueUrl: (locationId: number, filters: { departmentId?: number; categoryId?: number; search?: string } = {}) =>
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
    destinations: (locationId: number) =>
      call<TransferDestination[]>(() => apiClient.get(`/transfers/destinations?${query({ locationId })}`)),

    browse: (locationId: number, filters: { status?: TransferStatus; includeInbound?: boolean } = {}) =>
      call<TransferRow[]>(() => apiClient.get(`/transfers?${query({ locationId, ...filters })}`)),

    get: (id: number) => call<Transfer>(() => apiClient.get(`/transfers/${id}`)),

    create: (fromLocationId: number, toLocationId: number, notes?: string) =>
      call<Transfer>(() => apiClient.post('/transfers', { fromLocationId, toLocationId, notes })),

    upsertLine: (id: number, productId: number, quantity: number) =>
      call<Transfer>(() => apiClient.post(`/transfers/${id}/lines`, { productId, quantity })),

    removeLine: (id: number, lineId: number) =>
      call<Transfer>(() => apiClient.delete(`/transfers/${id}/lines/${lineId}`)),

    ship: (id: number) => call<Transfer>(() => apiClient.post(`/transfers/${id}/ship`)),

    /** An empty line list receives everything still outstanding. */
    receive: (id: number, lines?: Array<{ lineId: number; quantity: number }>) =>
      call<Transfer>(() => apiClient.post(`/transfers/${id}/receive`, { lines: lines ?? null })),

    cancel: (id: number) => call<Transfer>(() => apiClient.post(`/transfers/${id}/cancel`)),
  },

  /** Stock counts (guide p.22): count, review the variances, then post. */
  stockCounts: {
    browse: (locationId: number, status?: StockCountStatus) =>
      call<StockCountRow[]>(() => apiClient.get(`/stock-counts?${query({ locationId, status })}`)),

    get: (id: number, varianceOnly = false, take = 500) =>
      call<StockCount>(() => apiClient.get(`/stock-counts/${id}?${query({ varianceOnly, take })}`)),

    start: (locationId: number, departmentId?: number, notes?: string) =>
      call<StockCount>(() => apiClient.post('/stock-counts', { locationId, departmentId, notes })),

    importLines: (id: number, items: Array<{ stockCode: string; countedQty: number; notes?: string }>) =>
      call<CountImportResult>(() => apiClient.post(`/stock-counts/${id}/lines`, { items })),

    importCsv: (id: number, csv: string) =>
      call<CountImportResult>(() => apiClient.post(`/stock-counts/${id}/import`, { csv })),

    removeLine: (id: number, lineId: number) =>
      call<StockCount>(() => apiClient.delete(`/stock-counts/${id}/lines/${lineId}`)),

    post: (id: number, reason?: string) =>
      call<StockCount>(() => apiClient.post(`/stock-counts/${id}/post`, { reason })),

    cancel: (id: number) => call<StockCount>(() => apiClient.post(`/stock-counts/${id}/cancel`)),

    exportUrl: (id: number, varianceOnly = true) =>
      `/api/proxy/stock-counts/${id}/export?${query({ varianceOnly })}`,
  },

  /**
   * Fiscal years and the year-end close (guide p.29). The close destroys nothing, which is what
   * makes reopening a safe thing to offer.
   */
  fiscalYears: {
    list: (locationId: number) =>
      call<FiscalYear[]>(() => apiClient.get(`/fiscal-years?${query({ locationId })}`)),

    open: (locationId: number, year: number, notes?: string) =>
      call<FiscalYear>(() => apiClient.post('/fiscal-years', { locationId, year, notes })),

    /** `dryRun` calculates everything and writes nothing. Always run it first. */
    close: (id: number, dryRun: boolean) =>
      call<FiscalYearCloseResult>(() => apiClient.post(`/fiscal-years/${id}/close?${query({ dryRun })}`)),

    reopen: (id: number) => call<FiscalYear>(() => apiClient.post(`/fiscal-years/${id}/reopen`)),

    history: (locationId: number, year?: number, productId?: number, take = 500) =>
      call<ArchiveRow[]>(() => apiClient.get(`/fiscal-years/history?${query({ locationId, year, productId, take })}`)),

    historyExportUrl: (locationId: number, year?: number) =>
      `/api/proxy/fiscal-years/history/export?${query({ locationId, year })}`,
  },

  /**
   * The legacy migration pipeline (doc 09 §3): analyze → stage → validate → dry-run → import.
   * Content goes up base64 so a DBF survives the round trip.
   */
  migration: {
    kinds: () => call<LegacySourceKind[]>(() => apiClient.get('/migration/kinds')),

    batches: (locationId: number) =>
      call<MigrationBatch[]>(() => apiClient.get(`/migration/batches?${query({ locationId })}`)),

    batch: (id: number) => call<MigrationBatch>(() => apiClient.get(`/migration/batches/${id}`)),

    analysis: (id: number) => call<AnalysisReport>(() => apiClient.get(`/migration/batches/${id}/analysis`)),

    validation: (id: number) =>
      call<ValidationFinding[]>(() => apiClient.get(`/migration/batches/${id}/validation`)),

    reconciliation: (id: number) =>
      call<ReconciliationReport>(() => apiClient.get(`/migration/batches/${id}/reconciliation`)),

    rows: (id: number, problemsOnly = true, take = 200) =>
      call<StagingRow[]>(() => apiClient.get(`/migration/batches/${id}/rows?${query({ problemsOnly, take })}`)),

    stage: (locationId: number, fileName: string, entity: string, base64: string) =>
      call<MigrationBatch>(() =>
        apiClient.post('/migration/stage', { locationId, fileName, entity, content: base64, isBase64: true })),

    validate: (id: number) => call<MigrationBatch>(() => apiClient.post(`/migration/batches/${id}/validate`)),

    dryRun: (id: number, totals: LegacyControlTotals | null) =>
      call<ReconciliationReport>(() => apiClient.post(`/migration/batches/${id}/dry-run`, totals)),

    import: (id: number, totals: LegacyControlTotals | null) =>
      call<ReconciliationReport>(() => apiClient.post(`/migration/batches/${id}/import`, totals)),

    cancel: (id: number) => call<void>(() => apiClient.post(`/migration/batches/${id}/cancel`)),
  },

  /** Staff, the time clock and commissions (guide p.33, p.75–76). */
  staff: {
    browse: (locationId: number, includeInactive = false) =>
      call<StaffRow[]>(() => apiClient.get(`/staff?${query({ locationId, includeInactive })}`)),

    /** The roles this deployment can assign — read from the server, never hardcoded in the picker. */
    roles: () => call<AssignableRole[]>(() => apiClient.get('/staff/roles')),

    /** Onboards a colleague: one sign-in and one staff record, created together. */
    create: (body: CreateStaffBody) => call<StaffRow>(() => apiClient.post('/staff', body)),

    /**
     * An administrator setting someone's password for them. The id travels in the path so the
     * password stays out of the query string, and out of the web server's request log.
     */
    resetPassword: (staffId: number, newPassword: string) =>
      call<void>(() => apiClient.post(`/staff/${staffId}/password`, { newPassword })),

    myTimeClock: (locationId: number) =>
      call<TimeClockState>(() => apiClient.get(`/staff/time-clock/me?${query({ locationId })}`)),

    clockIn: (locationId: number) =>
      call<TimeClockState>(() => apiClient.post('/staff/time-clock/in', { locationId })),

    clockOut: (locationId: number) =>
      call<TimeClockState>(() => apiClient.post('/staff/time-clock/out', { locationId })),

    timeClock: (locationId: number, from: string, to: string, staffId?: number) =>
      call<TimeClockEntry[]>(() => apiClient.get(`/staff/time-clock?${query({ locationId, from, to, staffId })}`)),

    amendTimeClock: (id: number, clockIn: string, clockOut: string | null) =>
      call<TimeClockEntry>(() => apiClient.put(`/staff/time-clock/${id}`, { clockIn, clockOut })),

    deleteTimeClock: (id: number) => call<void>(() => apiClient.delete(`/staff/time-clock/${id}`)),

    commissionRules: (staffId: number) =>
      call<CommissionRule[]>(() => apiClient.get(`/staff/${staffId}/commission-rules`)),

    saveCommissionRule: (body: unknown) =>
      call<CommissionRule>(() => apiClient.post('/staff/commission-rules', body)),

    deleteCommissionRule: (id: number) => call<void>(() => apiClient.delete(`/staff/commission-rules/${id}`)),

    hours: (locationId: number, from: string, to: string, staffId?: number) =>
      call<HoursReportResult>(() => apiClient.get(`/staff/reports/hours?${query({ locationId, from, to, staffId })}`)),

    hoursExportUrl: (locationId: number, from: string, to: string, staffId?: number) =>
      `/api/proxy/staff/reports/hours/export?${query({ locationId, from, to, staffId })}`,

    commissions: (locationId: number, from: string, to: string, staffId?: number, includeDetail = false) =>
      call<CommissionReportResult>(() =>
        apiClient.get(`/staff/reports/commissions?${query({ locationId, from, to, staffId, includeDetail })}`)),

    commissionsExportUrl: (locationId: number, from: string, to: string, staffId?: number) =>
      `/api/proxy/staff/reports/commissions/export?${query({ locationId, from, to, staffId })}`,
  },

  /** The accounting link (doc 09 §1) — what to post, whether it can be posted, and what happened. */
  accounting: {
    preflight: (locationId: number) =>
      call<PreflightReport>(() => apiClient.get(`/sync/accounting/preflight?${query({ locationId })}`)),

    run: (entity: SyncEntityName, locationId: number, extra: SyncRunOptions = {}) =>
      call<SyncRunResult>(() =>
        apiClient.post(`/sync/accounting/push/${entity}?${query({ locationId, ...extra })}`)),

    /** The generated file itself, downloaded rather than fetched — it is meant to be handed on. */
    exportUrl: (entity: SyncEntityName, locationId: number, extra: SyncRunOptions = {}) =>
      `/api/proxy/sync/accounting/${entity}/export?${query({ locationId, ...extra })}`,

    log: (filters: { entity?: string; status?: string; skip?: number; take?: number } = {}) =>
      call<SyncLogPage>(() => apiClient.get(`/sync/accounting/log?${query({ ...filters })}`)),

    logDetail: (id: number) => call<SyncLogDetail>(() => apiClient.get(`/sync/accounting/log/${id}`)),

    mappings: (provider = 'csv') =>
      call<ExternalMapRow[]>(() => apiClient.get(`/sync/accounting/mappings?${query({ provider })}`)),

    saveMapping: (body: {
      provider: string;
      entityType: string;
      localId?: number | null;
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
    get: (productId: number) => call<Matrix>(() => apiClient.get(`/products/${productId}/matrix`)),

    define: (productId: number, dimensions: MatrixDimension[]) =>
      call<Matrix>(() => apiClient.post(`/products/${productId}/matrix`, { dimensions })),
  },

  settings: {
    get: (locationId: number) => call<SettingsSnapshot>(() => apiClient.get(`/settings?${query({ locationId })}`)),

    business: (body: unknown) => call<unknown>(() => apiClient.put('/settings/business', body)),

    taxes: (body: unknown) => call<unknown>(() => apiClient.post('/settings/taxes', body)),

    pos: (body: unknown) => call<unknown>(() => apiClient.put('/settings/pos', body)),

    numbering: (body: unknown) => call<unknown>(() => apiClient.put('/settings/numbering', body)),

    pricingLadder: (body: unknown) => call<unknown>(() => apiClient.put('/settings/pricing-ladder', body)),

    station: (body: unknown) => call<unknown>(() => apiClient.post('/settings/stations', body)),

    deactivateStation: (id: number) => call<void>(() => apiClient.post(`/settings/stations/${id}/deactivate`)),

    printer: (body: unknown) => call<unknown>(() => apiClient.post('/settings/printers', body)),

    scale: (body: unknown) => call<unknown>(() => apiClient.post('/settings/scales', body)),

    poleDisplay: (body: unknown) => call<unknown>(() => apiClient.post('/settings/pole-displays', body)),

    reader: (body: unknown) => call<unknown>(() => apiClient.post('/settings/readers', body)),

    tender: (body: unknown) => call<unknown>(() => apiClient.post('/settings/tenders', body)),

    removeTender: (id: number, locationId: number) =>
      call<void>(() => apiClient.delete(`/settings/tenders/${id}?${query({ locationId })}`)),

    currency: (body: unknown) => call<unknown>(() => apiClient.post('/settings/currencies', body)),

    staff: (body: unknown) => call<unknown>(() => apiClient.post('/settings/staff', body)),
  },
};
