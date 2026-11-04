/**
 * What the supply chain pages work out on the page: the words and chip colours of their statuses, receiving in batches
 * (the expiry offered from the shelf life, the checks before a receipt is sent), totals, which steps a requisition
 * offers, and the lines a reorder suggestion sends.
 */
import type { ChipTone } from '@/components/ui/page';
import { addDays } from './dates';
import { parseNumber } from './forms';
import type {
  LandedCharge,
  PurchaseOrderLine,
  ReceiveRequest,
  ReceivedBatch,
  ReorderLineRequest,
  ReorderRow,
  RequisitionStatus,
} from './types';

type Status = Record<string, { label: string; tone: ChipTone }>;

export const PO_STATUS: Status = {
  Draft: { label: 'Draft', tone: 'plain' },
  Posted: { label: 'Ordered', tone: 'warn' },
  Received: { label: 'Received', tone: 'ok' },
};

export const REQUISITION_STATUS: Status = {
  Draft: { label: 'Draft', tone: 'plain' },
  Submitted: { label: 'Waiting for approval', tone: 'warn' },
  Approved: { label: 'Approved', tone: 'ok' },
  Rejected: { label: 'Rejected', tone: 'danger' },
  Ordered: { label: 'Ordered', tone: 'dark' },
};

export const MATCH_STATUS: Status = {
  Matched: { label: 'Matched', tone: 'ok' },
  Held: { label: 'Held', tone: 'danger' },
  Accepted: { label: 'Accepted', tone: 'ok' },
  Rejected: { label: 'Rejected', tone: 'plain' },
};

export const LANDED_STATUS: Status = {
  Draft: { label: 'Draft', tone: 'plain' },
  Posted: { label: 'Posted', tone: 'ok' },
};

export const LANDED_BASIS: Record<string, string> = {
  Value: 'By value',
  Quantity: 'By quantity',
  Weight: 'By weight',
};

export const CHARGE_KINDS: Record<string, string> = {
  Freight: 'Freight',
  Duty: 'Duty',
  Insurance: 'Insurance',
  Other: 'Other',
};

/** Adds up money or quantities in thousandths, so 0.1 + 0.2 stays 0.3. */
export function sum<T>(rows: readonly T[], value: (row: T) => number): number {
  let total = 0;
  for (const row of rows) total += Math.round((value(row) || 0) * 1000);
  return total / 1000;
}

/** Money rounded to cents. */
export function cents(value: number): number {
  return Math.round(value * 100) / 100 || 0;
}

/** A requisition's total at the expected costs, which the approver's limit is checked against. */
export function requisitionTotal(lines: ReadonlyArray<{ quantity: number; costEach: number }>): number {
  return cents(sum(lines, (l) => l.quantity * l.costEach));
}

export function chargesTotal(charges: readonly LandedCharge[]): number {
  return cents(sum(charges, (c) => c.amount));
}

/**
 * The expiry date offered when goods arrive: the day they were made (or today) plus the item's shelf life, as the server
 * works it out (`Fefo.SuggestedExpiry`); none without a shelf life.
 */
export function suggestedExpiry(shelfLifeDays: number, madeOn: string | null, today: string): string | null {
  return shelfLifeDays > 0 ? addDays(madeOn || today, shelfLifeDays) : null;
}

/** "Expired 3 days ago", "Expires today", "12 days left", with the chip colour: red when gone, sun within `warnDays`. */
export function expiryText(daysLeft: number | null, warnDays = 30): { text: string; tone: ChipTone } {
  if (daysLeft === null) return { text: 'No expiry', tone: 'plain' };
  if (daysLeft < 0) return { text: daysLeft === -1 ? 'Expired yesterday' : `Expired ${-daysLeft} days ago`, tone: 'danger' };
  if (daysLeft === 0) return { text: 'Expires today', tone: 'danger' };
  return { text: daysLeft === 1 ? '1 day left' : `${daysLeft} days left`, tone: daysLeft <= warnDays ? 'warn' : 'ok' };
}

// Receiving ----------------------------------------------------------------------------------------------------------

/** One batch as the receive dialog holds it: text boxes. */
export interface BatchForm {
  batchNumber: string;
  pieces: string;
  expiresOn: string;
  madeOn: string;
}

/** One line of the receive dialog: the pieces and cost that arrived, and for an item kept in batches, its batches. */
export interface ReceiveForm {
  lineId: string;
  pieces: string;
  costEach: string;
  batches: BatchForm[];
}

