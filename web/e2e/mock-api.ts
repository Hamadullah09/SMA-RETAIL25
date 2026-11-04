/**
 * Answers the web app's /api requests in the browser with `page.route`, following docs/API.md. Each test can change
 * who is signed in, override any answer, and look at the requests the app sent.
 */
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Page, Request, Route } from '@playwright/test';
import * as f from './fixtures';
import { financeAnswer } from './mock-finance';
import { productionAnswer } from './production-mock';
import type { ItemDetail, UserInfo } from '../src/lib/types';
import { createHrMock } from './hr-mock';
import type { PurchaseOrderStatus } from '../src/lib/types';

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
  // E4: opportunities move between stages as the test drags them.
  const e4Opportunities = f.opportunities.map((o) => ({ ...o }));
  // Purchase orders, requisitions, match lines and vouchers change as the supply chain tests work on them.
  const supply = supplyMock();
  const requests: SentRequest[] = [];
  const overrides = options.overrides ?? {};
  const hrAnswer = createHrMock();

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
    // Finance (phase E1): e2e/mock-finance.ts.
    if (path.startsWith('/api/finance/')) return financeAnswer(r);
    const hr = hrAnswer(r);
    if (hr) return hr;
    const production = productionAnswer(r);
    if (production) return production;
    const supplyAnswer = supply(r);
    if (supplyAnswer) return supplyAnswer;

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

      // Phase E4 ------------------------------------------------------------------------------------------------------
      case key === 'GET /api/crm/stages':
        return { json: f.stagesE4 };
      case key === 'PUT /api/crm/stages':
        return { json: (r.body as Array<{ id?: string | null; name: string; probability: number; outcome: string }>).map((s, i) => ({ ...s, id: s.id ?? `st-x${i}`, sortOrder: i + 1 })) };
      case key === 'GET /api/opportunities/board': {
        const owner = query.get('ownerId');
        return { json: f.boardOf(e4Opportunities.filter((o) => !owner || o.ownerId === owner)) };
      }
      case key === 'GET /api/opportunities/report':
        return { json: f.wonLost };
      case key === 'GET /api/opportunities':
        return { json: paged(search(e4Opportunities, query, (o) => `${o.title} ${o.clientName ?? ''} ${o.leadName ?? ''}`), query) };
      case /^POST \/api\/opportunities\/[^/]+\/move$/.test(key): {
        const card = e4Opportunities.find((o) => o.id === id);
        const body = r.body as { stageId: string; lostReason?: string | null };
        const stage = f.stagesE4.find((s) => s.id === body.stageId);
        if (!card || !stage) return { status: 404, json: { error: 'That opportunity was not found.' } };
        if (stage.outcome === 'Lost' && !body.lostReason) return { status: 400, json: { error: 'Say why it was lost.' } };
        Object.assign(card, {
          stageId: stage.id, stage: stage.name, outcome: stage.outcome, probability: stage.probability, weightedValue: (card.expectedValue * stage.probability) / 100,
          lostReason: stage.outcome === 'Lost' ? body.lostReason : null, version: 'v-2',
        });
        return { json: card };
      }
      case /^GET \/api\/opportunities\/[^/]+$/.test(key):
        return { json: e4Opportunities.find((o) => o.id === id) ?? e4Opportunities[0] };
      case key === 'GET /api/leads': {
        const status = query.get('status');
        return { json: paged(search(f.leads.filter((l) => !status || l.status === status), query, (l) => `${l.name} ${l.company ?? ''} ${l.phone ?? ''}`), query) };
      }
      case /^POST \/api\/leads\/[^/]+\/convert$/.test(key): {
        const lead = f.leads.find((l) => l.id === id) ?? f.leads[0];
        const clientId = (r.body as { clientId?: string | null })?.clientId ?? 'cl-new';
        return { json: { lead: { ...lead, status: 'Converted', clientId, clientName: lead.name, version: 'v-2' }, clientId, clientNumber: '10000005' } };
      }
      case /^GET \/api\/leads\/[^/]+$/.test(key):
        return { json: f.leads.find((l) => l.id === id) ?? f.leads[0] };
      case key === 'GET /api/activities/mine':
        return { json: f.myTasksFor(user.id, query.get('due') === 'today') };
      case key === 'GET /api/activities': {
        const done = query.get('done');
        return { json: paged(f.activities.filter((a) => done === null || String(a.isDone) === done), query) };
      }
      case /^POST \/api\/activities\/[^/]+\/done$/.test(key): {
        const a = f.activities.find((x) => x.id === id) ?? f.activities[0];
        const done = (r.body as { done: boolean }).done;
        return { json: { ...a, isDone: done, due: done ? 'done' : a.due, version: 'v-2' } };
      }
      case key === 'GET /api/segments':
        return { json: f.segmentsE4 };
      case key === 'POST /api/segments/preview':
        return { json: f.segmentResult };
      case /^POST \/api\/segments\/[^/]+\/evaluate$/.test(key):
        return { json: f.segmentResult };
      case /^GET \/api\/segments\/[^/]+\/csv$/.test(key):
        return { body: '﻿Number,Name,WhatsApp\n10000002,Daniel Kim,+14165550199\n', contentType: 'text/csv' };
      case /^GET \/api\/segments\/[^/]+$/.test(key):
        return { json: f.segmentsE4.find((s) => s.id === id) ?? f.segmentsE4[0] };
      case key === 'GET /api/whatsapp/status':
        return { json: f.whatsAppStatus };
      case key === 'GET /api/whatsapp/templates':
        return { json: f.templates };
      case key === 'GET /api/campaigns': {
        const status = query.get('status');
        return { json: paged(f.campaigns.filter((c) => !status || c.status === status), query) };
      }
      case /^GET \/api\/campaigns\/[^/]+\/messages$/.test(key): {
        const status = query.get('status');
        return { json: paged(f.campaignMessages.filter((m) => m.campaignId === id && (!status || m.status === status)), query) };
      }
      case /^GET \/api\/campaigns\/[^/]+\/results$/.test(key):
        return { json: { ...f.campaignResults, days: Number(query.get('days') ?? 7) } };
      case /^POST \/api\/campaigns\/[^/]+\/schedule$/.test(key): {
        const c = f.campaigns.find((x) => x.id === id) ?? f.campaigns[1];
        const sendAt = (r.body as { sendAtUtc: string | null }).sendAtUtc;
        return { json: { ...c, status: 'Scheduled', recipients: 2, counts: { ...c.counts, queued: 2 }, scheduledAtUtc: sendAt ?? new Date().toISOString(), version: 'v-2' } };
      }
      case /^POST \/api\/campaigns\/[^/]+\/cancel$/.test(key): {
        const c = f.campaigns.find((x) => x.id === id) ?? f.campaigns[2];
        return { json: { ...c, status: 'Cancelled', version: 'v-2' } };
      }
      case /^GET \/api\/campaigns\/[^/]+$/.test(key):
        return f.campaigns.some((c) => c.id === id) ? { json: f.campaigns.find((c) => c.id === id) } : { status: 404, json: { error: 'That campaign was not found.' } };
      case /^GET \/api\/clients\/[^/]+\/whatsapp$/.test(key):
        return { json: f.clientMessages };
      case /^POST \/api\/clients\/[^/]+\/whatsapp$/.test(key): {
        const template = f.templates.find((t) => t.id === (r.body as { templateId: string }).templateId) ?? f.templates[0];
        return {
          json: { ...f.clientMessages[0], id: 'm-new', campaignId: null, template: template.name, status: 'Sent', sentAtUtc: new Date().toISOString(), deliveredAtUtc: null, readAtUtc: null },
        };
      }
      case key === 'GET /api/promotions': {
        const active = query.get('active') === 'true';
        return { json: search(f.promotions.filter((p) => !active || p.isActive), query, (p) => `${p.name} ${p.couponCode ?? ''}`).map(f.promotionRow) };
      }
      case key === 'GET /api/promotions/report':
        return { json: f.promotionReport };
      case /^GET \/api\/promotions\/[^/]+$/.test(key):
        return { json: f.promotions.find((p) => p.id === id) ?? f.promotions[0] };
      case key === 'POST /api/promotions' || /^PUT \/api\/promotions\/[^/]+$/.test(key): {
        const body = r.body as { name: string; id?: string | null };
        return { status: method === 'POST' ? 201 : 200, json: { ...body, id: body.id ?? 'p-new', version: 'v-2', couponUses: 0, summary: body.name, live: true } };
      }
      case key === 'GET /api/targets': {
        const year = Number(query.get('year')), month = Number(query.get('month'));
        return { json: f.targetsE4.filter((t) => t.year === year && t.month === month && (!query.get('storeId') || t.storeId === query.get('storeId'))) };
      }
      case key === 'GET /api/targets/progress':
        return { json: f.targetProgress };

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

