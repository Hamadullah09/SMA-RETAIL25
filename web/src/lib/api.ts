/**
 * The one way the web app talks to RetailPlus.Server.
 *
 * - Same origin in production; `NEXT_PUBLIC_API_BASE` in development (e.g. http://localhost:5080).
 * - Every request sends the session cookie (`credentials: 'include'`); the token itself is never seen by JavaScript.
 * - Every request that changes data carries `X-Requested-With: retailplus-web`, the server's CSRF guard.
 * - A failure throws an {@link ApiError} with the server's `error` message. A 401 sends the user to the sign-in page
 *   and back here afterwards.
 */
import type {
  AuditRow,
  BatchInfo,
  Category,
  ChangePasswordRequest,
  ClientDetail,
  ClientRow,
  CommissionRow,
  CommissionRule,
  Dashboard,
  Department,
  EnrollmentCodeInfo,
  GiftCardDetail,
  GiftCardRow,
  HoursRow,
  ImageKind,
  ItemDetail,
  ItemEveryStorePlan,
  ItemEveryStoreResult,
  ItemRow,
  NewStationRequest,
  Paged,
  PermissionInfo,
  PinRequest,
  ReportInfo,
  ReportTable,
  RoleInfo,
  SaleDetail,
  SaleRow,
  SignInRequest,
  SignInResponse,
  LocationInfo,
  NewRfidHubResponse,
  RfidHubInfo,
  StaffMember,
  StationInfo,
  UploadResponse,
  StockCountDetail,
  StockCountRow,
  StockMovementRow,
  StoreInfo,
  StoreRequest,
  StoreSettings,
  SupplierDetail,
  SupplierRow,
  TemporaryPassword,
  TillInfo,
  TimeClockEntry,
  TransferDetail,
  TransferRow,
  UserInfo,
  UserRequest,
  UserRow,
  AttachmentInfo,
  BankAccountRequest,
  BankAccountRow,
  BankTransferRequest,
  BankTransferRow,
  BillDetail,
  BillRequest,
  BillRow,
  BudgetAmount,
  BudgetInfo,
  DepositCandidate,
  DepositRequest,
  DepositRow,
  ExpenseDetail,
  ExpenseRequest,
  ExpenseRow,
  FinanceAccount,
  FinanceAccountRequest,
  FinanceHome,
  FinanceReportInfo,
  FinanceSettings,
  FiscalQueue,
  JournalDetail,
  JournalRequest,
  JournalRow,
  MappingRow,
  OpeningRequest,
  PaymentDetail,
  PaymentRequest,
  PaymentRow,
  PaymentRunInfo,
  PaymentRunResult,
  PostDaysResult,
  Reconciliation,
  RecurringExpense,
  StatementFormat,
  StatementImportResult,
  StoreFinance,
  SupplierFinance,
  SupplierStatement,
  SuspenseRow,
  TaxRuleSet,
  WithholdingInfo,
  YearInfo,
} from './types';

/** The header the server's CSRF guard asks for on requests that change data. */
export const CSRF_HEADER = 'X-Requested-With';
export const CSRF_VALUE = 'retailplus-web';

/** What a 403 says when the server gives no reason. */
export const FORBIDDEN_MESSAGE = 'Your role cannot open this page.';

const FALLBACK: Record<number, string> = {
  0: 'The server cannot be reached. Check the connection and try again.',
  400: 'The server refused this. Check what you typed.',
  401: 'Sign in again.',
  403: FORBIDDEN_MESSAGE,
  404: 'Not found. It may have been deleted.',
  409: 'Someone else changed this. Reload and try again.',
  413: 'The file is too large.',
};

/** A request the server refused or could not answer. `message` is the server's `error`, ready to show. */
export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }

  get isForbidden(): boolean {
    return this.status === 403;
  }

  get isConflict(): boolean {
    return this.status === 409;
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }
}

/** The message for a status when the body has no `error`. */
export function fallbackMessage(status: number): string {
  return FALLBACK[status] ?? (status >= 500 ? 'The server had a problem. Try again in a moment.' : `The request failed (${status}).`);
}

