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
  protocol: 'Llrp' | 'Http' | 'Mqtt' | 'Simulator';
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
