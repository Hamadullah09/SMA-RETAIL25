/**
 * Customer segments: the rules typed as text in the editor (so a half-typed number is not lost), turned into the
 * server's `SegmentRules` with their problems, and the rules in words.
 */
import { isBusinessDate } from './dates';
import { formatDate, formatMoney } from './format';
import { parseNumber, type Errors } from './forms';
import type { SegmentRules } from './types';

export const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

export type PeriodMode = 'all' | 'days' | 'dates';
export type Consent = '' | 'yes' | 'no';

export interface RuleForm {
  period: PeriodMode;
  periodDays: string;
  from: string;
  to: string;
  minSpend: string;
  maxSpend: string;
  minVisits: string;
  maxVisits: string;
  lastPurchaseBefore: string;
  lastPurchaseAfter: string;
  noPurchaseForDays: string;
  storeId: string;
  departmentId: string;
  birthdayMonth: string;
  minPoints: string;
  maxPoints: string;
  minBalance: string;
  whatsAppConsent: Consent;
}

export type RuleKey = keyof RuleForm;

const text = (n: number | null | undefined) => (n === null || n === undefined ? '' : String(n));

export function rulesToForm(rules: SegmentRules | null | undefined): RuleForm {
  const r = rules ?? {};
  return {
    period: r.periodDays ? 'days' : r.from || r.to ? 'dates' : 'all',
    periodDays: text(r.periodDays),
    from: r.from ?? '',
    to: r.to ?? '',
    minSpend: text(r.minSpend),
    maxSpend: text(r.maxSpend),
    minVisits: text(r.minVisits),
    maxVisits: text(r.maxVisits),
    lastPurchaseBefore: r.lastPurchaseBefore ?? '',
    lastPurchaseAfter: r.lastPurchaseAfter ?? '',
    noPurchaseForDays: text(r.noPurchaseForDays),
    storeId: r.storeId ?? '',
    departmentId: r.departmentId ?? '',
    birthdayMonth: text(r.birthdayMonth),
    minPoints: text(r.minPoints),
    maxPoints: text(r.maxPoints),
    minBalance: text(r.minBalance),
    whatsAppConsent: r.whatsAppConsent === true ? 'yes' : r.whatsAppConsent === false ? 'no' : '',
  };
}

/**
 * The rules the form holds, and what is wrong with it. Empty boxes are rules left out; a period is sent only with the
 * way it is given (last N days, or dates).
 */
export function formToRules(form: RuleForm): { rules: SegmentRules; errors: Errors<RuleKey> } {
  const errors: Errors<RuleKey> = {};
  const rules: SegmentRules = {};

  function number(key: RuleKey, label: string, { whole = false, min = 0 } = {}): number | null {
    const raw = String(form[key]).trim();
    if (!raw) return null;
    const n = parseNumber(raw);
    if (n === null) errors[key] = `${label} is not a number.`;
    else if (n < min) errors[key] = min === 0 ? `${label} cannot be negative.` : `${label} is at least ${min}.`;
    else if (whole && !Number.isInteger(n)) errors[key] = `${label} is a whole number.`;
    else return n;
    return null;
  }

  function date(key: RuleKey, label: string): string | null {
    const raw = String(form[key]).trim();
    if (!raw) return null;
    if (!isBusinessDate(raw)) {
      errors[key] = `${label} is not a date.`;
      return null;
    }
    return raw;
  }

  if (form.period === 'days') {
    const days = number('periodDays', 'The number of days', { whole: true, min: 1 });
    if (days === null && !errors.periodDays) errors.periodDays = 'Type the number of days.';
    rules.periodDays = days;
  } else if (form.period === 'dates') {
    rules.from = date('from', 'The first day');
    rules.to = date('to', 'The last day');
    if (!rules.from && !rules.to && !errors.from && !errors.to) errors.from = 'Choose the first day, the last day or both.';
    if (rules.from && rules.to && rules.from > rules.to) errors.to = 'The last day is before the first day.';
  }
  rules.minSpend = number('minSpend', 'The least spend');
  rules.maxSpend = number('maxSpend', 'The most spend');
  if (rules.minSpend !== null && rules.maxSpend !== null && rules.maxSpend! < rules.minSpend!) errors.maxSpend = 'The most is below the least.';
  rules.minVisits = number('minVisits', 'The fewest visits', { whole: true });
  rules.maxVisits = number('maxVisits', 'The most visits', { whole: true });
  if (rules.minVisits !== null && rules.maxVisits !== null && rules.maxVisits! < rules.minVisits!) errors.maxVisits = 'The most is below the fewest.';
  rules.lastPurchaseBefore = date('lastPurchaseBefore', 'The date');
  rules.lastPurchaseAfter = date('lastPurchaseAfter', 'The date');
  rules.noPurchaseForDays = number('noPurchaseForDays', 'The number of days', { whole: true, min: 1 });
  rules.storeId = form.storeId || null;
  rules.departmentId = form.departmentId || null;
  rules.birthdayMonth = form.birthdayMonth ? Number(form.birthdayMonth) : null;
  rules.minPoints = number('minPoints', 'The fewest points');
  rules.maxPoints = number('maxPoints', 'The most points');
  if (rules.minPoints !== null && rules.maxPoints !== null && rules.maxPoints! < rules.minPoints!) errors.maxPoints = 'The most is below the fewest.';
  rules.minBalance = number('minBalance', 'The balance owing');
  rules.whatsAppConsent = form.whatsAppConsent === 'yes' ? true : form.whatsAppConsent === 'no' ? false : null;

  // Leave out what is not set, so the server and the list see only the rules in use.
  for (const key of Object.keys(rules) as (keyof SegmentRules)[]) if (rules[key] === null || rules[key] === undefined) delete rules[key];
  return { rules, errors };
}