/** The `error` of a JSON error body, or the fallback for the status. */
export function errorMessage(status: number, body: unknown): string {
  if (body && typeof body === 'object' && 'error' in body) {
    const error = (body as { error: unknown }).error;
    if (typeof error === 'string' && error.trim()) return error;
  }
  return fallbackMessage(status);
}

export type QueryValue = string | number | boolean | null | undefined;
export type Query = Record<string, QueryValue>;

/** `?a=1&b=x`, leaving out empty values. */
export function queryString(query?: Query): string {
  if (!query) return '';
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue;
    params.set(key, String(value));
  }
  const text = params.toString();
  return text ? '?' + text : '';
}

/** The sign-in page, with a way back to `here`. */
export function signInUrl(here?: string): string {
  if (!here || here.startsWith('/sign-in')) return '/sign-in/';
  return '/sign-in/?next=' + encodeURIComponent(here);
}

/** Only a path on this site may follow a sign-in, never another origin. */
export function safeNext(next: string | null | undefined): string | null {
  if (!next || !next.startsWith('/') || next.startsWith('//') || next.startsWith('/\\')) return null;
  if (next.startsWith('/sign-in')) return null;
  return next;
}

export interface ApiOptions {
  /** API origin; empty for the same origin. */
  base?: string;
  fetch?: typeof fetch;
  /** Called on a 401 with the sign-in URL. The browser goes there by default. */
  onUnauthorized?: (signIn: string) => void;
  /** Where the user is now, for the way back after signing in. */
  currentPath?: () => string;
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  query?: Query;
  /** A JSON body. */
  body?: unknown;
  /** A multipart body. */
  form?: FormData;
  signal?: AbortSignal;
  /** False on the sign-in request itself: a wrong password is a 401 that must not leave the page. */
  redirectOn401?: boolean;
}

function changesData(method: string): boolean {
  return method !== 'GET' && method !== 'HEAD';
}

function defaultBase(): string {
  const base = process.env.NEXT_PUBLIC_API_BASE ?? '';
  return base.replace(/\/+$/, '');
}

function browserPath(): string {
  if (typeof window === 'undefined') return '/';
  return window.location.pathname + window.location.search;
}

function browserGo(url: string): void {
  if (typeof window !== 'undefined') window.location.assign(url);
}

