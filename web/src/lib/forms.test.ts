import { describe, expect, it } from 'vitest';
import { checkBarcode, checkEmail, checkNumber, numberText, parseNumber, textOrNull } from './forms';

describe('form helpers', () => {
  it('reads numbers people type', () => {
    expect(parseNumber('12.50')).toBe(12.5);
    expect(parseNumber('12,5')).toBe(12.5);
    expect(parseNumber(' 7 ')).toBe(7);
    expect(parseNumber('-3')).toBe(-3);
    expect(parseNumber('')).toBeNull();
    expect(parseNumber('abc')).toBeNull();
    expect(parseNumber('1.2.3')).toBeNull();
    expect(parseNumber('-')).toBeNull();
  });

  it('checks money and quantities', () => {
    expect(checkNumber('', { required: true, label: 'Price' })).toBe('Price is needed.');
    expect(checkNumber('', { label: 'Price' })).toBeUndefined();
    expect(checkNumber('x', { label: 'Price' })).toBe('Price is not a number.');
    expect(checkNumber('-1', { label: 'Price' })).toBe('Price cannot be negative.');
    expect(checkNumber('1.234', { label: 'Price' })).toBe('Price has at most 2 decimals.');
    expect(checkNumber('1.234', { decimals: 3 })).toBeUndefined();
    expect(checkNumber('0', { min: 1, label: 'Units per case' })).toBe('Units per case is at least 1.');
    expect(checkNumber('-5', { min: null })).toBeUndefined();
  });

  it('checks barcodes and their check digit', () => {
    expect(checkBarcode('')).toBeUndefined();
    expect(checkBarcode('036000291452')).toBeUndefined(); // UPC-A
    expect(checkBarcode('4006381333931')).toBeUndefined(); // EAN-13
    expect(checkBarcode('96385074')).toBeUndefined(); // EAN-8
    expect(checkBarcode('036000291453')).toBe("The barcode's check digit is not right.");
    expect(checkBarcode('12345')).toBe('A barcode is 8, 12 or 13 digits (EAN-8, UPC-A or EAN-13).');
    expect(checkBarcode('ABCDEFGH')).toBe('A barcode is 8, 12 or 13 digits (EAN-8, UPC-A or EAN-13).');
  });

  it('checks e-mail addresses', () => {
    expect(checkEmail('')).toBeUndefined();
    expect(checkEmail('a@b.co')).toBeUndefined();
    expect(checkEmail('a@b')).toBe('This is not an e-mail address.');
  });

  it('turns empty text into null', () => {
    expect(textOrNull('  ')).toBeNull();
    expect(textOrNull(' x ')).toBe('x');
    expect(textOrNull(null)).toBeNull();
    expect(numberText(1.5)).toBe('1.5');
    expect(numberText(null)).toBe('');
  });
});
