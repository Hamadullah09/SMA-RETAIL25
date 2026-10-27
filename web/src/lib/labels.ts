/**
 * Plain words for the server's enum names (they arrive as strings: `TransferOut`, `Shipped`…), the way the desktop
 * shows them. Unknown names are split into words, so a value added on the server still reads well.
 */
import type { ChipTone } from '@/components/ui/page';

export function words(name: string | null | undefined): string {
  if (!name) return '';
  const spaced = name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

export const MOVEMENT_KINDS: Record<string, string> = {
  Opening: 'Opening stock',
  Sale: 'Sale',
  Return: 'Return',
  Void: 'Voided sale',
  Receipt: 'Received',
  Adjustment: 'Adjustment',
  Import: 'Import',
  Count: 'Stock count',
  TransferOut: 'Transfer out',
  TransferIn: 'Transfer in',
  CaseBreakOut: 'Case broken',
  CaseBreakIn: 'From a case',
};

export const TRANSFER_STATUS: Record<string, { label: string; tone: ChipTone }> = {
  Draft: { label: 'Draft', tone: 'plain' },
  Shipped: { label: 'In transit', tone: 'warn' },
  Received: { label: 'Received', tone: 'ok' },
  Cancelled: { label: 'Cancelled', tone: 'plain' },
};

export const COUNT_STATUS: Record<string, { label: string; tone: ChipTone }> = {
  Open: { label: 'Open', tone: 'warn' },
  Posted: { label: 'Posted', tone: 'ok' },
  Cancelled: { label: 'Cancelled', tone: 'plain' },
};

export const GIFT_CARD_KINDS: Record<string, string> = {
  Card: 'Gift card',
  Certificate: 'Certificate',
};

export const GIFT_CARD_HISTORY: Record<string, string> = {
  Issue: 'Sold',
  Reload: 'Reloaded',
  Redeem: 'Spent',
  Refund: 'Refunded',
  Adjust: 'Adjusted',
};

/** The audit log's actions (`Data/Auditing.cs`). */
export const AUDIT_ACTIONS = [
  'Added',
  'Changed',
  'Deleted',
  'Undeleted',
  'Sign in',
  'Failed sign-in',
  'Approval',
  'Approval refused',
  'Void sale',
  'Void invoice',
  'No sale',
  'End of day',
  'Backup',
  'Restore',
  'Import',
  'Year end',
  'Time clock',
  'Time clock edit',
  'Gift card adjust',
];

export const COMMISSION_SCOPES: Record<string, string> = {
  AllItems: 'All items',
  Department: 'A department',
  Category: 'A category',
  Item: 'One item',
};

/** The commission rule's `target`: how its rate is read (CommissionBasis on the desktop). */
export const COMMISSION_TARGETS: Record<string, string> = {
  PercentOfNetSales: 'Percent of net sales',
  AmountPerUnit: 'Amount per unit',
};

export function label(map: Record<string, string>, name: string | null | undefined): string {
  if (!name) return '';
  return map[name] ?? words(name);
}
