/**
 * Promotions at the till: the kinds and what each needs, a blank promotion, the checks before saving, and the
 * promotion in words for the editor's preview (the server keeps its own `summary` for the list).
 */
import { formatDate, formatMoney } from './format';
import type { PromotionKind, PromotionRequest, PromotionScope, PromotionTarget } from './types';

export const PROMOTION_KINDS: ReadonlyArray<{ key: PromotionKind; label: string; hint: string }> = [
  { key: 'PercentOff', label: 'Percent off', hint: 'A percentage off each line of the items it applies to.' },
  { key: 'AmountOff', label: 'Amount off', hint: 'An amount off each unit of the items it applies to.' },
  { key: 'BuyXGetY', label: 'Buy X get Y', hint: 'Buy some, get more free or at a discount; the cheapest are the ones given.' },
  { key: 'BundlePrice', label: 'Bundle price', hint: 'Items bought together for one price.' },
  { key: 'SpendOver', label: 'Spend over', hint: 'A discount on the sale once the customer spends enough; shown as a line of its own.' },
];

export const SCOPES: ReadonlyArray<{ key: PromotionScope; label: string }> = [
  { key: 'AllItems', label: 'All items' },
  { key: 'Departments', label: 'Departments' },
  { key: 'Categories', label: 'Categories' },
  { key: 'Items', label: 'Stock codes' },
];

/** The days of the week as the server numbers them (0 Sunday), shown Monday first. */
export const WEEK_DAYS: ReadonlyArray<{ day: number; short: string; long: string }> = [
  { day: 1, short: 'Mon', long: 'Monday' },
  { day: 2, short: 'Tue', long: 'Tuesday' },
  { day: 3, short: 'Wed', long: 'Wednesday' },
  { day: 4, short: 'Thu', long: 'Thursday' },
  { day: 5, short: 'Fri', long: 'Friday' },
  { day: 6, short: 'Sat', long: 'Saturday' },
  { day: 0, short: 'Sun', long: 'Sunday' },
];

export function kindLabel(kind: PromotionKind | string): string {
  return PROMOTION_KINDS.find((k) => k.key === kind)?.label ?? kind;
}

export function blankPromotion(): PromotionRequest {
  return {
    id: null,
    version: null,
    name: '',
    description: null,
    kind: 'PercentOff',
    isActive: true,
    startsOn: null,
    endsOn: null,
    days: [],
    startTime: null,
    endTime: null,
    priority: 0,
    combinable: true,
    allStores: true,
    storeIds: [],
    scope: 'AllItems',
    targets: [],
    percent: 10,
    amount: 0,
    buyQuantity: 2,
    getQuantity: 1,
    getPercent: 100,
    rewardTargets: [],
    bundlePrice: 0,
    bundleParts: [],
    spendThreshold: 0,
    couponCode: null,
    couponMaxUses: 0,
  };
}

/** "Mon, Wed, Fri", "Weekdays", "Weekends" or "Every day". */
export function daysText(days: readonly number[]): string {
  const set = new Set(days);
  if (set.size === 0 || set.size === 7) return 'Every day';
  if (set.size === 5 && [1, 2, 3, 4, 5].every((d) => set.has(d))) return 'Weekdays';
  if (set.size === 2 && set.has(0) && set.has(6)) return 'Weekends';
  return WEEK_DAYS.filter((d) => set.has(d.day)).map((d) => d.short).join(', ');
}

/** "17:00–19:00" (past midnight when the end is before the start), or "" for all day. */
export function hoursText(start: string | null, end: string | null): string {
  if (!start && !end) return '';
  return `${start ?? '00:00'}–${end ?? '24:00'}${start && end && end < start ? ' (past midnight)' : ''}`;
}

function oneTarget(t: PromotionTarget): boolean {
  return [t.departmentId, t.categoryId, t.plu].filter((v) => v !== null && v !== undefined && v !== '').length === 1;
}

