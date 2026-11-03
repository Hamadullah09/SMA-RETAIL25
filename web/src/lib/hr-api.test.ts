import { describe, expect, it, vi } from 'vitest';
import { CSRF_HEADER, CSRF_VALUE, createApi } from './api';

type Call = { url: string; init: RequestInit };

function fakeFetch(status: number, body?: unknown, contentType = 'application/json') {
  const calls: Call[] = [];
  const fn = vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ url: String(url), init: init ?? {} });
    const text = body === undefined ? '' : typeof body === 'string' ? body : JSON.stringify(body);
    return new Response(status === 204 ? null : text, { status, headers: { 'Content-Type': contentType } });
  });
  return { fetch: fn as unknown as typeof fetch, calls };
}

const headers = (call: Call) => call.init.headers as Record<string, string>;

describe('the HR and payroll client', () => {
  it('lists employees with the filters, without the CSRF header', async () => {
    const { fetch, calls } = fakeFetch(200, { items: [], total: 0, page: 1, pageSize: 50 });
    await createApi({ base: '', fetch }).employees({ storeId: 's1', status: 'active', search: '', departmentId: null, page: 2 });
    expect(calls[0].url).toBe('/api/hr/employees?storeId=s1&status=active&page=2');
    expect(calls[0].init.method).toBe('GET');
    expect(headers(calls[0])[CSRF_HEADER]).toBeUndefined();
  });

  it.each([
    ['POST', '/api/payroll/runs/r%2F1/calculate', (api: ReturnType<typeof createApi>) => api.calculateRun('r/1', 'v-1'), { version: 'v-1' }],
    ['POST', '/api/payroll/runs/r1/approve', (api: ReturnType<typeof createApi>) => api.approveRun('r1', 'v-2'), { version: 'v-2' }],
    ['POST', '/api/payroll/runs/r1/reopen', (api: ReturnType<typeof createApi>) => api.reopenRun('r1', 'v-3'), { version: 'v-3' }],
    ['POST', '/api/payroll/runs/r1/post', (api: ReturnType<typeof createApi>) => api.postRun('r1', 'v-4'), { version: 'v-4' }],
    ['POST', '/api/hr/leave-requests/l1/approve', (api: ReturnType<typeof createApi>) => api.approveLeave('l1', 'Enjoy'), { note: 'Enjoy' }],
    ['POST', '/api/hr/attendance/corrections/c1/reject', (api: ReturnType<typeof createApi>) => api.rejectCorrection('c1'), { note: null }],
    ['PUT', '/api/hr/stores/s1/country', (api: ReturnType<typeof createApi>) => api.setStoreCountry('s1', null), { country: null }],
    ['POST', '/api/payroll/loans/n1/cancel', (api: ReturnType<typeof createApi>) => api.cancelLoan('n1', 'Paid back'), { reason: 'Paid back' }],
  ])('%s %s carries the CSRF header and its body', async (method, url, call, body) => {
    const { fetch, calls } = fakeFetch(200, {});
    await call(createApi({ base: '', fetch }));
    expect(calls[0].url).toBe(url);
    expect(calls[0].init.method).toBe(method);
    expect(headers(calls[0])[CSRF_HEADER]).toBe(CSRF_VALUE);
    expect(JSON.parse(String(calls[0].init.body))).toEqual(body);
  });

  it('deletes with DELETE', async () => {
    const { fetch, calls } = fakeFetch(204);
    const api = createApi({ base: '', fetch });
    await api.deleteEmployee('e1');
    await api.deletePayrollRun('r1');
    await api.deleteRosterChange('d1');
    expect(calls.map((c) => `${c.init.method} ${c.url}`)).toEqual(['DELETE /api/hr/employees/e1', 'DELETE /api/payroll/runs/r1', 'DELETE /api/hr/roster/changes/d1']);
  });

  it('downloads the bank list and a report as files', async () => {
    const { fetch, calls } = fakeFetch(200, 'Employee code,Employee\n', 'text/csv');
    const api = createApi({ base: '', fetch });
    const blob = await api.bankTransfers('r1');
    expect(await blob.text()).toContain('Employee code');
    await api.hrReportCsv('eobi', { month: '2026-11', storeId: null });
    expect(calls.map((c) => c.url)).toEqual(['/api/payroll/runs/r1/bank-transfers', '/api/hr/reports/eobi/csv?month=2026-11']);
  });

  it('asks for a month sheet and a week roster by their parameters', async () => {
    const { fetch, calls } = fakeFetch(200, {});
    const api = createApi({ base: '', fetch });
    await api.attendanceSheet({ month: '2026-11', storeId: 's1' });
    await api.roster({ week: '2026-11-04' });
    await api.leaveBalances({ year: 2026, employeeId: 'e1' });
    expect(calls.map((c) => c.url)).toEqual(['/api/hr/attendance/sheet?month=2026-11&storeId=s1', '/api/hr/roster?week=2026-11-04', '/api/hr/leave-balances?year=2026&employeeId=e1']);
  });
});
