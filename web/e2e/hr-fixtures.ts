/**
 * HR and payroll answers (phase E3) that follow the server's Endpoints.Hr.cs and Endpoints.Payroll.cs, for the
 * end-to-end tests. Re-exported from fixtures.ts; nothing here reads the other fixtures, so the two files load in
 * either order.
 */
import type {
  AttendanceRow,
  AttendanceSheet,
  CashSheet,
  CorrectionRow,
  EmployeeDetail,
  EmployeeDocumentRow,
  EmployeeRow,
  HolidayInfo,
  HrListItem,
  HrReportInfo,
  HrSettingsInfo,
  LeaveBalanceRow,
  LeaveRequestRow,
  LeaveTypeInfo,
  LoanRow,
  PayslipDetail,
  PayslipRow,
  ReportTable,
  RosterInfo,
  RuleSetInfo,
  RunDetail,
  RunRow,
  SalaryInfo,
  ShiftInfo,
  UserInfo,
} from '../src/lib/types';

const pad = (n: number) => String(n).padStart(2, '0');
const day = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const daysAgo = (n: number) => {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return day(d);
};
const now = new Date();
const thisMonth = `${now.getFullYear()}-${pad(now.getMonth() + 1)}`;

export const hrPermissionKeys = ['hr.view', 'hr.edit', 'hr.attendance', 'hr.leave', 'payroll.run', 'payroll.approve'];

/** The standard HR officer: everything of HR but approving payroll. */
export const hrOfficer: UserInfo = {
  id: 'u-hr',
  userName: 'hina',
  fullName: 'Hina Raza',
  role: 'HR officer',
  permissions: ['web.signin', 'reports.staff', 'hr.view', 'hr.edit', 'hr.attendance', 'hr.leave', 'payroll.run'],
  storeId: 's-main',
  storeName: 'Downtown',
  mustChangePassword: false,
  canChangeShared: false,
};

/** A manager on the web: sees HR and approves leave, but no payroll. */
export const hrManager: UserInfo = {
  ...hrOfficer,
  id: 'u-mgr',
  userName: 'omar',
  fullName: 'Omar Farooq',
  role: 'Manager',
  permissions: ['web.signin', 'hr.view', 'hr.leave'],
};

export const hrDepartments: HrListItem[] = [
  { id: 'hd-1', version: 'v1', name: 'Sales floor', isActive: true, employees: 2 },
  { id: 'hd-2', version: 'v1', name: 'Warehouse', isActive: true, employees: 1 },
];

export const designations: HrListItem[] = [
  { id: 'ds-1', version: 'v1', name: 'Cashier', isActive: true, employees: 1 },
  { id: 'ds-2', version: 'v1', name: 'Store manager', isActive: true, employees: 1 },
];

export const hrEmployees: EmployeeRow[] = [
  {
    id: 'e-1', code: 'E0001', fullName: 'Ayesha Khan', designation: 'Cashier', department: 'Sales floor', storeId: 's-main', storeCode: 'MAIN',
    employmentType: 'Permanent', joiningDate: '2024-03-01', leavingDate: null, active: true, phone: '0300 1234567', userName: 'akua',
  },
  {
    id: 'e-2', code: 'E0002', fullName: 'Bilal Ahmed', designation: 'Store manager', department: 'Sales floor', storeId: 's-main', storeCode: 'MAIN',
    employmentType: 'Permanent', joiningDate: '2021-07-15', leavingDate: null, active: true, phone: null, userName: null,
  },
  {
    id: 'e-3', code: 'E0003', fullName: 'Sana Malik', designation: null, department: 'Warehouse', storeId: 's-west', storeCode: 'WEST',
    employmentType: 'Contract', joiningDate: '2023-01-10', leavingDate: daysAgo(3), active: true, phone: null, userName: null,
  },
];