/** What is wrong with a promotion before it goes to the server; empty when it may be saved. */
export function checkPromotion(p: PromotionRequest): string[] {
  const problems: string[] = [];
  if (!p.name.trim()) problems.push('Give the promotion a name.');
  if (p.startsOn && p.endsOn && p.startsOn > p.endsOn) problems.push('The last day is before the first day.');
  if (!p.allStores && p.storeIds.length === 0) problems.push('Choose the stores, or every store.');
  if ((p.startTime && !/^\d{2}:\d{2}$/.test(p.startTime)) || (p.endTime && !/^\d{2}:\d{2}$/.test(p.endTime))) problems.push('Times are like 17:30.');
  if (p.kind !== 'BundlePrice' && p.scope !== 'AllItems' && p.targets.length === 0) problems.push('Choose what it applies to.');
  if ([...p.targets, ...p.rewardTargets, ...p.bundleParts].some((t) => !oneTarget(t))) problems.push('Each line names one department, category or stock code.');
  switch (p.kind) {
    case 'PercentOff':
      if (!(p.percent > 0 && p.percent <= 100)) problems.push('The percent is more than 0 and at most 100.');
      break;
    case 'AmountOff':
      if (!(p.amount > 0)) problems.push('Type the amount off each unit.');
      break;
    case 'BuyXGetY':
      if (!(p.buyQuantity >= 1 && Number.isInteger(p.buyQuantity))) problems.push('Buy is a whole number, at least 1.');
      if (!(p.getQuantity >= 1 && Number.isInteger(p.getQuantity))) problems.push('Get is a whole number, at least 1.');
      if (!(p.getPercent > 0 && p.getPercent <= 100)) problems.push('The discount on what they get is more than 0 and at most 100 (100 is free).');
      break;
    case 'BundlePrice':
      if (p.bundleParts.length < 2 && !(p.bundleParts.length === 1 && p.bundleParts[0].quantity >= 2)) problems.push('A bundle has at least two items.');
      if (p.bundleParts.some((t) => !(t.quantity >= 1 && Number.isInteger(t.quantity)))) problems.push('Each part of the bundle has a whole quantity.');
      if (!(p.bundlePrice > 0)) problems.push('Type the bundle price.');
      break;
    case 'SpendOver':
      if (!(p.spendThreshold > 0)) problems.push('Type how much the customer must spend.');
      if (!(p.percent > 0) && !(p.amount > 0)) problems.push('Type the percent or the amount off.');
      if (p.percent > 100) problems.push('The percent is at most 100.');
      break;
  }
  if (p.couponCode && !/^[A-Za-z0-9-]{3,30}$/.test(p.couponCode.trim())) problems.push('A coupon code has 3 to 30 letters, digits or dashes.');
  if (p.couponMaxUses < 0 || !Number.isInteger(p.couponMaxUses)) problems.push('The coupon limit is a whole number (0 is no limit).');
  return problems;
}

export interface TargetNames {
  department?(id: string): string | undefined;
  category?(id: string): string | undefined;
}

/** "FOOTWEAR", "category SALE", "TS-100-M"; the server's name when it sent one. */
export function targetText(t: PromotionTarget, names: TargetNames = {}): string {
  if (t.name) return t.name;
  if (t.departmentId) return names.department?.(t.departmentId) ?? 'a department';
  if (t.categoryId) return names.category?.(t.categoryId) ?? 'a category';
  return t.plu ?? '';
}

function listText(targets: readonly PromotionTarget[], names: TargetNames): string {
  return targets.map((t) => targetText(t, names)).join(', ');
}

/** The promotion in words: what, on what, when, where, and the coupon. */
export function describePromotion(p: PromotionRequest, names: TargetNames & { symbol?: string; stores?(ids: string[]): string } = {}): string {
  const money = (n: number) => formatMoney(n, names.symbol ?? '');
  const on = p.scope === 'AllItems' || p.targets.length === 0 ? 'everything' : listText(p.targets, names);
  let what: string;
  switch (p.kind) {
    case 'PercentOff':
      what = `${p.percent}% off ${on}`;
      break;
    case 'AmountOff':
      what = `${money(p.amount)} off each unit of ${on}`;
      break;
    case 'BuyXGetY': {
      const reward = p.rewardTargets.length ? listText(p.rewardTargets, names) : 'the same';
      what = `Buy ${p.buyQuantity} of ${on}, get ${p.getQuantity} ${reward === 'the same' ? 'more' : `of ${reward}`} ${p.getPercent >= 100 ? 'free' : `at ${p.getPercent}% off`}`;
      break;
    }
    case 'BundlePrice':
      what = `${p.bundleParts.map((t) => `${t.quantity} × ${targetText(t, names)}`).join(' + ') || 'A bundle'} for ${money(p.bundlePrice)}`;
      break;
    case 'SpendOver':
      what = `Spend ${money(p.spendThreshold)}${on === 'everything' ? '' : ` on ${on}`}, get ${p.percent > 0 ? `${p.percent}%` : money(p.amount)} off`;
      break;
  }
  const when: string[] = [];
  if (p.startsOn && p.endsOn) when.push(`${formatDate(p.startsOn)} to ${formatDate(p.endsOn)}`);
  else if (p.startsOn) when.push(`from ${formatDate(p.startsOn)}`);
  else if (p.endsOn) when.push(`until ${formatDate(p.endsOn)}`);
  const days = daysText(p.days);
  if (days !== 'Every day') when.push(days);
  const hours = hoursText(p.startTime, p.endTime);
  if (hours) when.push(hours);
  const parts = [what];
  if (when.length) parts.push(when.join(', '));
  parts.push(p.allStores ? 'every store' : names.stores?.(p.storeIds) ?? `${p.storeIds.length} store${p.storeIds.length === 1 ? '' : 's'}`);
  if (p.couponCode) parts.push(`coupon ${p.couponCode.trim().toUpperCase()}${p.couponMaxUses === 1 ? ', one use' : p.couponMaxUses > 1 ? `, ${p.couponMaxUses} uses` : ''}`);
  if (!p.combinable) parts.push('not with other promotions');
  return parts.join(' · ');
}
