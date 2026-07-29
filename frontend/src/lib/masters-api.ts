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
  SaleDetail,
  SalesLogPage,
  SettingsSnapshot,
  SupplierForm,
  SupplierRow,
  SupplierSort,
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
