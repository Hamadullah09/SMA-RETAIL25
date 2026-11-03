/**
 * The pages of the web app, the permission each needs (docs/API.md, "Web app pages") and the commands of the Ctrl+K
 * palette. The navigation shows only what the signed-in user may open; Ctrl+1 … Ctrl+0 open the first ten pages, with
 * the same key on every account, like the desktop's screens.
 */

export const P = {
  webSignIn: 'web.signin',
  dashboard: 'dashboard.view',
  reports: 'reports.view',
  reportsStaff: 'reports.staff',
  inventoryView: 'inventory.view',
  inventoryEdit: 'inventory.edit',
  clientsView: 'clients.view',
  clientsEdit: 'clients.edit',
  suppliersEdit: 'suppliers.edit',
  giftCards: 'giftcards.issue',
  timeClockEdit: 'timeclock.edit',
  setupUsers: 'setup.users',
  setupStore: 'setup.store',
  storesManage: 'stores.manage',
  storesAll: 'stores.all',
  audit: 'audit.view',
  syncSend: 'sync.send',
  hrView: 'hr.view',
  hrEdit: 'hr.edit',
  hrAttendance: 'hr.attendance',
  hrLeave: 'hr.leave',
  payrollRun: 'payroll.run',
  payrollApprove: 'payroll.approve',
} as const;

/** The icon of a page, by name; `components/shell/icons.tsx` maps the names to lucide icons. */
export type IconName =
  | 'home'
  | 'tills'
  | 'packages'
  | 'journal'
  | 'inventory'
  | 'clients'
  | 'suppliers'
  | 'stock'
  | 'giftcards'
  | 'reports'
  | 'staff'
  | 'users'
  | 'roles'
  | 'stores'
  | 'setup'
  | 'audit'
  | 'account'
  | 'new'
  | 'upload'
  | 'signout'
  | 'report'
  | 'employees'
  | 'attendance'
  | 'shifts'
  | 'leave'
  | 'payroll'
  | 'hrreports'
  | 'hrsetup';

export interface PageDef {
  key: string;
  title: string;
  /** With the trailing slash the static export uses. */
  path: string;
  icon: IconName;
  /** The permission besides `web.signin`; null for every signed-in user. */
  permission: string | null;
  /** In the navigation panel (detail pages and Account are not). */
  nav: boolean;
  /** For detail pages: the page the navigation highlights. */
  parent?: string;
  shortcut?: string;
  /** More words the palette finds the page by. */
  keywords?: string;
  /** The group the page is listed under in the navigation and the palette (`HR`). */
  group?: string;
  /** Opens with any one of these permissions instead of `permission` alone. */
  anyOf?: readonly string[];
}

