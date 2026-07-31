/**
 * The back-office record shapes, mirroring the DTOs the API returns.
 *
 * Written out rather than inferred from a schema generator so a field that disappears server-side is
 * a type error here, not a blank box on a form somebody notices a week later.
 */

export type ProductType =
  | 'Standard'
  | 'Matrix'
  | 'Serialized'
  | 'Kit'
  | 'NonStock'
  | 'Rental'
  | 'Service'
  | 'Shipping'
  | 'Admission'
  | 'GiftCard';

export const productTypes: ProductType[] = [
  'Standard',
  'Matrix',
  'Serialized',
  'Kit',
  'NonStock',
  'Rental',
  'Service',
  'Shipping',
  'Admission',
  'GiftCard',
];

export type ProductSort = 'StockCode' | 'Name' | 'OnHand' | 'RegularPrice' | 'Margin';

export interface CursorPage<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface ProductRow {
  id: string;
  stockCode: string;
  name: string;
  type: ProductType;
  departmentName: string | null;
  categoryName: string | null;
  regularPrice: number;
  avgCost: number;
  grossMarginPct: number;
  onHand: number;
  onOrder: number;
  reorderPoint: number;
  tax1Applies: boolean;
  tax2Applies: boolean;
  upc: string | null;
  isDeleted: boolean;
}

export interface ProductPriceLevel {
  level: number;
  price: number;
}

export interface PriceBreakRow {
  level: number;
  minQuantity: number;
}

export interface SalePricing {
  discountPct: number;
  startsOn: string;
  endsOn: string;
}

export interface BonusPricing {
  buyQty: number;
  freeQty: number;
}

export interface ProductSupplierRow {
  supplierId: string;
  supplierName: string;
  rank: number;
  cost: number;
  reorderNumber: string | null;
  caseQty: number;
  minimumOrderQty: number;
}

export interface LinkedProduct {
  id: string;
  stockCode: string;
  name: string;
}

export interface KitComponentRow {
  componentProductId: string;
  stockCode: string;
  name: string;
  quantity: number;
}

export interface ProductForm {
  id: string;
  locationId: string;
  stockCode: string;
  name: string;
  description: string | null;
  type: ProductType;
  upc: string | null;
  tax1Applies: boolean;
  tax2Applies: boolean;
  regularPrice: number;
  lastCost: number;
  avgCost: number;
  grossMarginPct: number;
  baseStock: number;
  reorderPoint: number;
  reorderQty: number;
  onHand: number;
  onOrder: number;
  caseQty: number;
  shipWeight: number;
  binLocation: string | null;
  posMessage: string | null;
  invoiceMessage: string | null;
  notes: string | null;
  departmentId: string | null;
  departmentName: string | null;
  categoryId: string | null;
  categoryName: string | null;
  substitute: LinkedProduct | null;
  tagAlong: LinkedProduct | null;
  parent: LinkedProduct | null;
  levels: ProductPriceLevel[];
  breaks: PriceBreakRow[];
  sale: SalePricing | null;
  bonus: BonusPricing | null;
  suppliers: ProductSupplierRow[];
  kitComponents: KitComponentRow[];
  isDeleted: boolean;
  createdAt: string;
  modifiedAt: string | null;
}

export interface ReferenceRow {
  id: string;
  name: string;
  code: string | null;
  sortOrder: number;
  isActive: boolean;
  usageCount: number;
}

export interface Address {
  line1?: string | null;
  line2?: string | null;
  city?: string | null;
  stateOrProvince?: string | null;
  postalCode?: string | null;
  country?: string | null;
}

export interface ContactDetails {
  phone?: string | null;
  extension?: string | null;
  mobile?: string | null;
  fax?: string | null;
  email?: string | null;
  website?: string | null;
}

export type CustomerSort = 'Number' | 'Name' | 'Company' | 'Balance';

export interface CustomerRow {
  id: string;
  customerNumber: number;
  firstName: string;
  lastName: string;
  company: string | null;
  displayName: string;
  city: string | null;
  stateOrProvince: string | null;
  phone: string | null;
  email: string | null;
  clientType: string | null;
  creditLimit: number;
  balanceDue: number;
  priceLevel: number;
  lastPurchaseOn: string | null;
  isDeleted: boolean;
}

export interface CustomerForm {
  id: string;
  locationId: string;
  customerNumber: number;
  firstName: string;
  lastName: string;
  company: string | null;
  title: string | null;
  billingAddress: Address;
  shipToAddress: Address;
  contact: ContactDetails;
  clientType: string | null;
  birthday: string | null;
  notes: string | null;
  lastPurchaseOn: string | null;
  lastMailingOn: string | null;
  accountNumber: number;
  creditLimit: number;
  balanceDue: number;
  usualDiscountPct: number;
  priceLevel: number;
  exemptTax1: boolean;
  exemptTax2: boolean;
  rewardPoints: number;
  isDeleted: boolean;
  createdAt: string;
  modifiedAt: string | null;
}

