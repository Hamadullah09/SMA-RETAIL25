import { describe, expect, it } from 'vitest';
import { blankPromotion, checkPromotion, daysText, describePromotion, hoursText } from './promotions';
import type { PromotionRequest } from './types';

const make = (p: Partial<PromotionRequest>): PromotionRequest => ({ ...blankPromotion(), name: 'Test', ...p });

describe('promotion checks', () => {
  it('passes a plain percent off', () => {
    expect(checkPromotion(make({}))).toEqual([]);
  });

  it('asks for a name, stores and what it applies to', () => {
    expect(checkPromotion(make({ name: ' ' }))).toContain('Give the promotion a name.');
    expect(checkPromotion(make({ allStores: false, storeIds: [] }))).toContain('Choose the stores, or every store.');
    expect(checkPromotion(make({ scope: 'Departments', targets: [] }))).toContain('Choose what it applies to.');
    expect(checkPromotion(make({ scope: 'Items', targets: [{ plu: 'A', departmentId: 'd-1', quantity: 1 }] }))).toContain('Each line names one department, category or stock code.');
    expect(checkPromotion(make({ startsOn: '2026-12-01', endsOn: '2026-11-01' }))).toContain('The last day is before the first day.');
  });

  it('checks each kind', () => {
    expect(checkPromotion(make({ percent: 0 }))).toContain('The percent is more than 0 and at most 100.');
    expect(checkPromotion(make({ kind: 'AmountOff', amount: 0 }))).toContain('Type the amount off each unit.');
    expect(checkPromotion(make({ kind: 'BuyXGetY', buyQuantity: 0 }))).toContain('Buy is a whole number, at least 1.');
    expect(checkPromotion(make({ kind: 'BundlePrice', bundleParts: [{ plu: 'A', quantity: 1 }], bundlePrice: 10 }))).toContain('A bundle has at least two items.');
    expect(checkPromotion(make({ kind: 'BundlePrice', bundleParts: [{ plu: 'A', quantity: 1 }, { plu: 'B', quantity: 1 }], bundlePrice: 10 }))).toEqual([]);
    expect(checkPromotion(make({ kind: 'SpendOver', spendThreshold: 0, percent: 0, amount: 0 }))).toEqual([
      'Type how much the customer must spend.',
      'Type the percent or the amount off.',
    ]);
    expect(checkPromotion(make({ couponCode: 'a b' }))).toContain('A coupon code has 3 to 30 letters, digits or dashes.');
  });
});

describe('promotions in words', () => {
  it('names the days and hours', () => {
    expect(daysText([])).toBe('Every day');
    expect(daysText([1, 2, 3, 4, 5])).toBe('Weekdays');
    expect(daysText([6, 0])).toBe('Weekends');
    expect(daysText([5, 1])).toBe('Mon, Fri');
    expect(hoursText('17:00', '19:00')).toBe('17:00–19:00');
    expect(hoursText('22:00', '02:00')).toBe('22:00–02:00 (past midnight)');
    expect(hoursText(null, null)).toBe('');
  });

  it('describes every kind', () => {
    const names = { department: () => 'FOOTWEAR', symbol: '$' };
    expect(describePromotion(make({ percent: 15, scope: 'Departments', targets: [{ departmentId: 'd-2', quantity: 1 }] }), names)).toBe('15% off FOOTWEAR · every store');
    expect(describePromotion(make({ kind: 'AmountOff', amount: 2 }), names)).toBe('$2.00 off each unit of everything · every store');
    expect(describePromotion(make({ kind: 'BuyXGetY', scope: 'Items', targets: [{ plu: 'SK-001', quantity: 1 }] }), names)).toBe('Buy 2 of SK-001, get 1 more free · every store');
    expect(describePromotion(make({ kind: 'BuyXGetY', getPercent: 50, rewardTargets: [{ plu: 'CP-012', quantity: 1 }] }), names)).toBe(
      'Buy 2 of everything, get 1 of CP-012 at 50% off · every store',
    );
    expect(describePromotion(make({ kind: 'BundlePrice', bundleParts: [{ plu: 'TS-100-M', quantity: 2 }, { plu: 'CP-012', quantity: 1 }], bundlePrice: 40 }), names)).toBe(
      '2 × TS-100-M + 1 × CP-012 for $40.00 · every store',
    );
    expect(describePromotion(make({ kind: 'SpendOver', spendThreshold: 100, percent: 0, amount: 10, allStores: false, storeIds: ['s-main'] }), names)).toBe(
      'Spend $100.00, get $10.00 off · 1 store',
    );
  });

  it('adds the hours, the coupon and when it stands alone', () => {
    expect(describePromotion(make({ days: [5], startTime: '17:00', endTime: '19:00', couponCode: 'happy5', couponMaxUses: 1, combinable: false }))).toBe(
      '10% off everything · Fri, 17:00–19:00 · every store · coupon HAPPY5, one use · not with other promotions',
    );
  });
});
