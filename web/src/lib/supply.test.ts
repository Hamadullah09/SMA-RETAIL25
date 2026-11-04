import { describe, expect, it } from 'vitest';
import {
  chargesTotal,
  expiryText,
  ordersFor,
  receiveErrors,
  receiveForms,
  receiveRequest,
  reorderLines,
  requisitionSteps,
  requisitionTotal,
  suggestedExpiry,
  sum,
} from './supply';
import type { PurchaseOrderLine, ReorderRow } from './types';

function line(id: string, pieces: number, tracksBatches = false, shelfLifeDays = 0): PurchaseOrderLine {
  return {
    id, itemId: 'i-' + id, plu: 'P' + id, description: 'Item ' + id, supplier: null, caseQuantity: 1, orderQuantity: pieces, pieces, costEach: 2.5, orderedCost: 2.5,
    orderCost: pieces * 2.5, quantityReceived: 0, tracksBatches, shelfLifeDays, bin: null, batches: [],
  };
}

describe('receiving in batches', () => {
  const lines = [line('a', 10), line('b', 6, true, 30)];

  it('starts with everything ordered and one batch with the expiry from the shelf life', () => {
    const forms = receiveForms(lines, '2026-11-04');
    expect(forms[0]).toEqual({ lineId: 'a', pieces: '10', costEach: '2.5', batches: [] });
    expect(forms[1].batches).toEqual([{ batchNumber: '', pieces: '6', expiresOn: '2026-12-04', madeOn: '' }]);
    expect(suggestedExpiry(10, '2026-11-01', '2026-11-04')).toBe('2026-11-11');
    expect(suggestedExpiry(0, null, '2026-11-04')).toBeNull();
  });

  it('asks for batch numbers and batches that add up to the pieces', () => {
    const forms = receiveForms(lines, '2026-11-04');
    expect(receiveErrors(lines, forms)).toEqual({ b: 'Every batch needs its number.' });
    forms[1].batches = [
      { batchNumber: 'l1', pieces: '4', expiresOn: '2026-12-01', madeOn: '' },
      { batchNumber: 'L2', pieces: '1', expiresOn: '', madeOn: '' },
    ];
    expect(receiveErrors(lines, forms).b).toBe('The batches add up to 5, not the 6 pieces received.');
    forms[1].batches[1].pieces = '2';
    expect(receiveErrors(lines, forms)).toEqual({});
    forms[1].batches[1].madeOn = '2026-12-10';
    forms[1].batches[1].expiresOn = '2026-12-01';
    expect(receiveErrors(lines, forms).b).toBe('A batch cannot expire before it was made.');
    forms[0].pieces = 'x';
    expect(receiveErrors(lines, forms).a).toBe('Enter the pieces received (0 or more).');
  });

  it('lets a batch-kept line that did not arrive go without batches', () => {
    const forms = receiveForms(lines, '2026-11-04');
    forms[1].pieces = '0';
    forms[1].batches = [];
    expect(receiveErrors(lines, forms)).toEqual({});
  });

  it('sends the batches in capitals, and none for items not kept in batches', () => {
    const forms = receiveForms(lines, '2026-11-04');
    forms[1].batches[0].batchNumber = ' lot-9 ';
    const request = receiveRequest('v-1', 12.5, lines, forms);
    expect(request.version).toBe('v-1');
    expect(request.shippingCost).toBe(12.5);
    expect(request.lines[0]).toEqual({ lineId: 'a', pieces: 10, costEach: 2.5, batches: null });
    expect(request.lines[1].batches).toEqual([{ batchNumber: 'LOT-9', pieces: 6, expiresOn: '2026-12-04', madeOn: null }]);
  });
});

describe('totals and statuses', () => {
  it('adds money without floating-point dust', () => {
    expect(sum([{ v: 0.1 }, { v: 0.2 }], (r) => r.v)).toBe(0.3);
    expect(requisitionTotal([{ quantity: 3, costEach: 1.1 }, { quantity: 2, costEach: 0.35 }])).toBe(4);
    expect(chargesTotal([{ kind: 'Freight', description: null, amount: 24.1 }, { kind: 'Duty', description: null, amount: 5.9 }])).toBe(30);
  });

  it('says how far a batch is from expiring', () => {
    expect(expiryText(-3)).toEqual({ text: 'Expired 3 days ago', tone: 'danger' });
    expect(expiryText(-1).text).toBe('Expired yesterday');
    expect(expiryText(0)).toEqual({ text: 'Expires today', tone: 'danger' });
    expect(expiryText(5, 30)).toEqual({ text: '5 days left', tone: 'warn' });
    expect(expiryText(45, 30).tone).toBe('ok');
    expect(expiryText(null).text).toBe('No expiry');
  });

  it('offers the steps a requisition allows, approval only to someone who may approve', () => {
    expect(requisitionSteps('Draft', false)).toEqual({ edit: true, submit: true, decide: false, order: false, remove: true });
    expect(requisitionSteps('Submitted', false).decide).toBe(false);
    expect(requisitionSteps('Submitted', true).decide).toBe(true);
    expect(requisitionSteps('Approved', false).order).toBe(true);
    expect(requisitionSteps('Rejected', true)).toEqual({ edit: true, submit: false, decide: false, order: false, remove: true });
    expect(requisitionSteps('Ordered', true)).toEqual({ edit: false, submit: false, decide: false, order: false, remove: false });
  });
});

describe('reorder suggestions', () => {
  const row = (itemId: string, suggestedPieces: number, supplierId: string | null): ReorderRow => ({
    itemId, plu: itemId, name: itemId, supplierId, supplier: supplierId, onHand: 0, onOrder: 0, customerOrdered: 0, unitsSold: 0, averageDailySales: 0,
    leadTimeDays: 7, reorderPoint: 0, reorderLevel: 0, targetStock: 0, caseQuantity: 1, minimumOrder: 0, suggestedPieces, orderQuantity: suggestedPieces, costEach: 1,
    value: suggestedPieces,
  });
  const rows = [row('a', 12, 's-1'), row('b', 6, 's-1'), row('c', 4, null), row('d', 3, 's-2')];

  it('sends the chosen lines with the pieces typed, one order per supplier', () => {
    const lines = reorderLines(rows, new Set(['a', 'b', 'c']), { b: '8', c: '0' });
    expect(lines).toEqual([
      { itemId: 'a', pieces: 12, supplierId: 's-1' },
      { itemId: 'b', pieces: 8, supplierId: 's-1' },
    ]);
    expect(ordersFor(lines)).toBe(1);
    expect(ordersFor(reorderLines(rows, new Set(['a', 'c', 'd']), {}))).toBe(3);
  });
});
