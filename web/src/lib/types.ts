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
