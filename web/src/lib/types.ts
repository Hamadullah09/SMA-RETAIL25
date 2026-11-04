/**
 * The server's JSON shapes, as docs/API.md lists them. Names are the server's (camelCase); enums are strings.
 *
 * Conventions from the contract: business dates are `yyyy-MM-dd`; fields ending in `Utc` are UTC ISO-8601 with `Z`;
 * other date-times are the store's local time without an offset; money is a number with 2 decimals.
 */

/** `yyyy-MM-dd`, a business date. */
export type BusinessDate = string;
/** ISO-8601 in UTC, ending in `Z`. */
export type UtcDateTime = string;
/** ISO-8601 without an offset, in the store's local time. */
export type LocalDateTime = string;

export interface ErrorBody {
  error: string;
}

/** A *paged* list. */
export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

// Session -------------------------------------------------------------------------------------------------------------

export interface UserInfo {
  id: string;
  userName: string;
  fullName: string | null;
  role: string | null;
  permissions: string[];
  storeId: string | null;
  storeName: string | null;
  mustChangePassword: boolean;
  /**
   * May change the company-wide records (clients, suppliers, departments, categories, roles, commission rules): true
   * with `stores.all`, or while the company has a single store.
   */
  canChangeShared?: boolean;
}

export interface SignInRequest {
  userName: string;
  password: string;
}