export function employeeDetail(id: string): EmployeeDetail {
  const row = hrEmployees.find((e) => e.id === id) ?? hrEmployees[0];
  return {
    id: row.id, version: 'v-1', code: row.code, fullName: row.fullName, fatherOrHusbandName: 'Imran Khan', cnic: '35202-1234567-1', dateOfBirth: '1995-05-20',
    gender: 'Female', maritalStatus: 'Married', address: '12 Mall Road', city: 'Lahore', phone: row.phone, email: null, emergencyContactName: 'Imran Khan',
    emergencyContactPhone: '0301 7654321', emergencyContactRelation: 'Husband', joiningDate: row.joiningDate, confirmationDate: null, leavingDate: row.leavingDate,
    leavingReason: row.leavingDate ? 'Moved city' : null, employmentType: row.employmentType, departmentId: 'hd-1', designationId: row.designation ? 'ds-1' : null,
    storeId: row.storeId, managerId: row.id === 'e-1' ? 'e-2' : null, userId: row.userName ? 'u-3' : null, shiftId: 'sh-1', tracksAttendance: true,
    overtimeEligible: true, eobiApplies: true, socialSecurityApplies: true, paymentMethod: 'Bank', bankName: 'Standard Chartered', iban: 'PK36SCBL0000001123456702',
    ntn: null, eobiNumber: 'EOBI-778', socialSecurityNumber: null, priorTaxYear: null, priorTaxableIncome: 0, priorTaxDeducted: 0, notes: null,
    managerName: row.id === 'e-1' ? 'Bilal Ahmed' : null, userName: row.userName, country: 'PK',
  };
}

export const employeeDocuments: EmployeeDocumentRow[] = [
  { id: 'doc-1', employeeId: 'e-1', title: 'CNIC copy', fileName: 'cnic.pdf', contentType: 'application/pdf', size: 182_000, uploadedAtUtc: '2026-10-01T09:00:00Z', uploadedBy: 'hina' },
];

export const hrSettings: HrSettingsInfo = {
  companyCountry: 'PK',
  countries: ['PK'],
  stores: [
    { storeId: 's-main', code: 'MAIN', name: 'Downtown', country: 'PK', own: false },
    { storeId: 's-west', code: 'WEST', name: 'Westside Mall', country: 'PK', own: true },
    { storeId: 's-north', code: 'NORTH', name: 'Northgate', country: 'PK', own: false },
  ],
};

export const ruleSets: RuleSetInfo[] = [
  {
    id: 'rs-1', version: 'v-1', country: 'PK', name: 'Pakistan, tax year 2025-26 (Finance Act 2025)', effectiveFrom: '2025-07-01', effectiveTo: null, taxYearStartMonth: 7,
    minimumWage: 40000, eobiEmployerRate: 5, eobiEmployeeRate: 1, socialSecurityProvince: 'Punjab (PESSI)', socialSecurityRate: 6, socialSecurityWageCeiling: 40000,
    socialSecurityBasis: 'Wage', workingDaysPerWeek: 6, hoursPerWeek: 48, overtimeMultiplier: 2, overtimeMinimumMinutes: 30, dailyRateBasis: 'CalendarDays',
    latenessRule: 'DayPerLates', latesPerDay: 3, deductAbsences: true, surchargeThreshold: 10_000_000, surchargeRate: 9, gratuityEnabled: true, gratuityDaysPerYear: 30,
    gratuityMinimumMonths: 12, gratuityPartYearMonths: 6, gratuityOnBasic: false, cnicPattern: '^\\d{5}-\\d{7}-\\d$', cnicExample: '35202-1234567-1',
    ibanPattern: '^PK[0-9]{2}[A-Z]{4}[0-9]{16}$', ibanExample: 'PK36SCBL0000001123456702',
    notes: 'Seeded from the Finance Act 2025. Check every figure after each budget (June).',
    taxSlabs: [
      { incomeFrom: 0, incomeTo: 600000, fixedTax: 0, rate: 0 },
      { incomeFrom: 600000, incomeTo: 1200000, fixedTax: 0, rate: 1 },
      { incomeFrom: 1200000, incomeTo: 2200000, fixedTax: 6000, rate: 11 },
      { incomeFrom: 2200000, incomeTo: 3200000, fixedTax: 116000, rate: 23 },
      { incomeFrom: 3200000, incomeTo: 4100000, fixedTax: 346000, rate: 30 },
      { incomeFrom: 4100000, incomeTo: null, fixedTax: 616000, rate: 35 },
    ],
    allowances: [
      { code: 'HRA', name: 'House rent', isTaxable: true, exemptPercentOfBasic: null, sortOrder: 1 },
      { code: 'MEDICAL', name: 'Medical', isTaxable: false, exemptPercentOfBasic: 10, sortOrder: 2 },
      { code: 'CONVEYANCE', name: 'Conveyance', isTaxable: true, exemptPercentOfBasic: null, sortOrder: 3 },
    ],
  },
];

