import { describe, expect, it } from 'vitest';
import { groupCartLines } from './cart-grouping';
import type { CartLine } from '@/types/pos';

let sequence = 0;

function line(overrides: Partial<CartLine>): CartLine {
  sequence += 1;

  return {
    sequence,
    productId: 1,
    variantId: null,
    stockCode: 'SHIRT01',
    name: 'Oxford shirt',
    variantLabel: null,
    epc: null,
    serialNumber: null,
    source: 'Rfid',
    lineType: 'Sale',
    quantity: 1,
    chargeableQuantity: 1,
    unitPrice: 20,
    priceOrigin: 'Regular',
    discountPct: 0,
    extendedNet: 20,
    tax1Applies: true,
    tax2Applies: true,
    tax1Amount: 1,
    tax2Amount: 1.4,
    requestedPriceLevel: null,
    hasManualPrice: false,
    note: null,
    unitWeight: 0,
    lineWeight: 0,
    ...overrides,
  } as CartLine;
}

describe('groupCartLines', () => {
  it('shows three tagged units of one item as one row of three, with their tags', () => {
    const rows = groupCartLines([
      line({ epc: 'TAG1' }),
      line({ epc: 'TAG2' }),
      line({ epc: 'TAG3' }),
    ]);

    expect(rows).toHaveLength(1);
    expect(rows[0].quantity).toBe(3);
    expect(rows[0].extendedNet).toBe(60);
    expect(rows[0].epcs).toEqual(['TAG1', 'TAG2', 'TAG3']);
  });

  it('keeps the unit added last within reach, so it is the one a tap takes off', () => {
    const first = line({ epc: 'TAG1' });
    const last = line({ epc: 'TAG2' });

    const [row] = groupCartLines([first, last]);

    expect(row.last.sequence).toBe(last.sequence);
  });

  it('never folds a unit at a different price into the others', () => {
    const rows = groupCartLines([line({ epc: 'TAG1' }), line({ epc: 'TAG2', unitPrice: 15, priceOrigin: 'Manual' })]);

    expect(rows).toHaveLength(2);
  });

  it('leaves untagged lines alone, even when they look identical', () => {
    const rows = groupCartLines([line({ source: 'Barcode' }), line({ source: 'Barcode' })]);

    expect(rows).toHaveLength(2);
  });

  it('keeps rows in the order their first unit arrived', () => {
    const rows = groupCartLines([
      line({ epc: 'A1' }),
      line({ productId: 2, stockCode: 'MUG01', epc: null }),
      line({ epc: 'A2' }),
    ]);

    expect(rows.map((row) => row.first.stockCode)).toEqual(['SHIRT01', 'MUG01']);
    expect(rows[0].quantity).toBe(2);
  });
});
