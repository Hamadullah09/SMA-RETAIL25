/**
 * Server answers that follow docs/API.md, for the end-to-end tests and the screenshots. Shapes come from
 * src/lib/types.ts, so a contract change that breaks them fails `npm run typecheck`.
 */
import type {
  AuditRow,
  BatchInfo,
  Category,
  ClientDetail,
  ClientRow,
  CommissionRow,
  CommissionRule,
  Dashboard,
  Department,
  GiftCardDetail,
  GiftCardRow,
  HoursRow,
  ItemDetail,
  ItemEveryStorePlan,
  ItemEveryStoreResult,
  ItemRow,
  PermissionInfo,
  ReportInfo,
  ReportTable,
  RoleInfo,
  SaleDetail,
  SaleRow,
  LocationInfo,
  StaffMember,
  RfidHubInfo,
  StationInfo,
  UploadResponse,
  StockCountDetail,
  StockCountRow,
  StockMovementRow,
  StoreInfo,
  StoreSettings,
  SupplierDetail,
  SupplierRow,
  TillInfo,
  TimeClockEntry,
  TransferDetail,
  TransferRow,
  UserInfo,
  UserRow,
  ActivityRow,
  CampaignResults,
  CampaignRow,
  LeadRow,
  MyTasks,
  OpportunityBoard,
  OpportunityRow,
  OpportunityStage,
  PromotionDetail,
  PromotionReportRow,
  PromotionRow,
  SalesTargetRow,
  SegmentResult,
  SegmentRow,
  TargetProgress,
  WhatsAppMessageRow,
  WhatsAppStatus,
  WhatsAppTemplate,
  WonLostReport,
} from '../src/lib/types';
import { productionPermissions } from './production-fixtures';
import type {
  ApprovalLimitRow,
  BatchRow,
  BinList,
  BinRow,
  ExpiryReport,
  LandedCostDetail,
  LandedCostRow,
  MatchReportRow,
  MatchRow,
  PurchaseOrderDetail,
  PurchaseOrderRow,
  ReceiptChoice,
  ReorderRow,
  RequisitionDetail,
  SupplierPerformance,
  SupplySettings,
} from '../src/lib/types';

