/**
 * The audit log, grouped the way somebody actually asks about it.
 *
 * The screen offered a free-text "Record type" box, so finding anything meant knowing that the
 * table is called `SalesTransaction` and typing it exactly. Worse, it meant guessing wrong in a way
 * that looks like an answer: type `Sales` and you get an empty list, which reads as "nothing
 * happened" rather than "no such record type".
 *
 * The groups below are not invented. Every entity type listed was read out of `audit_log_entries`
 * on the live database, so each category has rows behind it.
 *
 * A sale is the case that makes the point: it is not one record but four — the transaction, its
 * lines, its tenders, and the drawer movement that went with it. Asking "what happened with sales
 * today" against a single-value filter could only ever return a quarter of the answer.
 */
export interface LogCategory {
  readonly id: string;
  readonly label: string;

  /** Empty means every type — the "All" case, which sends no filter at all. */
  readonly entityTypes: readonly string[];

  /** Why this grouping exists, shown as the tab's title attribute. */
  readonly hint: string;
}

export const LOG_CATEGORIES: readonly LogCategory[] = [
  {
    id: 'all',
    label: 'All',
    entityTypes: [],
    hint: 'Every recorded action, newest first',
  },
  {
    id: 'sales',
    label: 'Sales',
    entityTypes: ['SalesTransaction', 'SaleLine', 'SaleTender', 'DrawerSession', 'DrawerLedgerEntry', 'SupervisorApproval'],
    hint: 'Transactions, their lines and tenders, and the drawer movements that went with them',
  },
  {
    id: 'purchases',
    label: 'Purchases',
    entityTypes: ['PurchaseOrder', 'PurchaseOrderLine', 'PurchaseOrderReceipt', 'Supplier'],
    hint: 'Orders raised, received and the suppliers they were raised against',
  },
  {
    id: 'stock',
    label: 'Stock',
    entityTypes: ['StockLevel', 'StockLedgerEntry', 'SerializedUnit', 'StockCount', 'StockTransfer', 'Product'],
    hint: 'Every movement of stock, including tagged units and counts',
  },
  {
    id: 'people',
    label: 'People',
    entityTypes: ['ApplicationUser', 'StaffProfile', 'Customer', 'CustomerAccount'],
    hint: 'Staff and customer records',
  },
  {
    id: 'security',
    label: 'Security',
    entityTypes: ['ApplicationUser', 'RolePermission', 'Permission'],
    hint: 'Sign-ins, refusals and permission changes',
  },
  {
    id: 'settings',
    label: 'Settings',
    entityTypes: ['Station', 'TenderType', 'PricingRuleSetting', 'FiscalYear'],
    hint: 'Configuration: stations, tenders, pricing rules and fiscal years',
  },
];

export function logCategory(id: string): LogCategory {
  return LOG_CATEGORIES.find((c) => c.id === id) ?? LOG_CATEGORIES[0];
}