export const PAGES: readonly PageDef[] = [
  { key: 'dashboard', title: 'Dashboard', path: '/', icon: 'home', permission: P.dashboard, nav: true, shortcut: 'Ctrl+1', keywords: 'home sales today' },
  { key: 'tills', title: 'Tills', path: '/tills/', icon: 'tills', permission: null, nav: true, shortcut: 'Ctrl+2', keywords: 'stations enrol enroll monitor' },
  { key: 'sales', title: 'Sales Journal', path: '/sales/', icon: 'journal', permission: P.reports, nav: true, shortcut: 'Ctrl+3', keywords: 'receipts invoices sales' },
  { key: 'items', title: 'Inventory', path: '/items/', icon: 'inventory', permission: P.inventoryView, nav: true, shortcut: 'Ctrl+4', keywords: 'items products prices' },
  { key: 'clients', title: 'Clients', path: '/clients/', icon: 'clients', permission: P.clientsView, nav: true, shortcut: 'Ctrl+5', keywords: 'customers' },
  { key: 'suppliers', title: 'Suppliers', path: '/suppliers/', icon: 'suppliers', permission: P.inventoryView, nav: true, shortcut: 'Ctrl+6', keywords: 'vendors' },
  { key: 'stock', title: 'Stock', path: '/stock/', icon: 'stock', permission: P.inventoryView, nav: true, shortcut: 'Ctrl+7', keywords: 'movements transfers stock counts' },
  { key: 'gift-cards', title: 'Gift cards', path: '/gift-cards/', icon: 'giftcards', permission: P.giftCards, nav: true, shortcut: 'Ctrl+8', keywords: 'certificates balance' },
  { key: 'reports', title: 'Reports', path: '/reports/', icon: 'reports', permission: P.reports, nav: true, shortcut: 'Ctrl+9' },
  { key: 'staff', title: 'Staff', path: '/staff/', icon: 'staff', permission: P.reportsStaff, nav: true, shortcut: 'Ctrl+0', keywords: 'time clock hours commission' },
  { key: 'packages', title: 'Packages', path: '/packages/', icon: 'packages', permission: null, nav: true, keywords: 'upload usb smapkg sync batches' },
  { key: 'users', title: 'Users', path: '/users/', icon: 'users', permission: P.setupUsers, nav: true, keywords: 'password pin staff code' },
  { key: 'roles', title: 'Roles', path: '/roles/', icon: 'roles', permission: P.setupUsers, nav: true, keywords: 'permissions' },
  { key: 'stores', title: 'Stores', path: '/stores/', icon: 'stores', permission: P.storesManage, nav: true, keywords: 'branches stations' },
  { key: 'settings', title: 'Setup', path: '/settings/', icon: 'setup', permission: P.setupStore, nav: true, keywords: 'settings business receipt branding logo watermark currency' },
  { key: 'audit', title: 'Audit log', path: '/audit/', icon: 'audit', permission: P.audit, nav: true, keywords: 'history changes who' },
  { key: 'sale', title: 'Sale', path: '/sales/view/', icon: 'journal', permission: P.reports, nav: false, parent: 'sales' },
  { key: 'item', title: 'Item', path: '/items/edit/', icon: 'inventory', permission: P.inventoryEdit, nav: false, parent: 'items' },
  { key: 'report', title: 'Report', path: '/reports/view/', icon: 'reports', permission: P.reports, nav: false, parent: 'reports' },
  { key: 'account', title: 'Change password', path: '/account/', icon: 'account', permission: null, nav: false, keywords: 'account password' },
  // HR and payroll (phase E3).
  { key: 'hr-employees', title: 'Employees', path: '/hr/employees/', icon: 'employees', permission: P.hrView, nav: true, group: 'HR', keywords: 'hr people staff cnic records' },
  { key: 'hr-attendance', title: 'Attendance', path: '/hr/attendance/', icon: 'attendance', permission: P.hrView, nav: true, group: 'HR', keywords: 'hr late absent present overtime sheet corrections' },
  { key: 'hr-shifts', title: 'Shifts and rosters', path: '/hr/shifts/', icon: 'shifts', permission: P.hrView, nav: true, group: 'HR', keywords: 'hr roster week schedule' },
  { key: 'hr-leave', title: 'Leave', path: '/hr/leave/', icon: 'leave', permission: P.hrView, nav: true, group: 'HR', keywords: 'hr holiday vacation requests balances approvals' },
  { key: 'hr-payroll', title: 'Payroll', path: '/hr/payroll/', icon: 'payroll', permission: P.payrollRun, anyOf: [P.payrollRun, P.payrollApprove], nav: true, group: 'HR', keywords: 'hr salary salaries payslips loans advances settlement' },
  { key: 'hr-reports', title: 'HR reports', path: '/hr/reports/', icon: 'hrreports', permission: P.hrView, nav: true, group: 'HR', keywords: 'hr salary sheet tax certificate eobi social security headcount turnover overtime' },
  { key: 'hr-settings', title: 'HR settings', path: '/hr/settings/', icon: 'hrsetup', permission: P.hrView, nav: true, group: 'HR', keywords: 'hr departments designations leave types holidays payroll rules tax slabs country' },
  { key: 'hr-employee', title: 'Employee', path: '/hr/employees/edit/', icon: 'employees', permission: P.hrView, nav: false, parent: 'hr-employees', group: 'HR' },
  { key: 'hr-run', title: 'Payroll run', path: '/hr/payroll/run/', icon: 'payroll', permission: P.payrollRun, anyOf: [P.payrollRun, P.payrollApprove], nav: false, parent: 'hr-payroll', group: 'HR' },
  { key: 'hr-payslip', title: 'Payslip', path: '/hr/payroll/payslip/', icon: 'payroll', permission: P.payrollRun, anyOf: [P.payrollRun, P.payrollApprove], nav: false, parent: 'hr-payroll', group: 'HR' },
];

