/**
 * Answers the web app's /api requests in the browser with `page.route`, following docs/API.md. Each test can change
 * who is signed in, override any answer, and look at the requests the app sent.
 */
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Page, Request, Route } from '@playwright/test';
import * as f from './fixtures';
import { productionAnswer } from './production-mock';
import type { ItemDetail, UserInfo } from '../src/lib/types';

export interface SentRequest {
  method: string;
  path: string;
  query: URLSearchParams;
  headers: Record<string, string>;
  body: unknown;
}

type Answer = { status?: number; json?: unknown; body?: string | Buffer; contentType?: string } | undefined;
type Override = (request: SentRequest) => Answer | Promise<Answer>;

export interface MockOptions {
  /** Who /api/auth/me says is signed in; null for nobody (401). */
  user?: UserInfo | null;
  /** Answers that win over the built-in ones, by `METHOD /api/path` (`GET /api/items/i-1`) or by a matcher. */
  overrides?: Record<string, Override>;
}

export interface MockApi {
  requests: SentRequest[];
  /** Signs the user in, as POST /api/auth/sign-in would. */
  signIn(user: UserInfo): void;
  last(method: string, path: string): SentRequest | undefined;
}

function paged<T>(items: T[], query: URLSearchParams) {
  const page = Number(query.get('page') ?? 1);
  const pageSize = Number(query.get('pageSize') ?? 50);
  return { items: items.slice((page - 1) * pageSize, page * pageSize), total: items.length, page, pageSize };
}

function search<T>(items: T[], query: URLSearchParams, text: (row: T) => string): T[] {
  const q = (query.get('search') ?? '').toLowerCase();
  return q ? items.filter((row) => text(row).toLowerCase().includes(q)) : items;
}

/** The store logo the mocked branding endpoint sends. */
const LOGO = readFileSync(join(__dirname, 'logo.jpg'));