export type SupplierSort = 'Number' | 'Company';

export interface SupplierRow {
  id: string;
  supplierNumber: string;
  company: string;
  contactName: string | null;
  city: string | null;
  stateOrProvince: string | null;
  phone: string | null;
  email: string | null;
  suppliedItemCount: number;
  isDeleted: boolean;
}

export interface SupplierForm {
  id: string;
  locationId: string;
  supplierNumber: string;
  company: string;
  contactFirstName: string | null;
  contactLastName: string | null;
  title: string | null;
  address: Address;
  contact: ContactDetails;
  suppliedItemCount: number;
  isDeleted: boolean;
  createdAt: string;
  modifiedAt: string | null;
}

/** The six legacy PO quantity-calculation methods (guide p.64). */
export type OrderQuantityStrategy =
  | 'Blank'
  | 'OneWeek'
  | 'TwoWeeks'
  | 'ReorderPointFixed'
  | 'ReorderPointToBase'
  | 'MonthlySales';

export const orderQuantityStrategies: { value: OrderQuantityStrategy; label: string }[] = [
  { value: 'Blank', label: 'Blank (manual entry)' },
  { value: 'OneWeek', label: 'One week of sales' },
  { value: 'TwoWeeks', label: 'Two weeks of sales' },
  { value: 'ReorderPointFixed', label: 'Reorder point → fixed quantity' },
  { value: 'ReorderPointToBase', label: 'Reorder point → base stock' },
  { value: 'MonthlySales', label: 'Trailing monthly sales' },
];

export type PurchaseOrderStatus = 'Draft' | 'Posted' | 'PartiallyReceived' | 'Received' | 'Closed' | 'Cancelled';

export interface PurchaseOrderRow {
  id: string;
  poNumber: number;
  supplierId: string;
  supplierCompany: string;
  status: PurchaseOrderStatus;
  quantityStrategy: OrderQuantityStrategy;
  postedOn: string | null;
  dueOn: string | null;
  total: number;
  lineCount: number;
}

export interface PurchaseOrderLineRow {
  id: string;
  productId: string;
  stockCode: string;
  productName: string;
  orderQty: number;
  caseQty: number;
  costEach: number;
  orderCost: number;
  qtyReceived: number;
  inStockAtGeneration: number;
  onOrderAtGeneration: number;
  backOrders: number;
}

export interface PurchaseOrderReceiptRow {
  id: string;
  receivedOn: string;
  freightTotal: number;
  staffId: string;
}

export type InvoiceStatus = 'Open' | 'Paid' | 'Void';

export interface InvoiceRow {
  id: string;
  invoiceNumber: number;
  issuedOn: string;
  dueOn: string;
  invoiceTotal: number;
  penaltyAccrued: number;
  balanceDue: number;
  status: InvoiceStatus;
  lastPaymentOn: string | null;
}

export interface CustomerAccountRow {
  customerId: string;
  accountNumber: number;
  customerName: string;
  creditLimit: number;
  balanceDue: number;
  openInvoiceCount: number;
}

export type AREntryType = 'Charge' | 'Payment' | 'LateCharge' | 'Refund' | 'Void' | 'Adjustment';

export interface ArLedgerEntryRow {
  id: string;
  invoiceId: string;
  entryType: AREntryType;
  amount: number;
  occurredAt: string;
}

export interface CustomerStatement {
  customerId: string;
  customerName: string;
  accountNumber: number;
  creditLimit: number;
  balanceDue: number;
  invoices: InvoiceRow[];
  ledger: ArLedgerEntryRow[];
}

export interface GiftCard {
  id: string;
  serialNumber: string;
  originalValue: number;
  remainingValue: number;
  issuedToCustomerId: string | null;
  issuedOn: string;
  expiresOn: string | null;
  isActive: boolean;
}

export interface LoyaltyPolicy {
  locationId: string;
  isEnabled: boolean;
  pointsPerDollar: number;
  minimumRequired: number;
  percentEnabled: boolean;
  rewardPercent: number;
  fixedEnabled: boolean;
  rewardFixedAmount: number;
  suppressIfSubtotalDiscountApplied: boolean;
}

export interface LoyaltyBalance {
  customerId: string;
  customerName: string;
  rewardPoints: number;
}

export type LoyaltyEntryType = 'Earned' | 'Redeemed' | 'ReturnClawback' | 'Manual';

