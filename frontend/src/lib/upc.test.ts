import { describe, expect, it } from 'vitest';
import { isUpcA, labelBarcodeKind, upcCheckDigit } from './upc';

describe('isUpcA', () => {
  it.each(['036000291452', '012345678905', '400000000015'])('accepts %s', (code) => {
    expect(isUpcA(code)).toBe(true);
  });

  it.each(['036000291453', '03600029145', '0036000291452', 'SHIRT01', '', null, undefined])(
    'refuses %s',
    (code) => {
      expect(isUpcA(code)).toBe(false);
    },
  );

  it('agrees with the server on the check digit', () => {
    // The same worked example the server's UpcATests pins.
    expect(upcCheckDigit('03600029145')).toBe(2);
  });
});

describe('labelBarcodeKind', () => {
  it('is UPC-A for an item that has a valid one', () => {
    expect(labelBarcodeKind('036000291452', 'SHIRT01')).toBe('upca');
  });

  it('falls back to Code 39 of the stock code for an item with no barcode', () => {
    expect(labelBarcodeKind(null, 'shirt-01')).toBe('code39');
  });

  it('is nothing at all for a code Code 39 cannot carry', () => {
    expect(labelBarcodeKind(null, 'SHIRT_01')).toBe('none');
  });
});