export async function mockApi(page: Page, options: MockOptions = {}): Promise<MockApi> {
  let user: UserInfo | null = options.user === undefined ? f.superAdmin : options.user;
  // RFID hub keys change as the test adds and revokes them.
  let hubs = f.rfidHubs.map((h) => ({ ...h }));
  const requests: SentRequest[] = [];
  const overrides = options.overrides ?? {};

  function builtIn(r: SentRequest): Answer {
    const { method, path, query } = r;
    const key = `${method} ${path}`;
    const id = path.split('/')[3];

    if (key === 'POST /api/auth/sign-in') {
      const body = r.body as { userName?: string; password?: string };
      if (body?.password !== 'secret') return { status: 401, json: { error: 'Wrong user name or password, or the account is locked for a few minutes.' } };
      user = body.userName === 'kofi' ? f.supervisor : f.superAdmin;
      return { json: { token: 'not-for-javascript', expiresAtUtc: new Date(Date.now() + 8 * 3600_000).toISOString(), user } };
    }
    if (key === 'POST /api/auth/sign-out') {
      user = null;
      return { status: 204 };
    }
    if (!user) return { status: 401, json: { error: 'Sign in again.' } };
    if (method !== 'GET' && r.headers['x-requested-with'] !== 'retailplus-web') return { status: 403, json: { error: 'Missing X-Requested-With.' } };
    const production = productionAnswer(r);
    if (production) return production;

    switch (true) {
      case key === 'GET /api/auth/me':
        return { json: user };
      case key === 'POST /api/auth/change-password':
        user = { ...user, mustChangePassword: false };
        return { status: 204 };
      case key === 'GET /api/stores':
        return { json: user.permissions.includes('stores.all') ? f.stores : f.stores.filter((s) => s.id === user!.storeId) };
      case /^GET \/api\/stores\/[^/]+\/stations$/.test(key):
        return { json: f.stations[id] ?? [] };
      case /^GET \/api\/stores\/[^/]+\/locations$/.test(key):
        return f.locations[id] ? { json: f.locations[id] } : { status: 404, json: { error: 'No such store.' } };
      case /^GET \/api\/stores\/[^/]+\/rfid-hubs$/.test(key):
        return { json: hubs.filter((h) => h.storeId === id).sort((a, b) => Number(a.revoked) - Number(b.revoked)) };
      case /^POST \/api\/stores\/[^/]+\/rfid-hubs$/.test(key): {
        const name = ((r.body as { name?: string })?.name ?? '').trim() || 'RFID hub';
        const hub = { id: 'h-new', storeId: id, name, keyHint: f.newHubKey.slice(0, 8), createdAtUtc: new Date().toISOString(), createdBy: user.userName,
          lastSeenAtUtc: null, lastAddress: null, revoked: false, revokedAtUtc: null };
        hubs = [...hubs, hub];
        return { status: 201, json: { hub, key: f.newHubKey } };
      }
      case /^POST \/api\/rfid-hubs\/[^/]+\/revoke$/.test(key): {
        const hub = hubs.find((h) => h.id === id);
        if (!hub) return { status: 404, json: { error: 'That RFID hub was not found.' } };
        Object.assign(hub, { revoked: true, revokedAtUtc: new Date().toISOString() });
        return { json: hub };
      }
      case key === 'GET /api/staff':
        return { json: f.staff };
      case /^POST \/api\/stores\/[^/]+\/stations$/.test(key):
        return { json: { station: f.station(4, id), code: 'K7Q2-9XMB', expiresAtUtc: new Date(Date.now() + 24 * 3600_000).toISOString() } };
      case /^POST \/api\/stations\/[^/]+\/retire$/.test(key):
        return { json: { ...f.station(3), retired: true } };
      case /^POST \/api\/stations\/[^/]+\/enrollment-code$/.test(key):
        return { json: { station: f.station(21, 's-north'), code: 'P4LM-77ZD', expiresAtUtc: new Date(Date.now() + 24 * 3600_000).toISOString() } };
      case /^GET \/api\/stores\/[^/]+\/settings$/.test(key):
        return { json: f.settings };
      case /^PUT \/api\/stores\/[^/]+\/settings$/.test(key):
        return { json: { ...(r.body as object), version: 'v-2' } };
      case /^GET \/api\/stores\/[^/]+\/images\/logo$/.test(key):
        return { body: LOGO, contentType: 'image/jpeg' };
      case /^GET \/api\/stores\/[^/]+\/images\/watermark$/.test(key):
        return { status: 404, json: { error: 'No watermark yet.' } };
      case key === 'GET /api/tills':
        return { json: query.get('storeId') ? f.tills.filter((t) => t.storeId === query.get('storeId')) : f.tills };
      case key === 'GET /api/sync/batches':
        return { json: f.batches };
      case key === 'POST /api/sync/upload-file':
        return { json: f.uploadResponse };
      case key === 'GET /api/dashboard':
        return { json: { ...f.dashboard, from: query.get('from') ?? f.dashboard.from, to: query.get('to') ?? f.dashboard.to } };
      case key === 'GET /api/sales':
        return { json: paged(search(f.sales, query, (s) => `${s.invoiceNumber} ${s.client ?? ''}`), query) };
      case /^GET \/api\/sales\/[^/]+$/.test(key):
        return { json: f.saleDetail(id) };
      case key === 'GET /api/items':
        return { json: paged(search(f.items, query, (i) => `${i.plu} ${i.name}`), query) };
      case /^GET \/api\/items\/[^/]+$/.test(key):
        return { json: f.itemDetail(id) };
      case /^GET \/api\/items\/[^/]+\/every-store$/.test(key):
        return { json: f.everyStorePlan(id) };
      case /^PUT \/api\/items\/[^/]+$/.test(key) && query.get('everyStore') === 'true':
        return { json: f.everyStoreResult(id, { ...(r.body as ItemDetail), version: 'v-2' }) };
      case /^PUT \/api\/items\/[^/]+$/.test(key):
        return { json: { ...(r.body as object), version: 'v-2' } };
      case key === 'POST /api/items':
        return { json: { ...(r.body as object), id: 'i-new', version: 'v-1', stock: [] } };
      case key === 'GET /api/departments':
        return { json: f.departments };
      case key === 'GET /api/categories':
        return { json: query.get('departmentId') ? f.categories.filter((c) => c.departmentId === query.get('departmentId')) : f.categories };
      case key === 'GET /api/clients':
        return { json: paged(search(f.clients, query, (c) => `${c.name} ${c.company ?? ''}`), query) };
      case /^GET \/api\/clients\/[^/]+$/.test(key):
        return { json: f.clientDetail(id) };
      case key === 'GET /api/suppliers':
        return { json: paged(search(f.suppliers, query, (s) => `${s.code} ${s.company}`), query) };
      case /^GET \/api\/suppliers\/[^/]+$/.test(key):
        return { json: f.supplierDetail(id) };
      case key === 'GET /api/stock/movements':
        return { json: paged(f.movements, query) };
      case key === 'GET /api/transfers':
        return { json: paged(search(f.transfers, query, (t) => `${t.number} ${t.fromLocation} ${t.toLocation} ${t.fromStore} ${t.toStore}`), query) };
      case /^GET \/api\/transfers\/[^/]+$/.test(key):
        return { json: f.transferDetail(id) };
      case key === 'GET /api/stock-counts':
        return { json: paged(search(f.counts, query, (c) => `${c.number} ${c.locationCode}`), query) };
      case /^GET \/api\/stock-counts\/[^/]+$/.test(key):
        return { json: f.countDetail(id) };
      case key === 'GET /api/gift-cards':
        return { json: paged(search(f.giftCards, query, (g) => `${g.number} ${g.client ?? ''}`), query) };
      case /^GET \/api\/gift-cards\/[^/]+$/.test(key):
        return { json: f.giftCardDetail(id) };
      case key === 'GET /api/reports':
        return { json: f.reports };
      case /^GET \/api\/reports\/[^/]+\/csv$/.test(key):
        return { body: 'Date,Customers\n2026-10-28,148\n', contentType: 'text/csv' };
      case /^GET \/api\/reports\/[^/]+$/.test(key):
        return { json: { ...f.dailyReport, title: f.reports.find((x) => x.key === id)?.title ?? f.dailyReport.title } };
      case key === 'GET /api/permissions':
        return { json: f.permissions };
      case key === 'GET /api/roles':
        return { json: f.roles };
      case key === 'GET /api/users':
        return { json: f.users };
      case /^POST \/api\/users\/[^/]+\/reset-password$/.test(key):
        return { json: { temporaryPassword: 'Plum-Kite-42' } };
      case key === 'GET /api/audit':
        return { json: paged(f.audit, query) };
      case key === 'GET /api/time-clock':
        return { json: f.timeClock };
      case key === 'GET /api/time-clock/hours':
        return { json: f.hours };
      case key === 'GET /api/commission':
        return { json: f.commission };
      case key === 'GET /api/commission-rules':
        return { json: f.commissionRules };
      case method === 'DELETE':
        return { status: 204 };
      case method === 'POST' || method === 'PUT':
        return { json: { ...(r.body as object), id: (r.body as { id?: string })?.id ?? 'new-id', version: 'v-2' } };
      default:
        return { status: 404, json: { error: `The mock has no answer for ${key}.` } };
    }
  }

  await page.route('**/api/**', async (route: Route, request: Request) => {
    const url = new URL(request.url());
    let body: unknown = null;
    const buffer = request.postDataBuffer();
    if (buffer && (request.headers()['content-type'] ?? '').includes('application/json')) {
      try {
        body = JSON.parse(buffer.toString('utf8'));
      } catch {
        body = buffer.toString('utf8');
      }
    } else if (buffer) {
      // Multipart uploads: kept as text so a test can look for the field and file names.
      body = buffer.toString('latin1');
    }
    const sent: SentRequest = { method: request.method(), path: url.pathname, query: url.searchParams, headers: request.headers(), body };
    requests.push(sent);

    const override = overrides[`${sent.method} ${sent.path}`];
    const answer = (override ? await override(sent) : undefined) ?? builtIn(sent) ?? { status: 404 };
    const status = answer.status ?? 200;
    if (status === 204) return route.fulfill({ status });
    if (answer.json !== undefined) return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(answer.json) });
    return route.fulfill({ status, contentType: answer.contentType ?? 'application/octet-stream', body: answer.body ?? '' });
  });

  return {
    requests,
    signIn(next) {
      user = next;
    },
    last(method, path) {
      return [...requests].reverse().find((r) => r.method === method && r.path === path);
    },
  };
}
