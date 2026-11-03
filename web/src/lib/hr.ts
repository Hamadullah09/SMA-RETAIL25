/**
 * Words, chips and small rules of the HR and payroll pages: statuses as people read them, minutes and months, the
 * payroll run's steps and which of its buttons a role gets, and the payslip's sections.
 */
import type { ChipTone } from '@/components/ui/page';
import type {
  AttendanceStatus,
  EmploymentType,
  HrApproval,
  LeaveRequestStatus,
  LoanStatus,
  PayrollRunStatus,
  PayslipLineInfo,
  PayslipLineKind,
} from './types';

export const ATTENDANCE_STATUS: Record<AttendanceStatus, { label: string; tone: ChipTone }> = {
  Present: { label: 'Present', tone: 'ok' },
  Absent: { label: 'Absent', tone: 'danger' },
  Leave: { label: 'On leave', tone: 'warn' },
  Holiday: { label: 'Holiday', tone: 'plain' },
  Off: { label: 'Day off', tone: 'plain' },
  Scheduled: { label: 'To come', tone: 'plain' },
};

export const LEAVE_STATUS: Record<LeaveRequestStatus, { label: string; tone: ChipTone }> = {
  Pending: { label: 'Waiting', tone: 'warn' },
  Approved: { label: 'Approved', tone: 'ok' },
  Rejected: { label: 'Rejected', tone: 'danger' },
  Cancelled: { label: 'Cancelled', tone: 'plain' },
};

export const APPROVAL: Record<HrApproval, { label: string; tone: ChipTone }> = {
  Pending: { label: 'Waiting', tone: 'warn' },
  Approved: { label: 'Approved', tone: 'ok' },
  Rejected: { label: 'Rejected', tone: 'danger' },
};

export const RUN_STATUS: Record<PayrollRunStatus, { label: string; tone: ChipTone }> = {
  Draft: { label: 'Draft', tone: 'plain' },
  Calculated: { label: 'Calculated', tone: 'warn' },
  Approved: { label: 'Approved', tone: 'dark' },
  Posted: { label: 'Posted', tone: 'ok' },
  Paid: { label: 'Paid', tone: 'ok' },
};

export const LOAN_STATUS: Record<LoanStatus, { label: string; tone: ChipTone }> = {
  Active: { label: 'Being recovered', tone: 'warn' },
  Settled: { label: 'Recovered', tone: 'ok' },
  Cancelled: { label: 'Cancelled', tone: 'plain' },
};

export const EMPLOYMENT_TYPES: Record<EmploymentType, string> = {
  Permanent: 'Permanent',
  Contract: 'Contract',
  Probation: 'Probation',
  DailyWage: 'Daily wage',
};

export const GENDERS: Record<string, string> = { Male: 'Male', Female: 'Female', Other: 'Other' };
export const MARITAL_STATUS: Record<string, string> = { Unmarried: 'Unmarried', Married: 'Married', Divorced: 'Divorced', Widowed: 'Widowed' };
export const PAYMENT_METHODS: Record<string, string> = { Cash: 'Cash (on the cash sheet)', Bank: 'Bank transfer' };
export const LEAVE_KINDS: Record<string, string> = {
  Annual: 'Annual',
  Casual: 'Casual',
  Sick: 'Sick',
  Maternity: 'Maternity',
  Paternity: 'Paternity',
  Unpaid: 'Unpaid',
  Other: 'Other',
};
export const LOAN_KINDS: Record<string, string> = { Loan: 'Loan', Advance: 'Salary advance' };
export const DAILY_RATE_BASIS: Record<string, string> = {
  CalendarDays: 'Days of the month',
  ThirtyDays: 'Always 30 days',
  WorkingDays: 'Working days of the month',
};
export const LATENESS_RULES: Record<string, string> = {
  None: 'Not taken off pay',
  DayPerLates: "A day's pay for a number of late arrivals",
  PerMinute: 'Every minute late, at the hourly rate',
};
export const SOCIAL_SECURITY_BASIS: Record<string, string> = { Wage: 'The wage earned', MinimumWage: 'The minimum wage' };
export const ATTENDANCE_CHOICES: AttendanceStatus[] = ['Present', 'Absent', 'Leave', 'Holiday', 'Off'];

/** The days of the week as the server names them, Monday first. */
export const WEEK_DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'] as const;

/** `Mon–Sat`, `Mon–Fri`, `Every day`, or the days one by one (`Mon, Wed, Fri`). */
export function daysText(days: readonly string[]): string {
  const set = new Set(days);
  const flags = WEEK_DAYS.map((d) => set.has(d));
  if (flags.every(Boolean)) return 'Every day';
  if (flags.every((on) => !on)) return 'No days';
  const first = flags.indexOf(true);
  const last = flags.lastIndexOf(true);
  const together = flags.slice(first, last + 1).every(Boolean);
  const short = (i: number) => WEEK_DAYS[i].slice(0, 3);
  if (together && last > first) return `${short(first)}–${short(last)}`;
  return WEEK_DAYS.map((_, i) => i).filter((i) => flags[i]).map(short).join(', ');
}

