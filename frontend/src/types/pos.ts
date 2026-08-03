/**
 * Server contracts for the till. These mirror the DTOs in Retail25.Application.Carts.Dtos; enums
 * cross the wire as names so the client never depends on the server's numbering.
 */

export type CartStatus = 'Active' | 'Suspended' | 'Completed' | 'Voided';

export type LineType = 'Sale' | 'Return' | 'TradeIn';

export type LineSource =
  | 'Rfid'
  | 'Barcode'
  | 'StockCode'
  | 'Manual'
  | 'Unknown'
  | 'KitComponent'
  | 'RandomWeight'
  | 'Serial'
  | 'Variant'
  | 'TagAlong';

/** Why a line rang at the price it did (doc 04 §2). Badged on the cart list. */
export type PriceOrigin =
  | 'Regular'
  | 'Level2'
  | 'Level3'
  | 'Level4'
  | 'Break'
  | 'Sale'
  | 'Bonus'
  | 'Manual'
  | 'RandomWeight'
  | 'ClientLevel';

export type AdjustmentType =
  | 'SubtotalDiscount'
  | 'Coupon'
  | 'BottleReturn'
  | 'GiftCertificate'
  | 'LoyaltyReward';

export interface CartLine {
  id: string;
  sequence: number;
  productId: string;
  variantId: string | null;
  stockCode: string;
  name: string;
  variantLabel: string | null;
  epc: string | null;
  serialNumber: string | null;
  source: LineSource;
  lineType: LineType;
  quantity: number;
  chargeableQuantity: number;
  unitPrice: number;
  priceOrigin: PriceOrigin;
  discountPct: number;
  extendedNet: number;
  tax1Applies: boolean;
  tax2Applies: boolean;
  tax1Amount: number;
  tax2Amount: number;
  requestedPriceLevel: number | null;
  hasManualPrice: boolean;
  note: string | null;
}

export interface CartAdjustment {
  id: string;
  type: AdjustmentType;
  label: string;
  amount: number;
  serial: string | null;
}

/** Tax names come from configuration, so a Canadian and a UK store label these differently. */
export interface CartTotals {
  subtotal: number;
  discountTotal: number;
  tax1Name: string;
  tax1Total: number;
  tax2Name: string;
  tax2Total: number;
  addOnChargeName: string;
  addOnCharge: number;
  grandTotal: number;
  taxInclusive: boolean;
  loyaltyPointsEarned: number;
  loyaltyPointsRedeemed: number;
  itemCount: number;
}

export interface CartCustomer {
  id: string;
  customerNumber: number;
  name: string;
  priceLevel: number;
  usualDiscountPct: number;
  exemptTax1: boolean;
  exemptTax2: boolean;
  rewardPoints: number;
  accountBalance: number;
  creditLimit: number;
}

export interface Cart {
  id: string;
  stationId: string;
  locationId: string;
  staffId: string;
  status: CartStatus;
  revision: number;
  heldName: string | null;
  customer: CartCustomer | null;
  lines: CartLine[];
  adjustments: CartAdjustment[];
  totals: CartTotals;
  taxOverride1: boolean | null;
  taxOverride2: boolean | null;
}

/** The station's effective behaviour after per-station overrides are folded over store policy. */
export interface StationPolicy {
  stationId: string;
  stationCode: string;
  fastScanMode: boolean;
  autoSaveSales: boolean;
  confirmBeforeSaving: boolean;
  scanRandomWeightBarcodes: boolean;
  allowTaxOverride: boolean;
  staffMayDiscount: boolean;
  allowItemListEdit: boolean;
  requireSupervisorToVoid: boolean;
  defaultTenderTypeId: string | null;
  minimumTender: number;
  currencyCode: string;
  currencySymbol: string;
}

export interface SuspendedCart {
  id: string;
  label: string | null;
  staffId: string;
  customerName: string | null;
  lineCount: number;
  grandTotal: number;
  suspendedAt: string;
}

export interface DrawerTenderTotal {
  tenderName: string;
  amount: number;
  count: number;
}

export interface DrawerTotals {
  sessionId: string;
  stationId: string;
  status: 'Open' | 'Closed';
  businessDate: string;
  openedAt: string;
  closedAt: string | null;
  openingFloat: number;
  cashSales: number;
  cashRefunds: number;
  payIns: number;
  payOuts: number;
  expectedCash: number;
  countedCash: number | null;
  variance: number | null;
  netSales: number;
  tax1Collected: number;
  tax2Collected: number;
  costOfGoodsSold: number;
  transactionCount: number;
  tenderTotals: DrawerTenderTotal[];
}

export interface TenderRequest {
  tenderTypeId: string;
  amount: number;
  amountTendered?: number;
  reference?: string | null;
  cardToken?: string | null;
}

export interface CompleteSaleResult {
  transactionId: string;
  transactionNumber: number;
  grandTotal: number;
  changeGiven: number;
  roundingAdjustment: number;
  loyaltyPointsEarned: number;
  invoiceId: string | null;
}

export interface TenderType {
  id: string;
  code: string;
  displayName: string;
  behaviour: 'Cash' | 'Card' | 'GiftCard' | 'GiftCertificate' | 'OnAccount' | 'Manual';
  sortOrder: number;
  opensCashDrawer: boolean;
  allowsOverTender: boolean;
  roundsToMinimumTender: boolean;
  requiresReference: boolean;
  isActive: boolean;
}

/** One cell of a matrix item's grid (guide p.39–40). */
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

export type SerializedUnitState =
  | 'Provisioned'
  | 'InStock'
  | 'InCart'
  | 'Reserved'
  | 'Sold'
  | 'Returned'
  | 'Transferred'
  | 'Void'
  | 'Lost';

/** One physical unit: a serial number, an EPC, or both (doc 06 §1). */
export interface SerializedUnit {
  id: string;
  serialNumber: string | null;
  epc: string | null;
  state: SerializedUnitState;
  variantId: string | null;
  variantLabel: string | null;
  receivedOn: string;
  lastSeenAt: string | null;
}

/** A tag the server refused, with the reason shown verbatim to the cashier (doc 06 §2). */
export interface RejectedTag {
  epc: string;
  reason: string;
  message: string;
  at: number;
}

export interface PeripheralStatus {
  readerOnline: boolean;
  printerOnline: boolean;
  scaleOnline: boolean;
  drawerOnline: boolean;
  poleDisplayOnline: boolean;
  readRate: number;
}

/** RFC 7807 with the machine-readable code the server always attaches. */
export interface ProblemDetails {
  status: number;
  title: string;
  detail: string;
  code: string;
  arguments?: Record<string, unknown>;
}

/* ------------------------------------------------------------------ product grid */

export interface PosGridItem {
  id: string;
  stockCode: string;
  name: string;
  regularPrice: number;
  onHand: number;
  type: string;
  hasImage: boolean;
  departmentId: string | null;
  categoryId: string | null;
}

export interface PosGridGroup {
  id: string;
  name: string;
  code: string | null;
  sortOrder: number;
  itemCount: number;
}

export interface PosGridPage {
  items: PosGridItem[];
  departments: PosGridGroup[];
  categories: PosGridGroup[];
  total: number;
  /**
   * Whether anything in the current filter has a picture. The grid opens as tiles when it does and
   * as rows when it does not — a screen of identical placeholders is worse than a list.
   */
  anyImages: boolean;
}