export interface LoyaltyLedgerEntryRow {
  id: string;
  entryType: LoyaltyEntryType;
  points: number;
  occurredAt: string;
}

export interface ReceivablesAgingRow {
  customerId: string;
  customerName: string;
  current: number;
  days30: number;
  days60: number;
  days90Plus: number;
  total: number;
}

export type CustomerOrderStatus = 'Open' | 'PartiallyFilled' | 'Filled' | 'Cancelled';

export interface CustomerOrderLine {
  id: string;
  productId: string;
  stockCode: string;
  productName: string;
  orderedQty: number;
  filledQty: number;
  unitPrice: number;
}

export interface CustomerOrder {
  id: string;
  orderNumber: number;
  customerId: string;
  customerName: string;
  status: CustomerOrderStatus;
  orderedOn: string;
  notes: string | null;
  lines: CustomerOrderLine[];
}

export type LayawayStatus = 'Open' | 'PaidInFull' | 'Cancelled';

export interface LayawayLine {
  id: string;
  productId: string;
  stockCode: string;
  productName: string;
  quantity: number;
  unitPrice: number;
}

export interface Layaway {
  id: string;
  layawayNumber: number;
  customerId: string;
  customerName: string;
  status: LayawayStatus;
  total: number;
  amountPaid: number;
  createdOn: string;
  lines: LayawayLine[];
}

export type PriceQuoteStatus = 'Open' | 'Converted' | 'Expired' | 'Cancelled';

export interface PriceQuoteLine {
  id: string;
  productId: string;
  stockCode: string;
  productName: string;
  quantity: number;
  unitPrice: number;
}

export interface PriceQuote {
  id: string;
  quoteNumber: number;
  customerId: string;
  customerName: string;
  status: PriceQuoteStatus;
  issuedOn: string;
  expiresOn: string | null;
  total: number;
  lines: PriceQuoteLine[];
}

export interface StockLevelRow {
  id: string;
  stockCode: string;
  productName: string;
  onHand: number;
  onOrder: number;
  committed: number;
  available: number;
  reorderPoint: number;
  reorderQty: number;
  avgCost: number;
}

export interface PurchaseOrderDetail {
  id: string;
  poNumber: number;
  locationId: string;
  supplierId: string;
  supplierCompany: string;
  status: PurchaseOrderStatus;
  quantityStrategy: OrderQuantityStrategy;
  headerText: string | null;
  postedOn: string | null;
  dueOn: string | null;
  total: number;
  lines: PurchaseOrderLineRow[];
  receipts: PurchaseOrderReceiptRow[];
}

export type DeletedEntityKind = 'Product' | 'Customer' | 'Supplier' | 'Department' | 'Category';

export interface DeletedRow {
  kind: DeletedEntityKind;
  id: string;
  reference: string;
  name: string;
  deletedAt: string | null;
  deletedBy: string | null;
  deletedByName: string | null;
}

// --- Settings ------------------------------------------------------------------------------------

export interface BusinessSettings {
  locationId: string;
  businessName: string;
  address: Address;
  contact: ContactDetails;
  licenceNumber: string | null;
  taxRegistrationNumber: string | null;
  locationName: string;
  legacyCode: string;
  timeZoneId: string;
  businessDayStart: string;
  baseCurrencyCode: string;
}

export interface TaxSettings {
  id: string | null;
  effectiveFrom: string;
  effectiveTo: string | null;
  tax1Enabled: boolean;
  tax1Name: string;
  tax1Rate: number;
  tax2Enabled: boolean;
  tax2Name: string;
  tax2Rate: number;
  tax2Compound: boolean;
  addOnChargeEnabled: boolean;
  addOnChargeName: string;
  addOnChargeRate: number;
  addOnChargeTaxable: boolean;
  taxationType: 'Exclusive' | 'Inclusive';
  registrationNumber: string | null;
  isCurrent: boolean;
}

export interface PosSettings {
  applyTax1: boolean;
  applyTax2: boolean;
  allowTaxOverride: boolean;
  applyAddOnCharge: boolean;
  fastScanMode: boolean;
  autoSaveSales: boolean;
  confirmBeforeSavingSales: boolean;
  scanRandomWeightBarcodes: boolean;
  staffMayDiscount: boolean;
  allowItemListEdit: boolean;
  trackStaffSales: boolean;
  requireSupervisorToVoid: boolean;
  useEmployeeTimeClock: boolean;
  printCreditCardSignatureLine: boolean;
  printClientNameOnSalesSlip: boolean;
  carryOverCityStateZip: boolean;
  defaultTenderTypeId: string | null;
  abandonedCartTimeoutMinutes: number;
}

export type ReaderMode = 'Off' | 'OnDemand' | 'Continuous';

