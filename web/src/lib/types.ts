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
