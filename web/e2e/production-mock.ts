/**
 * The manufacturing part of the mocked API (`mock-api.ts` asks here first). Actions on an order answer the order with
 * its new status, so a test sees what the page shows after them.
 */
import * as f from './production-fixtures';
import type { ProductionOrderDetail } from '../src/lib/production-types';

interface Request {
  method: string;
  path: string;
  query: URLSearchParams;
  body: unknown;
}

type Answer = { status?: number; json?: unknown; body?: string; contentType?: string } | undefined;

export function productionAnswer(r: Request): Answer {
  const { method, path } = r;
  const key = `${method} ${path}`;
  const parts = path.split('/');
  const id = parts[3];
  const body = (r.body ?? {}) as Record<string, unknown>;
  const order = (status?: ProductionOrderDetail['status']) => ({ json: f.orderDetail(id, status) });

  switch (true) {
    case key === 'GET /api/work-centres':
      return { json: f.workCentres };
    case key === 'POST /api/work-centres':
      return { status: 201, json: { ...f.workCentres[0], ...body, id: 'w-new', version: 'v-1', storeCode: 'MAIN', locationCode: 'POS' } };
    case /^PUT \/api\/work-centres\/[^/]+$/.test(key):
      return { json: { ...f.workCentres[0], ...body, id, version: 'v-2', storeCode: 'MAIN', locationCode: 'POS' } };
    case /^DELETE \/api\/(work-centres|boms|quality\/plans)\/[^/]+$/.test(key):
      return { status: 204 };

    case key === 'GET /api/boms': {
      const search = (r.query.get('search') ?? '').toLowerCase();
      return { json: f.boms.filter((b) => !search || `${b.productCode} ${b.productName}`.toLowerCase().includes(search)).filter((b) => r.query.get('active') !== 'true' || b.isActive) };
    }
    case key === 'GET /api/boms/compare':
      return { json: f.differences };
    case /^GET \/api\/boms\/[^/]+\/explode$/.test(key):
      return { json: f.exploded };
    case /^GET \/api\/boms\/[^/]+\/cost$/.test(key):
      return { json: { ...f.cost, lotQuantity: Number(r.query.get('quantity') ?? 10) } };
    case /^POST \/api\/boms\/[^/]+\/copy$/.test(key):
      return { status: 201, json: { ...f.bomDetail(id), id: 'b-copy', version: 'v-1', revision: 3, isActive: false } };
    case /^PUT \/api\/boms\/[^/]+\/routing$/.test(key):
      return { json: { ...f.bomDetail(id), operations: body.operations, version: 'v-3' } };
    case /^GET \/api\/boms\/[^/]+$/.test(key):
      return { json: f.bomDetail(id) };
    case key === 'POST /api/boms':
      return { status: 201, json: { ...(body as object), id: 'b-new', version: 'v-1', revision: 1 } };
    case /^PUT \/api\/boms\/[^/]+$/.test(key):
      return { json: { ...(body as object), version: 'v-3' } };

    case key === 'GET /api/production-orders/board':
      return { json: f.board };
    case key === 'GET /api/production-orders':
      return { json: { items: f.orders, total: f.orders.length, page: 1, pageSize: 50 } };
    case key === 'POST /api/production-orders':
      return { status: 201, json: f.orderDetail('o-2', 'Planned') };
    case /^PUT \/api\/production-orders\/[^/]+$/.test(key):
      return order();
    case /^POST \/api\/production-orders\/[^/]+\/release$/.test(key):
      return { json: { order: f.orderDetail(id, 'Released'), shortages: [{ stockCode: 'SUGAR', description: 'Sugar 1 kg', required: 4.4, available: 3, missing: 1.4, isMade: false }] } };
    case /^POST \/api\/production-orders\/[^/]+\/receive$/.test(key):
      return order(body.complete ? 'Completed' : 'InProgress');
    case /^POST \/api\/production-orders\/[^/]+\/close$/.test(key):
      return order('Closed');
    case /^POST \/api\/production-orders\/[^/]+\/cancel$/.test(key):
      return order('Cancelled');
    case /^POST \/api\/production-orders\/[^/]+\/(issue|issue-remaining|scrap|report|post-costs|operations\/\d+\/(start|finish))$/.test(key):
      return order('InProgress');
    case /^GET \/api\/production-orders\/[^/]+\/inspections$/.test(key):
      return { json: f.inspections.filter((i) => i.productionOrderId === id) };
    case /^POST \/api\/production-orders\/[^/]+\/inspect$/.test(key):
      return { json: { ...f.inspections[0], id: 'in-new', productionOrderId: id } };
    case /^GET \/api\/production-orders\/[^/]+$/.test(key):
      return order();
    case key === 'POST /api/production/post-costs':
      return { json: { orders: 3, problems: [] } };
    case key === 'GET /api/production/customer-orders':
      return { json: [{ id: 'co-1', invoiceNumber: 10000077, name: 'Akua Owusu', plu: 'CAKE', description: 'Chocolate cake', quantity: 2, orderDate: null }] };

    case key === 'POST /api/mrp/run':
      return { json: f.mrp };
    case key === 'GET /api/mrp/suggestions':
      return { json: f.mrp };
    case key === 'POST /api/mrp/firm':
      return { json: { made: ['Production order 100000009: 4 x CAKE'], problems: [] } };
    case key === 'POST /api/mrp/dismiss':
      return { json: { dismissed: ((body.ids as unknown[]) ?? []).length } };
    case key === 'GET /api/mrp/lead-times':
      return { json: f.leadTimes };
    case /^PUT \/api\/mrp\/lead-times\/[^/]+$/.test(key):
      return { status: 204 };
    case key === 'GET /api/capacity':
      return { json: f.capacity };

    case key === 'GET /api/quality/plans':
      return { json: f.qualityPlans };
    case key === 'POST /api/quality/plans':
      return { status: 201, json: { ...(body as object), id: 'qp-new', version: 'v-1' } };
    case /^PUT \/api\/quality\/plans\/[^/]+$/.test(key):
      return { json: { ...(body as object), version: 'v-2' } };
    case key === 'GET /api/quality/inspections':
      return { json: f.inspections };
    case key === 'GET /api/quality/receipts':
      return { json: f.receivedOrders };
    case /^POST \/api\/quality\/receipts\/[^/]+\/inspect$/.test(key):
      return { json: { ...f.inspections[0], id: 'in-po', kind: 'Incoming', purchaseOrderId: parts[4], stockCode: 'SUGAR', passed: true } };

    case key === 'GET /api/production/reports':
      return { json: f.productionReports };
    case /^GET \/api\/production\/reports\/[^/]+\/csv$/.test(key):
      return { body: 'Status,Orders\nplanned,1\n', contentType: 'text/csv' };
    case /^GET \/api\/production\/reports\/[^/]+$/.test(key):
      return { json: { ...f.statusReport, title: f.productionReports.find((x) => x.key === parts[4])?.title ?? f.statusReport.title } };
    default:
      return undefined;
  }
}