export interface StationSettings {
  id: string;
  stationCode: string;
  name: string | null;
  fastScanMode: boolean | null;
  autoSaveSales: boolean | null;
  confirmBeforeSaving: boolean | null;
  scanRandomWeightBarcodes: boolean | null;
  defaultTenderTypeId: string | null;
  printerProfileId: string | null;
  readerProfileId: string | null;
  scaleProfileId: string | null;
  poleDisplayProfileId: string | null;
  readerMode: ReaderMode;
  isActive: boolean;
  agentVersion: string | null;
  lastHeartbeat: string | null;
  agentOnline: boolean;
}

export interface PrinterSettings {
  id: string;
  stationId: string | null;
  name: string;
  setupCommand: string | null;
  cutterCommand: string | null;
  redCommand: string | null;
  blackCommand: string | null;
  port: string | null;
  defaultCopies: number;
  pageEject: boolean;
  extraCopyOnCard: boolean;
  initializeSerial: boolean;
  output: 'Invoice' | 'Slip40' | 'Slip20';
  columns: number;
  drawerTrigger: string;
  drawerRepeat: number;
  openDrawerOnPrint: boolean;
  isActive: boolean;
}

export interface ScaleSettings {
  id: string;
  stationId: string | null;
  name: string;
  port: string;
  baudRate: number;
  dataBits: number;
  parity: string;
  stopBits: string;
  getWeightCommand: string;
  zeroCommand: string;
  unit: string;
  timeoutMs: number;
  isActive: boolean;
}

export interface PoleDisplaySettings {
  id: string;
  stationId: string | null;
  name: string;
  port: string;
  baudRate: number;
  line1Width: number;
  line2Width: number;
  idleLine1: string;
  idleLine2: string;
  clearCommand: string;
  line1Command: string;
  line2Command: string;
  isActive: boolean;
}

export interface ReaderSettings {
  id: string;
  stationId: string | null;
  name: string;
  host: string;
  port: number;
  protocol: 'Llrp' | 'Http' | 'Mqtt' | 'Simulator' | 'UhfSerial';
  antennaZones: string;
  rssiThresholdDbm: number;
  minimumReadCount: number;
  debounceMs: number;
  coalesceMs: number;
  flushIntervalMs: number;
  maxBatchSize: number;
  autoAcceptBatches: boolean;
  continuousMode: boolean;
  isActive: boolean;
}

export type TenderBehaviour = 'Cash' | 'Card' | 'GiftCard' | 'GiftCertificate' | 'OnAccount' | 'Manual';

export interface TenderSettings {
  id: string;
  code: string;
  displayName: string;
  behaviour: TenderBehaviour;
  sortOrder: number;
  iconKey: string | null;
  opensCashDrawer: boolean;
  allowsOverTender: boolean;
  roundsToMinimumTender: boolean;
  countsTowardsDrawerCash: boolean;
  requiresReference: boolean;
  printsSignatureCopy: boolean;
  allowedForRefunds: boolean;
  currencyCode: string | null;
  externalAccountingKey: string | null;
  isActive: boolean;
}

export interface CurrencySettings {
  id: string;
  code: string;
  name: string;
  symbol: string;
  scale: number;
  rounding: 'AwayFromZero' | 'ToEven' | 'Down' | 'Up';
  minimumTender: number;
  isBaseCurrency: boolean;
  exchangeRate: number;
  exchangeRateUpdatedAt: string | null;
  isActive: boolean;
}

export type SequenceKind =
  | 'Customer'
  | 'Supplier'
  | 'Product'
  | 'Invoice'
  | 'PurchaseOrder'
  | 'Transaction'
  | 'StockCount'
  | 'Transfer';

export interface NumberSequenceSettings {
  id: string;
  kind: SequenceKind;
  prefix: string;
  padWidth: number;
  nextNumber: number;
  highWaterMark: number;
  sample: string;
}

export interface PricingRuleSettings {
  id: string;
  ruleKey: string;
  order: number;
  enabled: boolean;
  parametersJson: string | null;
}

export interface StaffSettings {
  id: string;
  userId: string;
  staffCode: string;
  firstName: string;
  lastName: string;
  accessLevel: number;
  isActive: boolean;
  hasPin: boolean;
  pinLocked: boolean;
  pinLockedUntil: string | null;
}

export interface SettingsSnapshot {
  business: BusinessSettings;
  taxes: TaxSettings[];
  pos: PosSettings;
  stations: StationSettings[];
  printers: PrinterSettings[];
  scales: ScaleSettings[];
  poleDisplays: PoleDisplaySettings[];
  readers: ReaderSettings[];
  tenders: TenderSettings[];
  currencies: CurrencySettings[];
  numbering: NumberSequenceSettings[];
  pricingRules: PricingRuleSettings[];
  staff: StaffSettings[];
}

