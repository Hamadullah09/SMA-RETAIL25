import { describe, expect, it } from 'vitest';
import { addMonths, daysText, formatDays, formatMinutes, markTone, monthKey, monthName, nextTaxYear, payslipSections, runActions, taxYearName, taxYearOf } from './hr';
import { canOpen, commandsFor, navFor, navKeyForPath, pageForPath, searchCommands } from './nav';
import type { RuleSetInfo } from './types';

describe('HR words', () => {
  it('writes the days of a shift short', () => {
    expect(daysText(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'])).toBe('Mon–Sat');
    expect(daysText(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'])).toBe('Mon–Fri');
    expect(daysText(['Monday', 'Wednesday', 'Friday'])).toBe('Mon, Wed, Fri');
    expect(daysText(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'])).toBe('Every day');
    expect(daysText([])).toBe('No days');
    expect(daysText(['Sunday'])).toBe('Sun');
  });

  it('writes minutes as hours and minutes', () => {
    expect(formatMinutes(0)).toBe('');
    expect(formatMinutes(45)).toBe('45 min');
    expect(formatMinutes(60)).toBe('1 h');
    expect(formatMinutes(65)).toBe('1 h 05 min');
    expect(formatMinutes(null)).toBe('');
    expect(formatDays(0.5)).toBe('0.5');
    expect(formatDays(2)).toBe('2');
  });

  it('names months and tax years', () => {
    expect(monthKey(new Date(2026, 10, 4))).toBe('2026-11');
    expect(monthName('2026-11', 'en-US')).toBe('November 2026');
    expect(monthName(2027, 1, 'en-US')).toBe('January 2027');
    expect(addMonths('2026-12', 1)).toBe('2027-01');
    expect(addMonths('2026-01', -1)).toBe('2025-12');
    expect(taxYearName(2026)).toBe('2026-27');
    expect(taxYearName(2026, 1)).toBe('2026');
    expect(taxYearOf(new Date(2026, 10, 4))).toBe(2026);
    expect(taxYearOf(new Date(2027, 2, 1))).toBe(2026);
  });

  it('marks the sheet by what a day was', () => {
    expect(markTone('A')).toBe('danger');
    expect(markTone('L')).toBe('warn');
    expect(markTone('P')).toBe('ok');
    expect(markTone('AL')).toBe('plain');
  });
});

describe('a payroll run', () => {
  it('offers the buttons of its step to the roles that may press them', () => {
    const both = { run: true, approve: true };
    expect(runActions('Draft', both)).toEqual(['calculate', 'delete']);
    expect(runActions('Calculated', both)).toEqual(['calculate', 'approve', 'delete']);
    expect(runActions('Approved', both)).toEqual(['reopen', 'post']);
    expect(runActions('Posted', both)).toEqual(['pay']);
    expect(runActions('Paid', both)).toEqual([]);
    // An HR officer prepares, someone else approves.
    expect(runActions('Calculated', { run: true, approve: false })).toEqual(['calculate', 'delete']);
    expect(runActions('Calculated', { run: false, approve: true })).toEqual(['approve']);
    expect(runActions('Approved', { run: false, approve: true })).toEqual(['reopen']);
  });

  it('splits a payslip into earnings, deductions and the employer part', () => {
    const sections = payslipSections([
      { kind: 'Earning', code: 'BASIC', name: 'Basic pay', amount: 50000, quantity: 30, rate: null },
      { kind: 'Earning', code: 'HRA', name: 'House rent', amount: 20000.1, quantity: null, rate: null },
      { kind: 'Deduction', code: 'TAX', name: 'Income tax', amount: 1234.56, quantity: null, rate: null },
      { kind: 'EmployerContribution', code: 'EOBI-ER', name: 'EOBI (employer)', amount: 2000, quantity: null, rate: 5 },
    ]);
    expect(sections.map((s) => [s.title, s.total])).toEqual([
      ['Earnings', 70000.1],
      ['Deductions', 1234.56],
      ['Paid by the employer', 2000],
    ]);
    expect(payslipSections([]).length).toBe(0);
  });
});

describe('the payroll rules of the next tax year', () => {
  const pakistan: RuleSetInfo = {
    id: 'rs-1', version: 'v-1', country: 'PK', name: 'Pakistan, tax year 2026 (July 2025 to June 2026)', effectiveFrom: '2025-07-01',
    effectiveTo: '2026-06-30', taxYearStartMonth: 7, minimumWage: 40000, eobiEmployerRate: 5, eobiEmployeeRate: 1, socialSecurityProvince: 'Punjab (PESSI)',
    socialSecurityRate: 6, socialSecurityWageCeiling: 40000, socialSecurityBasis: 'MinimumWage', workingDaysPerWeek: 6, hoursPerWeek: 48,
    overtimeMultiplier: 2, overtimeMinimumMinutes: 30, dailyRateBasis: 'CalendarDays', latenessRule: 'DayPerLates', latesPerDay: 3, deductAbsences: true,
    surchargeThreshold: 10_000_000, surchargeRate: 9, gratuityEnabled: true, gratuityDaysPerYear: 30, gratuityMinimumMonths: 12, gratuityPartYearMonths: 6,
    gratuityOnBasic: false, cnicPattern: null, cnicExample: null, ibanPattern: null, ibanExample: null, notes: 'Tax year 2026 only.',
    taxSlabs: [{ incomeFrom: 0, incomeTo: 600000, fixedTax: 0, rate: 0 }, { incomeFrom: 600000, incomeTo: null, fixedTax: 0, rate: 1 }],
    allowances: [{ code: 'MEDICAL', name: 'Medical', isTaxable: false, exemptPercentOfBasic: 10, sortOrder: 1 }],
  };

  it('starts a year later as a new rule set without an end, named for its year', () => {
    const next = nextTaxYear(pakistan);
    expect([next.id, next.version, next.effectiveFrom, next.effectiveTo, next.name]).toEqual([null, null, '2026-07-01', null, 'PK, tax year 2026-27']);
    expect(nextTaxYear({ ...pakistan, effectiveFrom: '2026-01-01', taxYearStartMonth: 1 }).name).toBe('PK, tax year 2027');
  });

  it('keeps the figures to change, but asks for them to be checked instead of keeping last year’s note', () => {
    const next = nextTaxYear(pakistan);
    expect([next.minimumWage, next.surchargeRate, next.taxSlabs, next.allowances]).toEqual([40000, 9, pakistan.taxSlabs, pakistan.allowances]);
    expect(next.notes).toContain('Started from Pakistan, tax year 2026 (July 2025 to June 2026).');
    expect(next.notes).toContain('Check every figure');
    next.taxSlabs[1].rate = 5;
    expect(pakistan.taxSlabs[1].rate).toBe(1);
  });
});

describe('the HR group in the navigation', () => {
  const hrOfficer = ['web.signin', 'reports.staff', 'hr.view', 'hr.edit', 'hr.attendance', 'hr.leave', 'payroll.run'];
  const approver = ['web.signin', 'hr.view', 'payroll.approve'];
  const manager = ['web.signin', 'hr.view', 'hr.leave'];

  it('lists the HR pages together under HR, after the others', () => {
    const pages = navFor(hrOfficer);
    const hr = pages.filter((p) => p.group === 'HR').map((p) => p.title);
    expect(hr).toEqual(['Employees', 'Attendance', 'Shifts and rosters', 'Leave', 'Payroll', 'HR reports', 'HR settings']);
    expect(pages.slice(-hr.length).every((p) => p.group === 'HR')).toBe(true);
  });

  it('opens Payroll with payroll.run or payroll.approve, and not with hr.view alone', () => {
    const payroll = pageForPath('/hr/payroll/')!;
    expect(canOpen(payroll, hrOfficer)).toBe(true);
    expect(canOpen(payroll, approver)).toBe(true);
    expect(canOpen(payroll, manager)).toBe(false);
    expect(canOpen(payroll, ['payroll.run'])).toBe(false);
    expect(navFor(manager).map((p) => p.title)).not.toContain('Payroll');
    expect(canOpen(pageForPath('/hr/payroll/run/')!, approver)).toBe(true);
  });

  it('highlights the list of a detail page', () => {
    expect(navKeyForPath('/hr/employees/edit/')).toBe('hr-employees');
    expect(navKeyForPath('/hr/payroll/payslip/')).toBe('hr-payroll');
  });

  it('finds every HR page and action by typing hr, with the group on them', () => {
    const commands = commandsFor(hrOfficer);
    const found = searchCommands(commands, 'hr');
    const titles = found.map((c) => c.title);
    for (const title of ['Go to Employees', 'Go to Attendance', 'Go to Leave', 'Go to Payroll', 'Go to HR settings', 'New employee', 'New leave request', 'New payroll run']) {
      expect(titles).toContain(title);
    }
    expect(found.filter((c) => c.title.startsWith('Go to')).every((c) => c.group === 'HR')).toBe(true);
    expect(commandsFor(manager).map((c) => c.title)).not.toContain('New payroll run');
    expect(commandsFor(manager).map((c) => c.title)).not.toContain('New employee');
  });
});