export const leaveTypes: LeaveTypeInfo[] = [
  ['lt-al', 'AL', 'Annual leave', 'Annual', 14, true],
  ['lt-cl', 'CL', 'Casual leave', 'Casual', 10, true],
  ['lt-sl', 'SL', 'Sick leave', 'Sick', 8, true],
  ['lt-ul', 'UL', 'Unpaid leave', 'Unpaid', 0, false],
].map(([id, code, name, kind, days, paid], i) => ({
  id: id as string, version: 'v-1', country: 'PK', code: code as string, name: name as string, kind: kind as LeaveTypeInfo['kind'], daysPerYear: days as number,
  isPaid: paid as boolean, accruesMonthly: code === 'AL', carryOverMaxDays: code === 'AL' ? 14 : 0, encashable: code === 'AL', countsCalendarDays: false, onlyFor: null,
  minimumServiceMonths: code === 'AL' ? 12 : 0, isActive: true, sortOrder: i + 1,
}));

export const holidays: HolidayInfo[] = [
  { id: 'hol-1', version: 'v-1', country: 'PK', date: `${now.getFullYear()}-08-14`, name: 'Independence Day' },
  { id: 'hol-2', version: 'v-1', country: 'PK', date: `${now.getFullYear()}-11-09`, name: 'Iqbal Day' },
  { id: 'hol-3', version: 'v-1', country: 'PK', date: `${now.getFullYear()}-12-25`, name: 'Quaid-e-Azam Day; Christmas' },
];