// --- Sales log (guide p.14–15, p.101) ------------------------------------------------------------

export type TransactionStatus = 'Completed' | 'Voided' | 'Reversal' | 'Suspended';

export interface SalesLogRow {
  id: string;
  transactionNumber: number;
  completedAt: string;
  businessDate: string;
  stationCode: string;
  staffName: string;
  customerName: string | null;
  lineCount: number;
  subtotal: number;
  discountTotal: number;
  tax1Total: number;
  tax2Total: number;
  grandTotal: number;
  status: TransactionStatus;
}

export interface SalesLogPage {
  rows: SalesLogRow[];
  totalCount: number;
  pageTotal: number;
  grandTotal: number;
}

export interface SaleDetailLine {
  sequence: number;
  stockCode: string;
  name: string;
  quantity: number;
  unitPrice: number;
  discountPct: number;
  extendedNet: number;
  tax1Amount: number;
  tax2Amount: number;
  priceOrigin: string;
  lineType: string;
  epc: string | null;
}

export interface SaleDetailTender {
  tenderName: string;
  amount: number;
  amountTendered: number;
  changeGiven: number;
  reference: string | null;
}

export interface SaleDetail {
  id: string;
  transactionNumber: number;
  completedAt: string;
  status: TransactionStatus;
  stationCode: string;
  staffName: string;
  customerName: string | null;
  lines: SaleDetailLine[];
  tenders: SaleDetailTender[];
  subtotal: number;
  discountTotal: number;
  tax1Name: string;
  tax1Total: number;
  tax2Name: string;
  tax2Total: number;
  addOnCharge: number;
  grandTotal: number;
  changeGiven: number;
  reversesTransactionId: string | null;
  voidedByTransactionId: string | null;
  voidReason: string | null;
}

// --- Audit log (doc 07 §Audit) -------------------------------------------------------------------

export type AuditAction =
  | 'Created'
  | 'Updated'
  | 'Deleted'
  | 'SignedIn'
  | 'SignInFailed'
  | 'PermissionDenied'
  | 'StepUpGranted'
  | 'StepUpDenied';

export interface AuditLogRow {
  id: string;
  occurredAt: string;
  action: AuditAction;
  actorName: string | null;
  actorStaffId: string | null;
  stationId: string | null;
  ipAddress: string | null;
  entityType: string;
  entityId: string | null;
  operation: string | null;
  beforeJson: string | null;
  afterJson: string | null;
  approverName: string | null;
  reason: string | null;
  correlationId: string | null;
}

export interface AuditLogPage {
  rows: AuditLogRow[];
  totalCount: number;
}

// --- Matrix items (guide p.39–40) ----------------------------------------------------------------

export interface MatrixDimension {
  position: number;
  name: string;
  values: string[];
}

export interface ProductVariant {
  id: string;
  variantCode: string;
  dim1Value: string;
  dim2Value: string | null;
  dim3Value: string | null;
  upc: string | null;
  onHand: number;
  isActive: boolean;
}

export interface Matrix {
  productId: string;
  stockCode: string;
  name: string;
  dimensions: MatrixDimension[];
  variants: ProductVariant[];
}

// ---------------------------------------------------------------------------------------------
// Reports (guide p.15–27, p.56, p.83–84)
// ---------------------------------------------------------------------------------------------

export type SalesAnalysisGroupBy = 'Product' | 'Department' | 'Client' | 'Day' | 'Week' | 'Month';

export interface SalesAnalysisFilters {
  locationId: string;
  from: string;
  to: string;
  groupBy?: SalesAnalysisGroupBy;
  departmentId?: string;
  productId?: string;
  customerId?: string;
  includeVoided?: boolean;
  top?: number;
  sortBy?: string;
}

export interface SalesAnalysisRow {
  groupKey: string;
  groupLabel: string;
  quantity: number;
  netSales: number;
  discountTotal: number;
  taxTotal: number;
  /** Null when the caller lacks cost visibility — the server omits it, the client does not hide it. */
  cogs: number | null;
  grossMargin: number | null;
  grossMarginPct: number | null;
  transactionCount: number;
}

export interface SalesAnalysisResult {
  rows: SalesAnalysisRow[];
  grandQuantity: number;
  grandNetSales: number;
  grandCogs: number | null;
  grandGrossMargin: number | null;
}

export interface TaxReportRow {
  taxName: string;
  rate: number;
  taxableBase: number;
  taxCollected: number;
  transactionCount: number;
}