/** The receive dialog's first state: everything ordered arrives at the order's cost, one batch per batch-kept line. */
export function receiveForms(lines: readonly PurchaseOrderLine[], today: string): ReceiveForm[] {
  return lines.map((l) => ({
    lineId: l.id,
    pieces: String(l.pieces),
    costEach: String(l.costEach),
    batches: l.tracksBatches ? [{ batchNumber: '', pieces: String(l.pieces), expiresOn: suggestedExpiry(l.shelfLifeDays, null, today) ?? '', madeOn: '' }] : [],
  }));
}

/**
 * What stops a receipt from going: pieces or costs that are not numbers, and for items kept in batches, a batch without
 * a number, or batches that do not add up to the pieces received. By line id; empty when it may go.
 */
export function receiveErrors(lines: readonly PurchaseOrderLine[], forms: readonly ReceiveForm[]): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const form of forms) {
    const line = lines.find((l) => l.id === form.lineId);
    if (!line) continue;
    const pieces = parseNumber(form.pieces);
    if (pieces === null || pieces < 0) {
      errors[form.lineId] = 'Enter the pieces received (0 or more).';
      continue;
    }
    const cost = parseNumber(form.costEach);
    if (cost === null || cost < 0) {
      errors[form.lineId] = 'Enter the cost of one piece.';
      continue;
    }
    if (!line.tracksBatches || pieces === 0) continue;
    const used = form.batches.filter((b) => b.batchNumber.trim() || (parseNumber(b.pieces) ?? 0) !== 0);
    if (used.length === 0) {
      errors[form.lineId] = 'Kept in batches: enter the batch number and expiry of what arrived.';
      continue;
    }
    if (used.some((b) => !b.batchNumber.trim())) {
      errors[form.lineId] = 'Every batch needs its number.';
      continue;
    }
    if (used.some((b) => parseNumber(b.pieces) === null)) {
      errors[form.lineId] = 'Enter the pieces of each batch.';
      continue;
    }
    if (used.some((b) => b.madeOn && b.expiresOn && b.expiresOn < b.madeOn)) {
      errors[form.lineId] = 'A batch cannot expire before it was made.';
      continue;
    }
    const total = sum(used, (b) => parseNumber(b.pieces) ?? 0);
    if (total !== pieces) errors[form.lineId] = `The batches add up to ${total}, not the ${pieces} pieces received.`;
  }
  return errors;
}

/** The receipt the server takes from the dialog (call {@link receiveErrors} first). */
export function receiveRequest(version: string, shippingCost: number, lines: readonly PurchaseOrderLine[], forms: readonly ReceiveForm[]): ReceiveRequest {
  return {
    version,
    shippingCost,
    lines: forms.map((form) => {
      const line = lines.find((l) => l.id === form.lineId);
      const batches: ReceivedBatch[] = (line?.tracksBatches ? form.batches : [])
        .filter((b) => b.batchNumber.trim())
        .map((b) => ({
          batchNumber: b.batchNumber.trim().toUpperCase(),
          pieces: parseNumber(b.pieces) ?? 0,
          expiresOn: b.expiresOn || null,
          madeOn: b.madeOn || null,
        }));
      return { lineId: form.lineId, pieces: parseNumber(form.pieces) ?? 0, costEach: parseNumber(form.costEach), batches: batches.length ? batches : null };
    }),
  };
}

// Requisitions -------------------------------------------------------------------------------------------------------

/** The steps a requisition offers now, for a user who may manage purchasing and perhaps approve. */
export function requisitionSteps(status: RequisitionStatus, mayApprove: boolean) {
  return {
    edit: status === 'Draft' || status === 'Rejected',
    submit: status === 'Draft',
    decide: status === 'Submitted' && mayApprove,
    order: status === 'Approved',
    remove: status === 'Draft' || status === 'Rejected',
  };
}

// Reorder suggestions ------------------------------------------------------------------------------------------------

/** The suggestions chosen, with the pieces typed for them (the suggestion when untouched); nothing with no pieces. */
export function reorderLines(rows: readonly ReorderRow[], chosen: ReadonlySet<string>, pieces: Readonly<Record<string, string>>): ReorderLineRequest[] {
  return rows
    .filter((r) => chosen.has(r.itemId))
    .map((r) => ({ itemId: r.itemId, pieces: parseNumber(pieces[r.itemId] ?? String(r.suggestedPieces)) ?? 0, supplierId: r.supplierId }))
    .filter((l) => l.pieces > 0);
}

/** How many orders the chosen lines make: one per supplier (lines without one make one more). */
export function ordersFor(lines: readonly ReorderLineRequest[]): number {
  return new Set(lines.map((l) => l.supplierId ?? '')).size;
}