// Supply chain (phase E2) -----------------------------------------------------------------------------------------

/**
 * The supply chain endpoints (src/RetailPlus.Server/Endpoints.Supply.cs): purchase orders and receiving, requisitions and
 * their approval, batches, bins and their lists, the three-way match, landed costs, reorder suggestions, supplier
 * performance and the stores' settings. Undefined for any other request.
 */
function supplyMock(): (r: SentRequest) => Answer {
  const orderStatus = new Map<string, PurchaseOrderStatus>();
  const requisitions = new Map<string, ReturnType<typeof f.requisitionDetail>>([
    ['rq-2', f.requisitionDetail('rq-2', 'Submitted')],
    ['rq-1', f.requisitionDetail('rq-1', 'Draft')],
  ]);
  const matches = f.matchRows.map((m) => ({ ...m }));
  const vouchers = new Map<string, ReturnType<typeof f.landedCostDetail>>(f.landedCostRows.map((v) => [v.id, f.landedCostDetail(v.id)]));
  let settings = { ...f.supplySettings };

  const order = (id: string) => f.purchaseOrderDetail(id, orderStatus.get(id));
  const requisitionRow = (d: ReturnType<typeof f.requisitionDetail>) => ({
    id: d.id, number: d.number, storeCode: d.storeCode, locationCode: d.locationCode, status: d.status, createdAt: d.createdAt, requestedBy: d.requestedBy, neededBy: d.neededBy,
    lines: d.lines.length, total: d.total, decidedBy: d.decidedBy, decidedAt: d.decidedAt, note: d.note,
  });
  const step = (id: string, status: ReturnType<typeof f.requisitionDetail>['status'], body: { version?: string }): Answer => {
    const current = requisitions.get(id);
    if (!current) return { status: 404, json: { error: 'That requisition was not found.' } };
    if (body?.version !== current.version) return { status: 409, json: { error: 'Someone else changed this record. Load it again and make your changes again.' } };
    const next = { ...f.requisitionDetail(id, status), number: current.number, note: current.note };
    requisitions.set(id, next);
    return { json: next };
  };
  const paged = <T,>(items: T[], query: URLSearchParams) => {
    const page = Number(query.get('page') ?? 1);
    const pageSize = Number(query.get('pageSize') ?? 50);
    return { items: items.slice((page - 1) * pageSize, page * pageSize), total: items.length, page, pageSize };
  };

  return (r) => {
    const { method, path, query } = r;
    const key = `${method} ${path}`;
    const parts = path.split('/');
    const id = parts[3];
    const body = (r.body ?? {}) as Record<string, unknown>;
    switch (true) {
      // Purchase orders
      case key === 'GET /api/purchase-orders':
        return { json: paged(f.purchaseOrders.map((p) => ({ ...p, status: orderStatus.get(p.id) ?? p.status })), query) };
      case /^GET \/api\/purchase-orders\/[^/]+$/.test(key):
        return { json: order(id) };
      case /^POST \/api\/purchase-orders\/[^/]+\/post$/.test(key):
        orderStatus.set(id, 'Posted');
        return { json: order(id) };
      case /^POST \/api\/purchase-orders\/[^/]+\/receive$/.test(key):
        orderStatus.set(id, 'Received');
        return { json: order(id) };
      case /^GET \/api\/purchase-orders\/[^/]+\/put-away$/.test(key):
        return { json: f.putAwayList(id) };

      // Requisitions
      case key === 'GET /api/requisitions': {
        const status = query.get('status');
        return { json: paged([...requisitions.values()].filter((d) => !status || d.status === status).map(requisitionRow), query) };
      }
      case key === 'POST /api/requisitions': {
        const made = { ...f.requisitionDetail('rq-new', 'Draft'), id: 'rq-new', number: 10000003, note: (body.note as string) ?? null, version: 'v-new' };
        requisitions.set('rq-new', made);
        return { status: 201, json: made };
      }
      case /^GET \/api\/requisitions\/[^/]+$/.test(key):
        return requisitions.has(id) ? { json: requisitions.get(id) } : { status: 404, json: { error: 'That requisition was not found.' } };
      case /^PUT \/api\/requisitions\/[^/]+$/.test(key): {
        const current = requisitions.get(id)!;
        const saved = { ...current, note: (body.note as string) ?? null, version: current.version + '+' };
        requisitions.set(id, saved);
        return { json: saved };
      }
      case /^POST \/api\/requisitions\/[^/]+\/submit$/.test(key):
        return step(id, 'Submitted', body);
      case /^POST \/api\/requisitions\/[^/]+\/approve$/.test(key):
        return step(id, 'Approved', body);
      case /^POST \/api\/requisitions\/[^/]+\/reject$/.test(key):
        return step(id, 'Rejected', body);
      case /^POST \/api\/requisitions\/[^/]+\/order$/.test(key): {
        const answer = step(id, 'Ordered', body);
        if (answer?.status) return answer;
        return { json: { orders: [{ id: 'po-9', number: 10000009, supplier: 'Cotton Co. Ltd' }, { id: 'po-10', number: 10000010, supplier: 'Sole Brothers' }] } };
      }
      case /^DELETE \/api\/requisitions\/[^/]+$/.test(key):
        requisitions.delete(id);
        return { status: 204 };
      case key === 'GET /api/supply/approval-limits':
        return { json: f.approvalLimits };
      case /^PUT \/api\/supply\/approval-limits\/[^/]+$/.test(key):
        return { json: f.approvalLimits.map((l) => (l.roleId === parts[4] ? { ...l, maxAmount: body.maxAmount as number | null } : l)) };

      // Batches
      case key === 'GET /api/batches': {
        const q = (query.get('search') ?? '').toLowerCase();
        const rows = f.batchRows.filter((b) => (query.get('inStock') === 'false' || b.onHand !== 0) && (!q || `${b.plu} ${b.itemName} ${b.batchNumber}`.toLowerCase().includes(q)));
        return { json: paged(rows, query) };
      }
      case key === 'GET /api/batches/expiring':
        return { json: { ...f.expiringReport, days: Number(query.get('days') ?? settings.expiryWarningDays) } };
      case key === 'GET /api/batches/expired':
        return { json: f.expiredReport };
      case key === 'POST /api/batches':
        return { status: 201, json: { ...f.batchRows[2], id: 'bt-new', batchNumber: String(body.batchNumber ?? '').toUpperCase(), expiresOn: body.expiresOn ?? null } };
      case /^PUT \/api\/batches\/[^/]+$/.test(key):
        return { json: { ...(f.batchRows.find((b) => b.id === id) ?? f.batchRows[0]), expiresOn: body.expiresOn ?? null, note: body.note ?? null, version: 'v-2' } };
      case /^POST \/api\/batches\/[^/]+\/adjust$/.test(key): {
        const b = f.batchRows.find((x) => x.id === id) ?? f.batchRows[0];
        const quantity = Number(body.quantity ?? 0);
        return { json: { ...b, onHand: body.setTo ? quantity : b.onHand + quantity } };
      }

      // Bins and their lists
      case key === 'GET /api/bins':
        return { json: f.binRows };
      case key === 'POST /api/bins':
        return { status: 201, json: { ...f.binRows[1], id: 'bn-new', code: String(body.code ?? '').toUpperCase(), zone: body.zone ?? null, items: 0 } };
      case /^PUT \/api\/bins\/[^/]+$/.test(key):
        return { json: { ...(f.binRows.find((b) => b.id === id) ?? f.binRows[0]), ...body, version: 'v-2' } };
      case /^DELETE \/api\/bins\/[^/]+$/.test(key):
        return (f.binRows.find((b) => b.id === id)?.items ?? 0) > 0
          ? { status: 400, json: { error: `${f.binRows.find((b) => b.id === id)!.items} items are kept in bin ${f.binRows.find((b) => b.id === id)!.code}; move them first, or make the bin inactive.` } }
          : { status: 204 };
      case key === 'POST /api/bins/assign':
        return { json: { changed: (body.itemIds as string[] | undefined)?.length ?? 0 } };
      case key === 'GET /api/bins/count-sheet':
        return { json: f.countSheet };
      case key === 'GET /api/bins/pick-orders':
        return { json: f.pickList };
      case /^GET \/api\/transfers\/[^/]+\/pick$/.test(key):
        return { json: { ...f.pickList, title: 'Pick list: transfer' } };

      // Three-way match
      case key === 'GET /api/matching': {
        const status = query.get('status');
        return { json: paged(matches.filter((m) => !status || m.status === status), query) };
      }
      case key === 'POST /api/matching/run':
        return { json: { lines: 2, matched: 1, held: 1, posted: 1 } };
      case /^POST \/api\/matching\/[^/]+\/(accept|reject)$/.test(key): {
        const m = matches.find((x) => x.id === id);
        if (!m) return { status: 404, json: { error: 'That bill line was not found.' } };
        if (body.version !== m.version) return { status: 409, json: { error: 'Someone else changed this record.' } };
        Object.assign(m, { status: parts[4] === 'accept' ? 'Accepted' : 'Rejected', decidedBy: 'sara', decidedAtUtc: new Date().toISOString(), decisionNote: body.note ?? null, version: 'v-2' });
        return { json: m };
      }
      case key === 'GET /api/matching/received-not-billed':
        return { json: f.receivedNotBilled };
      case key === 'GET /api/matching/billed-not-received':
        return { json: f.billedNotReceived };

      // Landed costs
      case key === 'GET /api/landed-costs':
        return { json: paged(f.landedCostRows.map((v) => ({ ...v, status: vouchers.get(v.id)?.status ?? v.status })), query) };
      case key === 'GET /api/landed-costs/receipts':
        return { json: f.landedReceipts };
      case /^GET \/api\/landed-costs\/[^/]+(\/preview)?$/.test(key):
        return vouchers.has(id) ? { json: vouchers.get(id) } : { status: 404, json: { error: 'That voucher was not found.' } };
      case key === 'POST /api/landed-costs': {
        const charges = (body.charges as Array<{ amount: number }>) ?? [];
        const made = { ...f.landedCostDetail('lc-2'), id: 'lc-new', number: 100000000003, version: 'v-new', reference: (body.reference as string) ?? null, total: charges.reduce((s, c) => s + c.amount, 0) };
        vouchers.set('lc-new', made);
        return { status: 201, json: made };
      }
      case /^PUT \/api\/landed-costs\/[^/]+$/.test(key):
        return { json: vouchers.get(id) };
      case /^POST \/api\/landed-costs\/[^/]+\/post$/.test(key): {
        const v = vouchers.get(id)!;
        if (body.version !== v.version) return { status: 409, json: { error: 'Someone else changed this record.' } };
        const posted = { ...v, status: 'Posted' as const, version: 'v-posted', postedAtUtc: new Date().toISOString(), postedBy: 'sara' };
        vouchers.set(id, posted);
        return { json: posted };
      }
      case /^DELETE \/api\/landed-costs\/[^/]+$/.test(key):
        vouchers.delete(id);
        return { status: 204 };

      // Reorder suggestions, supplier performance, settings
      case key === 'GET /api/reorder': {
        const supplier = query.get('supplierId');
        const all = query.get('all') === 'true';
        return { json: { locationId: query.get('locationId') ?? 'l-main-pos', rows: f.reorderRows.filter((x) => (all || x.suggestedPieces > 0) && (!supplier || x.supplierId === supplier)) } };
      }
      case key === 'POST /api/reorder/orders':
        return { json: { orders: [{ id: 'po-11', number: 10000011, supplier: 'Cotton Co. Ltd' }, { id: 'po-12', number: 10000012, supplier: 'Sole Brothers' }] } };
      case key === 'POST /api/reorder/requisition': {
        const made = { ...f.requisitionDetail('rq-reorder', body.submit ? 'Submitted' : 'Draft'), number: 10000004 };
        requisitions.set('rq-reorder', made);
        return { status: 201, json: made };
      }
      case key === 'GET /api/supplier-performance':
        return { json: { ...f.supplierPerformance, from: query.get('from') ?? f.supplierPerformance.from, to: query.get('to') ?? f.supplierPerformance.to } };
      case key === 'GET /api/supply/settings':
        return { json: { ...settings, storeId: query.get('storeId') ?? settings.storeId } };
      case key === 'PUT /api/supply/settings':
        settings = { ...(body as typeof settings), version: 'v-2' };
        return { json: settings };
      default:
        return undefined;
    }
  };
}