export interface TaxReportResult {
  rows: TaxReportRow[];
  totalTaxCollected: number;
  totalNetSales: number;
  registrationNumber: string | null;
}

export interface StockValuationRow {
  departmentId: string | null;
  departmentName: string;
  productCount: number;
  unitsOnHand: number;
  costValue: number;
  retailValue: number;
  potentialMargin: number;
}

export interface StockValuationResult {
  rows: StockValuationRow[];
  totalUnits: number;
  totalCostValue: number;
  totalRetailValue: number;
}

export interface StockValuationDetailRow {
  productId: string;
  stockCode: string;
  name: string;
  departmentName: string;
  onHand: number;
  avgCost: number;
  extendedCost: number;
  regularPrice: number;
  extendedRetail: number;
}

export interface StockValuationDetailPage {
  rows: StockValuationDetailRow[];
  totalCount: number;
}

export type StockPositionKind = 'Normal' | 'Understock' | 'Overstock';

export interface StockPositionRow {
  productId: string;
  stockCode: string;
  name: string;
  departmentName: string;
  onHand: number;
  onOrder: number;
  reorderPoint: number;
  baseStock: number;
  avgWeeklySales: number;
  weeksOfSupply: number;
  position: StockPositionKind;
}

export interface OnOrderRow {
  productId: string;
  stockCode: string;
  name: string;
  supplierName: string;
  poNumber: number;
  orderQty: number;
  qtyReceived: number;
  qtyOutstanding: number;
  costEach: number;
  expectedValue: number;
  postedOn: string | null;
  dueOn: string | null;
}

export interface StockReceivedRow {
  occurredAt: string;
  poNumber: number | null;
  supplierName: string;
  stockCode: string;
  productName: string;
  qtyReceived: number;
  unitCost: number;
  extendedCost: number;
  receiptFreightTotal: number;
}

export interface StockReceivedPage {
  rows: StockReceivedRow[];
  totalCount: number;
  totalCost: number;
}

export interface RewardPointsRow {
  customerId: string;
  customerName: string;
  earned: number;
  redeemed: number;
  adjusted: number;
  netChange: number;
  currentBalance: number;
}

export interface RewardPointsResult {
  rows: RewardPointsRow[];
  totalEarned: number;
  totalRedeemed: number;
}

// ---------------------------------------------------------------------------------------------
// Accounting sync (doc 09 §1)
// ---------------------------------------------------------------------------------------------

export type SyncEntityName = 'Customers' | 'Items' | 'Vendors' | 'Invoices' | 'PosRevenue' | 'Bill';

export interface SyncRunOptions {
  businessDate?: string;
  purchaseOrderId?: string;
  dueOn?: string;
}

export interface SyncRunResult {
  success: boolean;
  recordCount: number;
  error: string | null;
  output: string | null;
}

export interface PreflightCheck {
  requirement: string;
  satisfied: boolean;
  detail: string;
}

export interface PreflightReport {
  checks: PreflightCheck[];
  ready: boolean;
}

export interface SyncLogRow {
  id: string;
  provider: string;
  direction: 'Push' | 'Pull';
  entity: string;
  status: 'Success' | 'Failed';
  recordCount: number;
  errorMessage: string | null;
  occurredAt: string;
  durationMs: number;
}

export interface SyncLogPage {
  rows: SyncLogRow[];
  totalCount: number;
}

/** The full attempt — the modern "Last QB Request / Last QB Response" (guide p.111). */
export interface SyncLogDetail extends SyncLogRow {
  requestPayload: string | null;
  responsePayload: string | null;
}

export interface ExternalMapRow {
  id: string;
  entityType: string;
  localId: string | null;
  localKey: string | null;
  remoteId: string;
  remoteName: string | null;
  lastSyncedAt: string | null;
}

/* ---------------------------------------------------------------------------------------------
 * Printable documents (guide App. L)
 * ------------------------------------------------------------------------------------------- */

/** The label stocks the server knows how to lay out. Names match the backend enum exactly. */
export type LabelStock = 'Avery5160' | 'Avery8160' | 'Avery8163' | 'S644N';

/** One stock, as the picker shows it — the server owns the description on the box. */
export interface LabelStockOption {
  value: LabelStock;
  label: string;
}

export interface LabelRequestLine {
  productId: string;
  copies: number;
}

export interface PrintLabelsRequest {
  locationId: string;
  lines: LabelRequestLine[];
  stock: LabelStock;
  showBarcode: boolean;
  /** Labels already peeled off a part-used sheet, so printing resumes rather than wasting them. */
  skipLabels: number;
}

/* ---------------------------------------------------------------------------------------------
 * Bulk operations (guide p.20–22, p.45)
 * ------------------------------------------------------------------------------------------- */

