export type ProductType = 'Standard' | 'Combo' | 'GiftCertificate' | 'Voucher' | 'Deposit' | 'NonMerchandise';
export type ProductStatus = 'Active' | 'Deleted' | 'FutureDated' | 'PastDated';
export type LineType = 'Product' | 'Comment' | 'Discount' | 'Subtotal' | 'Savings' | 'Tax';
export type TransactionType = 'Sale' | 'Return' | 'Quote' | 'WorkOrder' | 'Deposit';
export type PaymentMethod = 'Cash' | 'Card' | 'GiftCertificate' | 'Credit' | 'Voucher' | 'Cheque' | 'StoreCredit';
export type TransactionStatus = 'Pending' | 'Completed' | 'Voided' | 'OnHold';
export type StockMovementType = 'OpeningBalance' | 'Receipt' | 'Transfer' | 'Adjustment' | 'Sale' | 'Return' | 'Shrinkage' | 'Found';
export type CountStatus = 'Open' | 'InProgress' | 'Recount' | 'Completed' | 'Cancelled';

export interface Product {
  id: number;
  stockCode: string;
  name: string;
  type: ProductType;
  departmentId?: number;
  regularPrice: number;
  lastCost: number;
  avgCost: number;
  tax1Applies: boolean;
  tax2Applies: boolean;
  upc?: string;
  binLocation?: string;
  description?: string;
  notes?: string;
  locationId: number;
}

export interface Customer {
  id: number;
  customerNumber: number;
  firstName: string;
  lastName: string;
  company?: string;
  email?: string;
  phone?: string;
  mobile?: string;
  addressLine1?: string;
  addressLine2?: string;
  city?: string;
  state?: string;
  postcode?: string;
  creditLimit?: number;
  balance?: number;
  isActive: boolean;
  locationId: number;
}

export interface CartLine {
  id: number;
  lineType: LineType;
  lineNumber: number;
  identifier?: string;
  description: string;
  originalPrice: number;
  sellingPrice: number;
  quantity: number;
  discountAmount: number;
  taxAmount: number;
  taxRate?: number;
  linkedLineNumber?: number;
}

export interface Cart {
  id: number;
  locationId: number;
  terminalId: number;
  staffId: number;
  customerId?: number;
  customerName?: string;
  transactionType: TransactionType;
  status: string;
  lines: CartLine[];
  subtotal: number;
  totalDiscount: number;
  tax1Total: number;
  tax2Total: number;
  grandTotal: number;
  itemCount: number;
}

export interface PaymentRecord {
  id: number;
  method: PaymentMethod;
  amount: number;
  reference?: string;
  receivedAt: string;
}

export interface SalesTransaction {
  id: number;
  transactionNumber: number;
  transactionType: TransactionType;
  locationId: number;
  terminalId: number;
  staffId: number;
  customerId?: number;
  customerName?: string;
  transactionDate: string;
  status: TransactionStatus;
  subtotal: number;
  discountAmount: number;
  tax1Total: number;
  tax2Total: number;
  grandTotal: number;
  amountTendered?: number;
  change?: number;
}

export interface StockLevel {
  id: number;
  productId: number;
  productName: string;
  locationId: number;
  onHand: number;
  reserved: number;
  available: number;
  reorderPoint: number;
  reorderQuantity: number;
}

export interface Department {
  id: number;
  code: string;
  name: string;
  parentDepartmentId?: number;
}

export interface Supplier {
  id: number;
  code: string;
  name: string;
  contactName?: string;
  phone?: string;
  email?: string;
}

export interface Location {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
}

export interface User {
  id: number;
  email: string;
  firstName?: string;
  lastName?: string;
}

export interface PaginatedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
