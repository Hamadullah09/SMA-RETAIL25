import {
  BarChart3,
  Boxes,
  ClipboardList,
  CreditCard,
  FileText,
  LayoutDashboard,
  Package,
  Receipt,
  Settings,
  ShoppingCart,
  Truck,
  Users,
  Warehouse,
  type LucideIcon,
} from 'lucide-react';

/**
 * Every screen in the application, declared once.
 *
 * There were three lists: the rail in sidebar.tsx, the command palette in command-palette.tsx, and
 * the cards on the index pages. Nothing kept them in step, and they had already drifted in ways
 * that were invisible from any one of them:
 *
 * - "Inventory" named `/catalog/products` in the rail and `/inventory` in the palette. Opposite
 *   destinations behind the same word, and both pages render the heading "Inventory".
 * - The palette could not reach `/dashboard`, `/sales`, `/orders`, `/admin/backup`,
 *   `/admin/accounting`, `/admin/rfid-health` or seven of the nine reports.
 * - The rail ignored permissions entirely while the palette filtered by them, so the same person
 *   saw different sets of destinations depending on how they went looking.
 *
 * One list means those cannot disagree again — a new route is added here or it exists nowhere.
 */
export interface AppRoute {
  href: string;
  /** What this screen is called. The same word everywhere it is named. */
  label: string;
  icon: LucideIcon;
  /**
   * Where it sits in the rail. Absent means reachable but not listed — a child page, or a screen
   * you arrive at from somewhere else rather than navigate to.
   */
  section?: 'Main' | 'Operations' | 'Others';
  /** The rail row this sits under. */
  parent?: string;
  /**
   * The permission that governs it, where one is actually enforced in the page.
   *
   * Deliberately absent on the report leaves and a few admin screens: they carry no client-side
   * guard today and are authorised by the API. Inventing a key here would hide working navigation
   * from somebody the server would have let through — a worse failure than showing a row that
   * turns out to be refused, because a hidden row cannot be asked about.
   */
  permission?: string;
  /** Extra words somebody might search for. The label is always searched; these are the synonyms. */
  keywords?: string;
  /** Help topic slug. Resolved from the route so Ctrl+H cannot open a guide for another screen. */
  helpTopic?: string;
  /** Hidden from the command palette. Only for screens that are not a destination. */
  hiddenFromPalette?: boolean;
  /**
   * Shown as an expandable row beneath its parent in the rail.
   *
   * Not every child is one. The registry knows all thirty-three screens so the palette, breadcrumbs
   * and help can reach them, but hanging all eight reports and all ten admin screens off the rail
   * would double its height and bury the eleven rows somebody actually navigates by. Reports and
   * Administration keep their own index pages, which is where that breadth belongs; the rail keeps
   * the shape people have already learned.
   */
  inRail?: boolean;
}