/** A typed client. The app uses the shared {@link api}; tests make their own with a fake `fetch`. */
export function createApi(options: ApiOptions = {}) {
  const base = options.base ?? defaultBase();
  const doFetch: typeof fetch = options.fetch ?? ((input, init) => fetch(input, init));
  const onUnauthorized = options.onUnauthorized ?? browserGo;
  const currentPath = options.currentPath ?? browserPath;
  let redirecting = false;

  function unauthorized(redirect: boolean, message: string): ApiError {
    if (redirect && !redirecting) {
      redirecting = true;
      onUnauthorized(signInUrl(currentPath()));
      // Let the next 401 redirect again if the user comes back without a reload (tests, back button).
      setTimeout(() => (redirecting = false), 1000);
    }
    return new ApiError(401, message);
  }

  async function send(path: string, opts: RequestOptions = {}): Promise<Response> {
    const method = opts.method ?? 'GET';
    const headers: Record<string, string> = { Accept: 'application/json' };
    let body: BodyInit | undefined;
    if (opts.form) {
      body = opts.form;
    } else if (opts.body !== undefined) {
      headers['Content-Type'] = 'application/json';
      body = JSON.stringify(opts.body);
    }
    if (changesData(method)) headers[CSRF_HEADER] = CSRF_VALUE;

    let response: Response;
    try {
      response = await doFetch(base + '/api' + path + queryString(opts.query), {
        method,
        headers,
        body,
        credentials: 'include',
        signal: opts.signal,
      });
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') throw error;
      throw new ApiError(0, fallbackMessage(0));
    }
    if (response.ok) return response;

    let payload: unknown = null;
    try {
      payload = await response.json();
    } catch {
      payload = null;
    }
    const message = errorMessage(response.status, payload);
    if (response.status === 401) throw unauthorized(opts.redirectOn401 !== false, message);
    throw new ApiError(response.status, message);
  }

  async function json<T>(path: string, opts: RequestOptions = {}): Promise<T> {
    const response = await send(path, opts);
    if (response.status === 204) return undefined as T;
    const text = await response.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  async function blob(path: string, opts: RequestOptions = {}): Promise<Blob> {
    const response = await send(path, opts);
    return response.blob();
  }

  /**
   * A multipart upload with progress (0 to 1). fetch cannot report upload progress, so this one uses
   * XMLHttpRequest, with the same cookie, CSRF header and error mapping as everything else.
   */
  function upload<T>(path: string, form: FormData, onProgress?: (fraction: number) => void, method: 'POST' | 'PUT' = 'POST'): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      xhr.open(method, base + '/api' + path);
      xhr.withCredentials = true;
      xhr.setRequestHeader(CSRF_HEADER, CSRF_VALUE);
      xhr.setRequestHeader('Accept', 'application/json');
      xhr.upload.onprogress = (event) => {
        if (event.lengthComputable && onProgress) onProgress(event.loaded / event.total);
      };
      xhr.onerror = () => reject(new ApiError(0, fallbackMessage(0)));
      xhr.onload = () => {
        let payload: unknown = null;
        try {
          payload = xhr.responseText ? JSON.parse(xhr.responseText) : null;
        } catch {
          payload = null;
        }
        if (xhr.status >= 200 && xhr.status < 300) {
          onProgress?.(1);
          resolve(payload as T);
          return;
        }
        const message = errorMessage(xhr.status, payload);
        reject(xhr.status === 401 ? unauthorized(true, message) : new ApiError(xhr.status, message));
      };
      xhr.send(form);
    });
  }

  const get = <T>(path: string, query?: Query, signal?: AbortSignal) => json<T>(path, { query, signal });
  const post = <T>(path: string, body?: unknown) => json<T>(path, { method: 'POST', body: body ?? {} });
  const put = <T>(path: string, body: unknown) => json<T>(path, { method: 'PUT', body });
  const del = <T = void>(path: string) => json<T>(path, { method: 'DELETE' });
  const id = encodeURIComponent;

  return {
    base,
    send,
    json,
    blob,
    upload,

    // Session
    health: () => get<{ status: string; database: string; version: string }>('/health'),
    signIn: (request: SignInRequest) => json<SignInResponse>('/auth/sign-in', { method: 'POST', body: request, redirectOn401: false }),
    signOut: () => json<void>('/auth/sign-out', { method: 'POST', body: {}, redirectOn401: false }),
    me: () => get<UserInfo>('/auth/me'),
    changePassword: (request: ChangePasswordRequest) => post<void>('/auth/change-password', request),

    // Stores, tills, packages
    stores: () => get<StoreInfo[]>('/stores'),
    addStore: (request: StoreRequest) => post<StoreInfo>('/stores', request),
    saveStore: (storeId: string, request: StoreRequest) => put<StoreInfo>(`/stores/${id(storeId)}`, request),
    stations: (storeId: string) => get<StationInfo[]>(`/stores/${id(storeId)}/stations`),
    locations: (storeId: string) => get<LocationInfo[]>(`/stores/${id(storeId)}/locations`),
    rfidHubs: (storeId: string) => get<RfidHubInfo[]>(`/stores/${id(storeId)}/rfid-hubs`),
    /** Makes a hub key; the answer is the only place the key is ever seen. */
    addRfidHub: (storeId: string, name: string) => post<NewRfidHubResponse>(`/stores/${id(storeId)}/rfid-hubs`, { name }),
    revokeRfidHub: (hubId: string) => post<RfidHubInfo>(`/rfid-hubs/${id(hubId)}/revoke`),
    staff: (query: Query) => get<StaffMember[]>('/staff', query),
    addStation: (storeId: string, request: NewStationRequest) => post<EnrollmentCodeInfo>(`/stores/${id(storeId)}/stations`, request),
    enrollmentCode: (stationId: string) => post<EnrollmentCodeInfo>(`/stations/${id(stationId)}/enrollment-code`),
    retireStation: (stationId: string) => post<StationInfo>(`/stations/${id(stationId)}/retire`),
    tills: (query: Query) => get<TillInfo[]>('/tills', query),
    batches: (query: Query) => get<BatchInfo[]>('/sync/batches', query),
    uploadPackage: (file: File, onProgress?: (fraction: number) => void) => {
      const form = new FormData();
      form.append('file', file, file.name);
      return upload<UploadResponse>('/sync/upload-file', form, onProgress);
    },

    // Dashboard and sales
    dashboard: (query: Query, signal?: AbortSignal) => get<Dashboard>('/dashboard', query, signal),
    sales: (query: Query, signal?: AbortSignal) => get<Paged<SaleRow>>('/sales', query, signal),
    sale: (saleId: string) => get<SaleDetail>(`/sales/${id(saleId)}`),

    // Items
    items: (query: Query, signal?: AbortSignal) => get<Paged<ItemRow>>('/items', query, signal),
    item: (itemId: string) => get<ItemDetail>(`/items/${id(itemId)}`),
    /** Without `locationId`, the item goes to the first location of `storeId` (or the user's store). */
    addItem: (item: ItemDetail, storeId?: string | null) => json<ItemDetail>('/items', { method: 'POST', body: item, query: { storeId } }),
    saveItem: (itemId: string, item: ItemDetail) => put<ItemDetail>(`/items/${id(itemId)}`, item),
    /** Where a change at every store would go. */
    itemEveryStore: (itemId: string) => get<ItemEveryStorePlan>(`/items/${id(itemId)}/every-store`),
    /** Saves this row and copies what changed on it to the code's rows at every location the user may change. */
    saveItemEveryStore: (itemId: string, item: ItemDetail) =>
      json<ItemEveryStoreResult>(`/items/${id(itemId)}`, { method: 'PUT', body: item, query: { everyStore: true } }),
    deleteItem: (itemId: string) => del(`/items/${id(itemId)}`),
    undeleteItem: (itemId: string) => post<void>(`/items/${id(itemId)}/undelete`),
    departments: () => get<Department[]>('/departments'),
    categories: (departmentId?: string | null) => get<Category[]>('/categories', { departmentId }),
    addDepartment: (name: string) => post<Department>('/departments', { name }),
    deleteDepartment: (departmentId: string) => del(`/departments/${id(departmentId)}`),
    addCategory: (name: string) => post<Category>('/categories', { name }),
    deleteCategory: (categoryId: string) => del(`/categories/${id(categoryId)}`),

    // Clients
    clients: (query: Query, signal?: AbortSignal) => get<Paged<ClientRow>>('/clients', query, signal),
    client: (clientId: string) => get<ClientDetail>(`/clients/${id(clientId)}`),
    addClient: (client: ClientDetail) => post<ClientDetail>('/clients', client),
    saveClient: (clientId: string, client: ClientDetail) => put<ClientDetail>(`/clients/${id(clientId)}`, client),
    deleteClient: (clientId: string) => del(`/clients/${id(clientId)}`),
    undeleteClient: (clientId: string) => post<void>(`/clients/${id(clientId)}/undelete`),

    // Suppliers
    suppliers: (query: Query, signal?: AbortSignal) => get<Paged<SupplierRow>>('/suppliers', query, signal),
    supplier: (supplierId: string) => get<SupplierDetail>(`/suppliers/${id(supplierId)}`),
    addSupplier: (supplier: SupplierDetail) => post<SupplierDetail>('/suppliers', supplier),
    saveSupplier: (supplierId: string, supplier: SupplierDetail) => put<SupplierDetail>(`/suppliers/${id(supplierId)}`, supplier),
    deleteSupplier: (supplierId: string) => del(`/suppliers/${id(supplierId)}`),
    undeleteSupplier: (supplierId: string) => post<void>(`/suppliers/${id(supplierId)}/undelete`),

    // Stock
    movements: (query: Query, signal?: AbortSignal) => get<Paged<StockMovementRow>>('/stock/movements', query, signal),
    transfers: (query: Query, signal?: AbortSignal) => get<Paged<TransferRow>>('/transfers', query, signal),
    transfer: (transferId: string) => get<TransferDetail>(`/transfers/${id(transferId)}`),
    counts: (query: Query, signal?: AbortSignal) => get<Paged<StockCountRow>>('/stock-counts', query, signal),
    count: (countId: string) => get<StockCountDetail>(`/stock-counts/${id(countId)}`),

    // Gift cards
    giftCards: (query: Query, signal?: AbortSignal) => get<Paged<GiftCardRow>>('/gift-cards', query, signal),
    giftCard: (cardId: string) => get<GiftCardDetail>(`/gift-cards/${id(cardId)}`),

    // Reports
    reports: () => get<ReportInfo[]>('/reports'),
    report: (key: string, query: Query) => get<ReportTable>(`/reports/${id(key)}`, query),
    reportCsv: (key: string, query: Query) => blob(`/reports/${id(key)}/csv`, { query }),

    // Users and roles
    permissions: () => get<PermissionInfo[]>('/permissions'),
    roles: () => get<RoleInfo[]>('/roles'),
    addRole: (role: RoleInfo) => post<RoleInfo>('/roles', role),
    saveRole: (roleId: string, role: RoleInfo) => put<RoleInfo>(`/roles/${id(roleId)}`, role),
    deleteRole: (roleId: string) => del(`/roles/${id(roleId)}`),
    users: (query: Query) => get<UserRow[]>('/users', query),
    addUser: (user: UserRequest) => post<UserRow>('/users', user),
    saveUser: (userId: string, user: UserRequest) => put<UserRow>(`/users/${id(userId)}`, user),
    deleteUser: (userId: string) => del(`/users/${id(userId)}`),
    resetPassword: (userId: string) => post<TemporaryPassword>(`/users/${id(userId)}/reset-password`),
    setPin: (userId: string, request: PinRequest) => post<void>(`/users/${id(userId)}/pin`, request),

    // Audit, staff
    audit: (query: Query, signal?: AbortSignal) => get<Paged<AuditRow>>('/audit', query, signal),
    timeClock: (query: Query) => get<TimeClockEntry[]>('/time-clock', query),
    hours: (query: Query) => get<HoursRow[]>('/time-clock/hours', query),
    commission: (query: Query) => get<CommissionRow[]>('/commission', query),
    commissionRules: () => get<CommissionRule[]>('/commission-rules'),
    addCommissionRule: (rule: CommissionRule) => post<CommissionRule>('/commission-rules', rule),
    saveCommissionRule: (ruleId: string, rule: CommissionRule) => put<CommissionRule>(`/commission-rules/${id(ruleId)}`, rule),
    deleteCommissionRule: (ruleId: string) => del(`/commission-rules/${id(ruleId)}`),

    // Store settings and branding
    settings: (storeId: string) => get<StoreSettings>(`/stores/${id(storeId)}/settings`),
    saveSettings: (storeId: string, settings: StoreSettings) => put<StoreSettings>(`/stores/${id(storeId)}/settings`, settings),
    image: (storeId: string, kind: ImageKind) => blob(`/stores/${id(storeId)}/images/${kind}`),
    uploadImage: (storeId: string, kind: ImageKind, file: File, onProgress?: (fraction: number) => void) => {
      const form = new FormData();
      form.append('file', file, file.name);
      return upload<void>(`/stores/${id(storeId)}/images/${kind}`, form, onProgress, 'PUT');
    },
    deleteImage: (storeId: string, kind: ImageKind) => del(`/stores/${id(storeId)}/images/${kind}`),
    // Finance (phase E1)
    financeHome: (query: Query) => get<FinanceHome>('/finance/home', query),
    financeAccounts: (query?: Query) => get<FinanceAccount[]>('/finance/accounts', query),
    addFinanceAccount: (account: FinanceAccountRequest) => post<FinanceAccount>('/finance/accounts', account),
    saveFinanceAccount: (accountId: string, account: FinanceAccountRequest) => put<FinanceAccount>(`/finance/accounts/${id(accountId)}`, account),
    deleteFinanceAccount: (accountId: string) => del(`/finance/accounts/${id(accountId)}`),
    ledgerRoles: () => get<string[]>('/finance/roles'),
    financeSettings: () => get<FinanceSettings>('/finance/settings'),
    saveFinanceSettings: (settings: FinanceSettings) => put<FinanceSettings>('/finance/settings', settings),
    taxRules: () => get<TaxRuleSet[]>('/finance/tax-rules'),
    addTaxRules: (set: TaxRuleSet) => post<TaxRuleSet>('/finance/tax-rules', set),
    saveTaxRules: (setId: string, set: TaxRuleSet) => put<TaxRuleSet>(`/finance/tax-rules/${id(setId)}`, set),
    deleteTaxRules: (setId: string) => del(`/finance/tax-rules/${id(setId)}`),
    storeFinance: (storeId: string) => get<StoreFinance>(`/finance/stores/${id(storeId)}`),
    saveStoreFinance: (storeId: string, store: StoreFinance) => put<StoreFinance>(`/finance/stores/${id(storeId)}`, store),
    mapping: (storeId?: string | null) => get<MappingRow[]>('/finance/mapping', { storeId }),
    saveMapping: (storeId: string | null, rows: MappingRow[]) => json<MappingRow[]>('/finance/mapping', { method: 'PUT', body: rows, query: { storeId } }),
    postDays: (from: string, to: string, storeId: string | null) => post<PostDaysResult>('/finance/post-days', { from, to, storeId }),
    suspense: (query: Query) => get<SuspenseRow[]>('/finance/suspense', query),
    dailyJournalCsv: (query: Query) => blob('/finance/daily-journal', { query }),
    journals: (query: Query, signal?: AbortSignal) => get<Paged<JournalRow>>('/finance/journals', query, signal),
    journal: (entryId: string) => get<JournalDetail>(`/finance/journals/${id(entryId)}`),
    addJournal: (entry: JournalRequest) => post<JournalDetail>('/finance/journals', entry),
    saveJournal: (entryId: string, entry: JournalRequest) => put<JournalDetail>(`/finance/journals/${id(entryId)}`, entry),
    deleteJournal: (entryId: string) => del(`/finance/journals/${id(entryId)}`),
    approveJournal: (entryId: string) => post<JournalDetail>(`/finance/journals/${id(entryId)}/approve`),
    reverseJournal: (entryId: string, date: string, reason: string) => post<JournalDetail>(`/finance/journals/${id(entryId)}/reverse`, { date, reason }),
    /** Keeps a receipt or document with a journal entry, an expense or a bill. */
    attach: (owner: 'journals' | 'expenses' | 'bills', ownerId: string, file: File, onProgress?: (fraction: number) => void) => {
      const form = new FormData();
      form.append('file', file, file.name);
      return upload<AttachmentInfo>(`/finance/${owner}/${id(ownerId)}/attachments`, form, onProgress);
    },
    attachmentUrl: (attachmentId: string) => `${base}/api/finance/attachments/${id(attachmentId)}`,
    deleteAttachment: (attachmentId: string) => del(`/finance/attachments/${id(attachmentId)}`),
    years: () => get<YearInfo[]>('/finance/years'),
    addYear: (date: string) => post<YearInfo[]>('/finance/years', { date }),
    lockPeriod: (periodId: string) => post<YearInfo[]>(`/finance/periods/${id(periodId)}/lock`),
    unlockPeriod: (periodId: string) => post<YearInfo[]>(`/finance/periods/${id(periodId)}/unlock`),
    closeYear: (yearId: string) => post<YearInfo[]>(`/finance/years/${id(yearId)}/close`),
    reopenYear: (yearId: string, reason: string) => post<YearInfo[]>(`/finance/years/${id(yearId)}/reopen`, { reason }),
    openingBalances: (request: OpeningRequest) => post<JournalDetail>('/finance/opening-balances', request),
    budget: (yearId: string, storeId: string | null) => get<BudgetInfo>('/finance/budgets', { yearId, storeId }),
    saveBudget: (yearId: string, storeId: string | null, amounts: BudgetAmount[]) =>
      json<BudgetInfo>('/finance/budgets', { method: 'PUT', body: { amounts }, query: { yearId, storeId } }),
    copyBudget: (yearId: string, storeId: string | null, fromBudget: boolean, percent: number) =>
      post<{ copied: number }>('/finance/budgets/copy', { yearId, storeId, fromBudget, percent }),
    financeReports: () => get<FinanceReportInfo[]>('/finance/reports'),
    financeReport: (key: string, query: Query) => get<ReportTable>(`/finance/reports/${id(key)}`, query),
    financeReportCsv: (key: string, query: Query) => blob(`/finance/reports/${id(key)}/csv`, { query }),
    ledgerExport: (format: 'tally' | 'iif' | 'csv', query: Query) => blob('/finance/export', { query: { ...query, format } }),
    fiscalQueue: (query: Query) => get<FiscalQueue>('/finance/fiscal', query),
    retryFiscal: (rowId: string) => post<void>(`/finance/fiscal/${id(rowId)}/retry`),
    bills: (query: Query, signal?: AbortSignal) => get<Paged<BillRow>>('/finance/bills', query, signal),
    billsDue: (query: Query) => get<BillRow[]>('/finance/bills/due', query),
    bill: (billId: string) => get<BillDetail>(`/finance/bills/${id(billId)}`),
    addBill: (bill: BillRequest) => post<BillDetail>('/finance/bills', bill),
    saveBill: (billId: string, bill: BillRequest) => put<BillDetail>(`/finance/bills/${id(billId)}`, bill),
    deleteBill: (billId: string) => del(`/finance/bills/${id(billId)}`),
    postBill: (billId: string) => post<BillDetail>(`/finance/bills/${id(billId)}/post`),
    voidBill: (billId: string, date: string, reason: string) => post<BillDetail>(`/finance/bills/${id(billId)}/void`, { date, reason }),
    payments: (query: Query, signal?: AbortSignal) => get<Paged<PaymentRow>>('/finance/payments', query, signal),
    payment: (paymentId: string) => get<PaymentDetail>(`/finance/payments/${id(paymentId)}`),
    pay: (request: PaymentRequest) => post<PaymentDetail>('/finance/payments', request),
    voidPayment: (paymentId: string, date: string, reason: string) => post<PaymentDetail>(`/finance/payments/${id(paymentId)}/void`, { date, reason }),
    paymentRuns: (query: Query) => get<PaymentRunInfo[]>('/finance/payment-runs', query),
    payRun: (request: PaymentRequest) => post<PaymentRunResult>('/finance/payment-runs', request),
    withholding: (supplierId: string, storeId: string | null) => get<WithholdingInfo>('/finance/withholding', { supplierId, storeId }),
    supplierFinance: (supplierId: string) => get<SupplierFinance>(`/finance/suppliers/${id(supplierId)}`),
    saveSupplierFinance: (supplierId: string, supplier: SupplierFinance) => put<SupplierFinance>(`/finance/suppliers/${id(supplierId)}`, supplier),
    supplierStatement: (supplierId: string, query: Query) => get<SupplierStatement>(`/finance/suppliers/${id(supplierId)}/statement`, query),
    expenses: (query: Query, signal?: AbortSignal) => get<Paged<ExpenseRow>>('/finance/expenses', query, signal),
    expense: (expenseId: string) => get<ExpenseDetail>(`/finance/expenses/${id(expenseId)}`),
    addExpense: (expense: ExpenseRequest) => post<ExpenseDetail>('/finance/expenses', expense),
    saveExpense: (expenseId: string, expense: ExpenseRequest) => put<ExpenseDetail>(`/finance/expenses/${id(expenseId)}`, expense),
    deleteExpense: (expenseId: string) => del(`/finance/expenses/${id(expenseId)}`),
    postExpense: (expenseId: string) => post<ExpenseDetail>(`/finance/expenses/${id(expenseId)}/post`),
    voidExpense: (expenseId: string, date: string, reason: string) => post<ExpenseDetail>(`/finance/expenses/${id(expenseId)}/void`, { date, reason }),
    recurringExpenses: (query: Query) => get<RecurringExpense[]>('/finance/recurring-expenses', query),
    addRecurringExpense: (row: RecurringExpense) => post<RecurringExpense>('/finance/recurring-expenses', row),
    saveRecurringExpense: (rowId: string, row: RecurringExpense) => put<RecurringExpense>(`/finance/recurring-expenses/${id(rowId)}`, row),
    deleteRecurringExpense: (rowId: string) => del(`/finance/recurring-expenses/${id(rowId)}`),
    runRecurringExpenses: (query: Query) => json<{ made: number }>('/finance/recurring-expenses/run', { method: 'POST', body: {}, query }),
    bankAccounts: (query: Query) => get<BankAccountRow[]>('/finance/bank-accounts', query),
    addBankAccount: (account: BankAccountRequest) => post<BankAccountRow>('/finance/bank-accounts', account),
    saveBankAccount: (accountId: string, account: BankAccountRequest) => put<BankAccountRow>(`/finance/bank-accounts/${id(accountId)}`, account),
    bankTransfers: (query: Query) => get<BankTransferRow[]>('/finance/bank-transfers', query),
    bankTransfer: (request: BankTransferRequest) => post<BankTransferRow>('/finance/bank-transfers', request),
    deposits: (query: Query) => get<DepositRow[]>('/finance/deposits', query),
    depositCandidates: (query: Query) => get<DepositCandidate[]>('/finance/deposits/candidates', query),
    deposit: (request: DepositRequest) => post<DepositRow>('/finance/deposits', request),
    statementFormat: (accountId: string) => get<StatementFormat>(`/finance/bank-accounts/${id(accountId)}/statement-format`),
    /** A statement CSV with the layout to read it by (saved for the bank). */
    importStatement: (accountId: string, file: File, format: StatementFormat, onProgress?: (fraction: number) => void) => {
      const form = new FormData();
      form.append('file', file, file.name);
      for (const [key, value] of Object.entries(format)) if (value !== null && value !== undefined) form.append(key, String(value));
      return upload<StatementImportResult>(`/finance/bank-accounts/${id(accountId)}/statements`, form, onProgress);
    },
    reconciliation: (accountId: string, query: Query) => get<Reconciliation>(`/finance/bank-accounts/${id(accountId)}/reconciliation`, query),
    autoMatch: (accountId: string) => post<{ matched: number }>(`/finance/bank-accounts/${id(accountId)}/auto-match`),
    matchLine: (lineId: string, journalLineId: string) => post<void>(`/finance/statement-lines/${id(lineId)}/match`, { journalLineId }),
    unmatchLine: (lineId: string) => post<void>(`/finance/statement-lines/${id(lineId)}/unmatch`),
    ignoreLine: (lineId: string) => post<void>(`/finance/statement-lines/${id(lineId)}/ignore`),
    lineEntry: (lineId: string, accountId: string | null, memo: string | null) =>
      post<JournalDetail>(`/finance/statement-lines/${id(lineId)}/entry`, { accountId, memo }),
  };
}

export type Api = ReturnType<typeof createApi>;

/** The app's client. */
export const api: Api = createApi();
