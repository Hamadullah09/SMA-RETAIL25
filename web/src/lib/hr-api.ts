/**
 * The HR and payroll endpoints (phase E3: `/api/hr/…` and `/api/payroll/…`), added to the one client in api.ts. They
 * use the client's own helpers, so they send the cookie, the CSRF header on changes and map errors like every call.
 */
import type { Query } from './api';
import type {
  AttendanceRow,
  AttendanceSheet,
  CashSheet,
  CorrectionRequest,
  CorrectionRow,
  EmployeeDetail,
  EmployeeDocumentRow,
  EmployeeRow,
  HolidayInfo,
  HrListItem,
  HrReportInfo,
  HrSettingsInfo,
  LeaveAdjustmentBody,
  LeaveBalanceRow,
  LeaveRequestBody,
  LeaveRequestRow,
  LeaveTypeInfo,
  LoanBody,
  LoanRow,
  Paged,
  PayBody,
  PayslipDetail,
  ReportTable,
  RosterChangeInfo,
  RosterInfo,
  RosterRequest,
  RuleSetInfo,
  RunBody,
  RunDetail,
  RunRow,
  SalaryInfo,
  SettlementBody,
  ShiftInfo,
} from './types';

/** The parts of the client the HR endpoints are built from. */
export interface HrClientParts {
  get<T>(path: string, query?: Query, signal?: AbortSignal): Promise<T>;
  post<T>(path: string, body?: unknown): Promise<T>;
  put<T>(path: string, body: unknown): Promise<T>;
  del<T = void>(path: string): Promise<T>;
  blob(path: string, opts?: { query?: Query }): Promise<Blob>;
  upload<T>(path: string, form: FormData, onProgress?: (fraction: number) => void, method?: 'POST' | 'PUT'): Promise<T>;
}

