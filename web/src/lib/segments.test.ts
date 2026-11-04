import { describe, expect, it } from 'vitest';
import { describeRules, formToRules, rulesToForm } from './segments';

describe('segment rules', () => {
  it('reads saved rules into the form and back unchanged', () => {
    const rules = { periodDays: 90, minSpend: 500, minVisits: 2, noPurchaseForDays: 60, birthdayMonth: 3, whatsAppConsent: true, storeId: 's-main' };
    const form = rulesToForm(rules);
    expect(form).toMatchObject({ period: 'days', periodDays: '90', minSpend: '500', birthdayMonth: '3', whatsAppConsent: 'yes', storeId: 's-main' });
    expect(formToRules(form)).toEqual({ rules, errors: {} });
  });

  it('leaves out what is not set', () => {
    expect(formToRules(rulesToForm({})).rules).toEqual({});
    expect(formToRules(rulesToForm({ whatsAppConsent: false })).rules).toEqual({ whatsAppConsent: false });
  });

  it('says what is wrong', () => {
    const form = { ...rulesToForm({}), minSpend: 'abc', maxVisits: '1.5', period: 'dates' as const, from: '2026-11-10', to: '2026-11-01', minPoints: '-3' };
    const { errors } = formToRules(form);
    expect(errors.minSpend).toMatch(/not a number/);
    expect(errors.maxVisits).toMatch(/whole number/);
    expect(errors.to).toMatch(/before the first day/);
    expect(errors.minPoints).toMatch(/negative/);
    expect(formToRules({ ...rulesToForm({}), period: 'days', periodDays: '' }).errors.periodDays).toMatch(/number of days/);
    expect(formToRules({ ...rulesToForm({}), minSpend: '900', maxSpend: '100' }).errors.maxSpend).toMatch(/below/);
  });

  it('sends a period only the way it is given', () => {
    const form = { ...rulesToForm({}), period: 'all' as const, periodDays: '30', from: '2026-01-01' };
    expect(formToRules(form).rules).toEqual({});
  });

  it('puts the rules in words', () => {
    const text = describeRules(
      { periodDays: 90, minSpend: 500, minVisits: 2, departmentId: 'd-2', storeId: 's-main', birthdayMonth: 3, minBalance: 10, whatsAppConsent: true },
      { store: () => 'Downtown', department: () => 'FOOTWEAR', symbol: '$' },
    );
    expect(text).toBe(
      'Spent at least $500.00 in the last 90 days · 2 or more visits in the last 90 days · Bought from FOOTWEAR in the last 90 days · At Downtown · Birthday in March · Owes at least $10.00 · Agreed to WhatsApp',
    );
    expect(describeRules({})).toBe('Every client');
    expect(describeRules({ noPurchaseForDays: 60, maxPoints: 10 })).toBe('No purchase for 60 days · At most 10 points');
  });
});
