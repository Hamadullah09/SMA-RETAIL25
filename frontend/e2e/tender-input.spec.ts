import { test, expect } from '@playwright/test';
import { parseTenderInput } from '../src/lib/tender-input';

/**
 * The cash field, character by character.
 *
 * A pure-logic suite living in the e2e folder because it is the only test runner this project has.
 * It opens no browser and needs no server, so it runs everywhere the rest of the suite does.
 *
 * Every case below was reachable in production. `Number(tendered) || due` accepted the lot: `NaN`
 * for the letters, `0` for the empty and whitespace cases, and every one of them is falsy, so every
 * one of them became the exact amount owed and settled a sale with an empty drawer.
 */

const DUE = 2500;

test.describe('tender input — refused', () => {
  const refused: Array<[string, string]> = [
    ['abc', 'letters'],
    ['xyz', 'letters'],
    ['12abc', 'digits then letters'],
    ['abc12', 'letters then digits'],
    ['1,2,3', 'commas that are not thousands grouping'],
    ['1..2', 'two decimal points'],
    ['--', 'signs only'],
    ['++', 'signs only'],
    ['.', 'bare decimal point'],
    ['-', 'bare sign'],
    ['NaN', 'the literal string NaN'],
    ['Infinity', 'the literal string Infinity'],
    ['-Infinity', 'negative infinity'],
    ['1e5', 'exponent notation'],
    ['0x10', 'hexadecimal'],
    ['-100', 'a negative amount'],
    ['2500.005', 'more precision than the currency has'],
    ['999999999999', 'larger than the till will settle'],
    ['100', 'less than the amount due'],
    ['Rs', 'a currency mark with no number'],
  ];

  for (const [input, why] of refused) {
    test(`refuses ${JSON.stringify(input)} (${why})`, () => {
      const result = parseTenderInput(input, DUE);

      expect(result.ok, `${JSON.stringify(input)} must not parse`).toBe(false);
      if (!result.ok) expect(result.message.length).toBeGreaterThan(0);
    });
  }
});

test.describe('tender input — accepted', () => {
  test('an exact amount', () => {
    expect(parseTenderInput('2500', DUE)).toEqual({ ok: true, exact: false, amount: 2500 });
  });

  test('an amount with decimals', () => {
    expect(parseTenderInput('2500.50', DUE)).toEqual({ ok: true, exact: false, amount: 2500.5 });
  });

  test('overpayment, for change', () => {
    expect(parseTenderInput('5000', DUE)).toEqual({ ok: true, exact: false, amount: 5000 });
  });

  test('thousands separators, as printed on a note', () => {
    expect(parseTenderInput('2,500', DUE)).toEqual({ ok: true, exact: false, amount: 2500 });
  });

  test('a currency mark the cashier typed out of habit', () => {
    expect(parseTenderInput('Rs 2500', DUE)).toEqual({ ok: true, exact: false, amount: 2500 });
    expect(parseTenderInput('PKR 2500', DUE)).toEqual({ ok: true, exact: false, amount: 2500 });
    expect(parseTenderInput('Rs. 2,500.00', DUE)).toEqual({ ok: true, exact: false, amount: 2500 });
  });

  /**
   * Blank is the one falsy input that stays meaningful, and it is not a fallback: the field's
   * placeholder has always shown the amount due, so an empty field is the cashier saying "exactly
   * that". It is reported as `exact` rather than as a number so the caller cannot confuse it with
   * a parsed zero.
   */
  test('blank means the exact money', () => {
    expect(parseTenderInput('', DUE)).toEqual({ ok: true, exact: true, amount: null });
    expect(parseTenderInput('   ', DUE)).toEqual({ ok: true, exact: true, amount: null });
  });

  test('zero is refused against a bill, and allowed against nothing', () => {
    expect(parseTenderInput('0', DUE).ok).toBe(false);
    expect(parseTenderInput('0', 0)).toEqual({ ok: true, exact: false, amount: 0 });
  });
});

test.describe('tender input — the original defect', () => {
  /**
   * The regression this file exists for. Before the fix these all returned the amount due, and the
   * sale settled.
   */
  for (const input of ['abc', '', '   ', 'xyz', '--']) {
    test(`${JSON.stringify(input)} never silently becomes the amount due`, () => {
      const result = parseTenderInput(input, DUE);
      const settledSilently = result.ok && !result.exact && result.amount === DUE;

      expect(settledSilently, 'must not resolve to the amount owed').toBe(false);
    });
  }
});