export type BulkPriceTarget = 'RegularPrice' | 'LastCost';
export type BulkAdjustMethod = 'Percentage' | 'FixedAmount' | 'SetTo' | 'MarkupOnCost';
export type PriceRounding = 'None' | 'NearestCent' | 'EndsIn99' | 'EndsIn95' | 'WholeNumber';

/** Which items a batch operation touches. Everything null means every item at the location. */
export interface BulkFilter {
  locationId: string;
  departmentId?: string | null;
  categoryId?: string | null;
  supplierId?: string | null;
  search?: string | null;
  type?: ProductType | null;
}

export interface BulkPricePreviewRow {
  productId: string;
  stockCode: string;
  name: string;
  current: number;
  proposed: number;
  avgCost: number;
  proposedMarginPct: number;
}

export interface BulkPricePreview {
  rows: BulkPricePreviewRow[];
  /** How many items the filter matches — not how many rows came back. */
  matchedCount: number;
  shownCount: number;
  wouldGoNegative: number;
}

export type TransferStatus = 'Draft' | 'InTransit' | 'Received' | 'Cancelled';

export interface TransferLine {
  id: string;
  productId: string;
  stockCode: string;
  productName: string;
  quantity: number;
  quantityReceived: number;
  outstanding: number;
  unitCost: number;
  sourceOnHand: number;
}

export interface Transfer {
  id: string;
  transferNumber: number;
  fromLocationId: string;
  fromLocationName: string;
  toLocationId: string;
  toLocationName: string;
  status: TransferStatus;
  notes: string | null;
  shippedAt: string | null;
  receivedAt: string | null;
  totalValue: number;
  lines: TransferLine[];
}

export interface TransferRow {
  id: string;
  transferNumber: number;
  fromLocationName: string;
  toLocationName: string;
  status: TransferStatus;
  lineCount: number;
  totalValue: number;
  shippedAt: string | null;
  createdAt: string;
}

export type StockCountStatus = 'InProgress' | 'Posted' | 'Cancelled';

export interface StockCountLine {
  id: string;
  productId: string;
  stockCode: string;
  productName: string;
  countedQty: number;
  systemQtyAtCount: number;
  variance: number;
  unitCost: number;
  varianceValue: number;
  notes: string | null;
}

export interface StockCount {
  id: string;
  countNumber: number;
  locationId: string;
  departmentId: string | null;
  departmentName: string | null;
  status: StockCountStatus;
  notes: string | null;
  postedAt: string | null;
  createdAt: string;
  /** Totals describe the whole count, not the filtered view of it. */
  lineCount: number;
  varianceCount: number;
  netVarianceValue: number;
  lines: StockCountLine[];
}

export interface StockCountRow {
  id: string;
  countNumber: number;
  status: StockCountStatus;
  departmentName: string | null;
  lineCount: number;
  varianceCount: number;
  netVarianceValue: number;
  postedAt: string | null;
  createdAt: string;
}

/** What an import did — the skipped list is what makes a bad file diagnosable. */
export interface CountImportResult {
  imported: number;
  updated: number;
  skipped: string[];
}

export interface TransferDestination {
  id: string;
  code: string;
  name: string;
}

/* ---------------------------------------------------------------------------------------------
 * Staff, the time clock and commissions (guide p.33, p.75–76)
 * ------------------------------------------------------------------------------------------- */

export type CommissionType = 'Percentage' | 'Fixed' | 'PercentOfProfit';

export interface StaffRow {
  id: string;
  staffCode: string;
  fullName: string;
  /** Legacy 0–4. Level 0 is the trainee preset, whose sales are practice. */
  accessLevel: number;
  isActive: boolean;
  isClockedIn: boolean;
  clockedInAt: string | null;
}

export interface TimeClockState {
  entryId: string | null;
  staffId: string;
  staffName: string;
  isClockedIn: boolean;
  clockedInAt: string | null;
  /** Hours on the shift currently running. Zero when clocked out. */
  hoursSoFar: number;
  /** Today's total, including the shift still running. */
  hoursToday: number;
}

export interface TimeClockEntry {
  id: string;
  staffId: string;
  staffName: string;
  clockIn: string;
  clockOut: string | null;
  hoursWorked: number | null;
}

export interface CommissionRule {
  id: string;
  staffId: string;
  productId: string | null;
  productName: string | null;
  departmentId: string | null;
  departmentName: string | null;
  commissionType: CommissionType;
  value: number;
  maxCommission: number | null;
  isActive: boolean;
}

export interface HoursRow {
  staffId: string;
  staffCode: string;
  staffName: string;
  shifts: number;
  hoursWorked: number;
  /** Shifts with no clock-out. Their hours are deliberately not counted. */
  openShifts: number;
  firstIn: string | null;
  lastOut: string | null;
}

