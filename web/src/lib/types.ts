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
