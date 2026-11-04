/**
 * The finance part of the mocked server (phase E1): answers every `/api/finance/...` request from
 * `e2e/finance-fixtures.ts`, following docs/API.md. `e2e/mock-api.ts` hands these requests here; a test overrides any
 * answer the usual way.
 */
import * as f from './finance-fixtures';
import type { SentRequest } from './mock-api';
import type { BillRequest, ExpenseRequest, JournalRequest, PaymentRequest } from '../src/lib/types';

type Answer = { status?: number; json?: unknown; body?: string; contentType?: string };

function paged<T>(items: T[], query: URLSearchParams) {
  const page = Number(query.get('page') ?? 1);
  const pageSize = Number(query.get('pageSize') ?? 50);
  return { items: items.slice((page - 1) * pageSize, page * pageSize), total: items.length, page, pageSize };
}

export function financeAnswer(r: SentRequest): Answer {
  const { method, path, query } = r;
  const rest = path.slice('/api/finance'.length);
  const key = `${method} ${rest}`;
  const id = rest.split('/')[2];

  switch (true) {
    case key === 'GET /home':
      return { json: f.financeHome };
    case key === 'GET /accounts':
      return { json: f.financeAccounts };
    case key === 'GET /roles':
      return { json: ['Cash', 'Bank', 'Inventory', 'AccountsPayable', 'AccountsReceivable', 'Suspense'] };
    case key === 'GET /bank-accounts':
      return { json: query.get('storeId') ? f.bankAccounts.filter((b) => b.storeId === null || b.storeId === query.get('storeId')) : f.bankAccounts };
    case /^GET \/bank-accounts\/[^/]+\/reconciliation$/.test(key):
      return { json: { ...f.reconciliation, bankAccountId: id, asOf: query.get('asOf') ?? f.today } };
    case /^GET \/bank-accounts\/[^/]+\/statement-format$/.test(key):
      return { json: f.statementFormat };
    case /^POST \/bank-accounts\/[^/]+\/statements$/.test(key):
      return { json: { statementId: 'st-2', lines: 14, fromDate: '2026-10-01', toDate: '2026-10-31', closingBalance: 24950 } };
    case /^POST \/bank-accounts\/[^/]+\/auto-match$/.test(key):
      return { json: { matched: 1 } };
    case /^POST \/statement-lines\/[^/]+\/(match|unmatch|ignore)$/.test(key):
      return { status: 204 };
    case /^POST \/statement-lines\/[^/]+\/entry$/.test(key):
      return { json: { ...f.journalDetail('je-12'), id: 'je-30', number: 30, kind: 'Bank', memo: 'Service charges' } };
    case key === 'GET /bank-transfers' || key === 'GET /deposits' || key === 'GET /deposits/candidates':
      return { json: [] };
    case key === 'GET /journals': {
      const status = query.get('status');
      return { json: paged(status ? f.journals.filter((j) => j.status === status) : f.journals, query) };
    }
    case /^GET \/journals\/[^/]+$/.test(key):
      return { json: f.journalDetail(id) };
    case key === 'POST /journals': {
      const body = r.body as JournalRequest;
      return { status: 201, json: { ...f.journalDetail('je-20'), id: 'je-new', memo: body.memo, date: body.date, createdBy: 'nadia' } };
    }
    case /^POST \/journals\/[^/]+\/approve$/.test(key):
      return { json: { ...f.journalDetail(id), status: 'Posted', number: 21, postedBy: 'nadia', postedAtUtc: new Date().toISOString(), needsApproval: false } };
    case key === 'GET /bills':
      return { json: paged(query.get('status') === 'open' ? f.bills.filter((b) => b.open > 0) : f.bills, query) };
    case key === 'GET /bills/due':
      return { json: f.bills.filter((b) => b.kind === 'Bill') };
    case /^GET \/bills\/[^/]+$/.test(key):
      return { json: f.billDetail(id) };
    case key === 'POST /bills': {
      const body = r.body as BillRequest;
      const subtotal = body.lines.reduce((sum, l) => sum + l.quantity * l.unitPrice, 0);
      const tax = body.lines.reduce((sum, l) => sum + (l.quantity * l.unitPrice * l.taxRate) / 100, 0);
      return { status: 201, json: { ...f.billDetail('bill-1'), id: 'bill-new', number: 0, status: 'Draft', supplierId: body.supplierId, subtotal, taxTotal: tax, total: subtotal + tax } };
    }
    case /^POST \/bills\/[^/]+\/post$/.test(key):
      return { json: { ...f.billDetail('bill-1'), id, number: 104, status: 'Posted' } };
    case key === 'GET /withholding':
      return { json: f.withholding };
    case /^GET \/suppliers\/[^/]+$/.test(key):
      return { json: { supplierId: id, taxNumber: '1234567-8', salesTaxNumber: null, isFiler: true, withholdingKind: 'Goods', withholdingExempt: false, paymentTermsDays: 30, defaultAccountId: null } };
    case key === 'GET /payments':
      return { json: paged(f.payments, query) };
    case key === 'POST /payment-runs': {
      const body = r.body as PaymentRequest;
      return {
        json: {
          run: { id: 'run-1', number: 1, date: body.date, bankAccountId: body.bankAccountId, total: 101700, payments: 1, notes: null, paidBy: 'nadia', paidAtUtc: new Date().toISOString() },
          payments: [],
        },
      };
    }
    case key === 'GET /expenses':
      return { json: paged(f.expenses, query) };
    case /^GET \/expenses\/[^/]+$/.test(key):
      return { json: { expense: f.expenses.find((e) => e.id === id) ?? f.expenses[0], attachments: [] } };
    case key === 'POST /expenses': {
      const body = r.body as ExpenseRequest;
      const expense = { ...f.expenses[0], ...body, id: 'ex-new', version: 'v-1', number: body.post ? 32 : 0, status: body.post ? 'Posted' : 'Draft', total: body.amount + body.taxAmount, attachments: 0 };
      return { status: 201, json: { expense, attachments: [] } };
    }
    case key === 'GET /recurring-expenses':
      return { json: f.recurringExpenses };
    case key === 'GET /reports':
      return { json: f.financeReports };
    case /^GET \/reports\/[^/]+\/csv$/.test(key):
      return { body: 'Code,Account,Amount\r\n4010,Sales,12000.00\r\n', contentType: 'text/csv' };
    case /^GET \/reports\/[^/]+$/.test(key): {
      const title = f.financeReports.find((x) => x.key === id)?.title ?? 'Report';
      return { json: { ...f.profitAndLoss, title, subtitle: `${query.get('from')} to ${query.get('to')}` } };
    }
    case key === 'GET /export':
      return { body: query.get('format') === 'tally' ? '<ENVELOPE/>' : 'Date,Number\r\n', contentType: 'text/plain' };
    case key === 'GET /daily-journal':
      return { body: 'Date,Account\r\n', contentType: 'text/csv' };
    case key === 'GET /years':
      return { json: f.years };
    case /^POST \/periods\/[^/]+\/(lock|unlock)$/.test(key): {
      const lock = rest.endsWith('/lock');
      return { json: f.years.map((y) => ({ ...y, periods: y.periods.map((p) => (p.id === id ? { ...p, status: lock ? 'Locked' : 'Open' } : p)) })) };
    }
    case /^POST \/years\/[^/]+\/close$/.test(key):
      return { json: f.years.map((y) => ({ ...y, status: 'Closed', closedAtUtc: new Date().toISOString(), closedBy: 'nadia', periods: y.periods.map((p) => ({ ...p, status: 'Closed' })) })) };
    case key === 'GET /budgets':
      return { json: { ...f.budget, yearId: query.get('yearId') ?? f.budget.yearId, storeId: query.get('storeId') } };
    case key === 'PUT /budgets':
      return { json: { ...f.budget, storeId: query.get('storeId') } };
    case key === 'POST /budgets/copy':
      return { json: { copied: 24 } };
    case key === 'GET /suspense':
      return { json: f.suspense };
    case key === 'POST /post-days':
      return { json: { entries: 3, problems: [] } };
    case key === 'GET /settings':
      return { json: f.financeSettings };
    case key === 'PUT /settings':
      return { json: { ...(r.body as object), version: 'v-2' } };
    case key === 'GET /tax-rules':
      return { json: f.taxRules };
    case /^GET \/stores\/[^/]+$/.test(key):
      return { json: f.storeFinance(id) };
    case key === 'GET /mapping':
      return { json: f.mapping };
    case key === 'PUT /mapping':
      return { json: r.body };
    case key === 'GET /fiscal':
      return { json: f.fiscalQueue };
    case method === 'DELETE':
      return { status: 204 };
    case method === 'POST' || method === 'PUT':
      return { json: { ...(r.body as object), id: 'new-id', version: 'v-2' } };
    default:
      return { status: 404, json: { error: `The mock has no answer for ${method} ${path}.` } };
  }
}
