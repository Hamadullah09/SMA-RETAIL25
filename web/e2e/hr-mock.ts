/**
 * The HR and payroll part of the mocked API (phase E3), called from mock-api.ts before its own answers. Payroll runs
 * keep their status as the test presses their buttons, and a stale version is a 409 as on the server.
 */
import * as h from './hr-fixtures';
import type { RunRow } from '../src/lib/types';

interface Request {
  method: string;
  path: string;
  query: URLSearchParams;
  body: unknown;
}

type Answer = { status?: number; json?: unknown; body?: string; contentType?: string } | undefined;

function paged<T>(items: T[], query: URLSearchParams) {
  const page = Number(query.get('page') ?? 1);
  const pageSize = Number(query.get('pageSize') ?? 50);
  return { items: items.slice((page - 1) * pageSize, page * pageSize), total: items.length, page, pageSize };
}

export function createHrMock() {
  const runs: Record<string, RunRow['status']> = { 'run-1': 'Draft', 'run-2': 'Calculated' };
  const next: Record<string, RunRow['status']> = { calculate: 'Calculated', approve: 'Approved', reopen: 'Calculated', post: 'Posted', pay: 'Paid' };

  return function hrAnswer(r: Request): Answer {
    const { method, path, query } = r;
    if (!path.startsWith('/api/hr/') && !path.startsWith('/api/payroll/')) return undefined;
    const key = `${method} ${path}`;
    const parts = path.split('/');
    const id = parts[4];

    switch (true) {
      case key === 'GET /api/hr/departments':
        return { json: h.hrDepartments };
      case key === 'GET /api/hr/designations':
        return { json: h.designations };
      case key === 'GET /api/hr/settings':
        return { json: h.hrSettings };
      case /^PUT \/api\/hr\/stores\/[^/]+\/country$/.test(key):
        return { json: h.hrSettings };
      case key === 'GET /api/hr/rule-sets':
        return { json: h.ruleSets };
      case key === 'GET /api/hr/leave-types':
        return { json: h.leaveTypes };
      case key === 'GET /api/hr/holidays':
        return { json: h.holidays };
      case key === 'GET /api/hr/employees': {
        const status = query.get('status') ?? 'active';
        const search = (query.get('search') ?? '').toLowerCase();
        const rows = h.hrEmployees.filter((e) => (status === 'left' ? !e.active : status === 'all' ? true : e.active)).filter((e) => !search || `${e.code} ${e.fullName}`.toLowerCase().includes(search));
        return { json: paged(rows, query) };
      }
      case key === 'GET /api/hr/employees/next-code':
        return { json: { code: 'E0004' } };
      case /^GET \/api\/hr\/employees\/[^/]+$/.test(key):
        return { json: h.employeeDetail(id) };
      case key === 'POST /api/hr/employees':
        return { status: 201, json: { ...(r.body as object), id: 'e-new', version: 'v-1', code: (r.body as { code?: string }).code ?? 'E0004' } };
      case /^PUT \/api\/hr\/employees\/[^/]+$/.test(key):
        return (r.body as { version?: string }).version === 'stale' ? { status: 409, json: { error: 'Someone else changed this record.' } } : { json: { ...(r.body as object), version: 'v-2' } };
      case /^GET \/api\/hr\/employees\/[^/]+\/documents$/.test(key):
        return { json: h.employeeDocuments };
      case /^GET \/api\/hr\/employees\/[^/]+\/salary$/.test(key):
        return { json: h.salaryHistory };
      case /^POST \/api\/hr\/employees\/[^/]+\/salary$/.test(key):
        return { status: 201, json: { ...(r.body as object), id: 'sal-new', changedBy: 'sara' } };
      case /^GET \/api\/hr\/documents\/[^/]+$/.test(key):
        return { body: '%PDF-1.4', contentType: 'application/pdf' };
      case key === 'GET /api/hr/shifts':
        return { json: h.shifts };
      case key === 'GET /api/hr/roster':
        return { json: h.roster(query.get('week') ?? new Date().toISOString().slice(0, 10)) };
      case key === 'PUT /api/hr/roster':
        return { status: 204 };
      case key === 'POST /api/hr/roster/changes':
        return { json: { ...(r.body as object), id: 'rc-new' } };
      case key === 'GET /api/hr/attendance':
        return { json: query.get('employeeId') ? h.attendanceRows.filter((a) => a.employeeId === query.get('employeeId')) : h.attendanceRows };
      case key === 'GET /api/hr/attendance/sheet':
        return { json: h.attendanceSheet(query.get('month') ?? new Date().toISOString().slice(0, 7)) };
      case key === 'GET /api/hr/attendance/corrections':
        return { json: query.get('status') ? h.corrections.filter((c) => c.approval === query.get('status')) : h.corrections };
      case key === 'POST /api/hr/attendance/corrections':
        return { status: 201, json: { ...h.corrections[0], ...(r.body as object), id: 'corr-new', approval: 'Pending' } };
      case /^POST \/api\/hr\/attendance\/corrections\/[^/]+\/(approve|reject)$/.test(key):
        return { json: { ...h.corrections[0], approval: path.endsWith('approve') ? 'Approved' : 'Rejected', decidedBy: 'sara' } };
      case key === 'GET /api/hr/leave-requests': {
        const status = query.get('status');
        return { json: paged(h.leaveRequests.filter((l) => !status || l.status === status), query) };
      }
      case key === 'POST /api/hr/leave-requests':
        return { status: 201, json: { ...h.leaveRequests[0], ...(r.body as object), id: 'lr-new', status: 'Pending', name: 'Ayesha Khan', days: 2 } };
      case /^POST \/api\/hr\/leave-requests\/[^/]+\/(approve|reject|cancel)$/.test(key): {
        const action = parts[5];
        return { json: { ...h.leaveRequests[0], status: action === 'approve' ? 'Approved' : action === 'reject' ? 'Rejected' : 'Cancelled', mayDecide: false } };
      }
      case key === 'GET /api/hr/leave-balances':
        return { json: h.leaveBalances };
      case key === 'POST /api/hr/leave-adjustments':
        return { status: 201, json: { id: 'adj-1' } };
      case key === 'GET /api/hr/reports':
        return { json: h.hrReports };
      case /^GET \/api\/hr\/reports\/[^/]+\/csv$/.test(key):
        return { body: 'Code,Employee\nE0001,Ayesha Khan\n', contentType: 'text/csv' };
      case /^GET \/api\/hr\/reports\/[^/]+$/.test(key):
        return { json: h.hrReport(id) };

      case key === 'GET /api/payroll/loans':
        return { json: query.get('status') ? h.loans.filter((l) => l.status === query.get('status')) : h.loans };
      case key === 'POST /api/payroll/loans':
        return { status: 201, json: { ...h.loans[1], id: 'ln-new', number: 10000000003 } };
      case /^POST \/api\/payroll\/loans\/[^/]+\/cancel$/.test(key):
        return { json: { ...h.loans[1], status: 'Cancelled' } };
      case key === 'GET /api/payroll/runs':
        return { json: Object.keys(runs).map((runId) => h.payrollRun(runId, runs[runId])) };
      case key === 'POST /api/payroll/runs':
        runs['run-1'] = 'Draft';
        return { status: 201, json: h.payrollRun('run-1', 'Draft') };
      case key === 'POST /api/payroll/settlements':
        return { status: 201, json: h.payrollRun('run-2', runs['run-2']) };
      case /^GET \/api\/payroll\/runs\/[^/]+$/.test(key):
        return runs[id] ? { json: h.runDetail(id, runs[id]) } : { status: 404, json: { error: 'That payroll run was not found.' } };
      case /^GET \/api\/payroll\/runs\/[^/]+\/cash-sheet$/.test(key):
        return { json: h.cashSheet(runs[id] ?? 'Calculated') };
      case /^GET \/api\/payroll\/runs\/[^/]+\/bank-transfers$/.test(key):
        return { body: 'Employee code,Employee,CNIC,Bank,IBAN,Amount,Reference\nE0001,Ayesha Khan,,,PK36SCBL0000001123456702,73645.50,Salary\n', contentType: 'text/csv' };
      case /^POST \/api\/payroll\/runs\/[^/]+\/(calculate|approve|reopen|post|pay)$/.test(key): {
        const action = parts[5];
        const sent = (r.body as { version?: string })?.version;
        if (!runs[id]) return { status: 404, json: { error: 'That payroll run was not found.' } };
        if (sent !== `v-${runs[id]}`) return { status: 409, json: { error: 'Someone else changed this record. Load it again and make your changes again.' } };
        runs[id] = next[action];
        return { json: h.payrollRun(id, runs[id]) };
      }
      case /^DELETE \/api\/payroll\/runs\/[^/]+$/.test(key):
        delete runs[id];
        return { status: 204 };
      case /^GET \/api\/payroll\/payslips\/[^/]+$/.test(key):
        return { json: h.payslipDetail(id) };
      default:
        return undefined;
    }
  };
}