export const shifts: ShiftInfo[] = [
  { id: 'sh-1', version: 'v-1', name: 'Morning', startTime: '09:00', endTime: '17:00', breakMinutes: 60, graceMinutes: 10, days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'], storeId: 's-main', isActive: true },
  { id: 'sh-2', version: 'v-1', name: 'Evening', startTime: '14:00', endTime: '22:00', breakMinutes: 30, graceMinutes: 10, days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'], storeId: null, isActive: true },
];

export function roster(week: string): RosterInfo {
  const [y, m, d] = week.split('-').map(Number);
  const start = new Date(y, m - 1, d);
  const days = Array.from({ length: 7 }, (_, i) => day(new Date(start.getFullYear(), start.getMonth(), start.getDate() + i)));
  return {
    weekStart: week,
    days,
    rows: [
      { employeeId: 'e-1', code: 'E0001', name: 'Ayesha Khan', storeId: 's-main', usualShiftId: 'sh-1', weekShiftId: null, changes: [{ id: 'rc-1', employeeId: 'e-1', date: days[2], shiftId: null, note: 'Swapped' }] },
      { employeeId: 'e-2', code: 'E0002', name: 'Bilal Ahmed', storeId: 's-main', usualShiftId: 'sh-1', weekShiftId: 'sh-2', changes: [] },
    ],
  };
}

export const attendanceRows: AttendanceRow[] = [
  {
    employeeId: 'e-1', code: 'E0001', name: 'Ayesha Khan', date: daysAgo(1), status: 'Present', shift: 'Morning', shiftStart: `${daysAgo(1)}T09:00:00`, shiftEnd: `${daysAgo(1)}T17:00:00`,
    firstIn: `${daysAgo(1)}T09:25:00`, lastOut: `${daysAgo(1)}T18:10:00`, workedMinutes: 465, lateMinutes: 25, earlyMinutes: 0, overtimeMinutes: 0, holiday: null, leaveCode: null, leaveDays: 0, corrected: false,
  },
  {
    employeeId: 'e-2', code: 'E0002', name: 'Bilal Ahmed', date: daysAgo(1), status: 'Absent', shift: 'Morning', shiftStart: `${daysAgo(1)}T09:00:00`, shiftEnd: `${daysAgo(1)}T17:00:00`,
    firstIn: null, lastOut: null, workedMinutes: 0, lateMinutes: 0, earlyMinutes: 0, overtimeMinutes: 0, holiday: null, leaveCode: null, leaveDays: 0, corrected: false,
  },
  {
    employeeId: 'e-1', code: 'E0001', name: 'Ayesha Khan', date: daysAgo(2), status: 'Leave', shift: 'Morning', shiftStart: null, shiftEnd: null,
    firstIn: null, lastOut: null, workedMinutes: 0, lateMinutes: 0, earlyMinutes: 0, overtimeMinutes: 0, holiday: null, leaveCode: 'CL', leaveDays: 1, corrected: false,
  },
  {
    employeeId: 'e-2', code: 'E0002', name: 'Bilal Ahmed', date: daysAgo(2), status: 'Present', shift: 'Morning', shiftStart: `${daysAgo(2)}T09:00:00`, shiftEnd: `${daysAgo(2)}T17:00:00`,
    firstIn: `${daysAgo(2)}T08:55:00`, lastOut: `${daysAgo(2)}T19:00:00`, workedMinutes: 545, lateMinutes: 0, earlyMinutes: 0, overtimeMinutes: 65, holiday: null, leaveCode: null, leaveDays: 0, corrected: true,
  },
];

export function attendanceSheet(month: string): AttendanceSheet {
  const [y, m] = month.split('-').map(Number);
  const count = new Date(y, m, 0).getDate();
  const days = Array.from({ length: count }, (_, i) => `${y}-${pad(m)}-${pad(i + 1)}`);
  const marks = (pattern: string[]) => days.map((_, i) => pattern[i % pattern.length]);
  return {
    month,
    days,
    rows: [
      { employeeId: 'e-1', code: 'E0001', name: 'Ayesha Khan', marks: marks(['P', 'P', 'L', 'P', 'CL', 'P', 'O']), present: 20, late: 4, leftEarly: 0, absent: 0, leave: 4, holidays: 1, off: 4, overtimeHours: 0 },
      { employeeId: 'e-2', code: 'E0002', name: 'Bilal Ahmed', marks: marks(['P', 'A', 'P', 'P', 'P', 'H', 'O']), present: 21, late: 0, leftEarly: 1, absent: 4, leave: 0, holidays: 4, off: 4, overtimeHours: 6.5 },
    ],
  };
}

export const corrections: CorrectionRow[] = [
  {
    id: 'corr-1', employeeId: 'e-2', code: 'E0002', name: 'Bilal Ahmed', date: daysAgo(1), status: 'Present', workedMinutes: 480, lateMinutes: 0, earlyMinutes: 0, overtimeMinutes: 0,
    reason: 'At the supplier all day', approval: 'Pending', requestedBy: 'omar', requestedAtUtc: '2026-11-03T09:00:00Z', decidedBy: null, decidedAtUtc: null, decisionNote: null,
  },
];

export const leaveRequests: LeaveRequestRow[] = [
  {
    id: 'lr-1', employeeId: 'e-1', code: 'E0001', name: 'Ayesha Khan', leaveTypeId: 'lt-al', leaveType: 'Annual leave', fromDate: daysAgo(-10), toDate: daysAgo(-8), firstDayHalf: false,
    lastDayHalf: false, days: 3, reason: 'Family wedding', status: 'Pending', requestedBy: 'hina', requestedAtUtc: '2026-11-02T10:00:00Z', decidedBy: null, decidedAtUtc: null,
    decisionNote: null, mayDecide: true,
  },
  {
    id: 'lr-2', employeeId: 'e-2', code: 'E0002', name: 'Bilal Ahmed', leaveTypeId: 'lt-cl', leaveType: 'Casual leave', fromDate: daysAgo(2), toDate: daysAgo(2), firstDayHalf: true,
    lastDayHalf: false, days: 0.5, reason: null, status: 'Approved', requestedBy: 'hina', requestedAtUtc: '2026-11-01T10:00:00Z', decidedBy: 'sara', decidedAtUtc: '2026-11-01T11:00:00Z',
    decisionNote: null, mayDecide: false,
  },
];

export const leaveBalances: LeaveBalanceRow[] = [
  { employeeId: 'e-1', code: 'E0001', name: 'Ayesha Khan', leaveTypeId: 'lt-al', leaveCode: 'AL', leaveType: 'Annual leave', paid: true, limited: true, entitled: 14, earned: 11.67, carriedIn: 4, adjusted: 0, taken: 5, pending: 3, available: 7.67 },
  { employeeId: 'e-1', code: 'E0001', name: 'Ayesha Khan', leaveTypeId: 'lt-cl', leaveCode: 'CL', leaveType: 'Casual leave', paid: true, limited: true, entitled: 10, earned: 10, carriedIn: 0, adjusted: 0, taken: 2, pending: 0, available: 8 },
  { employeeId: 'e-1', code: 'E0001', name: 'Ayesha Khan', leaveTypeId: 'lt-ul', leaveCode: 'UL', leaveType: 'Unpaid leave', paid: false, limited: false, entitled: 0, earned: 0, carriedIn: 0, adjusted: 0, taken: 1, pending: 0, available: null },
];

export const salaryHistory: SalaryInfo[] = [
  {
    id: 'sal-2', effectiveFrom: '2026-07-01', basic: 60000, note: 'Yearly increment', changedBy: 'hina', gross: 90000, fixedDeductions: 500,
    components: [
      { kind: 'Allowance', code: 'HRA', name: 'House rent', amount: 24000, accountCode: null },
      { kind: 'Allowance', code: 'MEDICAL', name: 'Medical', amount: 6000, accountCode: null },
      { kind: 'Deduction', code: 'UNION', name: 'Union fee', amount: 500, accountCode: null },
    ],
  },
  { id: 'sal-1', effectiveFrom: '2024-03-01', basic: 50000, note: null, changedBy: 'hina', gross: 70000, fixedDeductions: 0, components: [{ kind: 'Allowance', code: 'HRA', name: 'House rent', amount: 20000, accountCode: null }] },
];

export const loans: LoanRow[] = [
  {
    id: 'ln-1', number: 10000000001, employeeId: 'e-1', code: 'E0001', name: 'Ayesha Khan', kind: 'Advance', issuedOn: '2026-09-15', amount: 30000, instalment: 10000, firstMonth: '2026-10-01',
    status: 'Active', recovered: 10000, outstanding: 20000, reason: 'School fees', paidFromAccount: 'Cash',
    instalments: [
      { number: 1, month: '2026-10-01', amount: 10000, recovered: 10000 },
      { number: 2, month: '2026-11-01', amount: 10000, recovered: 0 },
      { number: 3, month: '2026-12-01', amount: 10000, recovered: 0 },
    ],
  },
  {
    id: 'ln-2', number: 10000000002, employeeId: 'e-2', code: 'E0002', name: 'Bilal Ahmed', kind: 'Loan', issuedOn: daysAgo(1), amount: 50000, instalment: 5000, firstMonth: '2026-12-01',
    status: 'Active', recovered: 0, outstanding: 50000, reason: null, paidFromAccount: 'Bank', instalments: [],
  },
];

function run(id: string, number: number, status: RunRow['status'], extra: Partial<RunRow> = {}): RunRow {
  const [y, m] = thisMonth.split('-').map(Number);
  return {
    id, version: `v-${status}`, number, kind: 'Monthly', year: y, month: m, title: new Intl.DateTimeFormat('en-US', { month: 'long', year: 'numeric' }).format(new Date(y, m - 1, 1)),
    periodStart: `${thisMonth}-01`, periodEnd: day(new Date(y, m, 0)), storeId: 's-main', storeCode: 'MAIN', employeeId: null, employeeName: null, status,
    employees: status === 'Draft' ? 0 : 2, gross: status === 'Draft' ? 0 : 172345.5, deductions: status === 'Draft' ? 0 : 21345.5, net: status === 'Draft' ? 0 : 151000,
    tax: status === 'Draft' ? 0 : 9800, employerContributions: status === 'Draft' ? 0 : 6400, notes: null,
    warnings: status === 'Draft' ? [] : ['E0003 Sana Malik: no salary on 1 Nov 2026.'], createdBy: 'hina', calculatedBy: status === 'Draft' ? null : 'hina',
    calculatedAtUtc: status === 'Draft' ? null : '2026-11-03T10:00:00Z', approvedBy: ['Approved', 'Posted', 'Paid'].includes(status) ? 'sara' : null,
    approvedAtUtc: ['Approved', 'Posted', 'Paid'].includes(status) ? '2026-11-03T11:00:00Z' : null, postedBy: ['Posted', 'Paid'].includes(status) ? 'hina' : null,
    postedAtUtc: ['Posted', 'Paid'].includes(status) ? '2026-11-03T12:00:00Z' : null, journalNumber: ['Posted', 'Paid'].includes(status) ? 10000000042 : null,
    paidOn: status === 'Paid' ? day(now) : null, paidBy: status === 'Paid' ? 'hina' : null, paymentJournalNumber: status === 'Paid' ? 10000000043 : null,
    ...extra,
  };
}

export function payrollRun(id: string, status: RunRow['status']): RunRow {
  return id === 'run-2' ? run('run-2', 10000000002, status, { kind: 'FinalSettlement', title: 'Final settlement of Sana Malik', employeeId: 'e-3', employeeName: 'Sana Malik', storeId: 's-west', storeCode: 'WEST' }) : run('run-1', 10000000001, status);
}

export const payslips: PayslipRow[] = [
  {
    id: 'ps-1', employeeId: 'e-1', code: 'E0001', name: 'Ayesha Khan', designation: 'Cashier', department: 'Sales floor', storeCode: 'MAIN', paymentMethod: 'Bank', daysEmployed: 30,
    daysPresent: 24, paidLeaveDays: 2, unpaidLeaveDays: 0, absentDays: 0, lateDays: 4, overtimeHours: 0, basic: 60000, allowances: 30000, overtime: 0, commission: 2345.5,
    otherEarnings: 0, gross: 92345.5, attendance: 3000, tax: 4800, eobi: 400, loans: 10000, otherDeductions: 500, deductions: 18700, net: 73645.5, employerContributions: 2000,
  },
  {
    id: 'ps-2', employeeId: 'e-2', code: 'E0002', name: 'Bilal Ahmed', designation: 'Store manager', department: 'Sales floor', storeCode: 'MAIN', paymentMethod: 'Cash', daysEmployed: 30,
    daysPresent: 22, paidLeaveDays: 0.5, unpaidLeaveDays: 0, absentDays: 4, lateDays: 0, overtimeHours: 6.5, basic: 70000, allowances: 0, overtime: 10000, commission: 0,
    otherEarnings: 0, gross: 80000, attendance: 0, tax: 5000, eobi: 400, loans: 0, otherDeductions: 0, deductions: 5400, net: 74600, employerContributions: 4400,
  },
];

export function runDetail(id: string, status: RunRow['status']): RunDetail {
  return { run: payrollRun(id, status), payslips: status === 'Draft' ? [] : payslips };
}

export function payslipDetail(id: string): PayslipDetail {
  const row = payslips.find((p) => p.id === id) ?? payslips[0];
  const r = payrollRun('run-1', 'Approved');
  return {
    row, runId: r.id, runNumber: r.number, runTitle: r.title, runStatus: r.status, periodStart: r.periodStart, periodEnd: r.periodEnd, cnic: '35202-1234567-1',
    bankName: 'Standard Chartered', iban: 'PK36SCBL0000001123456702', monthlyBasic: 60000, taxable: 86000, taxableBefore: 344000, taxBefore: 19200,
    projectedAnnualTaxable: 1032000, projectedAnnualTax: 4320, companyName: 'SMA Retail', storeName: 'Downtown',
    lines: [
      { kind: 'Earning', code: 'BASIC', name: 'Basic pay', amount: 60000, quantity: 30, rate: null },
      { kind: 'Earning', code: 'HRA', name: 'House rent', amount: 24000, quantity: null, rate: null },
      { kind: 'Earning', code: 'MEDICAL', name: 'Medical', amount: 6000, quantity: null, rate: null },
      { kind: 'Earning', code: 'COMMISSION', name: 'Commission', amount: 2345.5, quantity: null, rate: null },
      { kind: 'Deduction', code: 'LATE', name: 'Late 4 times', amount: 3000, quantity: 1, rate: 3000 },
      { kind: 'Deduction', code: 'TAX', name: 'Income tax', amount: 4800, quantity: null, rate: null },
      { kind: 'Deduction', code: 'EOBI', name: 'EOBI (employee)', amount: 400, quantity: null, rate: 1 },
      { kind: 'Deduction', code: 'LOAN', name: 'Advance #10000000001, instalment 2', amount: 10000, quantity: null, rate: null },
      { kind: 'Deduction', code: 'UNION', name: 'Union fee', amount: 500, quantity: null, rate: null },
      { kind: 'EmployerContribution', code: 'EOBI-ER', name: 'EOBI (employer)', amount: 2000, quantity: null, rate: 5 },
    ],
  };
}

export function cashSheet(status: RunRow['status']): CashSheet {
  return { run: payrollRun('run-1', status), rows: [{ code: 'E0002', name: 'Bilal Ahmed', designation: 'Store manager', net: 74600 }], total: 74600 };
}

export const hrReports: HrReportInfo[] = [
  { key: 'salary-sheet', group: 'Payroll', title: 'Salary sheet', parameters: ['run'] },
  { key: 'tax-certificate', group: 'Payroll', title: 'Tax deducted (certificate)', parameters: ['employee', 'taxYear'] },
  { key: 'eobi', group: 'Payroll', title: 'EOBI return', parameters: ['month'] },
  { key: 'social-security', group: 'Payroll', title: 'Social security return', parameters: ['month'] },
  { key: 'attendance', group: 'Attendance and leave', title: 'Attendance', parameters: ['month'] },
  { key: 'leave', group: 'Attendance and leave', title: 'Leave taken', parameters: ['dates'] },
  { key: 'overtime', group: 'Attendance and leave', title: 'Overtime', parameters: ['dates'] },
  { key: 'headcount', group: 'People', title: 'Headcount and turnover', parameters: ['dates'] },
];

export function hrReport(key: string): ReportTable {
  if (key === 'eobi')
    return {
      title: 'EOBI return',
      subtitle: 'November 2026',
      columns: [
        { header: 'Code', kind: 'Text', total: false },
        { header: 'Employee', kind: 'Text', total: false },
        { header: 'Wage', kind: 'Money', total: true },
        { header: 'Employee share', kind: 'Money', total: true },
        { header: 'Employer share', kind: 'Money', total: true },
      ],
      rows: [
        ['E0001', 'Ayesha Khan', 89345.5, 400, 2000],
        ['E0002', 'Bilal Ahmed', 80000, 400, 2000],
      ],
      totals: ['Total', null, 169345.5, 800, 4000],
    };
  return {
    title: hrReports.find((r) => r.key === key)?.title ?? 'Report',
    subtitle: null,
    columns: [
      { header: 'Department', kind: 'Text', total: false },
      { header: 'At the start', kind: 'Count', total: true },
      { header: 'Joined', kind: 'Count', total: true },
      { header: 'Left', kind: 'Count', total: true },
      { header: 'At the end', kind: 'Count', total: true },
      { header: 'Turnover', kind: 'Percent', total: false },
    ],
    rows: [
      ['Sales floor', 2, 0, 0, 2, 0],
      ['Warehouse', 1, 0, 1, 0, 200],
    ],
    totals: ['Total', 3, 0, 1, 2, null],
  };
}