export interface HoursReportResult {
  rows: HoursRow[];
  totalHours: number;
  totalShifts: number;
  totalOpenShifts: number;
}

export interface CommissionRow {
  staffId: string;
  staffCode: string;
  staffName: string;
  lines: number;
  salesNet: number;
  commission: number;
  cappedLines: number;
}

export interface CommissionDetailRow {
  transactionId: string;
  transactionNumber: number;
  businessDate: string;
  stockCode: string;
  quantity: number;
  lineNet: number;
  commissionType: CommissionType;
  rateApplied: number;
  amount: number;
  wasCapped: boolean;
}

export interface CommissionReportResult {
  rows: CommissionRow[];
  detail: CommissionDetailRow[];
  totalCommission: number;
  totalSalesNet: number;
}

/* ---------------------------------------------------------------------------------------------
 * Fiscal years and the year-end close (guide p.29)
 * ------------------------------------------------------------------------------------------- */

export type FiscalYearStatus = 'Open' | 'Closed';

export interface FiscalYear {
  id: string;
  locationId: string;
  year: number;
  startsOn: string;
  endsOn: string;
  status: FiscalYearStatus;
  closedAt: string | null;
  archivedRows: number;
  archivedNetSales: number;
  notes: string | null;
}

/** What a close would do, or did — the same shape either way so the two are comparable. */
export interface FiscalYearCloseResult {
  year: number;
  wasDryRun: boolean;
  archiveRows: number;
  productsCheckpointed: number;
  netSales: number;
  costOfGoodsSold: number;
  grossMargin: number;
  transactionsCovered: number;
  warnings: string[];
}

export interface ArchiveRow {
  year: number;
  month: number;
  stockCode: string;
  name: string;
  quantitySold: number;
  netSales: number;
  costOfGoodsSold: number;
  grossMargin: number;
  transactionCount: number;
}

/* ---------------------------------------------------------------------------------------------
 * Legacy migration (doc 09 §3)
 * ------------------------------------------------------------------------------------------- */

export type MigrationStage = 'Staged' | 'Validated' | 'DryRun' | 'Imported' | 'Cancelled';
export type FindingSeverity = 'Warning' | 'Blocking';

/** One legacy file type, with the field order the guide documents for it. */
export interface LegacySourceKind {
  entity: string;
  displayName: string;
  guideReference: string;
  columns: string[];
  requiresBase64: boolean;
}

export interface MigrationBatch {
  id: string;
  sourceFileName: string;
  entity: string;
  sourceHash: string;
  stage: MigrationStage;
  rowsStaged: number;
  rowsDeletedInSource: number;
  blockingErrors: number;
  warnings: number;
  rowsImported: number;
  rowsSkipped: number;
  /** Only true after a dry run that found nothing blocking. */
  canImport: boolean;
  validatedAt: string | null;
  dryRunAt: string | null;
  importedAt: string | null;
  createdAt: string;
}

export interface ColumnProfile {
  name: string;
  populated: number;
  empty: number;
  distinctValues: number;
  shortestValue: string | null;
  longestValue: string | null;
  samples: string[];
}

export interface AnalysisReport {
  sourceFileName: string;
  format: string;
  detectedLayout: string;
  guideReference: string;
  rowCount: number;
  deletedRowCount: number;
  columnCount: number;
  columns: ColumnProfile[];
  notes: string[];
}

/** Every finding names its row and column, so nobody has to count lines in Notepad. */
export interface ValidationFinding {
  rowNumber: number;
  column: string | null;
  severity: FindingSeverity;
  code: string;
  message: string;
  value: string | null;
}

export interface StagingRow {
  rowNumber: number;
  legacyKey: string | null;
  isDeletedInSource: boolean;
  isValid: boolean | null;
  problems: string | null;
  outcome: string | null;
  values: Record<string, string | null>;
}

export interface ReconciliationLine {
  measure: string;
  imported: number;
  /** What the old system's own report said. Null when nothing was given to compare against. */
  legacyReported: number | null;
  variance: number | null;
  matches: boolean;
}

export interface ReconciliationReport {
  entity: string;
  rowsConsidered: number;
  rowsWouldImport: number;
  rowsWouldSkip: number;
  lines: ReconciliationLine[];
  warnings: string[];
}

/** Typed in off the old system's printout — there is no way to derive these. */
export interface LegacyControlTotals {
  itemCount?: number | null;
  inventoryValue?: number | null;
  receivablesBalance?: number | null;
  yearToDateSales?: number | null;
  customerCount?: number | null;
  supplierCount?: number | null;
}