/** `1 h 05 min`, `45 min`, or `` for none. */
export function formatMinutes(minutes: number | null | undefined): string {
  if (!minutes || !Number.isFinite(minutes) || minutes <= 0) return '';
  const whole = Math.round(minutes);
  const h = Math.floor(whole / 60);
  const m = whole % 60;
  if (h === 0) return `${m} min`;
  return m === 0 ? `${h} h` : `${h} h ${String(m).padStart(2, '0')} min`;
}

/** Days as a person writes them: `1`, `0.5`, `2.5`. */
export function formatDays(days: number | null | undefined): string {
  if (days === null || days === undefined || !Number.isFinite(days)) return '';
  return String(Math.round(days * 100) / 100);
}

/** `yyyy-MM` of a local Date. */
export function monthKey(date: Date = new Date()): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
}

/** `November 2026` for `2026-11` (or for a year and month). */
export function monthName(key: string, locale?: string): string;
export function monthName(year: number, month: number, locale?: string): string;
export function monthName(a: string | number, b?: number | string, c?: string): string {
  let year: number;
  let month: number;
  let locale: string | undefined;
  if (typeof a === 'string') {
    const [y, m] = a.split('-').map(Number);
    year = y;
    month = m;
    locale = typeof b === 'string' ? b : undefined;
  } else {
    year = a;
    month = Number(b);
    locale = c;
  }
  if (!year || !month) return String(a);
  return new Intl.DateTimeFormat(locale ?? 'en-US', { month: 'long', year: 'numeric' }).format(new Date(year, month - 1, 1));
}

/** The month `n` months after `key` (`yyyy-MM`). */
export function addMonths(key: string, n: number): string {
  const [y, m] = key.split('-').map(Number);
  return monthKey(new Date(y, m - 1 + n, 1));
}

/** The tax year as people name it: `2026-27` for one from July 2026, `2026` for a calendar year. */
export function taxYearName(startYear: number, startMonth = 7): string {
  return startMonth === 1 ? String(startYear) : `${startYear}-${String((startYear + 1) % 100).padStart(2, '0')}`;
}

/** The tax year (by its first calendar year) holding a day, for a year that starts in `startMonth`. */
export function taxYearOf(date: Date = new Date(), startMonth = 7): number {
  return date.getMonth() + 1 >= startMonth ? date.getFullYear() : date.getFullYear() - 1;
}

// Payroll runs -------------------------------------------------------------------------------------------------------

export const RUN_STEPS: readonly PayrollRunStatus[] = ['Draft', 'Calculated', 'Approved', 'Posted', 'Paid'];

export type RunAction = 'calculate' | 'approve' | 'reopen' | 'post' | 'pay' | 'delete';

/**
 * The buttons a run shows, by its status and what the role may do: `payroll.run` calculates, posts, pays and deletes;
 * `payroll.approve` approves and reopens.
 */
export function runActions(status: PayrollRunStatus, can: { run: boolean; approve: boolean }): RunAction[] {
  const actions: RunAction[] = [];
  if (can.run && (status === 'Draft' || status === 'Calculated')) actions.push('calculate');
  if (can.approve && status === 'Calculated') actions.push('approve');
  if (can.approve && status === 'Approved') actions.push('reopen');
  if (can.run && status === 'Approved') actions.push('post');
  if (can.run && status === 'Posted') actions.push('pay');
  if (can.run && (status === 'Draft' || status === 'Calculated')) actions.push('delete');
  return actions;
}

// Payslips -----------------------------------------------------------------------------------------------------------

export interface PayslipSection {
  kind: PayslipLineKind;
  title: string;
  lines: PayslipLineInfo[];
  total: number;
}

/** A payslip's earnings, deductions and the employer's contributions, each with its total (empty sections left out). */
export function payslipSections(lines: readonly PayslipLineInfo[]): PayslipSection[] {
  const titles: Record<PayslipLineKind, string> = { Earning: 'Earnings', Deduction: 'Deductions', EmployerContribution: 'Paid by the employer' };
  return (['Earning', 'Deduction', 'EmployerContribution'] as const)
    .map((kind) => {
      const own = lines.filter((l) => l.kind === kind);
      const total = Math.round(own.reduce((s, l) => s + l.amount, 0) * 100) / 100;
      return { kind, title: titles[kind], lines: own, total };
    })
    .filter((s) => s.lines.length > 0);
}

/** What a monthly sheet's marks mean. */
export const SHEET_MARKS: ReadonlyArray<{ mark: string; meaning: string }> = [
  { mark: 'P', meaning: 'present' },
  { mark: 'L', meaning: 'late' },
  { mark: 'E', meaning: 'left early' },
  { mark: 'A', meaning: 'absent' },
  { mark: 'AL, CL…', meaning: 'on leave (½ for half a day)' },
  { mark: 'H', meaning: 'holiday' },
  { mark: 'O', meaning: 'day off' },
  { mark: '·', meaning: 'still to come' },
];

/** The tone of a sheet mark. */
export function markTone(mark: string): 'ok' | 'warn' | 'danger' | 'plain' {
  if (mark === 'P' || mark === 'P½') return 'ok';
  if (mark === 'L' || mark === 'E') return 'warn';
  if (mark === 'A') return 'danger';
  return 'plain';
}

/** Saves a downloaded file under a name. */
export function saveBlob(blob: Blob, name: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
