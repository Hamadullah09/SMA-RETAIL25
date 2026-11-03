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