/** Whether a user with these permissions may open the page (any one of `anyOf` will do when it has some). */
export function canOpen(page: Pick<PageDef, 'permission' | 'anyOf'>, permissions: ReadonlySet<string> | readonly string[]): boolean {
  const has = (key: string) => (Array.isArray(permissions) ? permissions.includes(key) : (permissions as ReadonlySet<string>).has(key));
  if (!has(P.webSignIn)) return false;
  if (page.anyOf && page.anyOf.length > 0) return page.anyOf.some(has);
  return page.permission === null || has(page.permission);
}

/** The navigation panel's entries for this user, in order. */
export function navFor(permissions: ReadonlySet<string> | readonly string[]): PageDef[] {
  return PAGES.filter((page) => page.nav && canOpen(page, permissions));
}

/** Where a user lands: the dashboard when allowed, else their first page, else Change password. */
export function homeFor(permissions: ReadonlySet<string> | readonly string[]): string {
  const first = navFor(permissions)[0];
  return first ? first.path : '/account/';
}

/** `/sales/view` and `/sales/view/` are the same page. */
export function normalizePath(pathname: string): string {
  if (!pathname) return '/';
  const path = pathname.split(/[?#]/)[0];
  return path.endsWith('/') ? path : path + '/';
}

/** The page a path is, or undefined for unknown paths. */
export function pageForPath(pathname: string): PageDef | undefined {
  const path = normalizePath(pathname);
  return PAGES.find((page) => page.path === path);
}

/** The navigation entry to highlight for a path (a detail page highlights its list). */
export function navKeyForPath(pathname: string): string | undefined {
  const page = pageForPath(pathname);
  return page?.parent ?? page?.key;
}

/** The page a Ctrl+digit opens, for this user (undefined when the user may not open it). */
export function pageForShortcut(shortcut: string, permissions: ReadonlySet<string> | readonly string[]): PageDef | undefined {
  return navFor(permissions).find((page) => page.shortcut === shortcut);
}

// The Ctrl+K palette ------------------------------------------------------------------------------------------------

export type CommandKind = 'Screen' | 'Action' | 'Report';

export interface Command {
  id: string;
  title: string;
  kind: CommandKind;
  icon: IconName;
  /** Where it goes; actions without a path are handled by the shell (sign out). */
  path?: string;
  action?: 'sign-out';
  shortcut?: string;
  permission: string | null;
  anyOf?: readonly string[];
  keywords?: string;
  /** The group it belongs to (`HR`), shown next to its kind. */
  group?: string;
}

/** Common actions, as the desktop's Ctrl+K list offers them. */
export const ACTIONS: readonly Command[] = [
  { id: 'enrol', title: 'Enrol a till', kind: 'Action', icon: 'tills', path: '/tills/?enrol=1', permission: P.storesManage, keywords: 'new till station code' },
  { id: 'upload', title: 'Upload a package', kind: 'Action', icon: 'upload', path: '/packages/', permission: P.syncSend, keywords: 'usb smapkg' },
  { id: 'new-item', title: 'New item', kind: 'Action', icon: 'new', path: '/items/edit/', permission: P.inventoryEdit, keywords: 'add product' },
  { id: 'new-client', title: 'New client', kind: 'Action', icon: 'new', path: '/clients/?new=1', permission: P.clientsEdit, keywords: 'add customer' },
  { id: 'new-supplier', title: 'New supplier', kind: 'Action', icon: 'new', path: '/suppliers/?new=1', permission: P.suppliersEdit, keywords: 'add vendor' },
  { id: 'add-user', title: 'Add user', kind: 'Action', icon: 'new', path: '/users/?new=1', permission: P.setupUsers, keywords: 'new staff' },
  { id: 'new-role', title: 'New role', kind: 'Action', icon: 'new', path: '/roles/?new=1', permission: P.setupUsers },
  { id: 'add-store', title: 'Add store', kind: 'Action', icon: 'new', path: '/stores/?new=1', permission: P.storesManage, keywords: 'new branch' },
  { id: 'password', title: 'Change password', kind: 'Action', icon: 'account', path: '/account/', permission: null, keywords: 'account' },
  { id: 'sign-out', title: 'Sign out', kind: 'Action', icon: 'signout', action: 'sign-out', permission: null, keywords: 'log out switch user' },
  { id: 'new-employee', title: 'New employee', kind: 'Action', icon: 'new', path: '/hr/employees/edit/', permission: P.hrEdit, group: 'HR', keywords: 'hr add staff person' },
  { id: 'new-leave', title: 'New leave request', kind: 'Action', icon: 'new', path: '/hr/leave/?new=1', permission: P.hrView, group: 'HR', keywords: 'hr holiday vacation' },
  { id: 'new-run', title: 'New payroll run', kind: 'Action', icon: 'new', path: '/hr/payroll/?new=1', permission: P.payrollRun, group: 'HR', keywords: 'hr salary month' },
];

/** Everything the palette may offer this user: the pages, the actions and the reports of the catalog. */
export function commandsFor(
  permissions: ReadonlySet<string> | readonly string[],
  reports: ReadonlyArray<{ key: string; title: string; group: string }> = [],
): Command[] {
  const screens: Command[] = navFor(permissions).map((page) => ({
    id: 'page-' + page.key,
    title: 'Go to ' + page.title,
    kind: 'Screen',
    icon: page.icon,
    path: page.path,
    shortcut: page.shortcut,
    permission: page.permission,
    anyOf: page.anyOf,
    keywords: page.keywords,
    group: page.group,
  }));
  const actions = ACTIONS.filter((a) => canOpen(a, permissions));
  const reportCommands: Command[] = canOpen({ permission: P.reports }, permissions)
    ? reports.map((r) => ({
        id: 'report-' + r.key,
        title: r.title,
        kind: 'Report',
        icon: 'report',
        path: '/reports/view/?key=' + encodeURIComponent(r.key),
        permission: P.reports,
        keywords: r.group,
      }))
    : [];
  return [...screens, ...actions, ...reportCommands];
}

/**
 * The commands that match what was typed: every word must appear in the title or keywords. Titles that start with the
 * text come first, then the original order (screens, actions, reports).
 */
export function searchCommands(commands: readonly Command[], text: string): Command[] {
  const words = text.toLowerCase().split(/\s+/).filter(Boolean);
  if (words.length === 0) return [...commands];
  const scored: { command: Command; score: number; index: number }[] = [];
  commands.forEach((command, index) => {
    const title = command.title.toLowerCase();
    const plainTitle = title.replace(/^go to /, '');
    const haystack = `${title} ${command.keywords ?? ''} ${command.kind} ${command.group ?? ''}`.toLowerCase();
    if (!words.every((w) => haystack.includes(w))) return;
    const phrase = words.join(' ');
    const score = plainTitle.startsWith(phrase) ? 0 : title.includes(phrase) ? 1 : 2;
    scored.push({ command, score, index });
  });
  return scored.sort((a, b) => a.score - b.score || a.index - b.index).map((s) => s.command);
}