const minutesAgo = (m: number) => new Date(Date.now() - m * 60_000).toISOString().replace(/\.\d{3}Z$/, 'Z');
const today = (() => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
})();
const at = (time: string, date = today) => `${date}T${time}`;
const daysBefore = (n: number) => {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

// Permissions ---------------------------------------------------------------------------------------------------

export const permissions: PermissionInfo[] = [
  ['pos.sell', 'Sales', 'Ring up sales'],
  ['pos.discount', 'Sales', 'Give discounts and change prices at the POS'],
  ['pos.return', 'Sales', 'Take returns'],
  ['pos.void-line', 'Sales', 'Remove lines and cancel sales without a supervisor'],
  ['pos.void-sale', 'Sales', 'Void a completed sale'],
  ['pos.no-sale', 'Sales', 'Open the drawer without a sale'],
  ['pos.drawer', 'Sales', 'Open and count the drawer, pay in and pay out'],
  ['pos.end-of-day', 'Sales', 'Run End of Day'],
  ['pos.approve', 'Sales', "Approve what needs a supervisor at another user's till"],
  ['inventory.view', 'Stock', 'See items and stock'],
  ['inventory.edit', 'Stock', 'Add, change and delete items'],
  ['inventory.adjust', 'Stock', 'Adjust stock'],
  ['inventory.count', 'Stock', 'Count stock and post counts'],
  ['inventory.transfer', 'Stock', 'Send and receive transfers between stores'],
  ['inventory.prices', 'Stock', 'Change prices in bulk'],
  ['inventory.labels', 'Stock', 'Print labels'],
  ['purchasing.manage', 'Stock', 'Purchase orders and receiving'],
  ['clients.view', 'Customers', 'See clients'],
  ['clients.edit', 'Customers', 'Add and change clients'],
  ['suppliers.edit', 'Customers', 'Add and change suppliers'],
  ['invoices.manage', 'Customers', 'Invoices, quotes, layaways and their payments'],
  ['rma.manage', 'Customers', 'Returns to suppliers and from customers (RMA)'],
  ['giftcards.issue', 'Customers', 'Sell and reload gift cards'],
  ['timeclock.punch', 'Staff', 'Punch in and out'],
  ['timeclock.edit', 'Staff', 'Change time clock entries and commission rules'],
  ['dashboard.view', 'Reports', 'See the dashboard'],
  ['reports.view', 'Reports', 'Run reports'],
  ['reports.staff', 'Reports', 'Staff hours and commission reports'],
  ['setup.store', 'Back office', 'Business, taxes, POS settings, tenders, lists and texts'],
  ['setup.users', 'Back office', 'Users and roles'],
  ['setup.stations', 'Back office', "Stations and this station's settings"],
  ['rfid.manage', 'Back office', 'RFID readers and tags'],
  ['backup.manage', 'Back office', 'Back up, restore and rebuild the database'],
  ['import.run', 'Back office', 'Import Retail Plus data and CSV files'],
  ['yearend.run', 'Back office', 'Year end'],
  ['audit.view', 'Back office', 'See the audit log'],
  ['accounting.export', 'Back office', 'Export to accounting'],
  ['sync.send', 'Back office', "Send this till's data to the server"],
  ['web.signin', 'Web', 'Sign in to the web back office'],
  ['stores.manage', 'Web', 'Add stores and tills'],
  ['stores.all', 'Web', 'See and manage every store (super admin)'],
  ['hr.view', 'HR', 'See employees, shifts, attendance and leave'],
  ['hr.edit', 'HR', 'Add and change employees, HR lists, shifts, leave types, holidays and payroll rules'],
  ['hr.attendance', 'HR', 'Rosters, and attendance corrections and their approval'],
  ['hr.leave', 'HR', 'Approve leave and adjust leave balances'],
  ['payroll.run', 'HR', 'Salaries, loans, payroll runs, posting and paying them'],
  ['payroll.approve', 'HR', 'Approve payroll runs'],
  ['crm.view', 'CRM and marketing', 'See leads, opportunities and activities'],
  ['crm.edit', 'CRM and marketing', 'Change leads, opportunities and activities'],
  ['marketing.campaigns', 'CRM and marketing', 'Segments, WhatsApp templates and campaigns'],
  ['marketing.promotions', 'CRM and marketing', 'Promotions and coupons'],
  ['sales.targets', 'CRM and marketing', 'Sales targets'],
  ...productionPermissions,
].map(([key, group, description]) => ({ key, group, description }));

const everything = permissions.map((p) => p.key);

// Users -----------------------------------------------------------------------------------------------------------

export const superAdmin: UserInfo = {
  id: 'u-1',
  userName: 'sara',
  fullName: 'Sara Mensah',
  role: 'Super administrator',
  permissions: everything,
  storeId: null,
  storeName: null,
  mustChangePassword: false,
  canChangeShared: true,
};

/** A supervisor: dashboard, sales and reports, stock and clients, but no staff, users, setup or audit. */
export const supervisor: UserInfo = {
  id: 'u-2',
  userName: 'kofi',
  fullName: 'Kofi Boateng',
  role: 'Supervisor',
  permissions: ['web.signin', 'dashboard.view', 'reports.view', 'inventory.view', 'clients.view', 'giftcards.issue', 'timeclock.edit', 'sync.send'],
  storeId: 's-main',
  storeName: 'Downtown',
  mustChangePassword: false,
  canChangeShared: false,
};

/** One store's administrator in a company of several stores: everything but stores.all, so not the shared lists. */
export const storeAdmin: UserInfo = {
  id: 'u-6',
  userName: 'abena',
  fullName: 'Abena Ofori',
  role: 'Administrator',
  permissions: everything.filter((k) => k !== 'stores.all'),
  storeId: 's-main',
  storeName: 'Downtown',
  mustChangePassword: false,
  canChangeShared: false,
};

// Stores and tills ------------------------------------------------------------------------------------------------

export const stores: StoreInfo[] = [
  { id: 's-main', code: 'MAIN', name: 'Downtown', timeZoneId: 'Eastern Standard Time', currencySymbol: '$' },
  { id: 's-west', code: 'WEST', name: 'Westside Mall', timeZoneId: 'Eastern Standard Time', currencySymbol: '$' },
  { id: 's-north', code: 'NORTH', name: 'Northgate', timeZoneId: 'Central Standard Time', currencySymbol: '$' },
];

export const locations: Record<string, LocationInfo[]> = {
  's-main': [{ id: 'l-main-pos', code: 'POS', name: 'Downtown' }],
  's-west': [
    { id: 'l-west-pos', code: 'POS', name: 'Westside Mall' },
    { id: 'l-west-back', code: 'BACK', name: 'Back room' },
  ],
  's-north': [{ id: 'l-north-pos', code: 'POS', name: 'Northgate' }],
};

export const tills: TillInfo[] = [
  till('s-main', 'MAIN', 'Downtown', 1, 'Front counter', 'ok', 3, 1842.35, 61, true),
  till('s-main', 'MAIN', 'Downtown', 2, 'Back counter', 'ok', 9, 966.1, 28, true),
  till('s-main', 'MAIN', 'Downtown', 3, 'Shoe corner', 'behind', 60 * 30, 0, 0, false),
  till('s-west', 'WEST', 'Westside Mall', 11, 'Kiosk A', 'ok', 5, 1311.8, 44, true),
  till('s-west', 'WEST', 'Westside Mall', 12, 'Kiosk B', 'failed', 47, 412.5, 15, true, 2),
  till('s-north', 'NORTH', 'Northgate', 21, 'Main till', 'never', null, 0, 0, false),
  till('s-north', 'NORTH', 'Northgate', 22, 'Old till', 'retired', 60 * 24 * 40, 0, 0, false),
];

function till(
  storeId: string,
  storeCode: string,
  storeName: string,
  number: number,
  name: string,
  status: TillInfo['status'],
  uploadMinutesAgo: number | null,
  todayNetSales: number,
  todayTransactions: number,
  drawerOpen: boolean,
  failedBatches = 0,
): TillInfo {
  return {
    stationId: `st-${number}`,
    storeId,
    storeCode,
    storeName,
    number,
    name,
    enrolled: status !== 'never',
    retired: status === 'retired',
    appVersion: status === 'never' ? null : number === 22 ? '1.0.4' : '1.2.0',
    lastUploadAtUtc: uploadMinutesAgo === null ? null : minutesAgo(uploadMinutesAgo),
    lastDownloadAtUtc: uploadMinutesAgo === null ? null : minutesAgo(uploadMinutesAgo + 2),
    lastSaleAt: todayTransactions > 0 ? at('14:52:10') : null,
    todayNetSales,
    todayTransactions,
    drawerOpen,
    failedBatches,
    status,
  };
}

export function station(number: number, storeId = 's-main'): StationInfo {
  return {
    id: `st-${number}`,
    storeId,
    number,
    name: `Station ${String(number).padStart(3, '0')}`,
    enrolled: false,
    enrolledAtUtc: null,
    retired: false,
    enrollmentExpiresAtUtc: new Date(Date.now() + 24 * 3600_000).toISOString(),
    lastUploadAtUtc: null,
    lastDownloadAtUtc: null,
    appVersion: null,
    lastBatch: null,
    failedBatches: 0,
    behind: false,
  };
}

/** Downtown's RFID hub keys: one in use, one revoked. */
export const rfidHubs: RfidHubInfo[] = [
  {
    id: 'h-1', storeId: 's-main', name: 'Back office PC', keyHint: 'rfh_7Qx2', createdAtUtc: minutesAgo(60 * 24 * 12), createdBy: 'sara',
    lastSeenAtUtc: minutesAgo(1), lastAddress: '192.168.1.20', revoked: false, revokedAtUtc: null,
  },
  {
    id: 'h-0', storeId: 's-main', name: 'Old till PC', keyHint: 'rfh_K9pa', createdAtUtc: minutesAgo(60 * 24 * 90), createdBy: 'sara',
    lastSeenAtUtc: minutesAgo(60 * 24 * 31), lastAddress: '192.168.1.14', revoked: true, revokedAtUtc: minutesAgo(60 * 24 * 30),
  },
];

/** The key the mocked server makes: shown once, never in a list. */
export const newHubKey = 'rfh_N3wK3yForTheBackRoomHub0123456789abcdefghij';

export const stations: Record<string, StationInfo[]> = {
  's-main': [1, 2, 3].map((n) => ({ ...station(n), enrolled: true, enrollmentExpiresAtUtc: null, enrolledAtUtc: minutesAgo(60 * 24 * 30) })),
  's-west': [11, 12].map((n) => ({ ...station(n, 's-west'), enrolled: true, enrollmentExpiresAtUtc: null })),
  's-north': [21].map((n) => station(n, 's-north')),
};

export const batches: BatchInfo[] = [
  batch('b-1', 's-main', 1, 'Applied', 3, 'MAIN-001-eod.smapkg', 412),
  batch('b-2', 's-west', 11, 'Applied', 5, 'WEST-011-auto.smapkg', 88),
  batch('b-3', 's-west', 12, 'Failed', 47, 'WEST-012-usb.smapkg', 0, 'The package was signed with another key. Enrol the till again.'),
  batch('b-4', 's-main', 2, 'Duplicate', 120, 'MAIN-002-eod.smapkg', 0),
  batch('b-5', 's-main', 2, 'Applied', 9, 'MAIN-002-auto.smapkg', 57),
];

function batch(id: string, storeId: string, stationNumber: number, status: BatchInfo['status'], ago: number, fileName: string, rows: number, error: string | null = null): BatchInfo {
  return {
    id,
    batchId: `${id}-guid`,
    storeId,
    stationNumber,
    direction: 'Upload',
    status,
    fileName,
    rows,
    receivedAtUtc: minutesAgo(ago),
    appliedAtUtc: status === 'Applied' ? minutesAgo(ago) : null,
    appVersion: '1.2.0',
    error,
    notes: null,
  };
}

// Dashboard -------------------------------------------------------------------------------------------------------

const hourly = [0, 0, 0, 0, 0, 0, 0, 0, 0, 210.5, 388.2, 612.9, 845.4, 701.25, 655.1, 590.0, 480.75, 395.2, 310.4, 220.0, 95.0, 0, 0, 0];

export const dashboard: Dashboard = {
  from: today,
  to: today,
  totals: { netSales: 5532.75, tax: 719.26, total: 6252.01, transactions: 148, averageSale: 37.38, units: 391, returns: 84.5, discounts: 142.3, grossProfit: 2318.4, voids: 2 },
  previous: { netSales: 4987.1, tax: 648.32, total: 5635.42, transactions: 152, averageSale: 32.81, units: 402.5, returns: 40, discounts: 120, grossProfit: 2011.0, voids: 1 },
  byStore: [
    { storeId: 's-main', code: 'MAIN', name: 'Downtown', netSales: 2808.45, transactions: 89, averageSale: 31.56, lastUploadAtUtc: minutesAgo(3) },
    { storeId: 's-west', code: 'WEST', name: 'Westside Mall', netSales: 1724.3, transactions: 59, averageSale: 29.23, lastUploadAtUtc: minutesAgo(5) },
    { storeId: 's-north', code: 'NORTH', name: 'Northgate', netSales: 1000, transactions: 0, averageSale: 0, lastUploadAtUtc: null },
  ],
  byDay: Array.from({ length: 7 }, (_, i) => ({ date: daysBefore(6 - i), netSales: [4120.5, 3880.2, 4511.75, 5020.0, 6210.4, 4987.1, 5532.75][i], transactions: [120, 111, 130, 141, 170, 152, 148][i] })),
  byHour: hourly.map((netSales, hour) => ({ hour, netSales, transactions: Math.round(netSales / 37) })),
  paymentMix: [
    { tender: 'Cash', amount: 2410.2, count: 71 },
    { tender: 'Visa', amount: 1890.55, count: 44 },
    { tender: 'Debit', amount: 1201.3, count: 27 },
    { tender: 'Gift card', amount: 249.96, count: 6 },
  ],
  topItems: [
    { itemId: 'i-1', plu: 'TS-100-M', name: 'Cotton T-shirt, medium', units: 42, netSales: 754.0 },
    { itemId: 'i-2', plu: 'JN-220-32', name: 'Slim jeans 32', units: 11, netSales: 659.89 },
    { itemId: 'i-3', plu: 'SN-410', name: 'Canvas sneakers', units: 8, netSales: 519.92 },
    { itemId: 'i-4', plu: 'HD-090-L', name: 'Hoodie, large', units: 9, netSales: 449.91 },
    { itemId: 'i-5', plu: 'SK-001', name: 'Sport socks, 3 pack', units: 37, netSales: 369.63 },
    { itemId: 'i-6', plu: 'CP-012', name: 'Baseball cap', units: 14, netSales: 279.86 },
  ],
  byCashier: [
    { userId: 'u-3', userName: 'ama', netSales: 2210.4, transactions: 61 },
    { userId: 'u-4', userName: 'yaw', netSales: 1795.0, transactions: 50 },
    { userId: 'u-5', userName: 'efua', netSales: 1527.35, transactions: 37 },
  ],
  byStation: [
    { storeId: 's-main', storeCode: 'MAIN', stationNumber: 1, netSales: 1842.35, transactions: 61 },
    { storeId: 's-west', storeCode: 'WEST', stationNumber: 11, netSales: 1311.8, transactions: 44 },
    { storeId: 's-main', storeCode: 'MAIN', stationNumber: 2, netSales: 966.1, transactions: 28 },
    { storeId: 's-west', storeCode: 'WEST', stationNumber: 12, netSales: 412.5, transactions: 15 },
  ],
  drawers: [
    // Tills record the cash expected but no counted amount (docs/API.md, "How the server applies this contract").
    { storeCode: 'MAIN', stationNumber: 1, closedAt: at('21:04:00', daysBefore(1)), closedBy: 'ama', expected: 812.4, counted: null, variance: null },
    { storeCode: 'WEST', stationNumber: 11, closedAt: at('21:10:00', daysBefore(1)), closedBy: 'yaw', expected: 640.0, counted: 635.0, variance: -5 },
  ],
};

// Sales -----------------------------------------------------------------------------------------------------------

const names = ['Akua Owusu', null, 'Daniel Kim', null, 'Grace Adjei', null, null, 'Marc Dubois'];
export const sales: SaleRow[] = Array.from({ length: 23 }, (_, i) => {
  const net = Math.round((18 + ((i * 37) % 140) + (i % 3) * 0.45) * 100) / 100;
  const tax = Math.round(net * 0.13 * 100) / 100;
  return {
    id: `sale-${i + 1}`,
    storeCode: i % 3 === 2 ? 'WEST' : 'MAIN',
    stationNumber: i % 3 === 2 ? 11 : 1 + (i % 2),
    invoiceNumber: 10000001 + 40 - i,
    at: at(`${String(18 - Math.floor(i / 2)).padStart(2, '0')}:${String((i * 7) % 60).padStart(2, '0')}:00`),
    staff: ['ama', 'yaw', 'efua'][i % 3],
    client: names[i % names.length],
    units: 1 + (i % 4),
    netSales: net,
    tax,
    total: Math.round((net + tax) * 100) / 100,
    tenders: ['Cash', 'Visa', 'Debit', 'Cash, Gift card'][i % 4],
    voided: i === 4,
  };
});

export function saleDetail(id: string): SaleDetail {
  const row = sales.find((s) => s.id === id) ?? sales[0];
  return {
    ...row,
    id,
    invoiceNumber: 10000041,
    netSales: 69.47,
    tax: 9.03,
    total: 78.5,
    units: 3,
    client: 'Akua Owusu',
    tenders: 'Cash, Visa',
    lines: [
      { lineNumber: 1, plu: 'TS-100-M', description: 'Cotton T-shirt, medium', quantity: 2, price: 17.95, discount: 0, tax: 4.67, total: 35.9 },
      { lineNumber: 2, plu: 'CP-012', description: 'Baseball cap', quantity: 1, price: 19.99, discount: 2.0, tax: 2.34, total: 17.99 },
      { lineNumber: 3, plu: 'SK-001', description: 'Sport socks, 3 pack', quantity: 1, price: 9.99, discount: 0, tax: 1.3, total: 9.99 },
      { lineNumber: 4, plu: null, description: 'Gift wrap', quantity: 1, price: 5.59, discount: 0, tax: 0.72, total: 5.59 },
    ],
    payments: [
      { tender: 'Cash', amount: 40, currency: null, foreignAmount: null },
      { tender: 'Visa', amount: 38.5, currency: null, foreignAmount: null },
    ],
    voided: false,
    voidedBy: null,
    voidedAt: null,
  };
}

// Items -----------------------------------------------------------------------------------------------------------

export const departments: Department[] = [
  { id: 'd-1', name: 'Clothing' },
  { id: 'd-2', name: 'Footwear' },
  { id: 'd-3', name: 'Accessories' },
];

export const categories: Category[] = [
  { id: 'c-1', name: 'Tops', departmentId: 'd-1' },
  { id: 'c-2', name: 'Bottoms', departmentId: 'd-1' },
  { id: 'c-3', name: 'Sneakers', departmentId: 'd-2' },
  { id: 'c-4', name: 'Hats', departmentId: 'd-3' },
];

/** One row per location item: TS-100-M is stocked in two stores. */
export const items: ItemRow[] = [
  ['i-1', 'TS-100-M', 'Cotton T-shirt, medium', 'Clothing', 'Tops', 17.95, 6.2, 84, 'MAIN'],
  ['i-1w', 'TS-100-M', 'Cotton T-shirt, medium', 'Clothing', 'Tops', 18.5, 6.2, 40, 'WEST'],
  ['i-2', 'JN-220-32', 'Slim jeans 32', 'Clothing', 'Bottoms', 59.99, 24.0, 31, 'MAIN'],
  ['i-3', 'SN-410', 'Canvas sneakers', 'Footwear', 'Sneakers', 64.99, 28.5, 18, 'MAIN'],
  ['i-4', 'HD-090-L', 'Hoodie, large', 'Clothing', 'Tops', 49.99, 19.75, 7, 'WEST'],
  ['i-5', 'SK-001', 'Sport socks, 3 pack', 'Accessories', null, 9.99, 3.1, 260, 'MAIN'],
  ['i-6', 'CP-012', 'Baseball cap', 'Accessories', 'Hats', 19.99, 7.4, -2, 'WEST'],
  ['i-7', 'BL-300', 'Leather belt', 'Accessories', null, 34.5, 12.0, 0, 'MAIN'],
].map(([id, plu, name, department, category, price1, averageCost, onHand, storeCode]) => ({
  id: id as string,
  plu: plu as string,
  name: name as string,
  storeCode: storeCode as string,
  locationCode: 'POS',
  department: department as string,
  category: category as string | null,
  price1: price1 as number,
  averageCost: averageCost as number,
  onHand: onHand as number,
  isActive: id !== 'i-7',
}));

export function itemDetail(id: string): ItemDetail {
  const row = items.find((i) => i.id === id) ?? items[0];
  return {
    id: row.id,
    version: 'v-1',
    plu: row.plu,
    barcode: '036000291452',
    name: row.name,
    description: 'Soft ring-spun cotton, regular fit.',
    departmentId: 'd-1',
    categoryId: 'c-1',
    supplierId: 'sp-1',
    price1: row.price1,
    price2: 16.95,
    price3: 0,
    lastCost: 6.4,
    averageCost: row.averageCost,
    taxable1: true,
    taxable2: false,
    taxable3: false,
    reorderPoint: 20,
    reorderQuantity: 60,
    unitsPerCase: 12,
    isActive: true,
    notes: null,
    stock: [
      { storeCode: 'MAIN', locationCode: 'POS', onHand: 84 },
      { storeCode: 'WEST', locationCode: 'POS', onHand: 40 },
    ],
  };
}

/** Where a change at every store goes: TS-100-M is stocked at Downtown and Westside, not at Northgate. */
export function everyStorePlan(id: string): ItemEveryStorePlan {
  const row = items.find((i) => i.id === id) ?? items[0];
  const rows = items.filter((i) => i.plu === row.plu).map((i) => ({ itemId: i.id, storeCode: i.storeCode ?? '', locationCode: i.locationCode ?? '' }));
  const stocked = new Set(rows.map((r) => r.storeCode));
  const skipped = stores.filter((s) => !stocked.has(s.code)).map((s) => ({ storeCode: s.code, locationCode: 'POS', reason: 'No item with this stock code here.' }));
  return { rows, skipped };
}

export function everyStoreResult(id: string, item: ItemDetail): ItemEveryStoreResult {
  const plan = everyStorePlan(id);
  const self = plan.rows.find((r) => r.itemId === id) ?? plan.rows[0];
  const changedAt = [self, ...plan.rows.filter((r) => r !== self)];
  return { item, changed: changedAt.length, fields: ['price1'], changedAt, skipped: plan.skipped };
}

// Clients and suppliers -------------------------------------------------------------------------------------------

export const clients: ClientRow[] = [
  { id: 'cl-1', number: 10000001, name: 'Akua Owusu', company: null, phone: '416-555-0142', email: 'akua@example.com', balance: 0, points: 320, isDeleted: false },
  { id: 'cl-2', number: 10000002, name: 'Daniel Kim', company: 'Kim Design', phone: '416-555-0199', email: 'daniel@example.com', balance: 145.2, points: 80, isDeleted: false },
  { id: 'cl-3', number: 10000003, name: 'Grace Adjei', company: null, phone: null, email: null, balance: 0, points: 12, isDeleted: false },
  { id: 'cl-4', number: 10000004, name: 'Marc Dubois', company: 'Dubois & Fils', phone: '514-555-0110', email: 'marc@example.com', balance: -20, points: 0, isDeleted: false },
];

export function clientDetail(id: string): ClientDetail {
  const row = clients.find((c) => c.id === id) ?? clients[0];
  const [firstName, lastName] = row.name.split(' ');
  return {
    id: row.id,
    version: 'v-1',
    number: row.number,
    firstName,
    lastName,
    company: row.company,
    address1: '12 King Street West',
    address2: null,
    city: 'Toronto',
    province: 'ON',
    postalCode: 'M5H 1A1',
    country: 'Canada',
    phone: row.phone,
    phone2: null,
    email: row.email,
    creditLimit: 500,
    balance: row.balance,
    points: row.points,
    notes: null,
  };
}

export const suppliers: SupplierRow[] = [
  { id: 'sp-1', code: 'COTTON', company: 'Cotton Co. Ltd', contact: 'Ruth Mensah', phone: '416-555-0100', email: 'orders@cottonco.example', isDeleted: false },
  { id: 'sp-2', code: 'SOLE', company: 'Sole Brothers', contact: 'Ali Hassan', phone: '905-555-0123', email: null, isDeleted: false },
  { id: 'sp-3', code: 'CAPS', company: 'Top Caps Inc.', contact: null, phone: null, email: 'hello@topcaps.example', isDeleted: false },
];

export function supplierDetail(id: string): SupplierDetail {
  const row = suppliers.find((s) => s.id === id) ?? suppliers[0];
  return {
    id: row.id,
    version: 'v-1',
    code: row.code,
    company: row.company,
    contact: row.contact,
    address1: '400 Industrial Road',
    address2: null,
    city: 'Mississauga',
    province: 'ON',
    postalCode: 'L4W 1A1',
    country: 'Canada',
    phone: row.phone,
    fax: null,
    email: row.email,
    notes: null,
  };
}

// Stock -----------------------------------------------------------------------------------------------------------

export const movements: StockMovementRow[] = [
  { at: at('14:52:10'), storeCode: 'MAIN', locationCode: 'POS', plu: 'TS-100-M', name: 'Cotton T-shirt, medium', kind: 'Sale', quantity: -2, onHandAfter: 84, unitCost: 6.2, documentNumber: '10000041', reason: null, user: 'ama' },
  { at: at('11:20:00'), storeCode: 'MAIN', locationCode: 'POS', plu: 'TS-100-M', name: 'Cotton T-shirt, medium', kind: 'Receipt', quantity: 48, onHandAfter: 86, unitCost: 6.4, documentNumber: 'PO 3000012', reason: null, user: 'kofi' },
  { at: at('10:05:00'), storeCode: 'WEST', locationCode: 'POS', plu: 'CP-012', name: 'Baseball cap', kind: 'Adjustment', quantity: -1, onHandAfter: 6, unitCost: 7.4, documentNumber: null, reason: 'Damaged', user: 'yaw' },
  { at: at('09:15:00'), storeCode: 'MAIN', locationCode: 'POS', plu: 'SN-410', name: 'Canvas sneakers', kind: 'TransferOut', quantity: -4, onHandAfter: 18, unitCost: 28.5, documentNumber: 'T 1000003', reason: null, user: 'kofi' },
];

export const transfers: TransferRow[] = [
  { id: 't-3', number: 1000003, status: 'Shipped', fromStore: 'MAIN', fromLocation: 'POS', toStore: 'WEST', toLocation: 'POS', createdAt: at('09:10:00'), shippedAt: at('09:15:00'), receivedAt: null, lines: 2, units: 10, value: 228.0 },
  { id: 't-2', number: 1000002, status: 'Received', fromStore: 'WEST', fromLocation: 'POS', toStore: 'MAIN', toLocation: 'POS', createdAt: at('10:00:00', daysBefore(3)), shippedAt: at('10:30:00', daysBefore(3)), receivedAt: at('15:00:00', daysBefore(2)), lines: 1, units: 6, value: 118.5 },
];

export function transferDetail(id: string): TransferDetail {
  const row = transfers.find((t) => t.id === id) ?? transfers[0];
  return {
    ...row,
    lines: [
      { plu: 'SN-410', description: 'Canvas sneakers', quantityShipped: 4, quantityReceived: 0, unitCost: 28.5 },
      { plu: 'TS-100-M', description: 'Cotton T-shirt, medium', quantityShipped: 6, quantityReceived: 0, unitCost: 19 },
    ],
  };
}

export const counts: StockCountRow[] = [
  { id: 'k-2', number: 2000002, status: 'Open', storeCode: 'MAIN', locationCode: 'POS', startedAt: at('08:30:00'), postedAt: null, lines: 3, varianceUnits: -3, varianceValue: -26.4 },
  { id: 'k-1', number: 2000001, status: 'Posted', storeCode: 'WEST', locationCode: 'POS', startedAt: at('08:00:00', daysBefore(7)), postedAt: at('12:00:00', daysBefore(7)), lines: 40, varianceUnits: 2, varianceValue: 18.9 },
];

export function countDetail(id: string): StockCountDetail {
  const row = counts.find((c) => c.id === id) ?? counts[0];
  return {
    ...row,
    lines: [
      { plu: 'TS-100-M', description: 'Cotton T-shirt, medium', expected: 84, counted: 82, variance: -2, varianceCost: -12.4, notCounted: false },
      { plu: 'CP-012', description: 'Baseball cap', expected: 6, counted: 5, variance: -1, varianceCost: -7.4, notCounted: false },
      { plu: 'BL-300', description: 'Leather belt', expected: 3, counted: 0, variance: 0, varianceCost: 0, notCounted: true },
    ],
  };
}

// Gift cards ------------------------------------------------------------------------------------------------------

export const giftCards: GiftCardRow[] = [
  { id: 'g-1', number: '6035 0000 1234', kind: 'Card', storeCode: 'MAIN', issuedAt: at('12:00:00', daysBefore(20)), expiresOn: null, isActive: true, client: 'Akua Owusu', balance: 35.5 },
  { id: 'g-2', number: '6035 0000 1240', kind: 'Certificate', storeCode: 'WEST', issuedAt: at('16:30:00', daysBefore(5)), expiresOn: daysBefore(-360), isActive: true, client: null, balance: 50 },
  { id: 'g-3', number: '6035 0000 0999', kind: 'Card', storeCode: 'MAIN', issuedAt: at('10:00:00', daysBefore(400)), expiresOn: null, isActive: false, client: null, balance: 0 },
];

export function giftCardDetail(id: string): GiftCardDetail {
  const row = giftCards.find((g) => g.id === id) ?? giftCards[0];
  return {
    ...row,
    history: [
      { at: row.issuedAt, kind: 'Issue', amount: 50, balanceAfter: 50, storeCode: 'MAIN', stationNumber: 1, invoiceNumber: 10000003, user: 'ama', note: null },
      { at: at('13:15:00', daysBefore(2)), kind: 'Redeem', amount: -14.5, balanceAfter: 35.5, storeCode: 'MAIN', stationNumber: 2, invoiceNumber: 10000031, user: 'yaw', note: null },
    ],
  };
}

// Reports ---------------------------------------------------------------------------------------------------------

export const reports: ReportInfo[] = [
  ['daily', 'Sales', 'Daily sales summary', true],
  ['dept', 'Sales', 'Sales by department', true],
  ['items', 'Sales', 'Sales by item (best sellers)', true],
  ['staff', 'Sales', 'Sales and commission by staff', true],
  ['hourly', 'Sales', 'Sales by hour of day', true],
  ['tenders', 'Sales', 'Payments by tender type', true],
  ['tax', 'Sales', 'Sales tax collected', true],
  ['voids', 'Sales', 'Voided sales', true],
  ['stock', 'Inventory', 'Inventory valuation', false],
  ['reorder', 'Inventory', 'Items at or below reorder point', false],
  ['negative', 'Inventory', 'Items with negative stock', false],
  ['clients', 'Clients', 'Client list', false],
  ['balances', 'Clients', 'Client balances', false],
  ['suppliers', 'Suppliers', 'Supplier list', false],
  ['drawer', 'Drawer', 'End of day history', true],
  ['hours', 'Staff', 'Hours worked by staff member', true],
  ['giftcards', 'Gift cards', 'Gift card liability', false],
].map(([key, group, title, usesDates]) => ({ key: key as string, group: group as string, title: title as string, usesDates: usesDates as boolean }));

export const dailyReport: ReportTable = {
  title: 'Daily sales summary',
  subtitle: `${daysBefore(3)} to ${today}`,
  columns: [
    { header: 'Date', kind: 'Date', total: false },
    { header: 'Customers', kind: 'Count', total: true },
    { header: 'Net sales', kind: 'Money', total: true },
    { header: 'Tax 1', kind: 'Money', total: true },
    { header: 'Tax 2', kind: 'Money', total: true },
    { header: 'Total', kind: 'Money', total: true },
    { header: 'Cost', kind: 'Money', total: true },
    { header: 'Profit', kind: 'Money', total: true },
    { header: 'Margin', kind: 'Percent', total: false },
  ],
  rows: [
    [daysBefore(3), 141, 5020.0, 652.6, 0, 5672.6, 3010.0, 2010.0, 40.04],
    [daysBefore(2), 170, 6210.4, 807.35, 0, 7017.75, 3700.2, 2510.2, 40.42],
    [daysBefore(1), 152, 4987.1, 648.32, 0, 5635.42, 2976.1, 2011.0, 40.32],
    [today, 148, 5532.75, 719.26, 0, 6252.01, 3214.35, 2318.4, 41.9],
  ],
  totals: null,
};

// Users, roles, audit -------------------------------------------------------------------------------------------

export const roles: RoleInfo[] = [
  { id: 'r-1', version: 'v-1', name: 'Trainee', description: 'Practice sales that are not kept', level: 0, isSystem: true, permissions: ['pos.sell', 'inventory.view', 'clients.view', 'timeclock.punch'], userCount: 0 },
  { id: 'r-2', version: 'v-1', name: 'Cashier', description: 'Sales at the till', level: 1, isSystem: true, permissions: ['pos.sell', 'pos.return', 'pos.drawer', 'inventory.view', 'clients.view', 'clients.edit', 'giftcards.issue', 'timeclock.punch', 'sync.send'], userCount: 6 },
  { id: 'r-4', version: 'v-1', name: 'Supervisor', description: 'Approves voids, discounts and End of Day; reports', level: 3, isSystem: true, permissions: supervisor.permissions, userCount: 2 },
  { id: 'r-6', version: 'v-1', name: 'Administrator', description: 'Everything for one store, at the tills and on the web', level: 4, isSystem: true, permissions: everything.filter((k) => k !== 'stores.all'), userCount: 1 },
  { id: 'r-7', version: 'v-1', name: 'Super administrator', description: 'Everything, for every store', level: 4, isSystem: true, permissions: everything, userCount: 1 },
  { id: 'r-8', version: 'v-1', name: 'Weekend help', description: 'Sales and stock look-ups only', level: 1, isSystem: false, permissions: ['pos.sell', 'inventory.view'], userCount: 0 },
];

export const users: UserRow[] = [
  { id: 'u-1', version: 'v-1', userName: 'sara', fullName: 'Sara Mensah', roleId: 'r-7', role: 'Super administrator', storeId: null, storeCode: null, staffCode: '100', isActive: true, locked: false, mustChangePassword: false, lastSignInAtUtc: minutesAgo(2) },
  { id: 'u-2', version: 'v-1', userName: 'kofi', fullName: 'Kofi Boateng', roleId: 'r-4', role: 'Supervisor', storeId: 's-main', storeCode: 'MAIN', staffCode: '201', isActive: true, locked: false, mustChangePassword: false, lastSignInAtUtc: minutesAgo(60 * 5) },
  { id: 'u-3', version: 'v-1', userName: 'ama', fullName: 'Ama Darko', roleId: 'r-2', role: 'Cashier', storeId: 's-main', storeCode: 'MAIN', staffCode: '301', isActive: true, locked: false, mustChangePassword: false, lastSignInAtUtc: minutesAgo(30) },
  { id: 'u-4', version: 'v-1', userName: 'yaw', fullName: 'Yaw Asante', roleId: 'r-2', role: 'Cashier', storeId: 's-west', storeCode: 'WEST', staffCode: '302', isActive: true, locked: true, mustChangePassword: false, lastSignInAtUtc: minutesAgo(60 * 26) },
  { id: 'u-5', version: 'v-1', userName: 'efua', fullName: 'Efua Quaye', roleId: 'r-2', role: 'Cashier', storeId: 's-west', storeCode: 'WEST', staffCode: null, isActive: false, locked: false, mustChangePassword: true, lastSignInAtUtc: null },
];

/** `GET /api/staff`: active and inactive users, sorted by user name. */
export const staff: StaffMember[] = [
  { id: 'u-3', userName: 'ama', fullName: 'Ama Darko' },
  { id: 'u-5', userName: 'efua', fullName: 'Efua Quaye' },
  { id: 'u-2', userName: 'kofi', fullName: 'Kofi Boateng' },
  { id: 'u-1', userName: 'sara', fullName: 'Sara Mensah' },
  { id: 'u-4', userName: 'yaw', fullName: null },
];

/** What the server says after taking a package from a USB stick. */
export const uploadResponse: UploadResponse = {
  status: 'Applied',
  batchId: 'b-new-guid',
  inserted: 380,
  updated: 30,
  deleted: 2,
  notes: ['2 clients were known already and kept the server\'s details.'],
  error: null,
};

export const audit: AuditRow[] = [
  { id: 'a-3', atUtc: minutesAgo(12), storeCode: 'MAIN', stationNumber: 0, userName: 'sara', action: 'Changed', entityName: 'Item', entityId: 'i-1', details: 'TS-100-M', before: { Price1: 16.95, Description: 'Cotton T-shirt, medium', ReorderPoint: 10 }, after: { Price1: 17.95, Description: 'Cotton T-shirt, medium', ReorderPoint: 20 } },
  { id: 'a-2', atUtc: minutesAgo(48), storeCode: 'MAIN', stationNumber: 1, userName: 'ama', action: 'Void sale', entityName: 'Sale', entityId: 'sale-5', details: 'Invoice 10000037 voided, approved by kofi', before: null, after: null },
  { id: 'a-1', atUtc: minutesAgo(130), storeCode: 'WEST', stationNumber: 11, userName: 'yaw', action: 'Sign in', entityName: null, entityId: null, details: 'Signed in with staff code and PIN', before: null, after: null },
];

// Staff -----------------------------------------------------------------------------------------------------------

export const timeClock: TimeClockEntry[] = [
  { id: 'tc-1', userId: 'u-3', userName: 'ama', storeCode: 'MAIN', stationNumber: 1, inAt: at('08:58:00'), outAt: null, hours: null, editedBy: null, editReason: null },
  { id: 'tc-2', userId: 'u-4', userName: 'yaw', storeCode: 'WEST', stationNumber: 11, inAt: at('09:00:00', daysBefore(1)), outAt: at('17:30:00', daysBefore(1)), hours: 8.5, editedBy: 'kofi', editReason: 'Forgot to punch out' },
  { id: 'tc-3', userId: 'u-3', userName: 'ama', storeCode: 'MAIN', stationNumber: 1, inAt: at('09:02:00', daysBefore(1)), outAt: at('17:01:00', daysBefore(1)), hours: 7.98, editedBy: null, editReason: null },
];

export const hours: HoursRow[] = [
  { userId: 'u-3', userName: 'ama', days: 5, hours: 39.75 },
  { userId: 'u-4', userName: 'yaw', days: 4, hours: 32.5 },
];

export const commission: CommissionRow[] = [
  { staff: 'ama', sales: 61, units: 140, netSales: 2210.4, fromRules: 88.42, fromItems: 12.1, commission: 100.52 },
  { staff: 'yaw', sales: 50, units: 122, netSales: 1795.0, fromRules: 71.8, fromItems: 0, commission: 71.8 },
];

export const commissionRules: CommissionRule[] = [
  { id: 'cr-1', version: 'v-1', name: 'Everyone, all items', userId: null, userName: null, scope: 'AllItems', departmentId: null, categoryId: null, itemId: null, target: 'All items', percent: 2, basis: 'PercentOfNetSales', startsOn: null, endsOn: null, isActive: true },
  { id: 'cr-2', version: 'v-1', name: 'Footwear push', userId: 'u-3', userName: 'ama', scope: 'Department', departmentId: 'd-2', categoryId: null, itemId: null, target: 'Department FOOTWEAR', percent: 5, basis: 'PercentOfNetSales', startsOn: daysBefore(10), endsOn: daysBefore(-20), isActive: true },
  { id: 'cr-3', version: 'v-1', name: 'Socks bonus', userId: null, userName: null, scope: 'Item', departmentId: null, categoryId: null, itemId: 'i-5', target: 'Item SK-001 Sport socks, 3 pack', percent: 0.25, basis: 'AmountPerUnit', startsOn: null, endsOn: null, isActive: false },
];

// Settings --------------------------------------------------------------------------------------------------------

export const settings: StoreSettings = {
  version: 'v-1',
  companyName: 'SMA Fashion Downtown',
  address1: '12 King Street West',
  address2: null,
  city: 'Toronto',
  province: 'ON',
  postalCode: 'M5H 1A1',
  country: 'Canada',
  phone: '416-555-0100',
  fax: null,
  email: 'downtown@smafashion.example',
  website: 'smafashion.example',
  taxNumber: '81234 5678 RT0001',
  currencySymbol: '$',
  receiptHeader: 'Thank you for shopping with us',
  receiptFooter: 'Returns within 30 days with this receipt',
};

// HR and payroll (phase E3) ---------------------------------------------------------------------------------------

export * from './hr-fixtures';
// Phase E4: CRM -----------------------------------------------------------------------------------------------------

/** A marketing user of Downtown: CRM, segments, campaigns and promotions, but not targets nor the shared lists. */
export const marketer: UserInfo = {
  id: 'u-7',
  userName: 'nana',
  fullName: 'Nana Yeboah',
  role: 'Marketing',
  permissions: ['web.signin', 'dashboard.view', 'reports.view', 'clients.view', 'clients.edit', 'crm.view', 'crm.edit', 'marketing.campaigns', 'marketing.promotions'],
  storeId: 's-main',
  storeName: 'Downtown',
  mustChangePassword: false,
  canChangeShared: false,
};

// Supply chain (phase E2) -----------------------------------------------------------------------------------------

/** The supply chain permissions, added at the end of the Stock group as Permissions.cs does; the administrators get them. */
const supplyKeys: PermissionInfo[] = [
  ['purchasing.approve', 'Stock', 'Approve requisitions and bills held by the three-way match; landed costs'],
  ['inventory.batches', 'Stock', 'Batches and expiry dates'],
  ['inventory.bins', 'Stock', 'Bins, put-away and pick lists'],
].map(([key, group, description]) => ({ key, group, description }));
permissions.splice(permissions.findIndex((p) => p.key === 'purchasing.manage') + 1, 0, ...supplyKeys);
// The super administrator and their role share `everything`; the store administrator and the Administrator role have their own copies.
for (const list of [everything, storeAdmin.permissions, roles.find((r) => r.id === 'r-6')!.permissions]) list.push(...supplyKeys.map((p) => p.key));

/** A stock clerk on the web: purchase orders, requisitions, batches, bins; no approving, no setup. */
export const buyer: UserInfo = {
  id: 'u-8',
  userName: 'esi',
  fullName: 'Esi Amoah',
  role: 'Stock clerk',
  permissions: ['web.signin', 'inventory.view', 'inventory.edit', 'inventory.adjust', 'purchasing.manage', 'inventory.batches', 'reports.view'],
  storeId: 's-main',
  storeName: 'Downtown',
  mustChangePassword: false,
  canChangeShared: false,
};

/** A supervisor who may read the CRM and tick their own tasks, nothing more of E4. */
export const crmViewer: UserInfo = { ...supervisor, id: 'u-2', permissions: [...supervisor.permissions, 'crm.view'] };

export const stagesE4: OpportunityStage[] = [
  { id: 'st-new', name: 'New', sortOrder: 1, probability: 10, outcome: 'Open' },
  { id: 'st-qualified', name: 'Qualified', sortOrder: 2, probability: 30, outcome: 'Open' },
  { id: 'st-proposal', name: 'Proposal', sortOrder: 3, probability: 60, outcome: 'Open' },
  { id: 'st-won', name: 'Won', sortOrder: 4, probability: 100, outcome: 'Won' },
  { id: 'st-lost', name: 'Lost', sortOrder: 5, probability: 0, outcome: 'Lost' },
];

function opportunity(id: string, title: string, stageId: string, value: number, extra: Partial<OpportunityRow> = {}): OpportunityRow {
  const stage = stagesE4.find((s) => s.id === stageId)!;
  return {
    id, version: 'v-1', title, clientId: null, clientName: null, leadId: null, leadName: null, stageId, stage: stage.name, outcome: stage.outcome,
    expectedValue: value, probability: stage.probability, weightedValue: Math.round(value * stage.probability) / 100, expectedCloseOn: null, ownerId: 'u-1', ownerName: 'Sara Mensah',
    storeId: 's-main', storeCode: 'MAIN', lostReason: null, closedOn: null, notes: null, createdAtUtc: minutesAgo(60 * 24 * 10), ...extra,
  };
}

export const opportunities: OpportunityRow[] = [
  // o-1 came from lead l-3, made a client: the server keeps both links.
  opportunity('o-1', 'Uniforms for Kim Design', 'st-new', 1200, { clientId: 'cl-2', clientName: 'Daniel Kim', leadId: 'l-3', leadName: 'Daniel Kim', expectedCloseOn: daysBefore(-20), ownerId: 'u-3', ownerName: 'Ama Darko' }),
  opportunity('o-2', 'Wedding party suits', 'st-qualified', 3500, { leadId: 'l-1', leadName: 'Kwame Asante', expectedCloseOn: daysBefore(-40) }),
  opportunity('o-3', 'Office party gifts', 'st-proposal', 900, { clientId: 'cl-4', clientName: 'Marc Dubois', expectedCloseOn: daysBefore(-5) }),
  opportunity('o-4', 'School shoes', 'st-won', 640, { clientId: 'cl-1', clientName: 'Akua Owusu', closedOn: daysBefore(2) }),
  opportunity('o-5', 'Hotel staff wear', 'st-lost', 5000, { leadId: 'l-2', leadName: 'Esi Mensah', closedOn: daysBefore(4), lostReason: 'Price' }),
];

/** The board of a list of opportunities, every stage in order with its totals. */
export function boardOf(list: OpportunityRow[]): OpportunityBoard {
  return {
    stages: stagesE4.map((s) => {
      const items = list.filter((o) => o.stageId === s.id);
      return {
        id: s.id, name: s.name, outcome: s.outcome, probability: s.probability, opportunities: items, count: items.length,
        value: items.reduce((t, o) => t + o.expectedValue, 0), weighted: Math.round(items.reduce((t, o) => t + o.weightedValue, 0) * 100) / 100,
      };
    }),
  };
}

export const leads: LeadRow[] = [
  {
    id: 'l-1', version: 'v-1', name: 'Kwame Asante', company: null, phone: '416-555-0170', whatsAppNumber: '+14165550170', email: 'kwame@example.com', source: 'Walk-in',
    ownerId: 'u-1', ownerName: 'Sara Mensah', storeId: 's-main', storeCode: 'MAIN', status: 'Open', clientId: null, clientName: null, notes: 'Wedding in June',
    createdAtUtc: minutesAgo(60 * 24 * 3), convertedAtUtc: null,
  },
  {
    id: 'l-2', version: 'v-1', name: 'Esi Mensah', company: 'Mensah Catering', phone: null, whatsAppNumber: null, email: 'esi@mensah.example', source: 'Referral',
    ownerId: 'u-3', ownerName: 'Ama Darko', storeId: 's-main', storeCode: 'MAIN', status: 'Open', clientId: null, clientName: null, notes: null,
    createdAtUtc: minutesAgo(60 * 24 * 8), convertedAtUtc: null,
  },
  {
    id: 'l-3', version: 'v-1', name: 'Daniel Kim', company: 'Kim Design', phone: '416-555-0199', whatsAppNumber: null, email: 'daniel@example.com', source: 'Website',
    ownerId: 'u-1', ownerName: 'Sara Mensah', storeId: 's-main', storeCode: 'MAIN', status: 'Converted', clientId: 'cl-2', clientName: 'Daniel Kim', notes: null,
    createdAtUtc: minutesAgo(60 * 24 * 30), convertedAtUtc: minutesAgo(60 * 24 * 20),
  },
];

function activity(id: string, kind: ActivityRow['kind'], subject: string, due: ActivityRow['due'], dueAt: string | null, extra: Partial<ActivityRow> = {}): ActivityRow {
  return {
    id, version: 'v-1', kind, subject, notes: null, clientId: null, clientName: null, leadId: null, leadName: null, opportunityId: null, opportunityTitle: null, dueAt,
    isDone: due === 'done', doneAtUtc: due === 'done' ? minutesAgo(90) : null, ownerId: 'u-1', ownerName: 'Sara Mensah', storeId: 's-main', storeCode: 'MAIN', due,
    createdAtUtc: minutesAgo(60 * 24 * 5), ...extra,
  };
}

export const activities: ActivityRow[] = [
  activity('a-1', 'Call', 'Call Daniel about the uniforms', 'overdue', at('11:00:00', daysBefore(1)), { clientId: 'cl-2', clientName: 'Daniel Kim', opportunityId: 'o-1', opportunityTitle: 'Uniforms for Kim Design' }),
  activity('a-2', 'Visit', 'Measure the wedding party', 'today', at('15:00:00'), { leadId: 'l-1', leadName: 'Kwame Asante' }),
  activity('a-3', 'Task', 'Send the gift proposal', 'later', at('10:00:00', daysBefore(-3)), { clientId: 'cl-4', clientName: 'Marc Dubois', opportunityId: 'o-3', opportunityTitle: 'Office party gifts' }),
  activity('a-4', 'Note', 'Ask about a loyalty card', 'none', null, { clientId: 'cl-1', clientName: 'Akua Owusu' }),
  activity('a-5', 'Meeting', 'Catering menu tasting', 'today', at('12:30:00'), { leadId: 'l-2', leadName: 'Esi Mensah', ownerId: 'u-3', ownerName: 'Ama Darko' }),
  activity('a-6', 'Task', 'Thank Akua for the order', 'done', at('09:00:00', daysBefore(2)), { clientId: 'cl-1', clientName: 'Akua Owusu' }),
];

/** `GET /api/activities/mine` for the signed-in user. */
export function myTasksFor(userId: string, todayOnly: boolean): MyTasks {
  const mine = activities.filter((a) => a.ownerId === userId && !a.isDone);
  return {
    overdue: mine.filter((a) => a.due === 'overdue').length,
    dueToday: mine.filter((a) => a.due === 'today').length,
    items: todayOnly ? mine.filter((a) => a.due === 'overdue' || a.due === 'today') : mine,
  };
}

export const wonLost: WonLostReport = {
  from: daysBefore(29),
  to: today,
  won: { count: 1, value: 640 },
  lost: { count: 1, value: 5000 },
  winRate: 50,
  byOwner: [{ ownerId: 'u-1', ownerName: 'Sara Mensah', won: 1, wonValue: 640, lost: 1, lostValue: 5000 }],
  lostReasons: [{ reason: 'Price', count: 1, value: 5000 }],
  closed: opportunities.filter((o) => o.outcome !== 'Open'),
};

// Phase E4: segments and WhatsApp ----------------------------------------------------------------------------------

export const segmentsE4: SegmentRow[] = [
  {
    id: 'seg-1', version: 'v-1', name: 'Big spenders', description: 'Spent the most lately', rules: { periodDays: 90, minSpend: 500 },
    summary: 'Spent at least $500.00 in the last 90 days', storeId: null, lastCount: 42, lastEvaluatedAtUtc: minutesAgo(60 * 5),
  },
  {
    id: 'seg-2', version: 'v-1', name: 'Lapsed Downtown', description: null, rules: { noPurchaseForDays: 60, storeId: 's-main', whatsAppConsent: true },
    summary: 'At Downtown · No purchase for 60 days · Agreed to WhatsApp', storeId: 's-main', lastCount: 0, lastEvaluatedAtUtc: null,
  },
];

export const segmentResult: SegmentResult = {
  count: 3,
  evaluatedAtUtc: minutesAgo(0),
  members: [
    { clientId: 'cl-2', number: 10000002, name: 'Daniel Kim', phone: '416-555-0199', email: 'daniel@example.com', whatsAppNumber: '+14165550199', whatsAppOptIn: true, spend: 1840.5, visits: 9, lastPurchase: daysBefore(3), points: 80, balance: 145.2, birthday: null },
    { clientId: 'cl-1', number: 10000001, name: 'Akua Owusu', phone: '416-555-0142', email: 'akua@example.com', whatsAppNumber: '+14165550142', whatsAppOptIn: true, spend: 960, visits: 6, lastPurchase: daysBefore(2), points: 320, balance: 0, birthday: '1990-03-14' },
    { clientId: 'cl-4', number: 10000004, name: 'Marc Dubois', phone: '514-555-0110', email: 'marc@example.com', whatsAppNumber: null, whatsAppOptIn: false, spend: 612.25, visits: 3, lastPurchase: daysBefore(40), points: 0, balance: -20, birthday: null },
  ],
};

export const whatsAppStatus: WhatsAppStatus = { enabled: false, provider: 'recording', phoneNumberIdHint: null, webhookReady: false, messagesPerMinute: 60 };

export const templates: WhatsAppTemplate[] = [
  { id: 't-1', version: 'v-1', name: 'spring_sale', language: 'en', category: 'Marketing', body: 'Hi {FirstName}, our spring sale at {Store} starts Saturday: 20% off.', isActive: true, placeholders: ['FirstName', 'Store'] },
  { id: 't-2', version: 'v-1', name: 'points_reminder', language: 'en', category: 'Marketing', body: 'Hi {FirstName}, you have {Points} points to spend.', isActive: true, placeholders: ['FirstName', 'Points'] },
  { id: 't-3', version: 'v-1', name: 'old_offer', language: 'ur', category: 'Utility', body: 'Old offer', isActive: false, placeholders: [] },
];

const noMessages = { queued: 0, sent: 0, delivered: 0, read: 0, failed: 0, error: 0 };

export const campaigns: CampaignRow[] = [
  {
    id: 'cm-1', version: 'v-1', name: 'Spring sale', segmentId: 'seg-1', segment: 'Big spenders', templateId: 't-1', template: 'spring_sale', status: 'Sent', storeId: 's-main',
    storeCode: 'MAIN', scheduledAtUtc: minutesAgo(60 * 48), startedAtUtc: minutesAgo(60 * 48), finishedAtUtc: minutesAgo(60 * 47), recipients: 40,
    counts: { queued: 0, sent: 4, delivered: 18, read: 15, failed: 2, error: 1 }, createdBy: 'sara', createdAtUtc: minutesAgo(60 * 50),
  },
  {
    id: 'cm-2', version: 'v-1', name: 'Points reminder', segmentId: 'seg-2', segment: 'Lapsed Downtown', templateId: 't-2', template: 'points_reminder', status: 'Draft',
    storeId: 's-main', storeCode: 'MAIN', scheduledAtUtc: null, startedAtUtc: null, finishedAtUtc: null, recipients: 0, counts: noMessages, createdBy: 'sara', createdAtUtc: minutesAgo(30),
  },
  {
    id: 'cm-3', version: 'v-1', name: 'Weekend launch', segmentId: 'seg-1', segment: 'Big spenders', templateId: 't-1', template: 'spring_sale', status: 'Scheduled',
    storeId: 's-main', storeCode: 'MAIN', scheduledAtUtc: minutesAgo(-60 * 20), startedAtUtc: null, finishedAtUtc: null, recipients: 40, counts: { ...noMessages, queued: 40 },
    createdBy: 'sara', createdAtUtc: minutesAgo(90),
  },
];

export const campaignMessages: WhatsAppMessageRow[] = [
  { id: 'm-1', campaignId: 'cm-1', clientId: 'cl-1', clientName: 'Akua Owusu', toNumber: '+14165550142', template: 'spring_sale', body: 'Hi Akua, our spring sale at Downtown starts Saturday: 20% off.', status: 'Read', error: null, sentAtUtc: minutesAgo(60 * 48), deliveredAtUtc: minutesAgo(60 * 48 - 1), readAtUtc: minutesAgo(60 * 40), createdAtUtc: minutesAgo(60 * 48) },
  { id: 'm-2', campaignId: 'cm-1', clientId: 'cl-2', clientName: 'Daniel Kim', toNumber: '+14165550199', template: 'spring_sale', body: 'Hi Daniel, our spring sale at Downtown starts Saturday: 20% off.', status: 'Delivered', error: null, sentAtUtc: minutesAgo(60 * 48), deliveredAtUtc: minutesAgo(60 * 47), readAtUtc: null, createdAtUtc: minutesAgo(60 * 48) },
  { id: 'm-3', campaignId: 'cm-1', clientId: 'cl-3', clientName: 'Grace Adjei', toNumber: '+14165550111', template: 'spring_sale', body: 'Hi Grace, our spring sale at Downtown starts Saturday: 20% off.', status: 'Failed', error: 'The number is not on WhatsApp.', sentAtUtc: minutesAgo(60 * 48), deliveredAtUtc: null, readAtUtc: null, createdAtUtc: minutesAgo(60 * 48) },
];

export const campaignResults: CampaignResults = { counts: campaigns[0].counts, recipients: 40, days: 7, buyers: 9, sales: 12, salesTotal: 1432.75 };

// Phase E4: promotions and targets ---------------------------------------------------------------------------------

function promotion(id: string, name: string, extra: Partial<PromotionDetail>): PromotionDetail {
  return {
    id, version: 'v-1', name, kind: 'PercentOff', isActive: true, startsOn: null, endsOn: null, priority: 0, combinable: true, allStores: true, storeIds: [], couponCode: null,
    couponMaxUses: 0, couponUses: 0, summary: name, live: true, description: null, days: [], startTime: null, endTime: null, scope: 'AllItems', targets: [], percent: 0, amount: 0,
    buyQuantity: 0, getQuantity: 0, getPercent: 100, rewardTargets: [], bundlePrice: 0, bundleParts: [], spendThreshold: 0, ...extra,
  };
}

export const promotions: PromotionDetail[] = [
  promotion('p-1', 'Weekend footwear', { percent: 10, scope: 'Departments', targets: [{ departmentId: 'd-2', quantity: 1, name: 'Footwear' }], days: [6, 0], summary: '10% off Footwear · Weekends', live: false }),
  promotion('p-2', 'Socks 3 for 2', { kind: 'BuyXGetY', scope: 'Items', targets: [{ plu: 'SK-001', quantity: 1, name: 'SK-001 Sport socks, 3 pack' }], buyQuantity: 2, getQuantity: 1, summary: 'Buy 2 of SK-001, get 1 more free' }),
  promotion('p-3', 'Happy hour', { percent: 15, startTime: '17:00', endTime: '19:00', combinable: false, priority: 5, summary: '15% off everything · 17:00–19:00 · alone' }),
  promotion('p-4', 'Spend 100 save 10', { kind: 'SpendOver', spendThreshold: 100, amount: 10, couponCode: 'SAVE10', couponMaxUses: 100, couponUses: 12, summary: 'Spend $100.00, get $10.00 off · coupon SAVE10' }),
  promotion('p-5', 'Tee and cap', {
    kind: 'BundlePrice', bundlePrice: 25, allStores: false, storeIds: ['s-main'], isActive: false, live: false,
    bundleParts: [{ plu: 'TS-100-M', quantity: 1, name: 'TS-100-M Cotton T-shirt, medium' }, { plu: 'CP-012', quantity: 1, name: 'CP-012 Baseball cap' }], summary: 'TS-100-M + CP-012 for $25.00',
  }),
];

export function promotionRow(p: PromotionDetail): PromotionRow {
  const { id, version, name, kind, isActive, startsOn, endsOn, priority, combinable, allStores, storeIds, couponCode, couponMaxUses, couponUses, summary, live } = p;
  return { id, version, name, kind, isActive, startsOn, endsOn, priority, combinable, allStores, storeIds, couponCode, couponMaxUses, couponUses, summary, live };
}

export const promotionReport: PromotionReportRow[] = [
  { promotionId: 'p-2', name: 'Socks 3 for 2', kind: 'BuyXGetY', sales: 14, units: 42, netSales: 369.63, discount: 131.6, couponUses: 0 },
  { promotionId: 'p-4', name: 'Spend 100 save 10', kind: 'SpendOver', sales: 12, units: 0, netSales: 1630.2, discount: 120, couponUses: 12 },
];

const thisYear = new Date().getFullYear();
const thisMonth = new Date().getMonth() + 1;

export const targetsE4: SalesTargetRow[] = [
  { id: 'tg-1', version: 'v-1', storeId: 's-main', storeCode: 'MAIN', userId: null, userName: null, year: thisYear, month: thisMonth, amount: 60000, actual: 41250, percent: 68.8 },
  { id: 'tg-2', version: 'v-1', storeId: 's-main', storeCode: 'MAIN', userId: 'u-3', userName: 'ama', year: thisYear, month: thisMonth, amount: 15000, actual: 12100, percent: 80.7 },
  { id: 'tg-3', version: 'v-1', storeId: 's-west', storeCode: 'WEST', userId: null, userName: null, year: thisYear, month: thisMonth, amount: 40000, actual: 9000, percent: 22.5 },
];

export const targetProgress: TargetProgress = {
  year: thisYear,
  month: thisMonth,
  day: new Date().getDate(),
  daysInMonth: new Date(thisYear, thisMonth, 0).getDate(),
  target: 100000,
  actual: 50250,
  percent: 50.3,
  stores: [
    { storeId: 's-main', storeCode: 'MAIN', target: 60000, actual: 41250, percent: 68.8 },
    { storeId: 's-west', storeCode: 'WEST', target: 40000, actual: 9000, percent: 22.5 },
  ],
  people: [{ userId: 'u-3', userName: 'ama', storeCode: 'MAIN', target: 15000, actual: 12100, percent: 80.7 }],
};

/** Akua, who agreed to WhatsApp messages, as the client editor gets her. */
export function clientWithWhatsApp(): ClientDetail {
  return { ...clientDetail('cl-1'), whatsAppNumber: '+14165550142', whatsAppOptIn: true, whatsAppOptInAtUtc: minutesAgo(60 * 24 * 40), birthday: '1990-03-14', whatsAppAskedAtUtc: minutesAgo(60 * 24 * 40) };
}

export const clientMessages: WhatsAppMessageRow[] = [campaignMessages[0]];
export const purchaseOrders: PurchaseOrderRow[] = [
  { id: 'po-3', number: 10000003, storeCode: 'MAIN', locationCode: 'POS', status: 'Posted', createdAt: at('09:00:00', daysBefore(4)), postedAt: at('09:30:00', daysBefore(4)), receivedAt: null, expectedOn: today, supplier: 'Cotton Co. Ltd', description: 'Requisition 10000002 · Cotton Co. Ltd', lines: 2, totalCost: 412.8 },
  { id: 'po-2', number: 10000002, storeCode: 'MAIN', locationCode: 'POS', status: 'Draft', createdAt: at('11:00:00', daysBefore(1)), postedAt: null, receivedAt: null, expectedOn: null, supplier: 'Sole Brothers', description: 'Reorder suggestions', lines: 1, totalCost: 285 },
  { id: 'po-1', number: 10000001, storeCode: 'MAIN', locationCode: 'POS', status: 'Received', createdAt: at('10:00:00', daysBefore(12)), postedAt: at('10:05:00', daysBefore(12)), receivedAt: at('15:20:00', daysBefore(5)), expectedOn: daysBefore(6), supplier: 'Cotton Co. Ltd', description: 'Weekly order', lines: 2, totalCost: 520 },
];

export function purchaseOrderDetail(id: string, status?: PurchaseOrderDetail['status']): PurchaseOrderDetail {
  const row = purchaseOrders.find((p) => p.id === id) ?? purchaseOrders[0];
  const received = (status ?? row.status) === 'Received';
  return {
    id: row.id,
    version: 'v-1',
    number: row.number,
    storeId: 's-main',
    storeCode: row.storeCode,
    locationId: 'l-main-pos',
    locationCode: row.locationCode,
    status: status ?? row.status,
    createdAt: row.createdAt,
    postedAt: row.postedAt,
    receivedAt: received ? row.receivedAt ?? at('16:00:00') : null,
    expectedOn: row.expectedOn,
    supplierId: 'sp-1',
    supplier: row.supplier,
    description: row.description,
    createdBy: 'esi',
    receivedBy: received ? 'esi' : null,
    lines: [
      { id: `${id}-l1`, itemId: 'i-1', plu: 'TS-100-M', description: 'Cotton T-shirt, medium', supplier: row.supplier, caseQuantity: 12, orderQuantity: 4, pieces: 48, costEach: 6.4, orderedCost: 6.4, orderCost: 307.2, quantityReceived: received ? 48 : 0, tracksBatches: false, shelfLifeDays: 0, bin: 'A-01', batches: [] },
      { id: `${id}-l2`, itemId: 'i-8', plu: 'MK-500', description: 'Oat milk 1 L', supplier: row.supplier, caseQuantity: 1, orderQuantity: 24, pieces: 24, costEach: 4.4, orderedCost: 4.4, orderCost: 105.6, quantityReceived: received ? 24 : 0, tracksBatches: true, shelfLifeDays: 60, bin: 'C-02', batches: received ? [{ batchNumber: 'OAT-0912', pieces: 24, expiresOn: daysBefore(-55), madeOn: daysBefore(5) }] : [] },
    ],
    totalCost: row.totalCost,
  };
}

export function putAwayList(id: string): BinList {
  const po = purchaseOrderDetail(id, 'Received');
  return {
    title: `Put-away list: PO ${po.number}`,
    rows: [
      { zone: 'Aisle 1', bin: 'A-01', plu: 'TS-100-M', description: 'Cotton T-shirt, medium', quantity: 48, batchNumber: null, expiresOn: null, document: `PO ${po.number}`, onHand: 132 },
      { zone: 'Cold room', bin: 'C-02', plu: 'MK-500', description: 'Oat milk 1 L', quantity: 24, batchNumber: 'OAT-0912', expiresOn: daysBefore(-55), document: `PO ${po.number}`, onHand: 30 },
    ],
  };
}

export function requisitionDetail(id: string, status: RequisitionDetail['status'] = 'Submitted'): RequisitionDetail {
  const number = id === 'rq-1' ? 10000001 : 10000002;
  return {
    id,
    version: `v-${status}`,
    number,
    storeId: 's-main',
    storeCode: 'MAIN',
    locationId: 'l-main-pos',
    locationCode: 'POS',
    status,
    createdAt: at('08:40:00', daysBefore(1)),
    requestedBy: 'esi',
    neededBy: daysBefore(-7),
    note: 'For the weekend',
    submittedAt: status === 'Draft' ? null : at('08:45:00', daysBefore(1)),
    decidedAt: status === 'Approved' || status === 'Rejected' || status === 'Ordered' ? at('09:10:00') : null,
    decidedBy: status === 'Approved' || status === 'Rejected' || status === 'Ordered' ? 'sara' : null,
    decisionNote: status === 'Rejected' ? 'Too many' : null,
    orderedAt: status === 'Ordered' ? at('09:20:00') : null,
    total: 461.6,
    lines: [
      { id: `${id}-1`, lineNumber: 1, itemId: 'i-1', plu: 'TS-100-M', description: 'Cotton T-shirt, medium', quantity: 48, costEach: 6.4, value: 307.2, neededBy: null, supplierId: 'sp-1', supplier: 'Cotton Co. Ltd', note: null, purchaseOrderId: status === 'Ordered' ? 'po-9' : null, purchaseOrderNumber: status === 'Ordered' ? 10000009 : null },
      { id: `${id}-2`, lineNumber: 2, itemId: 'i-3', plu: 'SN-410', description: 'Canvas sneakers', quantity: 6, costEach: 25.733, value: 154.4, neededBy: null, supplierId: 'sp-2', supplier: 'Sole Brothers', note: null, purchaseOrderId: status === 'Ordered' ? 'po-10' : null, purchaseOrderNumber: status === 'Ordered' ? 10000010 : null },
    ],
  };
}

export const approvalLimits: ApprovalLimitRow[] = [
  { roleId: 'r-5', role: 'Manager', mayApprove: true, maxAmount: 2500 },
  { roleId: 'r-6', role: 'Administrator', mayApprove: true, maxAmount: null },
  { roleId: 'r-7', role: 'Super administrator', mayApprove: true, maxAmount: null },
  { roleId: 'r-2', role: 'Cashier', mayApprove: false, maxAmount: null },
];

function batchRow(id: string, plu: string, itemName: string, batchNumber: string, daysLeft: number | null, onHand: number, unitCost: number): BatchRow {
  return {
    id, version: 'v-1', itemId: plu === 'MK-500' ? 'i-8' : 'i-9', plu, itemName, storeCode: 'MAIN', locationCode: 'POS', batchNumber, expiresOn: daysLeft === null ? null : daysBefore(-daysLeft),
    madeOn: null, supplier: 'Cotton Co. Ltd', receivedOn: daysBefore(20), purchaseOrderNumber: 10000001, onHand, unitCost, value: Math.round(onHand * unitCost * 100) / 100, daysLeft,
    bin: 'C-02', note: null,
  };
}

export const batchRows: BatchRow[] = [
  batchRow('bt-1', 'MK-500', 'Oat milk 1 L', 'OAT-0811', -2, 4, 4.4),
  batchRow('bt-2', 'MK-500', 'Oat milk 1 L', 'OAT-0901', 5, 12, 4.4),
  batchRow('bt-3', 'MK-500', 'Oat milk 1 L', 'OAT-0912', 55, 24, 4.4),
  batchRow('bt-4', 'YG-120', 'Greek yogurt', 'YG-77', 12, 9, 2.1),
];

export const expiringReport: ExpiryReport = { days: 30, rows: [batchRows[1], batchRows[3]], units: 21, value: 71.7 };
export const expiredReport: ExpiryReport = { days: 0, rows: [batchRows[0]], units: 4, value: 17.6 };

export const binRows: BinRow[] = [
  { id: 'bn-1', version: 'v-1', locationId: 'l-main-pos', storeCode: 'MAIN', locationCode: 'POS', code: 'A-01', zone: 'Aisle 1', description: 'T-shirts', isActive: true, items: 12 },
  { id: 'bn-2', version: 'v-1', locationId: 'l-main-pos', storeCode: 'MAIN', locationCode: 'POS', code: 'A-02', zone: 'Aisle 1', description: null, isActive: true, items: 0 },
  { id: 'bn-3', version: 'v-1', locationId: 'l-main-pos', storeCode: 'MAIN', locationCode: 'POS', code: 'C-02', zone: 'Cold room', description: 'Dairy', isActive: true, items: 3 },
];

export const pickList: BinList = {
  title: 'Pick list: customer orders',
  rows: [
    { zone: 'Aisle 1', bin: 'A-01', plu: 'TS-100-M', description: 'Cotton T-shirt, medium', quantity: 2, batchNumber: null, expiresOn: null, document: 'Order 10000041 Akua Owusu', onHand: 84 },
    { zone: 'Cold room', bin: 'C-02', plu: 'MK-500', description: 'Oat milk 1 L', quantity: 3, batchNumber: 'OAT-0901', expiresOn: daysBefore(-5), document: 'Order 10000042 Daniel Kim', onHand: 40 },
  ],
};

export const countSheet: BinList = {
  title: 'Count sheet',
  rows: [{ zone: 'Aisle 1', bin: 'A-01', plu: 'TS-100-M', description: 'Cotton T-shirt, medium', quantity: 0, batchNumber: null, expiresOn: null, document: null, onHand: 84 }],
};

export const matchRows: MatchRow[] = [
  {
    id: 'm-1', version: 'v-1', storeCode: 'MAIN', purchaseOrderId: 'po-1', purchaseOrderNumber: 10000001, purchaseOrderLineId: 'po-1-l1', billId: 'b-1', billLineId: 'bl-1',
    billNumber: 'INV-5531', billDate: daysBefore(2), supplier: 'Cotton Co. Ltd', plu: 'TS-100-M', description: 'Cotton T-shirt, medium', quantityOrdered: 48, quantityReceived: 48,
    quantityBilled: 48, quantityMatched: 48, orderedCost: 6.4, receivedCost: 6.4, billedCost: 7.1, priceDifference: 33.6, status: 'Held',
    reason: 'Billed at 7.10 each for 6.40 ordered.', checkedAtUtc: minutesAgo(30), decidedAtUtc: null, decidedBy: null, decisionNote: null,
  },
  {
    id: 'm-2', version: 'v-1', storeCode: 'MAIN', purchaseOrderId: 'po-1', purchaseOrderNumber: 10000001, purchaseOrderLineId: 'po-1-l2', billId: 'b-1', billLineId: 'bl-2',
    billNumber: 'INV-5531', billDate: daysBefore(2), supplier: 'Cotton Co. Ltd', plu: 'MK-500', description: 'Oat milk 1 L', quantityOrdered: 24, quantityReceived: 24,
    quantityBilled: 24, quantityMatched: 24, orderedCost: 4.4, receivedCost: 4.4, billedCost: 4.45, priceDifference: 1.2, status: 'Matched',
    reason: null, checkedAtUtc: minutesAgo(30), decidedAtUtc: null, decidedBy: null, decisionNote: null,
  },
];

export const receivedNotBilled: MatchReportRow[] = [
  { purchaseOrderId: 'po-4', purchaseOrderNumber: 10000004, purchaseOrderLineId: 'po-4-l1', storeCode: 'MAIN', plu: 'SN-410', description: 'Canvas sneakers', supplier: 'Sole Brothers', ordered: 12, received: 12, billed: 0, difference: 12, costEach: 28.5, value: 342, receivedAt: at('12:00:00', daysBefore(3)) },
];

export const billedNotReceived: MatchReportRow[] = [
  { purchaseOrderId: 'po-3', purchaseOrderNumber: 10000003, purchaseOrderLineId: 'po-3-l1', storeCode: 'MAIN', plu: 'TS-100-M', description: 'Cotton T-shirt, medium', supplier: 'Cotton Co. Ltd', ordered: 48, received: 0, billed: 48, difference: -48, costEach: 6.4, value: 307.2, receivedAt: null },
];

export const landedCostRows: LandedCostRow[] = [
  { id: 'lc-2', number: 100000000002, storeCode: 'MAIN', date: today, status: 'Draft', basis: 'Value', reference: 'FR-881', total: 60, receipts: 1, note: null, postedAtUtc: null, postedBy: null },
  { id: 'lc-1', number: 100000000001, storeCode: 'MAIN', date: daysBefore(9), status: 'Posted', basis: 'Weight', reference: 'CUS-17', total: 145.5, receipts: 2, note: 'Customs', postedAtUtc: minutesAgo(60 * 24 * 9), postedBy: 'sara' },
];

export const landedReceipts: ReceiptChoice[] = [
  { purchaseOrderId: 'po-1', number: 10000001, storeCode: 'MAIN', locationCode: 'POS', receivedAt: at('15:20:00', daysBefore(5)), supplier: 'Cotton Co. Ltd', description: 'Weekly order', lines: 2, value: 412.8 },
  { purchaseOrderId: 'po-4', number: 10000004, storeCode: 'MAIN', locationCode: 'POS', receivedAt: at('12:00:00', daysBefore(3)), supplier: 'Sole Brothers', description: null, lines: 1, value: 342 },
];

export function landedCostDetail(id: string, status?: LandedCostDetail['status']): LandedCostDetail {
  const row = landedCostRows.find((v) => v.id === id) ?? landedCostRows[0];
  const s = status ?? row.status;
  return {
    id: row.id, version: `v-${s}`, number: row.number, storeId: 's-main', storeCode: row.storeCode, date: row.date, status: s, basis: row.basis, reference: row.reference, note: row.note,
    total: row.total,
    charges: [{ kind: 'Freight', description: 'Truck from Mississauga', amount: row.total }],
    receipts: [{ purchaseOrderId: 'po-1', number: 10000001, receivedAt: at('15:20:00', daysBefore(5)), supplier: 'Cotton Co. Ltd', value: 412.8 }],
    allocations: [
      { purchaseOrderLineId: 'po-1-l1', itemId: 'i-1', plu: 'TS-100-M', description: 'Cotton T-shirt, medium', quantity: 48, value: 307.2, weight: 9.6, share: 44.65, onHand: 40, toInventory: 37.21, toCostOfGoods: 7.44 },
      { purchaseOrderLineId: 'po-1-l2', itemId: 'i-8', plu: 'MK-500', description: 'Oat milk 1 L', quantity: 24, value: 105.6, weight: 24, share: 15.35, onHand: 24, toInventory: 15.35, toCostOfGoods: 0 },
    ],
    createdBy: 'sara', postedAtUtc: s === 'Posted' ? row.postedAtUtc ?? minutesAgo(1) : null, postedBy: s === 'Posted' ? 'sara' : null,
  };
}

export const reorderRows: ReorderRow[] = [
  { itemId: 'i-1', plu: 'TS-100-M', name: 'Cotton T-shirt, medium', supplierId: 'sp-1', supplier: 'Cotton Co. Ltd', onHand: 10, onOrder: 0, customerOrdered: 0, unitsSold: 90, averageDailySales: 3, leadTimeDays: 3, reorderPoint: 0, reorderLevel: 15, targetStock: 24, caseQuantity: 12, minimumOrder: 0, suggestedPieces: 24, orderQuantity: 2, costEach: 6.4, value: 153.6 },
  { itemId: 'i-3', plu: 'SN-410', name: 'Canvas sneakers', supplierId: 'sp-2', supplier: 'Sole Brothers', onHand: 1, onOrder: 0, customerOrdered: 0, unitsSold: 0, averageDailySales: 0, leadTimeDays: 7, reorderPoint: 3, reorderLevel: 3, targetStock: 8, caseQuantity: 1, minimumOrder: 0, suggestedPieces: 7, orderQuantity: 7, costEach: 28.5, value: 199.5 },
  { itemId: 'i-5', plu: 'SK-001', name: 'Sport socks, 3 pack', supplierId: 'sp-1', supplier: 'Cotton Co. Ltd', onHand: 260, onOrder: 0, customerOrdered: 0, unitsSold: 30, averageDailySales: 1, leadTimeDays: 3, reorderPoint: 0, reorderLevel: 10, targetStock: 24, caseQuantity: 1, minimumOrder: 0, suggestedPieces: 0, orderQuantity: 0, costEach: 3.1, value: 0 },
];

export const supplierPerformance: SupplierPerformance = {
  from: daysBefore(29),
  to: today,
  rows: [
    { supplierId: 'sp-1', supplier: 'Cotton Co. Ltd', orders: 4, onTime: 3, onTimePercent: 75, orderedPieces: 200, receivedPieces: 196, fillRatePercent: 98, priceIncreases: 1, priceDecreases: 0, averagePriceChangePercent: 4.69, returns: 1, returnedPieces: 3, returnedValue: 19.2, averageLeadDays: 4.5, receivedValue: 1310.4 },
    { supplierId: 'sp-2', supplier: 'Sole Brothers', orders: 2, onTime: 2, onTimePercent: 100, orderedPieces: 24, receivedPieces: 18, fillRatePercent: 75, priceIncreases: 0, priceDecreases: 1, averagePriceChangePercent: -2.5, returns: 0, returnedPieces: 0, returnedValue: 0, averageLeadDays: 6, receivedValue: 513 },
  ],
};

export const supplySettings: SupplySettings = {
  version: 'v-1', storeId: 's-main', blockExpiredSales: true, expiryWarningDays: 30, quantityTolerancePercent: 0, quantityToleranceUnits: 0, priceTolerancePercent: 2,
  priceToleranceAmount: 0, salesWindowDays: 30, safetyStockDays: 7, orderCoverDays: 14, defaultLeadTimeDays: 7, approverMustDiffer: false,
};