export interface RuleNames {
  store?(id: string): string | undefined;
  department?(id: string): string | undefined;
  symbol?: string;
}

/** The rules in words, one phrase per rule; "Every client" when none is set. */
export function describeRules(rules: SegmentRules | null | undefined, names: RuleNames = {}): string {
  const r = rules ?? {};
  const money = (n: number) => formatMoney(n, names.symbol ?? '');
  const period = r.periodDays ? ` in the last ${r.periodDays} days` : r.from && r.to ? ` from ${formatDate(r.from)} to ${formatDate(r.to)}` : r.from ? ` since ${formatDate(r.from)}` : r.to ? ` until ${formatDate(r.to)}` : '';
  const parts: string[] = [];
  if (r.minSpend != null && r.maxSpend != null) parts.push(`Spent ${money(r.minSpend)} to ${money(r.maxSpend)}${period}`);
  else if (r.minSpend != null) parts.push(`Spent at least ${money(r.minSpend)}${period}`);
  else if (r.maxSpend != null) parts.push(`Spent at most ${money(r.maxSpend)}${period}`);
  if (r.minVisits != null && r.maxVisits != null) parts.push(`${r.minVisits} to ${r.maxVisits} visits${period}`);
  else if (r.minVisits != null) parts.push(`${r.minVisits} or more visits${period}`);
  else if (r.maxVisits != null) parts.push(`At most ${r.maxVisits} visits${period}`);
  if (r.departmentId) parts.push(`Bought from ${names.department?.(r.departmentId) ?? 'a department'}${period}`);
  if (r.storeId) parts.push(`At ${names.store?.(r.storeId) ?? 'one store'}`);
  if (r.noPurchaseForDays != null) parts.push(`No purchase for ${r.noPurchaseForDays} days`);
  if (r.lastPurchaseBefore) parts.push(`Last bought before ${formatDate(r.lastPurchaseBefore)}`);
  if (r.lastPurchaseAfter) parts.push(`Last bought after ${formatDate(r.lastPurchaseAfter)}`);
  if (r.birthdayMonth) parts.push(`Birthday in ${MONTHS[r.birthdayMonth - 1] ?? r.birthdayMonth}`);
  if (r.minPoints != null && r.maxPoints != null) parts.push(`${r.minPoints} to ${r.maxPoints} points`);
  else if (r.minPoints != null) parts.push(`At least ${r.minPoints} points`);
  else if (r.maxPoints != null) parts.push(`At most ${r.maxPoints} points`);
  if (r.minBalance != null) parts.push(`Owes at least ${money(r.minBalance)}`);
  if (r.whatsAppConsent === true) parts.push('Agreed to WhatsApp');
  if (r.whatsAppConsent === false) parts.push('Not on WhatsApp');
  return parts.length ? parts.join(' · ') : 'Every client';
}