export const ROUTES: readonly AppRoute[] = [
  // --- Main -----------------------------------------------------------------------------------
  {
    href: '/dashboard',
    label: 'Dashboard',
    icon: LayoutDashboard,
    section: 'Main',
    keywords: 'home today overview takings summary',
    helpTopic: 'dashboard',
  },
  {
    href: '/pos',
    label: 'Point of Sale',
    icon: ShoppingCart,
    section: 'Main',
    permission: 'pos.sell',
    keywords: 'till sell cash register checkout scan',
    helpTopic: 'pos',
  },
  {
    href: '/sales',
    label: 'Previous sales',
    icon: Receipt,
    section: 'Main',
    keywords: 'history transactions receipts refund return reprint',
    helpTopic: 'sales',
  },

  // The rail's "Inventory" row means the catalogue — what you sell. "Stock" means how many you
  // have. They were the pair the two old lists disagreed about.
  {
    href: '/catalog/products',
    label: 'Inventory',
    icon: Package,
    section: 'Main',
    permission: 'catalog.read',
    keywords: 'products items catalogue sku barcode price',
    helpTopic: 'products',
  },
  {
    href: '/catalog/products',
    label: 'Products',
    inRail: true,
    icon: Package,
    parent: '/catalog/products',
    permission: 'catalog.read',
    hiddenFromPalette: true,
  },
  {
    href: '/catalog/bulk',
    label: 'Bulk changes',
    inRail: true,
    icon: Package,
    parent: '/catalog/products',
    permission: 'catalog.bulk_adjust',
    keywords: 'batch reprice price increase tax flags',
    helpTopic: 'bulk',
  },

  {
    href: '/inventory',
    label: 'Stock',
    icon: Warehouse,
    section: 'Main',
    permission: 'catalog.read',
    keywords: 'stock on hand levels quantity shortage reorder',
    helpTopic: 'inventory',
  },
  {
    href: '/inventory',
    label: 'Stock on hand',
    inRail: true,
    icon: Warehouse,
    parent: '/inventory',
    permission: 'catalog.read',
    hiddenFromPalette: true,
  },
  {
    href: '/inventory/counts',
    label: 'Stock counts',
    inRail: true,
    icon: Warehouse,
    parent: '/inventory',
    permission: 'inventory.count',
    keywords: 'stocktake count variance shrinkage',
    helpTopic: 'counts',
  },
  {
    href: '/inventory/transfers',
    label: 'Transfers',
    inRail: true,
    icon: Warehouse,
    parent: '/inventory',
    permission: 'inventory.transfer',
    keywords: 'move between stores locations van',
    helpTopic: 'transfers',
  },

  {
    href: '/customers',
    label: 'Customers',
    icon: Users,
    section: 'Main',
    permission: 'customer.read',
    keywords: 'clients accounts loyalty',
    helpTopic: 'customers',
  },

  // --- Operations -----------------------------------------------------------------------------
  {
    href: '/purchasing/suppliers',
    label: 'Suppliers',
    icon: Truck,
    section: 'Operations',
    permission: 'purchasing.read',
    keywords: 'vendors reorder',
    helpTopic: 'suppliers',
  },
  {
    href: '/purchasing',
    label: 'Purchasing',
    icon: FileText,
    section: 'Operations',
    permission: 'purchasing.read',
    keywords: 'purchase orders po receiving goods in',
    helpTopic: 'purchasing',
  },
  {
    href: '/receivables',
    label: 'Receivables',
    icon: CreditCard,
    section: 'Operations',
    permission: 'ar.read',
    keywords: 'ar invoices statements owed debtors credit',
    helpTopic: 'receivables',
  },
  {
    href: '/orders',
    label: 'Orders & Layaways',
    icon: ClipboardList,
    section: 'Operations',
    keywords: 'customer orders layaway quotes deposits',
    helpTopic: 'orders',
  },

  // --- Others ---------------------------------------------------------------------------------
  {
    href: '/reports',
    label: 'Reports',
    icon: BarChart3,
    section: 'Others',
    keywords: 'analysis figures',
    helpTopic: 'reports',
  },
  { href: '/reports/sales', label: 'Sales log', icon: BarChart3, parent: '/reports', keywords: 'history transactions receipts export', helpTopic: 'reports' },
  { href: '/reports/sales-analysis', label: 'Sales analysis', icon: BarChart3, parent: '/reports', keywords: 'margin profit by product department trend', helpTopic: 'reports' },
  { href: '/reports/stock-position', label: 'Stock position', icon: BarChart3, parent: '/reports', keywords: 'on hand understock reorder', helpTopic: 'reports' },
  { href: '/reports/stock-value', label: 'Stock value', icon: BarChart3, parent: '/reports', keywords: 'valuation cost worth', helpTopic: 'reports' },
  { href: '/reports/stock-received', label: 'Stock received', icon: BarChart3, parent: '/reports', keywords: 'goods in deliveries', helpTopic: 'reports' },
  { href: '/reports/on-order', label: 'On order', icon: BarChart3, parent: '/reports', keywords: 'incoming due purchase orders', helpTopic: 'reports' },
  { href: '/reports/reward-points', label: 'Reward points', icon: BarChart3, parent: '/reports', keywords: 'loyalty points balances', helpTopic: 'reports' },
  { href: '/reports/tax', label: 'Tax', icon: BarChart3, parent: '/reports', keywords: 'vat gst return liability', helpTopic: 'reports' },

  {
    href: '/admin',
    label: 'Administration',
    icon: Settings,
    section: 'Others',
    permission: 'settings.read',
    keywords: 'setup configuration',
    helpTopic: 'admin',
  },
  { href: '/admin/settings', label: 'Setup', icon: Settings, parent: '/admin', permission: 'settings.read', keywords: 'taxes pos printers hardware users stations tenders numbering departments categories groupings', helpTopic: 'settings' },
  { href: '/admin/staff', label: 'Staff', icon: Users, parent: '/admin', permission: 'staff.read', keywords: 'time clock hours commission payroll colleagues', helpTopic: 'staff' },
  { href: '/admin/audit', label: 'Audit log', icon: FileText, parent: '/admin', permission: 'audit.read', keywords: 'history who changed what', helpTopic: 'audit' },
  { href: '/admin/rfid', label: 'RFID readers', icon: Boxes, parent: '/admin', permission: 'terminals.read', keywords: 'reader antenna power frequency region tags epc uhf hardware', helpTopic: 'rfid' },
  { href: '/admin/rfid-health', label: 'RFID health', icon: Boxes, parent: '/admin', keywords: 'reader status diagnostics signal antenna', helpTopic: 'rfid' },
  { href: '/admin/accounting', label: 'Accounting', icon: FileText, parent: '/admin', keywords: 'ledger export nominal codes', helpTopic: 'accounting' },
  { href: '/admin/backup', label: 'Backup and restore', icon: Settings, parent: '/admin', permission: 'system.backup', keywords: 'database snapshot restore export save', helpTopic: 'backup' },
  { href: '/admin/undelete', label: 'Undelete items', icon: Settings, parent: '/admin', permission: 'catalog.delete', keywords: 'restore deleted recover bin', helpTopic: 'undelete' },
  { href: '/admin/migration', label: 'Bring data across', icon: Settings, parent: '/admin', permission: 'migration.run', keywords: 'migration import legacy dbf cutover convert', helpTopic: 'migration' },
  { href: '/admin/year-end', label: 'Year end', icon: Settings, parent: '/admin', permission: 'inventory.year_end', keywords: 'fiscal close archive history rollup', helpTopic: 'year-end' },
];

/** The rail, in the order it is drawn. */
export const NAV_SECTIONS: ReadonlyArray<{ heading: AppRoute['section']; items: AppRoute[] }> = (
  ['Main', 'Operations', 'Others'] as const
).map((heading) => ({
  heading,
  items: ROUTES.filter((route) => route.section === heading),
}));

/** Every child of a route — for breadcrumbs, index pages and help. */
export function childrenOf(href: string): AppRoute[] {
  return ROUTES.filter((route) => route.parent === href);
}

/** Only the children the rail expands to show. */
export function railChildrenOf(href: string): AppRoute[] {
  return ROUTES.filter((route) => route.parent === href && route.inRail);
}

/** Everything the command palette can reach. */
export const PALETTE_ROUTES: readonly AppRoute[] = ROUTES.filter((route) => !route.hiddenFromPalette);