export function hrEndpoints({ get, post, put, del, blob, upload }: HrClientParts) {
  const id = encodeURIComponent;
  return {
    // HR lists and settings (shared by every store)
    hrDepartments: () => get<HrListItem[]>('/hr/departments'),
    addHrDepartment: (item: HrListItem) => post<HrListItem>('/hr/departments', item),
    saveHrDepartment: (departmentId: string, item: HrListItem) => put<HrListItem>(`/hr/departments/${id(departmentId)}`, item),
    deleteHrDepartment: (departmentId: string) => del(`/hr/departments/${id(departmentId)}`),
    designations: () => get<HrListItem[]>('/hr/designations'),
    addDesignation: (item: HrListItem) => post<HrListItem>('/hr/designations', item),
    saveDesignation: (designationId: string, item: HrListItem) => put<HrListItem>(`/hr/designations/${id(designationId)}`, item),
    deleteDesignation: (designationId: string) => del(`/hr/designations/${id(designationId)}`),
    hrSettings: () => get<HrSettingsInfo>('/hr/settings'),
    /** A two-letter country, or null to follow the company's. */
    setStoreCountry: (storeId: string, country: string | null) => put<HrSettingsInfo>(`/hr/stores/${id(storeId)}/country`, { country }),
    ruleSets: (country?: string | null) => get<RuleSetInfo[]>('/hr/rule-sets', { country }),
    ruleSet: (ruleSetId: string) => get<RuleSetInfo>(`/hr/rule-sets/${id(ruleSetId)}`),
    addRuleSet: (set: RuleSetInfo) => post<RuleSetInfo>('/hr/rule-sets', set),
    saveRuleSet: (ruleSetId: string, set: RuleSetInfo) => put<RuleSetInfo>(`/hr/rule-sets/${id(ruleSetId)}`, set),
    deleteRuleSet: (ruleSetId: string) => del(`/hr/rule-sets/${id(ruleSetId)}`),
    leaveTypes: (country?: string | null) => get<LeaveTypeInfo[]>('/hr/leave-types', { country }),
    addLeaveType: (type: LeaveTypeInfo) => post<LeaveTypeInfo>('/hr/leave-types', type),
    saveLeaveType: (typeId: string, type: LeaveTypeInfo) => put<LeaveTypeInfo>(`/hr/leave-types/${id(typeId)}`, type),
    deleteLeaveType: (typeId: string) => del(`/hr/leave-types/${id(typeId)}`),
    holidays: (query: Query) => get<HolidayInfo[]>('/hr/holidays', query),
    addHoliday: (holiday: HolidayInfo) => post<HolidayInfo>('/hr/holidays', holiday),
    saveHoliday: (holidayId: string, holiday: HolidayInfo) => put<HolidayInfo>(`/hr/holidays/${id(holidayId)}`, holiday),
    deleteHoliday: (holidayId: string) => del(`/hr/holidays/${id(holidayId)}`),

    // Employees and their documents
    employees: (query: Query, signal?: AbortSignal) => get<Paged<EmployeeRow>>('/hr/employees', query, signal),
    nextEmployeeCode: () => get<{ code: string }>('/hr/employees/next-code'),
    employee: (employeeId: string) => get<EmployeeDetail>(`/hr/employees/${id(employeeId)}`),
    addEmployee: (employee: EmployeeDetail) => post<EmployeeDetail>('/hr/employees', employee),
    saveEmployee: (employeeId: string, employee: EmployeeDetail) => put<EmployeeDetail>(`/hr/employees/${id(employeeId)}`, employee),
    deleteEmployee: (employeeId: string) => del(`/hr/employees/${id(employeeId)}`),
    employeeDocuments: (employeeId: string) => get<EmployeeDocumentRow[]>(`/hr/employees/${id(employeeId)}/documents`),
    uploadEmployeeDocument: (employeeId: string, file: File, title: string | null, onProgress?: (fraction: number) => void) => {
      const form = new FormData();
      form.append('file', file, file.name);
      if (title) form.append('title', title);
      return upload<EmployeeDocumentRow>(`/hr/employees/${id(employeeId)}/documents`, form, onProgress);
    },
    employeeDocument: (documentId: string) => blob(`/hr/documents/${id(documentId)}`),
    deleteEmployeeDocument: (documentId: string) => del(`/hr/documents/${id(documentId)}`),

    // Shifts and the roster
    shifts: (query: Query) => get<ShiftInfo[]>('/hr/shifts', query),
    addShift: (shift: ShiftInfo) => post<ShiftInfo>('/hr/shifts', shift),
    saveShift: (shiftId: string, shift: ShiftInfo) => put<ShiftInfo>(`/hr/shifts/${id(shiftId)}`, shift),
    deleteShift: (shiftId: string) => del(`/hr/shifts/${id(shiftId)}`),
    /** `week`: any day of the week (its Monday is used). */
    roster: (query: Query) => get<RosterInfo>('/hr/roster', query),
    saveRoster: (request: RosterRequest) => put<void>('/hr/roster', request),
    addRosterChange: (change: RosterChangeInfo) => post<RosterChangeInfo>('/hr/roster/changes', change),
    deleteRosterChange: (changeId: string) => del(`/hr/roster/changes/${id(changeId)}`),

    // Attendance
    attendance: (query: Query) => get<AttendanceRow[]>('/hr/attendance', query),
    /** `month`: yyyy-MM. */
    attendanceSheet: (query: Query) => get<AttendanceSheet>('/hr/attendance/sheet', query),
    corrections: (query: Query) => get<CorrectionRow[]>('/hr/attendance/corrections', query),
    requestCorrection: (request: CorrectionRequest) => post<CorrectionRow>('/hr/attendance/corrections', request),
    approveCorrection: (correctionId: string, note?: string | null) => post<CorrectionRow>(`/hr/attendance/corrections/${id(correctionId)}/approve`, { note: note ?? null }),
    rejectCorrection: (correctionId: string, note?: string | null) => post<CorrectionRow>(`/hr/attendance/corrections/${id(correctionId)}/reject`, { note: note ?? null }),

    // Leave
    leaveRequests: (query: Query, signal?: AbortSignal) => get<Paged<LeaveRequestRow>>('/hr/leave-requests', query, signal),
    requestLeave: (request: LeaveRequestBody) => post<LeaveRequestRow>('/hr/leave-requests', request),
    approveLeave: (requestId: string, note?: string | null) => post<LeaveRequestRow>(`/hr/leave-requests/${id(requestId)}/approve`, { note: note ?? null }),
    rejectLeave: (requestId: string, note?: string | null) => post<LeaveRequestRow>(`/hr/leave-requests/${id(requestId)}/reject`, { note: note ?? null }),
    cancelLeave: (requestId: string) => post<LeaveRequestRow>(`/hr/leave-requests/${id(requestId)}/cancel`, {}),
    leaveBalances: (query: Query) => get<LeaveBalanceRow[]>('/hr/leave-balances', query),
    adjustLeave: (adjustment: LeaveAdjustmentBody) => post<unknown>('/hr/leave-adjustments', adjustment),

    // Salaries and loans
    salaryHistory: (employeeId: string) => get<SalaryInfo[]>(`/hr/employees/${id(employeeId)}/salary`),
    saveSalary: (employeeId: string, salary: SalaryInfo) => post<SalaryInfo>(`/hr/employees/${id(employeeId)}/salary`, salary),
    deleteSalary: (salaryId: string) => del(`/hr/salary/${id(salaryId)}`),
    loans: (query: Query) => get<LoanRow[]>('/payroll/loans', query),
    loan: (loanId: string) => get<LoanRow>(`/payroll/loans/${id(loanId)}`),
    issueLoan: (loan: LoanBody) => post<LoanRow>('/payroll/loans', loan),
    cancelLoan: (loanId: string, reason: string) => post<LoanRow>(`/payroll/loans/${id(loanId)}/cancel`, { reason }),

    // Payroll runs and payslips
    payrollRuns: (query: Query) => get<RunRow[]>('/payroll/runs', query),
    addPayrollRun: (run: RunBody) => post<RunRow>('/payroll/runs', run),
    addSettlement: (settlement: SettlementBody) => post<RunRow>('/payroll/settlements', settlement),
    payrollRun: (runId: string) => get<RunDetail>(`/payroll/runs/${id(runId)}`),
    deletePayrollRun: (runId: string) => del(`/payroll/runs/${id(runId)}`),
    calculateRun: (runId: string, version: string) => post<RunRow>(`/payroll/runs/${id(runId)}/calculate`, { version }),
    approveRun: (runId: string, version: string) => post<RunRow>(`/payroll/runs/${id(runId)}/approve`, { version }),
    reopenRun: (runId: string, version: string) => post<RunRow>(`/payroll/runs/${id(runId)}/reopen`, { version }),
    postRun: (runId: string, version: string) => post<RunRow>(`/payroll/runs/${id(runId)}/post`, { version }),
    payRun: (runId: string, pay: PayBody) => post<RunRow>(`/payroll/runs/${id(runId)}/pay`, pay),
    /** The bank transfer list, as a CSV file. */
    bankTransfers: (runId: string) => blob(`/payroll/runs/${id(runId)}/bank-transfers`),
    cashSheet: (runId: string) => get<CashSheet>(`/payroll/runs/${id(runId)}/cash-sheet`),
    payslip: (payslipId: string) => get<PayslipDetail>(`/payroll/payslips/${id(payslipId)}`),

    // HR reports
    hrReports: () => get<HrReportInfo[]>('/hr/reports'),
    hrReport: (key: string, query: Query) => get<ReportTable>(`/hr/reports/${id(key)}`, query),
    hrReportCsv: (key: string, query: Query) => blob(`/hr/reports/${id(key)}/csv`, { query }),
  };
}
