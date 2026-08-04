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
  id: number;
  sequence: number;
  productId: number;
  variantId: number | null;
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
  id: number;
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
  id: number;
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
  id: number;
  stationId: number;
  locationId: number;
  staffId: number;
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
  stationId: number;
  stationCode: string;
  fastScanMode: boolean;
  autoSaveSales: boolean;
  confirmBeforeSaving: boolean;
  scanRandomWeightBarcodes: boolean;
  allowTaxOverride: boolean;
  staffMayDiscount: boolean;
  allowItemListEdit: boolean;
  requireSupervisorToVoid: boolean;
  defaultTenderTypeId: number | null;
  minimumTender: number;
  currencyCode: string;
  currencySymbol: string;
}

export interface SuspendedCart {
  id: number;
  label: string | null;
  staffId: number;
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
  sessionId: number;
  stationId: number;
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
  tenderTypeId: number;
  amount: number;
  amountTendered?: number;
  reference?: string | null;
  cardToken?: string | null;
}

export interface CompleteSaleResult {
  transactionId: number;
  transactionNumber: number;
  grandTotal: number;
  changeGiven: number;
  roundingAdjustment: number;
  loyaltyPointsEarned: number;
  invoiceId: number | null;
}

export interface TenderType {
  id: number;
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
  id: number;
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
  id: number;
  serialNumber: string | null;
  epc: string | null;
  state: SerializedUnitState;
  variantId: number | null;
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
  id: number;
  stockCode: string;
  name: string;
  regularPrice: number;
  onHand: number;
  type: string;
  hasImage: boolean;
  departmentId: number | null;
  categoryId: number | null;
}

export interface PosGridGroup {
  id: number;
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