export interface SignInResponse {
  token: string;
  expiresAtUtc: UtcDateTime;
  user: UserInfo;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface Health {
  status: string;
  database: string;
  version: string;
}

// Stores, tills and packages ------------------------------------------------------------------------------------------

export interface StoreInfo {
  id: string;
  code: string;
  name: string;
  timeZoneId: string | null;
  /** From the store's settings, readable by everyone signed in. */
  currencySymbol?: string | null;
}

/** A stock location of a store (`GET /api/stores/{id}/locations`). */
export interface LocationInfo {
  id: string;
  code: string;
  name: string;
}

/** Someone to filter by (`GET /api/staff`): active and inactive users, sorted by user name. */
export interface StaffMember {
  id: string;
  userName: string;
  fullName: string | null;
}

export interface StoreRequest {
  code: string;
  name: string;
  timeZoneId: string | null;
}

export type SyncDirection = 'Upload' | 'Download';
export type SyncBatchStatus = 'Applied' | 'Duplicate' | 'Failed' | 'Waiting' | 'Sent';

export interface BatchInfo {
  id: string;
  batchId: string;
  storeId: string | null;
  stationNumber: number;
  direction: SyncDirection;
  status: SyncBatchStatus;
  fileName: string | null;
  rows: number;
  receivedAtUtc: UtcDateTime;
  appliedAtUtc: UtcDateTime | null;
  appVersion: string | null;
  error: string | null;
  notes: string | null;
}

/** What `POST /api/sync/upload-file` answers: what the server did with the package. */
export interface UploadResponse {
  status: SyncBatchStatus | (string & {});
  batchId: string;
  inserted: number;
  updated: number;
  deleted: number;
  notes: string[];
  error: string | null;
}

/** A store's RFID hub key (`GET /api/stores/{id}/rfid-hubs`); the key itself is shown only when it is made. */
export interface RfidHubInfo {
  id: string;
  storeId: string;
  /** Where the hub runs, e.g. "Back office PC". */
  name: string;
  /** The key's first characters, to tell keys apart. */
  keyHint: string;
  createdAtUtc: UtcDateTime;
  createdBy: string | null;
  lastSeenAtUtc: UtcDateTime | null;
  lastAddress: string | null;
  revoked: boolean;
  revokedAtUtc: UtcDateTime | null;
}

/** `POST /api/stores/{id}/rfid-hubs`: the new key, in this answer only. */
export interface NewRfidHubResponse {
  hub: RfidHubInfo;
  key: string;
}

export interface StationInfo {
  id: string;
  storeId: string;
  number: number;
  name: string | null;
  enrolled: boolean;
  enrolledAtUtc: UtcDateTime | null;
  retired: boolean;
  enrollmentExpiresAtUtc: UtcDateTime | null;
  lastUploadAtUtc: UtcDateTime | null;
  lastDownloadAtUtc: UtcDateTime | null;
  appVersion: string | null;
  lastBatch: BatchInfo | null;
  failedBatches: number;
  behind: boolean;
}

export interface NewStationRequest {
  number?: number | null;
  name?: string | null;
}

export interface EnrollmentCodeInfo {
  station: StationInfo;
  code: string;
  expiresAtUtc: UtcDateTime;
}

export type TillStatus = 'ok' | 'behind' | 'failed' | 'never' | 'retired';

export interface TillInfo {
  stationId: string;
  storeId: string;
  storeCode: string;
  storeName: string;
  number: number;
  name: string | null;
  enrolled: boolean;
  retired: boolean;
  appVersion: string | null;
  lastUploadAtUtc: UtcDateTime | null;
  lastDownloadAtUtc: UtcDateTime | null;
  lastSaleAt: LocalDateTime | null;
  todayNetSales: number;
  todayTransactions: number;
  drawerOpen: boolean;
  failedBatches: number;
  status: TillStatus;
}

// Dashboard ----------------------------------------------------------------------------------------------------------

export interface DashboardTotals {
  netSales: number;
  tax: number;
  total: number;
  transactions: number;
  averageSale: number;
  units: number;
  returns: number;
  discounts: number;
  grossProfit: number;
  voids: number;
}

export interface Dashboard {
  from: BusinessDate;
  to: BusinessDate;
  totals: DashboardTotals;
  previous: DashboardTotals;
  byStore: {
    storeId: string;
    code: string;
    name: string;
    netSales: number;
    transactions: number;
    averageSale: number;
    lastUploadAtUtc: UtcDateTime | null;
  }[];
  byDay: { date: BusinessDate; netSales: number; transactions: number }[];
  byHour: { hour: number; netSales: number; transactions: number }[];
  paymentMix: { tender: string; amount: number; count: number }[];
  topItems: { itemId: string | null; plu: string | null; name: string; units: number; netSales: number }[];
  byCashier: { userId: string | null; userName: string; netSales: number; transactions: number }[];
  byStation: { storeId: string; storeCode: string; stationNumber: number; netSales: number; transactions: number }[];
  drawers: {
    storeCode: string;
    stationNumber: number;
    closedAt: LocalDateTime | null;
    closedBy: string | null;
    expected: number;
    /** Null: tills do not record the counted cash. */
    counted: number | null;
    /** Null when nothing was counted. */
    variance: number | null;
  }[];
}

// Sales --------------------------------------------------------------------------------------------------------------

export type SaleStatusFilter = 'completed' | 'voided';

export interface SaleRow {
  id: string;
  storeCode: string;
  stationNumber: number;
  invoiceNumber: string | number;
  at: LocalDateTime;
  staff: string | null;
  client: string | null;
  units: number;
  netSales: number;
  tax: number;
  total: number;
  tenders: string;
  voided: boolean;
}

export interface SaleLine {
  lineNumber: number;
  plu: string | null;
  description: string | null;
  quantity: number;
  price: number;
  discount: number;
  tax: number;
  total: number;
}

export interface SalePayment {
  tender: string;
  amount: number;
  currency: string | null;
  foreignAmount: number | null;
}

export interface SaleDetail extends SaleRow {
  lines: SaleLine[];
  payments: SalePayment[];
  voidedBy: string | null;
  voidedAt: LocalDateTime | null;
  /** The tax office's number (FBR's POS integration), once the server reported the sale. */
  fiscalInvoiceNumber?: string | null;
}

// Items --------------------------------------------------------------------------------------------------------------

/**
 * One location's item: the model keeps a row (with its own price and stock) per location for a stock code, so a code
 * stocked in two stores is two rows.
 */
export interface ItemRow {
  id: string;
  plu: string;
  name: string;
  storeCode?: string;
  locationCode?: string;
  department: string | null;
  category: string | null;
  price1: number;
  averageCost: number;
  onHand: number;
  isActive: boolean;
}

export interface ItemStock {
  storeCode: string;
  locationCode: string;
  onHand: number;
}

export interface ItemDetail {
  id: string | null;
  version: string | null;
  plu: string;
  name: string;
  /** The item's long description. */
  description: string | null;
  barcode?: string | null;
  /** On a new item: the location it is for (otherwise the first location of the store sent as `storeId`). */
  locationId?: string | null;
  departmentId: string | null;
  categoryId: string | null;
  supplierId: string | null;
  price1: number;
  price2: number;
  price3: number;
  lastCost: number;
  averageCost: number;
  taxable1: boolean;
  taxable2: boolean;
  /** Always false: stores have two taxes. */
  taxable3: boolean;
  reorderPoint: number;
  reorderQuantity: number;
  unitsPerCase: number;
  /** False deletes the item; true brings a deleted one back. */
  isActive: boolean;
  /** The message shown at the till when the item is rung up. */
  notes: string | null;
  /** The same stock code at every location of the stores the user sees. */
  stock?: ItemStock[];
}

/** One location's row of a stock code. */
export interface ItemPlace {
  itemId: string;
  storeCode: string;
  locationCode: string;
}

/** A location a change at every store does not reach, and why. */
export interface ItemSkip {
  storeCode: string;
  locationCode: string;
  reason: string;
}

/** `GET /api/items/{id}/every-store`: the code's rows the user may change, and the locations without it. */
export interface ItemEveryStorePlan {
  rows: ItemPlace[];
  skipped: ItemSkip[];
}

/** `PUT /api/items/{id}?everyStore=true`: the rows changed (this one first), the fields copied, what was left alone. */
export interface ItemEveryStoreResult {
  item: ItemDetail;
  changed: number;
  fields: string[];
  changedAt: ItemPlace[];
  skipped: ItemSkip[];
}

export interface Department {
  id: string;
  name: string;
}

/** Categories are not kept under a department: `departmentId` is null. */
export interface Category {
  id: string;
  name: string;
  departmentId: string | null;
}

// Clients and suppliers ----------------------------------------------------------------------------------------------

export interface ClientRow {
  id: string;
  number: string | number;
  name: string;
  company: string | null;
  phone: string | null;
  email: string | null;
  balance: number;
  points: number;
  isDeleted: boolean;
}

export interface ClientDetail {
  id: string | null;
  version: string | null;
  number: string | number | null;
  firstName: string | null;
  lastName: string | null;
  company: string | null;
  address1: string | null;
  address2: string | null;
  city: string | null;
  province: string | null;
  postalCode: string | null;
  country: string | null;
  phone: string | null;
  phone2: string | null;
  email: string | null;
  creditLimit: number;
  /** Read-only. */
  balance: number;
  /** Read-only. */
  points: number;
  notes: string | null;
}

export interface SupplierRow {
  id: string;
  code: string;
  company: string;
  contact: string | null;
  phone: string | null;
  email: string | null;
  isDeleted: boolean;
}

export interface SupplierDetail {
  id: string | null;
  version: string | null;
  code: string;
  company: string;
  contact: string | null;
  address1: string | null;
  address2: string | null;
  city: string | null;
  province: string | null;
  postalCode: string | null;
  country: string | null;
  phone: string | null;
  fax: string | null;
  email: string | null;
  notes: string | null;
}

// Stock --------------------------------------------------------------------------------------------------------------

export interface StockMovementRow {
  at: LocalDateTime;
  storeCode: string;
  locationCode: string;
  plu: string;
  name: string;
  kind: string;
  quantity: number;
  onHandAfter: number;
  unitCost: number | null;
  documentNumber: string | number | null;
  reason: string | null;
  user: string | null;
}

export interface TransferRow {
  id: string;
  number: string | number;
  status: string;
  fromStore: string;
  fromLocation: string;
  toStore: string;
  toLocation: string;
  createdAt: LocalDateTime;
  shippedAt: LocalDateTime | null;
  receivedAt: LocalDateTime | null;
  lines: number;
  units: number;
  value: number;
}

export interface TransferLine {
  plu: string;
  description: string | null;
  quantityShipped: number;
  quantityReceived: number;
  unitCost: number;
}

export interface TransferDetail extends Omit<TransferRow, 'lines'> {
  lines: TransferLine[];
}

export interface StockCountRow {
  id: string;
  number: string | number;
  status: string;
  storeCode: string;
  locationCode: string;
  startedAt: LocalDateTime;
  postedAt: LocalDateTime | null;
  lines: number;
  varianceUnits: number;
  varianceValue: number;
}

export interface StockCountLine {
  plu: string;
  description: string | null;
  expected: number;
  counted: number;
  variance: number;
  varianceCost: number;
  notCounted: boolean;
}

export interface StockCountDetail extends Omit<StockCountRow, 'lines'> {
  lines: StockCountLine[];
}

// Gift cards ---------------------------------------------------------------------------------------------------------

export interface GiftCardRow {
  id: string;
  number: string;
  kind: string;
  storeCode: string | null;
  issuedAt: LocalDateTime;
  expiresOn: BusinessDate | null;
  isActive: boolean;
  client: string | null;
  balance: number;
}

export interface GiftCardHistoryLine {
  at: LocalDateTime;
  kind: string;
  amount: number;
  balanceAfter: number;
  storeCode: string | null;
  stationNumber: number | null;
  invoiceNumber: string | number | null;
  user: string | null;
  note: string | null;
}

export interface GiftCardDetail extends GiftCardRow {
  history: GiftCardHistoryLine[];
}

// Reports ------------------------------------------------------------------------------------------------------------

export interface ReportInfo {
  key: string;
  group: string;
  title: string;
  usesDates: boolean;
}

/** The server's `ColumnKind` names. */
export type ColumnKind = 'Text' | 'Count' | 'Quantity' | 'Money' | 'Percent' | 'Date' | (string & {});

export interface ReportColumn {
  header: string;
  kind: ColumnKind;
  total: boolean;
}

export type ReportCell = string | number | boolean | null;

export interface ReportTable {
  title: string;
  subtitle: string | null;
  columns: ReportColumn[];
  rows: ReportCell[][];
  totals?: ReportCell[] | null;
}

// Users and roles ----------------------------------------------------------------------------------------------------

export interface PermissionInfo {
  key: string;
  group: string;
  description: string;
}

export interface RoleInfo {
  id: string | null;
  version: string | null;
  name: string;
  description: string | null;
  level: number;
  isSystem: boolean;
  permissions: string[];
  userCount?: number;
}

export interface UserRow {
  id: string;
  version: string;
  userName: string;
  fullName: string | null;
  roleId: string | null;
  role: string | null;
  storeId: string | null;
  storeCode: string | null;
  staffCode: string | null;
  isActive: boolean;
  locked: boolean;
  mustChangePassword: boolean;
  lastSignInAtUtc: UtcDateTime | null;
}

export interface UserRequest {
  userName: string;
  fullName: string | null;
  roleId: string | null;
  storeId: string | null;
  isActive: boolean;
  password?: string;
  version?: string;
}

export interface TemporaryPassword {
  temporaryPassword: string;
}

export interface PinRequest {
  staffCode: string;
  pin: string;
}

// Audit log ----------------------------------------------------------------------------------------------------------

export interface AuditRow {
  id: string;
  atUtc: UtcDateTime;
  storeCode: string | null;
  stationNumber: number | null;
  userName: string | null;
  action: string;
  entityName: string | null;
  entityId: string | null;
  details: string | null;
  before: Record<string, unknown> | null;
  after: Record<string, unknown> | null;
}

// Time clock and commission ------------------------------------------------------------------------------------------

export interface TimeClockEntry {
  id: string;
  userId: string;
  userName: string;
  storeCode: string | null;
  stationNumber: number | null;
  inAt: LocalDateTime;
  outAt: LocalDateTime | null;
  hours: number | null;
  editedBy: string | null;
  editReason: string | null;
}

export interface HoursRow {
  userId: string;
  userName: string;
  days: number;
  hours: number;
}

export interface CommissionRow {
  staff: string;
  sales: number;
  units: number;
  netSales: number;
  fromRules: number;
  fromItems: number;
  commission: number;
}

export interface CommissionRule {
  id: string | null;
  version: string | null;
  name: string;
  userId: string | null;
  userName: string | null;
  scope: string;
  departmentId: string | null;
  categoryId: string | null;
  itemId: string | null;
  /** What the rule applies to, in words ("Department Footwear"); the server works it out from scope and ids. */
  target: string | null;
  /** The rate: a percent of net sales, or money per unit with `basis` AmountPerUnit. */
  percent: number;
  /** How `percent` is read (the server's extra field; PercentOfNetSales when missing). */
  basis?: string;
  startsOn: BusinessDate | null;
  endsOn: BusinessDate | null;
  isActive: boolean;
}

// Store settings -----------------------------------------------------------------------------------------------------

export interface StoreSettings {
  version: string | null;
  companyName: string | null;
  address1: string | null;
  address2: string | null;
  city: string | null;
  province: string | null;
  postalCode: string | null;
  country?: string | null;
  phone: string | null;
  fax?: string | null;
  email: string | null;
  website: string | null;
  taxNumber: string | null;
  currencySymbol: string | null;
  receiptHeader: string | null;
  receiptFooter: string | null;
}

export type ImageKind = 'logo' | 'watermark';

// Finance (phase E1, /api/finance) -----------------------------------------------------------------------------------

export type AccountType = 'Asset' | 'Liability' | 'Equity' | 'Income' | 'Expense';
export type CashFlowSection = 'Operating' | 'Investing' | 'Financing';
export type JournalKind =
  | 'Manual'
  | 'Daily'
  | 'SupplierBill'
  | 'SupplierPayment'
  | 'Expense'
  | 'Bank'
  | 'Payroll'
  | 'Production'
  | 'Inventory'
  | 'Opening'
  | 'Closing'
  | 'Adjustment'
  | 'Reversal'
  | (string & {});
export type JournalStatus = 'Draft' | 'Posted' | 'Reversed';
export type FiscalStatus = 'Open' | 'Locked' | 'Closed';
export type PartyKind = 'Client' | 'Supplier' | 'Employee';
export type WithholdingKind = 'Goods' | 'Services' | 'Rent' | 'Other';
export type BillKind = 'Bill' | 'DebitNote';
export type BillStatus = 'Draft' | 'Posted' | 'PartPaid' | 'Paid' | 'Void';
export type PaymentStatus = 'Posted' | 'Void';
export type ExpensePaidBy = 'Cash' | 'Bank' | 'OnAccount';
export type ExpenseStatus = 'Draft' | 'Posted' | 'Void';
export type RecurrenceFrequency = 'Weekly' | 'Monthly' | 'Quarterly' | 'Yearly';
export type BankAccountKind = 'Bank' | 'Cash' | 'Wallet';
export type StatementLineStatus = 'Unmatched' | 'Matched' | 'Entered' | 'Ignored';
export type FiscalInvoiceStatus = 'Pending' | 'Sent' | 'Failed' | 'Skipped';
export type FinanceComparison = 'none' | 'previousPeriod' | 'previousYear' | 'budget';

export interface FinanceHome {
  today: BusinessDate;
  accounts: BankAccountRow[];
  cashInHand: number;
  payablesOpen: number;
  payablesDue: number;
  billsDue: number;
  payablesOverdue: number;
  receivables: number;
  monthFrom: BusinessDate;
  monthIncome: number;
  monthExpenses: number;
  monthProfit: number;
  suspense: number;
  draftJournals: number;
  fiscalEnabled: boolean;
  fiscalPending: number;
  fiscalFailed: number;
}

export interface FinanceAccount {
  id: string;
  version: string;
  code: string;
  name: string;
  type: AccountType;
  parentId: string | null;
  isPosting: boolean;
  isActive: boolean;
  role: string | null;
  description: string | null;
  cashFlow: CashFlowSection | null;
  /** What the account holds today (debit positive), for the store chosen or the company. */
  balance: number;
}

export interface FinanceAccountRequest {
  code: string;
  name: string;
  type: AccountType;
  parentId: string | null;
  isPosting: boolean;
  isActive: boolean;
  role: string | null;
  description: string | null;
  cashFlow: CashFlowSection | null;
  version: string | null;
}

export interface FinanceSettings {
  version: string;
  baseCurrency: string;
  fiscalYearStartMonth: number;
  country: string;
  manualEntriesNeedApproval: boolean;
  autoPostOnSync: boolean;
  nightlyDays: number;
  nightlyHour: number;
  attachmentMaxKilobytes: number;
  /** Read-only: the FBR integration is switched on in the server's settings. */
  fiscalEnabled: boolean;
}

export interface SalesTaxRateInfo {
  name: string;
  rate: number;
  isDefault: boolean;
}

export interface WithholdingRuleInfo {
  kind: WithholdingKind;
  section: string | null;
  filerRate: number;
  nonFilerRate: number;
  threshold: number;
}

export interface TaxRuleSet {
  id: string | null;
  version: string | null;
  country: string;
  name: string;
  inputTaxClaimable: boolean;
  notes: string | null;
  isActive: boolean;
  salesTaxRates: SalesTaxRateInfo[];
  withholdingRules: WithholdingRuleInfo[];
}

export interface StoreFinance {
  storeId: string;
  taxRuleSetId: string | null;
  /** The rule set in use (the store's own, else the ledger country's). */
  ruleSet: string | null;
  depositBankAccountId: string | null;
}

export interface MappingRow {
  kind: string;
  key: string;
  accountCode: string | null;
  accountName: string | null;
}

export interface PostDaysResult {
  entries: number;
  problems: string[];
}

export interface SuspenseRow {
  entryId: string;
  number: number;
  date: BusinessDate;
  storeId: string | null;
  storeCode: string;
  memo: string;
  debit: number;
  credit: number;
}

export interface JournalRow {
  id: string;
  number: number;
  date: BusinessDate;
  kind: JournalKind;
  status: JournalStatus;
  storeCode: string | null;
  memo: string;
  reference: string | null;
  total: number;
  createdBy: string | null;
  postedBy: string | null;
  postedAtUtc: UtcDateTime | null;
}

export interface JournalLineInfo {
  lineNumber: number;
  accountId: string;
  code: string;
  name: string;
  storeId: string | null;
  debit: number;
  credit: number;
  memo: string | null;
  partyKind: PartyKind | null;
  partyId: string | null;
}

export interface AttachmentInfo {
  id: string;
  fileName: string;
  contentType: string;
  size: number;
  uploadedBy: string | null;
  createdAtUtc: UtcDateTime;
}

export interface JournalDetail {
  id: string;
  version: string;
  number: number;
  date: BusinessDate;
  kind: JournalKind;
  status: JournalStatus;
  storeId: string | null;
  storeCode: string | null;
  memo: string;
  reference: string | null;
  source: string | null;
  createdBy: string | null;
  postedBy: string | null;
  postedAtUtc: UtcDateTime | null;
  reversalOfId: string | null;
  reversedById: string | null;
  lines: JournalLineInfo[];
  attachments: AttachmentInfo[];
  /** A draft another person must approve. */
  needsApproval: boolean;
}

export interface JournalLineRequest {
  accountId: string;
  debit: number;
  credit: number;
  storeId?: string | null;
  memo?: string | null;
  partyKind?: PartyKind | null;
  partyId?: string | null;
}

export interface JournalRequest {
  date: BusinessDate;
  storeId: string | null;
  memo: string;
  reference: string | null;
  lines: JournalLineRequest[];
  /** Post at once when the settings let entries go without approval. */
  post?: boolean;
  version?: string | null;
}

export interface PeriodInfo {
  id: string;
  number: number;
  name: string;
  startDate: BusinessDate;
  endDate: BusinessDate;
  status: FiscalStatus;
}

export interface YearInfo {
  id: string;
  name: string;
  startDate: BusinessDate;
  endDate: BusinessDate;
  status: FiscalStatus;
  closedAtUtc: UtcDateTime | null;
  closedBy: string | null;
  periods: PeriodInfo[];
}

export interface OpeningRequest {
  date: BusinessDate;
  storeId: string | null;
  balanceToRetainedEarnings: boolean;
  lines: JournalLineRequest[];
}

export interface BudgetAmount {
  accountId: string;
  /** The first day of the month. */
  month: BusinessDate;
  amount: number;
}

export interface BudgetInfo {
  yearId: string;
  yearName: string;
  storeId: string | null;
  months: BusinessDate[];
  amounts: BudgetAmount[];
}

export interface FinanceReportInfo {
  key: string;
  title: string;
  usesDates: boolean;
  usesComparison: boolean;
  usesAccount: boolean;
  /** Run as of the last day (balance sheet, ageing). */
  asOfDate: boolean;
}

export interface BillRow {
  id: string;
  number: number;
  kind: BillKind;
  status: BillStatus;
  supplierId: string;
  supplier: string;
  storeId: string;
  storeCode: string;
  billDate: BusinessDate;
  dueDate: BusinessDate;
  supplierReference: string | null;
  currency: string;
  total: number;
  paid: number;
  open: number;
  daysOverdue: number;
  /** Before sales tax: withholding is worked out on it. */
  subtotal: number;
}

export interface BillLineInfo {
  lineNumber: number;
  accountId: string | null;
  accountCode: string | null;
  itemId: string | null;
  description: string;
  quantity: number;
  unitPrice: number;
  amount: number;
  taxRate: number;
  taxAmount: number;
  purchaseOrderId: string | null;
  purchaseOrderLineId: string | null;
}

export interface BillPaymentInfo {
  paymentId: string;
  number: number;
  date: BusinessDate;
  amount: number;
  status: PaymentStatus;
}

export interface BillDetail {
  id: string;
  version: string;
  number: number;
  kind: BillKind;
  status: BillStatus;
  supplierId: string;
  supplier: string;
  storeId: string;
  storeCode: string;
  billDate: BusinessDate;
  dueDate: BusinessDate;
  supplierReference: string | null;
  currency: string;
  exchangeRate: number;
  notes: string | null;
  subtotal: number;
  taxTotal: number;
  total: number;
  paid: number;
  open: number;
  originalBillId: string | null;
  expenseId: string | null;
  journalEntryId: string | null;
  createdBy: string | null;
  postedBy: string | null;
  postedAtUtc: UtcDateTime | null;
  voidReason: string | null;
  lines: BillLineInfo[];
  payments: BillPaymentInfo[];
  attachments: AttachmentInfo[];
}

export interface BillLineRequest {
  accountId: string | null;
  itemId: string | null;
  description: string;
  quantity: number;
  unitPrice: number;
  taxRate: number;
  taxAmount?: number;
  purchaseOrderId?: string | null;
  purchaseOrderLineId?: string | null;
}

export interface BillRequest {
  kind: BillKind;
  supplierId: string;
  storeId: string;
  billDate: BusinessDate;
  dueDate: BusinessDate | null;
  supplierReference: string | null;
  currency: string | null;
  exchangeRate: number;
  notes: string | null;
  originalBillId: string | null;
  lines: BillLineRequest[];
  version?: string | null;
}

export interface PaymentRow {
  id: string;
  number: number;
  date: BusinessDate;
  supplierId: string;
  supplier: string;
  storeCode: string;
  bankAccountId: string;
  bankAccount: string;
  currency: string;
  amount: number;
  withholdingAmount: number;
  reference: string | null;
  status: PaymentStatus;
  paymentRunId: string | null;
}

export interface PaymentAllocationInfo {
  billId: string;
  billNumber: number;
  kind: BillKind;
  amount: number;
}

export interface PaymentDetail extends Omit<PaymentRow, 'storeCode'> {
  storeId: string;
  storeCode: string;
  exchangeRate: number;
  withholdingKind: WithholdingKind;
  withholdingRate: number;
  notes: string | null;
  journalEntryId: string | null;
  allocations: PaymentAllocationInfo[];
}

export interface BillToPay {
  billId: string;
  amount: number;
}

export interface PaymentRequest {
  bankAccountId: string;
  date: BusinessDate;
  bills: BillToPay[];
  /** Left out: worked out from the store's rule set and the supplier's details. */
  withholdingAmount: number | null;
  exchangeRate: number;
  reference: string | null;
  notes: string | null;
}

export interface PaymentRunInfo {
  id: string;
  number: number;
  date: BusinessDate;
  bankAccountId: string;
  total: number;
  payments: number;
  notes: string | null;
  paidBy: string | null;
  paidAtUtc: UtcDateTime;
}

export interface PaymentRunResult {
  run: PaymentRunInfo;
  payments: PaymentRow[];
}

export interface WithholdingInfo {
  kind: WithholdingKind;
  rate: number;
  threshold: number;
  section: string | null;
  ruleSet: string | null;
}

export interface SupplierFinance {
  supplierId: string;
  taxNumber: string | null;
  salesTaxNumber: string | null;
  isFiler: boolean;
  withholdingKind: WithholdingKind;
  withholdingExempt: boolean;
  paymentTermsDays: number;
  defaultAccountId: string | null;
}

export interface SupplierStatementLine {
  date: BusinessDate;
  entryNumber: number;
  kind: JournalKind;
  memo: string;
  reference: string | null;
  billed: number;
  paid: number;
  balance: number;
}

export interface SupplierStatement {
  supplierId: string;
  supplier: string;
  from: BusinessDate;
  to: BusinessDate;
  opening: number;
  closing: number;
  lines: SupplierStatementLine[];
}

export interface ExpenseRow {
  id: string;
  version: string;
  number: number;
  date: BusinessDate;
  storeId: string;
  storeCode: string;
  payee: string;
  supplierId: string | null;
  accountId: string;
  accountCode: string;
  accountName: string;
  description: string | null;
  amount: number;
  taxAmount: number;
  total: number;
  paidBy: ExpensePaidBy;
  bankAccountId: string | null;
  reference: string | null;
  status: ExpenseStatus;
  supplierBillId: string | null;
  recurringExpenseId: string | null;
  journalEntryId: string | null;
  attachments: number;
}

export interface ExpenseDetail {
  expense: ExpenseRow;
  attachments: AttachmentInfo[];
}

export interface ExpenseRequest {
  date: BusinessDate;
  storeId: string;
  payee: string;
  supplierId: string | null;
  accountId: string;
  description: string | null;
  amount: number;
  taxAmount: number;
  paidBy: ExpensePaidBy;
  bankAccountId: string | null;
  reference: string | null;
  post?: boolean;
  version?: string | null;
}

export interface RecurringExpense {
  id: string | null;
  version: string | null;
  name: string;
  storeId: string;
  payee: string;
  supplierId: string | null;
  accountId: string;
  amount: number;
  taxAmount: number;
  paidBy: ExpensePaidBy;
  bankAccountId: string | null;
  frequency: RecurrenceFrequency;
  nextDate: BusinessDate;
  endDate: BusinessDate | null;
  postAutomatically: boolean;
  isActive: boolean;
  lastMadeOn: BusinessDate | null;
}

export interface BankAccountRow {
  id: string;
  version: string;
  name: string;
  kind: BankAccountKind;
  bankName: string | null;
  accountNumber: string | null;
  iban: string | null;
  currency: string;
  ledgerAccountId: string;
  ledgerCode: string;
  storeId: string | null;
  storeCode: string | null;
  openingBalance: number;
  openingDate: BusinessDate | null;
  isActive: boolean;
  balance: number;
}

export interface BankAccountRequest {
  name: string;
  kind: BankAccountKind;
  bankName: string | null;
  accountNumber: string | null;
  iban: string | null;
  currency: string | null;
  /** Left out: a ledger account is made for it. */
  ledgerAccountId: string | null;
  storeId: string | null;
  openingBalance: number;
  openingDate: BusinessDate | null;
  isActive: boolean;
  version?: string | null;
}

export interface BankTransferRequest {
  fromAccountId: string;
  toAccountId: string;
  date: BusinessDate;
  amount: number;
  toAmount: number | null;
  rate?: number;
  toRate?: number;
  reference: string | null;
  memo: string | null;
}

export interface BankTransferRow {
  id: string;
  number: number;
  date: BusinessDate;
  fromAccountId: string;
  fromAccount: string;
  toAccountId: string;
  toAccount: string;
  amount: number;
  toAmount: number;
  reference: string | null;
  memo: string | null;
  createdBy: string | null;
}

export interface DepositCandidate {
  drawerSessionId: string;
  storeId: string;
  storeCode: string;
  stationNumber: number;
  closedAt: LocalDateTime;
  cash: number;
  depositId: string | null;
}

export interface DepositRequest {
  storeId: string;
  bankAccountId: string;
  date: BusinessDate;
  amount: number;
  drawerSessionId: string | null;
  reference: string | null;
}

export interface DepositRow {
  id: string;
  number: number;
  date: BusinessDate;
  storeId: string;
  storeCode: string;
  bankAccountId: string;
  bankAccount: string;
  amount: number;
  drawerSessionId: string | null;
  reference: string | null;
  createdBy: string | null;
}

export interface StatementFormat {
  bankName: string;
  hasHeader: boolean;
  delimiter: string;
  dateColumn: string;
  dateFormat: string;
  descriptionColumn: string | null;
  referenceColumn: string | null;
  amountColumn: string | null;
  debitColumn: string | null;
  creditColumn: string | null;
  balanceColumn: string | null;
}

export interface StatementImportResult {
  statementId: string;
  lines: number;
  fromDate: BusinessDate;
  toDate: BusinessDate;
  closingBalance: number | null;
}

export interface StatementLineRow {
  id: string;
  date: BusinessDate;
  description: string | null;
  reference: string | null;
  /** Money in positive, money out negative. */
  amount: number;
  balance: number | null;
  status: StatementLineStatus;
  journalLineId: string | null;
  matchedBy: string | null;
}

export interface LedgerLineRow {
  journalLineId: string;
  entryId: string;
  number: number;
  date: BusinessDate;
  memo: string;
  reference: string | null;
  amount: number;
  matched: boolean;
}

export interface Reconciliation {
  bankAccountId: string;
  asOf: BusinessDate;
  statementBalance: number | null;
  ledgerBalance: number;
  uncleared: number;
  unmatchedStatement: number;
  unmatchedLines: number;
  reconciledBalance: number;
  difference: number | null;
  statementLines: StatementLineRow[];
  ledgerLines: LedgerLineRow[];
}

export interface FiscalRow {
  id: string;
  saleId: string;
  storeCode: string;
  stationNumber: number;
  invoiceNumber: number;
  soldAt: LocalDateTime;
  total: number;
  status: FiscalInvoiceStatus;
  attempts: number;
  lastError: string | null;
  fiscalNumber: string | null;
  sentAtUtc: UtcDateTime | null;
}

export interface FiscalQueue {
  enabled: boolean;
  rows: FiscalRow[];
}

/** The accountant's programs the ledger is exported for: Tally (XML), QuickBooks Desktop (IIF), QuickBooks Online (CSV), or plain CSV. */
export type LedgerExportFormat = 'tally' | 'iif' | 'qbo' | 'csv';
// HR and payroll (phase E3) ------------------------------------------------------------------------------------------

export type Gender = 'Male' | 'Female' | 'Other';
export type MaritalStatus = 'Unmarried' | 'Married' | 'Divorced' | 'Widowed';
export type EmploymentType = 'Permanent' | 'Contract' | 'Probation' | 'DailyWage';
export type SalaryPaymentMethod = 'Cash' | 'Bank';
export type AttendanceStatus = 'Present' | 'Absent' | 'Leave' | 'Holiday' | 'Off' | 'Scheduled';
export type HrApproval = 'Pending' | 'Approved' | 'Rejected';
export type LeaveKind = 'Annual' | 'Casual' | 'Sick' | 'Maternity' | 'Paternity' | 'Unpaid' | 'Other';
export type LeaveRequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';
export type SocialSecurityBasis = 'Wage' | 'MinimumWage';
export type DailyRateBasis = 'CalendarDays' | 'ThirtyDays' | 'WorkingDays';
export type LatenessRule = 'None' | 'DayPerLates' | 'PerMinute';
export type SalaryComponentKind = 'Allowance' | 'Deduction';
export type LoanKind = 'Loan' | 'Advance';
export type LoanStatus = 'Active' | 'Settled' | 'Cancelled';
export type PayrollRunKind = 'Monthly' | 'FinalSettlement';
export type PayrollRunStatus = 'Draft' | 'Calculated' | 'Approved' | 'Posted' | 'Paid';
export type PayslipLineKind = 'Earning' | 'Deduction' | 'EmployerContribution';

/** An HR department or a designation (`/api/hr/departments`, `/api/hr/designations`). */
export interface HrListItem {
  id: string | null;
  version: string | null;
  name: string;
  isActive: boolean;
  employees?: number;
}

export interface EmployeeRow {
  id: string;
  code: string;
  fullName: string;
  designation: string | null;
  department: string | null;
  storeId: string | null;
  storeCode: string | null;
  employmentType: EmploymentType;
  joiningDate: BusinessDate;
  leavingDate: BusinessDate | null;
  active: boolean;
  phone: string | null;
  userName: string | null;
}

export interface EmployeeDetail {
  id: string | null;
  version: string | null;
  code: string | null;
  fullName: string;
  fatherOrHusbandName: string | null;
  cnic: string | null;
  dateOfBirth: BusinessDate | null;
  gender: Gender | null;
  maritalStatus: MaritalStatus | null;
  address: string | null;
  city: string | null;
  phone: string | null;
  email: string | null;
  emergencyContactName: string | null;
  emergencyContactPhone: string | null;
  emergencyContactRelation: string | null;
  joiningDate: BusinessDate;
  confirmationDate: BusinessDate | null;
  leavingDate: BusinessDate | null;
  leavingReason: string | null;
  employmentType: EmploymentType;
  departmentId: string | null;
  designationId: string | null;
  storeId: string | null;
  managerId: string | null;
  userId: string | null;
  shiftId: string | null;
  tracksAttendance: boolean;
  overtimeEligible: boolean;
  eobiApplies: boolean;
  socialSecurityApplies: boolean;
  paymentMethod: SalaryPaymentMethod;
  bankName: string | null;
  iban: string | null;
  ntn: string | null;
  eobiNumber: string | null;
  socialSecurityNumber: string | null;
  priorTaxYear: number | null;
  priorTaxableIncome: number;
  priorTaxDeducted: number;
  notes: string | null;
  /** Read-only. */
  managerName?: string | null;
  userName?: string | null;
  country?: string | null;
}

export interface EmployeeDocumentRow {
  id: string;
  employeeId: string;
  title: string;
  fileName: string;
  contentType: string;
  size: number;
  uploadedAtUtc: UtcDateTime;
  uploadedBy: string | null;
}

export interface HrStoreCountry {
  storeId: string;
  code: string;
  name: string;
  country: string;
  /** The store names its own country (else it follows the company's). */
  own: boolean;
}

export interface HrSettingsInfo {
  companyCountry: string;
  countries: string[];
  stores: HrStoreCountry[];
}

export interface TaxSlabInfo {
  incomeFrom: number;
  incomeTo: number | null;
  fixedTax: number;
  rate: number;
}

export interface AllowanceRuleInfo {
  code: string;
  name: string;
  isTaxable: boolean;
  exemptPercentOfBasic: number | null;
  sortOrder: number;
}

export interface RuleSetInfo {
  id: string | null;
  version: string | null;
  country: string;
  name: string;
  effectiveFrom: BusinessDate;
  effectiveTo: BusinessDate | null;
  taxYearStartMonth: number;
  minimumWage: number;
  eobiEmployerRate: number;
  eobiEmployeeRate: number;
  socialSecurityProvince: string | null;
  socialSecurityRate: number;
  socialSecurityWageCeiling: number;
  socialSecurityBasis: SocialSecurityBasis;
  workingDaysPerWeek: number;
  hoursPerWeek: number;
  overtimeMultiplier: number;
  overtimeMinimumMinutes: number;
  dailyRateBasis: DailyRateBasis;
  latenessRule: LatenessRule;
  latesPerDay: number;
  deductAbsences: boolean;
  surchargeThreshold: number;
  surchargeRate: number;
  gratuityEnabled: boolean;
  gratuityDaysPerYear: number;
  gratuityMinimumMonths: number;
  gratuityPartYearMonths: number;
  gratuityOnBasic: boolean;
  cnicPattern: string | null;
  cnicExample: string | null;
  ibanPattern: string | null;
  ibanExample: string | null;
  notes: string | null;
  taxSlabs: TaxSlabInfo[];
  allowances: AllowanceRuleInfo[];
}

export interface LeaveTypeInfo {
  id: string | null;
  version: string | null;
  country: string;
  code: string;
  name: string;
  kind: LeaveKind;
  daysPerYear: number;
  isPaid: boolean;
  accruesMonthly: boolean;
  carryOverMaxDays: number;
  encashable: boolean;
  countsCalendarDays: boolean;
  onlyFor: Gender | null;
  minimumServiceMonths: number;
  isActive: boolean;
  sortOrder: number;
}

export interface HolidayInfo {
  id: string | null;
  version: string | null;
  country: string;
  date: BusinessDate;
  name: string;
}

export interface ShiftInfo {
  id: string | null;
  version: string | null;
  name: string;
  /** `HH:mm`. */
  startTime: string;
  endTime: string;
  breakMinutes: number;
  graceMinutes: number;
  /** Day names, Monday first ("Monday", "Tuesday"…). */
  days: string[];
  storeId: string | null;
  isActive: boolean;
}

export interface RosterChangeInfo {
  id: string | null;
  employeeId: string;
  date: BusinessDate;
  /** Null: a day off. */
  shiftId: string | null;
  note: string | null;
}

export interface RosterRowInfo {
  employeeId: string;
  code: string;
  name: string;
  storeId: string | null;
  usualShiftId: string | null;
  weekShiftId: string | null;
  changes: RosterChangeInfo[];
}

export interface RosterInfo {
  weekStart: BusinessDate;
  days: BusinessDate[];
  rows: RosterRowInfo[];
}

export interface RosterAssignment {
  employeeId: string;
  shiftId: string | null;
}

export interface RosterRequest {
  weekStart: BusinessDate;
  assignments: RosterAssignment[];
}

export interface AttendanceRow {
  employeeId: string;
  code: string;
  name: string;
  date: BusinessDate;
  status: AttendanceStatus;
  shift: string | null;
  shiftStart: LocalDateTime | null;
  shiftEnd: LocalDateTime | null;
  firstIn: LocalDateTime | null;
  lastOut: LocalDateTime | null;
  workedMinutes: number;
  lateMinutes: number;
  earlyMinutes: number;
  overtimeMinutes: number;
  holiday: string | null;
  leaveCode: string | null;
  leaveDays: number;
  corrected: boolean;
}

export interface AttendanceSheetRow {
  employeeId: string;
  code: string;
  name: string;
  /** One mark per day: P, L (late), E (left early), A, a leave code, H, O, · (still to come), empty when not employed. */
  marks: string[];
  present: number;
  late: number;
  leftEarly: number;
  absent: number;
  leave: number;
  holidays: number;
  off: number;
  overtimeHours: number;
}

export interface AttendanceSheet {
  /** `yyyy-MM`. */
  month: string;
  days: BusinessDate[];
  rows: AttendanceSheetRow[];
}

export interface CorrectionRow {
  id: string;
  employeeId: string;
  code: string;
  name: string;
  date: BusinessDate;
  status: AttendanceStatus;
  workedMinutes: number;
  lateMinutes: number;
  earlyMinutes: number;
  overtimeMinutes: number;
  reason: string;
  approval: HrApproval;
  requestedBy: string | null;
  requestedAtUtc: UtcDateTime;
  decidedBy: string | null;
  decidedAtUtc: UtcDateTime | null;
  decisionNote: string | null;
}

export interface CorrectionRequest {
  employeeId: string;
  date: BusinessDate;
  status: AttendanceStatus;
  workedMinutes: number;
  lateMinutes: number;
  earlyMinutes: number;
  overtimeMinutes: number;
  reason: string;
}

export interface DecisionRequest {
  note?: string | null;
}

export interface LeaveRequestRow {
  id: string;
  employeeId: string;
  code: string;
  name: string;
  leaveTypeId: string;
  leaveType: string;
  fromDate: BusinessDate;
  toDate: BusinessDate;
  firstDayHalf: boolean;
  lastDayHalf: boolean;
  days: number;
  reason: string | null;
  status: LeaveRequestStatus;
  requestedBy: string | null;
  requestedAtUtc: UtcDateTime;
  decidedBy: string | null;
  decidedAtUtc: UtcDateTime | null;
  decisionNote: string | null;
  /** The signed-in user may approve or reject it. */
  mayDecide: boolean;
}

export interface LeaveRequestBody {
  employeeId: string;
  leaveTypeId: string;
  fromDate: BusinessDate;
  toDate: BusinessDate;
  firstDayHalf: boolean;
  lastDayHalf: boolean;
  reason: string | null;
}

export interface LeaveBalanceRow {
  employeeId: string;
  code: string;
  name: string;
  leaveTypeId: string;
  leaveCode: string;
  leaveType: string;
  paid: boolean;
  /** False for leave without a limit (unpaid): `available` is null. */
  limited: boolean;
  entitled: number;
  earned: number;
  carriedIn: number;
  adjusted: number;
  taken: number;
  pending: number;
  available: number | null;
}

export interface LeaveAdjustmentBody {
  employeeId: string;
  leaveTypeId: string;
  year: number;
  days: number;
  reason: string;
}

export interface SalaryComponentInfo {
  kind: SalaryComponentKind;
  code: string;
  name: string;
  amount: number;
  accountCode: string | null;
}

export interface SalaryInfo {
  id: string | null;
  effectiveFrom: BusinessDate;
  basic: number;
  note: string | null;
  changedBy: string | null;
  components: SalaryComponentInfo[];
  /** Read-only: basic and allowances, and the fixed deductions. */
  gross?: number;
  fixedDeductions?: number;
}

export interface LoanInstalmentInfo {
  number: number;
  /** The first day of the month it is due. */
  month: BusinessDate;
  amount: number;
  recovered: number;
}

export interface LoanRow {
  id: string;
  number: number;
  employeeId: string;
  code: string;
  name: string;
  kind: LoanKind;
  issuedOn: BusinessDate;
  amount: number;
  instalment: number;
  firstMonth: BusinessDate;
  status: LoanStatus;
  recovered: number;
  outstanding: number;
  reason: string | null;
  paidFromAccount: string | null;
  instalments: LoanInstalmentInfo[];
}

export interface LoanBody {
  employeeId: string;
  kind: LoanKind;
  issuedOn: BusinessDate;
  amount: number;
  instalment: number;
  firstMonth: BusinessDate | null;
  reason: string | null;
  /** A role (Cash, Bank) or an account code. */
  paidFrom: string | null;
}

export interface RunRow {
  id: string;
  version: string;
  number: number;
  kind: PayrollRunKind;
  year: number;
  month: number;
  title: string;
  periodStart: BusinessDate;
  periodEnd: BusinessDate;
  storeId: string | null;
  storeCode: string | null;
  employeeId: string | null;
  employeeName: string | null;
  status: PayrollRunStatus;
  employees: number;
  gross: number;
  deductions: number;
  net: number;
  tax: number;
  employerContributions: number;
  notes: string | null;
  warnings: string[];
  createdBy: string | null;
  calculatedBy: string | null;
  calculatedAtUtc: UtcDateTime | null;
  approvedBy: string | null;
  approvedAtUtc: UtcDateTime | null;
  postedBy: string | null;
  postedAtUtc: UtcDateTime | null;
  journalNumber: number | null;
  paidOn: BusinessDate | null;
  paidBy: string | null;
  paymentJournalNumber: number | null;
}

export interface RunBody {
  year: number;
  month: number;
  storeId: string | null;
  notes: string | null;
}

export interface SettlementBody {
  employeeId: string;
  notes: string | null;
}

export interface PayBody {
  version: string;
  paidOn: BusinessDate | null;
  bankAccount: string | null;
  cashAccount: string | null;
}

export interface PayslipRow {
  id: string;
  employeeId: string;
  code: string;
  name: string;
  designation: string | null;
  department: string | null;
  storeCode: string | null;
  paymentMethod: SalaryPaymentMethod;
  daysEmployed: number;
  daysPresent: number;
  paidLeaveDays: number;
  unpaidLeaveDays: number;
  absentDays: number;
  lateDays: number;
  overtimeHours: number;
  basic: number;
  allowances: number;
  overtime: number;
  commission: number;
  otherEarnings: number;
  gross: number;
  attendance: number;
  tax: number;
  eobi: number;
  loans: number;
  otherDeductions: number;
  deductions: number;
  net: number;
  employerContributions: number;
}

export interface RunDetail {
  run: RunRow;
  payslips: PayslipRow[];
}

export interface PayslipLineInfo {
  kind: PayslipLineKind;
  code: string;
  name: string;
  amount: number;
  quantity: number | null;
  rate: number | null;
}

export interface PayslipDetail {
  row: PayslipRow;
  runId: string;
  runNumber: number;
  runTitle: string;
  runStatus: PayrollRunStatus;
  periodStart: BusinessDate;
  periodEnd: BusinessDate;
  cnic: string | null;
  bankName: string | null;
  iban: string | null;
  monthlyBasic: number;
  taxable: number;
  taxableBefore: number;
  taxBefore: number;
  projectedAnnualTaxable: number;
  projectedAnnualTax: number;
  companyName: string | null;
  storeName: string | null;
  lines: PayslipLineInfo[];
}

export interface CashSheetRow {
  code: string;
  name: string;
  designation: string | null;
  net: number;
}

export interface CashSheet {
  run: RunRow;
  rows: CashSheetRow[];
  total: number;
}

/** A parameter an HR report asks for: `run`, `employee`, `taxYear`, `month` (yyyy-MM) or `dates` (from and to). */
export type HrReportParameter = 'run' | 'employee' | 'taxYear' | 'month' | 'dates' | (string & {});

export interface HrReportInfo {
  key: string;
  group: string;
  title: string;
  parameters: HrReportParameter[];
}

// Phase E4: sales, CRM and marketing (docs/API.md, "New in phase E4") ------------------------------------------------

/** E4 additions to the client editor; a PUT that leaves them out keeps them. */
export interface ClientDetail {
  /** International form, `+923001234567`. */
  whatsAppNumber?: string | null;
  /** The client agreed to WhatsApp messages; given only with a number. */
  whatsAppOptIn?: boolean;
  /** Read-only: when consent was given. */
  whatsAppOptInAtUtc?: UtcDateTime | null;
  birthday?: BusinessDate | null;
  /** Read-only: when the client last answered about WhatsApp, yes or no (the till asks once). */
  whatsAppAskedAtUtc?: UtcDateTime | null;
}

/** E4 addition to the client list. */
export interface ClientRow {
  whatsAppOptIn?: boolean;
}

export type StageOutcome = 'Open' | 'Won' | 'Lost';

/** A column of the pipeline board (`GET /api/crm/stages`). */
export interface OpportunityStage {
  id: string;
  name: string;
  sortOrder: number;
  probability: number;
  outcome: StageOutcome;
}

/** What `PUT /api/crm/stages` takes, in board order. */
export interface StageRequest {
  id?: string | null;
  name: string;
  probability: number;
  outcome: StageOutcome;
}

export type LeadStatus = 'Open' | 'Converted' | 'Dropped';

export interface LeadRow {
  id: string;
  version: string;
  name: string;
  company: string | null;
  phone: string | null;
  whatsAppNumber: string | null;
  email: string | null;
  source: string | null;
  ownerId: string | null;
  ownerName: string | null;
  storeId: string;
  storeCode: string;
  status: LeadStatus;
  clientId: string | null;
  clientName: string | null;
  notes: string | null;
  createdAtUtc: UtcDateTime;
  convertedAtUtc: UtcDateTime | null;
}

export interface LeadRequest {
  name: string;
  company?: string | null;
  phone?: string | null;
  whatsAppNumber?: string | null;
  email?: string | null;
  source?: string | null;
  ownerId?: string | null;
  storeId?: string | null;
  notes?: string | null;
  status?: LeadStatus;
  version?: string | null;
}

export interface LeadConvertResult {
  lead: LeadRow;
  clientId: string;
  clientNumber: string | number | null;
}

export interface OpportunityRow {
  id: string;
  version: string;
  title: string;
  clientId: string | null;
  clientName: string | null;
  leadId: string | null;
  leadName: string | null;
  stageId: string;
  stage: string;
  outcome: StageOutcome;
  expectedValue: number;
  probability: number;
  weightedValue: number;
  expectedCloseOn: BusinessDate | null;
  ownerId: string | null;
  ownerName: string | null;
  storeId: string;
  storeCode: string;
  lostReason: string | null;
  closedOn: BusinessDate | null;
  notes: string | null;
  createdAtUtc: UtcDateTime;
}

export interface OpportunityRequest {
  title: string;
  clientId?: string | null;
  leadId?: string | null;
  stageId?: string | null;
  expectedValue: number;
  probability?: number | null;
  expectedCloseOn?: BusinessDate | null;
  ownerId?: string | null;
  storeId?: string | null;
  lostReason?: string | null;
  notes?: string | null;
  version?: string | null;
}

export interface BoardStage {
  id: string;
  name: string;
  outcome: StageOutcome;
  probability: number;
  count: number;
  value: number;
  weighted: number;
  opportunities: OpportunityRow[];
}

export interface OpportunityBoard {
  stages: BoardStage[];
}

export interface MoveRequest {
  stageId: string;
  version: string;
  lostReason?: string | null;
}

export interface WonLostReport {
  from: BusinessDate;
  to: BusinessDate;
  won: { count: number; value: number };
  lost: { count: number; value: number };
  winRate: number;
  byOwner: { ownerId: string | null; ownerName: string | null; won: number; wonValue: number; lost: number; lostValue: number }[];
  lostReasons: { reason: string; count: number; value: number }[];
  closed: OpportunityRow[];
}

export type ActivityKind = 'Call' | 'Visit' | 'Meeting' | 'Note' | 'Task';
export type ActivityDue = 'overdue' | 'today' | 'later' | 'none' | 'done';

export interface ActivityRow {
  id: string;
  version: string;
  kind: ActivityKind;
  subject: string;
  notes: string | null;
  clientId: string | null;
  clientName: string | null;
  leadId: string | null;
  leadName: string | null;
  opportunityId: string | null;
  opportunityTitle: string | null;
  /** The store's local time, or null. */
  dueAt: LocalDateTime | null;
  isDone: boolean;
  doneAtUtc: UtcDateTime | null;
  ownerId: string | null;
  ownerName: string | null;
  storeId: string;
  storeCode: string;
  due: ActivityDue;
  createdAtUtc: UtcDateTime;
}

export interface ActivityRequest {
  kind: ActivityKind;
  subject: string;
  notes?: string | null;
  clientId?: string | null;
  leadId?: string | null;
  opportunityId?: string | null;
  dueAt?: LocalDateTime | null;
  ownerId?: string | null;
  storeId?: string | null;
  isDone?: boolean;
  version?: string | null;
}

/** `GET /api/activities/mine`. */
export interface MyTasks {
  overdue: number;
  dueToday: number;
  items: ActivityRow[];
}

/** Every rule is optional; a segment matches the clients that meet all that are set. */
export interface SegmentRules {
  periodDays?: number | null;
  from?: BusinessDate | null;
  to?: BusinessDate | null;
  minSpend?: number | null;
  maxSpend?: number | null;
  minVisits?: number | null;
  maxVisits?: number | null;
  lastPurchaseBefore?: BusinessDate | null;
  lastPurchaseAfter?: BusinessDate | null;
  noPurchaseForDays?: number | null;
  storeId?: string | null;
  departmentId?: string | null;
  birthdayMonth?: number | null;
  minPoints?: number | null;
  maxPoints?: number | null;
  minBalance?: number | null;
  whatsAppConsent?: boolean | null;
}

export interface SegmentRow {
  id: string;
  version: string;
  name: string;
  description: string | null;
  rules: SegmentRules;
  /** The rules in words. */
  summary: string;
  storeId: string | null;
  /** The members when last evaluated; 0 before (see `lastEvaluatedAtUtc`). */
  lastCount: number;
  lastEvaluatedAtUtc: UtcDateTime | null;
}

export interface SegmentRequest {
  name: string;
  description?: string | null;
  rules: SegmentRules;
  version?: string | null;
}

export interface SegmentMember {
  clientId: string;
  number: string | number | null;
  name: string;
  phone: string | null;
  email: string | null;
  whatsAppNumber: string | null;
  whatsAppOptIn: boolean;
  spend: number;
  visits: number;
  lastPurchase: BusinessDate | null;
  points: number;
  balance: number;
  birthday: BusinessDate | null;
}

export interface SegmentResult {
  count: number;
  evaluatedAtUtc: UtcDateTime;
  /** The first 200, biggest spenders first. */
  members: SegmentMember[];
}

export interface WhatsAppStatus {
  enabled: boolean;
  /** `meta` sends through the Meta Cloud API; `recording` keeps the messages on the server only. */
  provider: 'meta' | 'recording' | (string & {});
  phoneNumberIdHint: string | null;
  webhookReady: boolean;
  messagesPerMinute: number;
}

export type TemplateCategory = 'Marketing' | 'Utility';

export interface WhatsAppTemplate {
  id: string;
  version: string;
  name: string;
  language: string;
  category: TemplateCategory;
  body: string;
  isActive: boolean;
  placeholders: string[];
}

export interface TemplateRequest {
  name: string;
  language: string;
  category: TemplateCategory;
  body: string;
  isActive: boolean;
  version?: string | null;
}

export type CampaignStatus = 'Draft' | 'Scheduled' | 'Sending' | 'Sent' | 'Cancelled';
export type MessageStatus = 'Queued' | 'Sent' | 'Delivered' | 'Read' | 'Failed' | 'Error';

export interface MessageCounts {
  queued: number;
  sent: number;
  delivered: number;
  read: number;
  failed: number;
  error: number;
}

export interface CampaignRow {
  id: string;
  version: string;
  name: string;
  segmentId: string;
  segment: string;
  templateId: string;
  template: string;
  status: CampaignStatus;
  storeId: string;
  storeCode: string;
  scheduledAtUtc: UtcDateTime | null;
  startedAtUtc: UtcDateTime | null;
  finishedAtUtc: UtcDateTime | null;
  recipients: number;
  counts: MessageCounts;
  createdBy: string | null;
  createdAtUtc: UtcDateTime;
}

export interface CampaignRequest {
  name: string;
  segmentId: string;
  templateId: string;
  storeId?: string | null;
  version?: string | null;
}

export interface WhatsAppMessageRow {
  id: string;
  campaignId: string | null;
  clientId: string;
  clientName: string;
  toNumber: string;
  template: string;
  body: string;
  status: MessageStatus;
  error: string | null;
  sentAtUtc: UtcDateTime | null;
  deliveredAtUtc: UtcDateTime | null;
  readAtUtc: UtcDateTime | null;
  createdAtUtc: UtcDateTime;
}

export interface CampaignResults {
  counts: MessageCounts;
  recipients: number;
  days: number;
  buyers: number;
  sales: number;
  salesTotal: number;
}

export type PromotionKind = 'PercentOff' | 'AmountOff' | 'BuyXGetY' | 'BundlePrice' | 'SpendOver';
export type PromotionScope = 'AllItems' | 'Departments' | 'Categories' | 'Items';

/** What a promotion applies to: one of a department, a category or a stock code. */
export interface PromotionTarget {
  departmentId?: string | null;
  categoryId?: string | null;
  plu?: string | null;
  quantity: number;
  /** Display text the server fills in. */
  name?: string | null;
}

export interface PromotionRow {
  id: string;
  version: string;
  name: string;
  kind: PromotionKind;
  isActive: boolean;
  startsOn: BusinessDate | null;
  endsOn: BusinessDate | null;
  priority: number;
  combinable: boolean;
  allStores: boolean;
  storeIds: string[];
  couponCode: string | null;
  couponMaxUses: number;
  couponUses: number;
  summary: string;
  live: boolean;
}

export interface PromotionDetail extends PromotionRow {
  description: string | null;
  /** 0 Sunday … 6 Saturday; empty every day. */
  days: number[];
  /** `HH:mm`. */
  startTime: string | null;
  endTime: string | null;
  scope: PromotionScope;
  targets: PromotionTarget[];
  percent: number;
  amount: number;
  buyQuantity: number;
  getQuantity: number;
  getPercent: number;
  rewardTargets: PromotionTarget[];
  bundlePrice: number;
  bundleParts: PromotionTarget[];
  spendThreshold: number;
}

/** `PromotionDetail` without the read-only fields, as POST and PUT take it. */
export type PromotionRequest = Omit<PromotionDetail, 'id' | 'version' | 'couponUses' | 'summary' | 'live'> & { id?: string | null; version?: string | null };

export interface PromotionReportRow {
  promotionId: string;
  name: string;
  kind: PromotionKind;
  sales: number;
  units: number;
  netSales: number;
  discount: number;
  couponUses: number;
}

export interface SalesTargetRow {
  id: string;
  version: string;
  storeId: string;
  storeCode: string;
  /** Null: the store's own target. */
  userId: string | null;
  userName: string | null;
  year: number;
  month: number;
  amount: number;
  actual: number;
  percent: number;
}

export interface SalesTargetRequest {
  storeId: string;
  userId?: string | null;
  year: number;
  month: number;
  amount: number;
}

export interface TargetProgressRow {
  target: number;
  actual: number;
  percent: number;
}

export interface TargetProgress extends TargetProgressRow {
  year: number;
  month: number;
  day: number;
  daysInMonth: number;
  stores: ({ storeId: string; storeCode: string } & TargetProgressRow)[];
  people: ({ userId: string; userName: string; storeCode: string } & TargetProgressRow)[];
}

// Supply chain (phase E2) --------------------------------------------------------------------------------------------

/** A batch (lot) of an item kept in batches, with its stock; `daysLeft` is negative once it has expired. */
export interface BatchRow {
  id: string;
  version: string;
  itemId: string;
  plu: string;
  itemName: string;
  storeCode: string;
  locationCode: string;
  batchNumber: string;
  expiresOn: BusinessDate | null;
  madeOn: BusinessDate | null;
  supplier: string | null;
  receivedOn: BusinessDate | null;
  purchaseOrderNumber: number | null;
  onHand: number;
  unitCost: number;
  value: number;
  daysLeft: number | null;
  bin: string | null;
  note: string | null;
}

export interface BatchRequest {
  version?: string | null;
  itemId?: string | null;
  batchNumber: string;
  expiresOn: BusinessDate | null;
  madeOn: BusinessDate | null;
  supplierId?: string | null;
  note?: string | null;
}

export interface BatchAdjustRequest {
  quantity: number;
  setTo: boolean;
  reason: string | null;
}

/** `GET /api/batches/expiring` and `/expired`: the batches with stock, their units and value. */
export interface ExpiryReport {
  days: number;
  rows: BatchRow[];
  units: number;
  value: number;
}

export interface BinRow {
  id: string;
  version: string;
  locationId: string;
  storeCode: string;
  locationCode: string;
  code: string;
  zone: string | null;
  description: string | null;
  isActive: boolean;
  /** Items kept in the bin (it is their default bin). */
  items: number;
}

export interface BinRequest {
  version?: string | null;
  locationId?: string | null;
  code: string;
  zone: string | null;
  description: string | null;
  isActive: boolean;
}

/** A line of a put-away, pick or count list, sorted by zone and bin. */
export interface BinListRow {
  zone: string | null;
  bin: string | null;
  plu: string;
  description: string;
  quantity: number;
  batchNumber: string | null;
  expiresOn: BusinessDate | null;
  document: string | null;
  onHand: number;
}

export interface BinList {
  title: string;
  rows: BinListRow[];
}

export type PurchaseOrderStatus = 'Draft' | 'Posted' | 'Received';

export interface PurchaseOrderRow {
  id: string;
  number: number;
  storeCode: string;
  locationCode: string;
  status: PurchaseOrderStatus;
  createdAt: LocalDateTime;
  postedAt: LocalDateTime | null;
  receivedAt: LocalDateTime | null;
  expectedOn: BusinessDate | null;
  supplier: string | null;
  description: string | null;
  lines: number;
  totalCost: number;
}

export interface ReceivedBatch {
  batchNumber: string;
  pieces: number;
  expiresOn: BusinessDate | null;
  madeOn: BusinessDate | null;
}

export interface PurchaseOrderLine {
  id: string;
  itemId: string | null;
  plu: string;
  description: string;
  supplier: string | null;
  caseQuantity: number;
  orderQuantity: number;
  /** Order quantity times case quantity. */
  pieces: number;
  costEach: number;
  orderedCost: number;
  orderCost: number;
  quantityReceived: number;
  tracksBatches: boolean;
  shelfLifeDays: number;
  bin: string | null;
  batches: ReceivedBatch[];
}

export interface PurchaseOrderDetail {
  id: string;
  version: string;
  number: number;
  storeId: string;
  storeCode: string;
  locationId: string;
  locationCode: string;
  status: PurchaseOrderStatus;
  createdAt: LocalDateTime;
  postedAt: LocalDateTime | null;
  receivedAt: LocalDateTime | null;
  expectedOn: BusinessDate | null;
  supplierId: string | null;
  supplier: string | null;
  description: string | null;
  createdBy: string | null;
  receivedBy: string | null;
  lines: PurchaseOrderLine[];
  totalCost: number;
}

export interface ReceiveLineRequest {
  lineId: string;
  pieces: number;
  costEach: number | null;
  batches: ReceivedBatch[] | null;
}

export interface ReceiveRequest {
  version: string;
  shippingCost: number;
  lines: ReceiveLineRequest[];
}

/** The version a step sends back (post, submit, approve…), and the note some steps take. */
export interface VersionRequest {
  version: string;
  note?: string | null;
}

export type RequisitionStatus = 'Draft' | 'Submitted' | 'Approved' | 'Rejected' | 'Ordered';

export interface RequisitionRow {
  id: string;
  number: number;
  storeCode: string;
  locationCode: string;
  status: RequisitionStatus;
  createdAt: LocalDateTime;
  requestedBy: string | null;
  neededBy: BusinessDate | null;
  lines: number;
  total: number;
  decidedBy: string | null;
  decidedAt: LocalDateTime | null;
  note: string | null;
}

export interface RequisitionLine {
  id: string;
  lineNumber: number;
  itemId: string | null;
  plu: string;
  description: string;
  quantity: number;
  costEach: number;
  value: number;
  neededBy: BusinessDate | null;
  supplierId: string | null;
  supplier: string | null;
  note: string | null;
  purchaseOrderId: string | null;
  purchaseOrderNumber: number | null;
}

export interface RequisitionDetail {
  id: string;
  version: string;
  number: number;
  storeId: string;
  storeCode: string;
  locationId: string;
  locationCode: string;
  status: RequisitionStatus;
  createdAt: LocalDateTime;
  requestedBy: string | null;
  neededBy: BusinessDate | null;
  note: string | null;
  submittedAt: LocalDateTime | null;
  decidedAt: LocalDateTime | null;
  decidedBy: string | null;
  decisionNote: string | null;
  orderedAt: LocalDateTime | null;
  total: number;
  lines: RequisitionLine[];
}

export interface RequisitionLineRequest {
  itemId: string | null;
  plu: string | null;
  quantity: number;
  neededBy: BusinessDate | null;
  supplierId: string | null;
  costEach: number | null;
  note: string | null;
}

export interface RequisitionRequest {
  version?: string | null;
  locationId?: string | null;
  neededBy: BusinessDate | null;
  note: string | null;
  lines: RequisitionLineRequest[];
}

export interface OrderMade {
  id: string;
  number: number;
  supplier: string | null;
}

export interface OrdersMade {
  orders: OrderMade[];
}

/** A role's limit for approving requisitions (`maxAmount` null: no limit). */
export interface ApprovalLimitRow {
  roleId: string;
  role: string;
  mayApprove: boolean;
  maxAmount: number | null;
}

export type BillMatchStatus = 'Matched' | 'Held' | 'Accepted' | 'Rejected';

/** A supplier bill line checked against its order line and receipt (three-way match). */
export interface MatchRow {
  id: string;
  version: string;
  storeCode: string;
  purchaseOrderId: string;
  purchaseOrderNumber: number;
  purchaseOrderLineId: string;
  billId: string;
  billLineId: string;
  billNumber: string | null;
  billDate: BusinessDate;
  supplier: string | null;
  plu: string;
  description: string;
  quantityOrdered: number;
  quantityReceived: number;
  quantityBilled: number;
  quantityMatched: number;
  orderedCost: number;
  receivedCost: number;
  billedCost: number;
  priceDifference: number;
  status: BillMatchStatus;
  reason: string | null;
  checkedAtUtc: UtcDateTime;
  decidedAtUtc: UtcDateTime | null;
  decidedBy: string | null;
  decisionNote: string | null;
}

export interface MatchRun {
  lines: number;
  matched: number;
  held: number;
  posted: number;
}

/** A line of "received but not billed" or "billed but not received". */
export interface MatchReportRow {
  purchaseOrderId: string;
  purchaseOrderNumber: number;
  purchaseOrderLineId: string;
  storeCode: string;
  plu: string;
  description: string;
  supplier: string | null;
  ordered: number;
  received: number;
  billed: number;
  difference: number;
  costEach: number;
  value: number;
  receivedAt: LocalDateTime | null;
}

export type LandedCostStatus = 'Draft' | 'Posted';
export type LandedCostBasis = 'Value' | 'Quantity' | 'Weight';
export type LandedCostChargeKind = 'Freight' | 'Duty' | 'Insurance' | 'Other';

export interface LandedCostRow {
  id: string;
  number: number;
  storeCode: string;
  date: BusinessDate;
  status: LandedCostStatus;
  basis: LandedCostBasis;
  reference: string | null;
  total: number;
  receipts: number;
  note: string | null;
  postedAtUtc: UtcDateTime | null;
  postedBy: string | null;
}

export interface LandedCharge {
  kind: LandedCostChargeKind;
  description: string | null;
  amount: number;
}

export interface LandedAllocation {
  purchaseOrderLineId: string;
  itemId: string | null;
  plu: string;
  description: string;
  quantity: number;
  value: number;
  weight: number;
  share: number;
  onHand: number;
  toInventory: number;
  toCostOfGoods: number;
}

export interface LandedReceipt {
  purchaseOrderId: string;
  number: number;
  receivedAt: LocalDateTime | null;
  supplier: string | null;
  value: number;
}

export interface LandedCostDetail {
  id: string;
  version: string;
  number: number;
  storeId: string;
  storeCode: string;
  date: BusinessDate;
  status: LandedCostStatus;
  basis: LandedCostBasis;
  reference: string | null;
  note: string | null;
  total: number;
  charges: LandedCharge[];
  receipts: LandedReceipt[];
  allocations: LandedAllocation[];
  createdBy: string | null;
  postedAtUtc: UtcDateTime | null;
  postedBy: string | null;
}

export interface LandedCostRequest {
  version?: string | null;
  storeId?: string | null;
  date: BusinessDate | null;
  basis: LandedCostBasis;
  reference: string | null;
  note: string | null;
  charges: LandedCharge[];
  purchaseOrderIds: string[];
}

/** A received order a landed cost voucher can be spread over. */
export interface ReceiptChoice {
  purchaseOrderId: string;
  number: number;
  storeCode: string;
  locationCode: string;
  receivedAt: LocalDateTime | null;
  supplier: string | null;
  description: string | null;
  lines: number;
  value: number;
}

export interface ReorderRow {
  itemId: string;
  plu: string;
  name: string;
  supplierId: string | null;
  supplier: string | null;
  onHand: number;
  onOrder: number;
  customerOrdered: number;
  unitsSold: number;
  averageDailySales: number;
  leadTimeDays: number;
  reorderPoint: number;
  reorderLevel: number;
  targetStock: number;
  caseQuantity: number;
  minimumOrder: number;
  suggestedPieces: number;
  orderQuantity: number;
  costEach: number;
  value: number;
}

export interface ReorderSuggestions {
  locationId: string;
  rows: ReorderRow[];
}

export interface ReorderLineRequest {
  itemId: string;
  pieces: number;
  supplierId: string | null;
}

export interface ReorderRequest {
  locationId?: string | null;
  lines: ReorderLineRequest[];
  submit?: boolean;
  neededBy?: BusinessDate | null;
}

export interface SupplierPerformanceRow {
  supplierId: string | null;
  supplier: string;
  orders: number;
  onTime: number;
  onTimePercent: number | null;
  orderedPieces: number;
  receivedPieces: number;
  fillRatePercent: number | null;
  priceIncreases: number;
  priceDecreases: number;
  averagePriceChangePercent: number | null;
  returns: number;
  returnedPieces: number;
  returnedValue: number;
  averageLeadDays: number | null;
  receivedValue: number;
}

export interface SupplierPerformance {
  from: BusinessDate;
  to: BusinessDate;
  rows: SupplierPerformanceRow[];
}

/** A store's supply chain settings; `version` is `new` while the store has none saved. */
export interface SupplySettings {
  version: string;
  storeId: string;
  blockExpiredSales: boolean;
  expiryWarningDays: number;
  quantityTolerancePercent: number;
  quantityToleranceUnits: number;
  priceTolerancePercent: number;
  priceToleranceAmount: number;
  salesWindowDays: number;
  safetyStockDays: number;
  orderCoverDays: number;
  defaultLeadTimeDays: number;
  approverMustDiffer: boolean;
}
