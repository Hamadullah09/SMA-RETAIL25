/**
 * The manufacturing calls of docs/API.md, made through the app's one client (`api.ts`): the same cookie, CSRF header
 * and error handling. `createProductionApi` takes any client, so the tests can give it one with a fake `fetch`.
 */
import { api as appApi, type Api, type Query } from './api';
import type {
  BomDetail,
  BomDifference,
  BomListRow,
  ByProductQuantity,
  CapacityRow,
  CostRollUp,
  CustomerOrderPick,
  ExplodedLine,
  FirmResult,
  IncomingInspectionRequest,
  Inspection,
  LeadTime,
  MrpRunRequest,
  MrpView,
  OutputInspectionRequest,
  PostCostsResponse,
  ProductionBoardColumn,
  ProductionOrderDetail,
  ProductionOrderPage,
  ProductionOrderRequest,
  ProductionReportResult,
  QualityPlan,
  ReceivedOrder,
  ReleaseResponse,
  RoutingOperation,
  WorkCentre,
  WorkCentreRequest,
} from './production-types';
import type { ReportInfo } from './types';

export function createProductionApi(client: Api) {
  const get = <T>(path: string, query?: Query) => client.json<T>(path, { query });
  const post = <T>(path: string, body?: unknown) => client.json<T>(path, { method: 'POST', body: body ?? {} });
  const put = <T>(path: string, body: unknown) => client.json<T>(path, { method: 'PUT', body });
  const del = (path: string) => client.json<void>(path, { method: 'DELETE' });
  const id = encodeURIComponent;
  const order = (orderId: string, action: string, body?: unknown) => post<ProductionOrderDetail>(`/production-orders/${id(orderId)}/${action}`, body);

  return {
    // Work centres
    workCentres: (query: Query) => get<WorkCentre[]>('/work-centres', query),
    addWorkCentre: (body: WorkCentreRequest) => post<WorkCentre>('/work-centres', body),
    saveWorkCentre: (centreId: string, body: WorkCentreRequest) => put<WorkCentre>(`/work-centres/${id(centreId)}`, body),
    deleteWorkCentre: (centreId: string) => del(`/work-centres/${id(centreId)}`),

    // Bills of materials and routings
    boms: (query: Query) => get<BomListRow[]>('/boms', query),
    bom: (bomId: string) => get<BomDetail>(`/boms/${id(bomId)}`),
    addBom: (bom: BomDetail) => post<BomDetail>('/boms', bom),
    saveBom: (bomId: string, bom: BomDetail) => put<BomDetail>(`/boms/${id(bomId)}`, bom),
    deleteBom: (bomId: string) => del(`/boms/${id(bomId)}`),
    copyBom: (bomId: string, productCode: string | null = null) => post<BomDetail>(`/boms/${id(bomId)}/copy`, { productCode }),
    saveRouting: (bomId: string, version: string | null, operations: RoutingOperation[]) => put<BomDetail>(`/boms/${id(bomId)}/routing`, { version, operations }),
    compareBoms: (from: string, to: string) => get<BomDifference[]>('/boms/compare', { from, to }),
    explodeBom: (bomId: string, query: Query = {}) => get<ExplodedLine[]>(`/boms/${id(bomId)}/explode`, query),
    bomCost: (bomId: string, query: Query = {}) => get<CostRollUp>(`/boms/${id(bomId)}/cost`, query),

    // Production orders
    orders: (query: Query) => get<ProductionOrderPage>('/production-orders', query),
    board: (query: Query) => get<ProductionBoardColumn[]>('/production-orders/board', query),
    order: (orderId: string) => get<ProductionOrderDetail>(`/production-orders/${id(orderId)}`),
    addOrder: (body: ProductionOrderRequest) => post<ProductionOrderDetail>('/production-orders', body),
    saveOrder: (orderId: string, body: ProductionOrderRequest) => put<ProductionOrderDetail>(`/production-orders/${id(orderId)}`, body),
    release: (orderId: string) => post<ReleaseResponse>(`/production-orders/${id(orderId)}/release`),
    cancel: (orderId: string, reason: string | null) => order(orderId, 'cancel', { reason }),
    close: (orderId: string) => order(orderId, 'close'),
    startOperation: (orderId: string, sequence: number) => order(orderId, `operations/${sequence}/start`),
    finishOperation: (orderId: string, sequence: number) => order(orderId, `operations/${sequence}/finish`),
    report: (orderId: string, body: { sequence: number; minutes: number; good: number; scrap: number; note: string | null }) => order(orderId, 'report', body),
    issue: (orderId: string, code: string, quantity: number, note: string | null = null) => order(orderId, 'issue', { code, quantity, note }),
    issueRemaining: (orderId: string, operationSequence: number | null = null) => order(orderId, 'issue-remaining', { operationSequence }),
    scrap: (orderId: string, code: string, quantity: number, reason: string | null) => order(orderId, 'scrap', { code, quantity, reason }),
    receive: (orderId: string, quantity: number, complete: boolean, byProducts: ByProductQuantity[] | null = null) =>
      order(orderId, 'receive', { quantity, complete, byProducts }),
    postCosts: (orderId: string) => order(orderId, 'post-costs'),
    postStoreCosts: (query: Query) => client.json<PostCostsResponse>('/production/post-costs', { method: 'POST', body: {}, query }),
    orderInspections: (orderId: string) => get<Inspection[]>(`/production-orders/${id(orderId)}/inspections`),
    inspectOutput: (orderId: string, body: OutputInspectionRequest) => post<Inspection>(`/production-orders/${id(orderId)}/inspect`, body),
    customerOrders: (query: Query) => get<CustomerOrderPick[]>('/production/customer-orders', query),

    // Planning and capacity
    runPlanning: (body: MrpRunRequest) => post<MrpView>('/mrp/run', body),
    suggestions: (query: Query) => get<MrpView>('/mrp/suggestions', query),
    firm: (ids: string[], quantities: Record<string, number> | null = null) => post<FirmResult>('/mrp/firm', { ids, quantities }),
    dismiss: (ids: string[]) => post<{ dismissed: number }>('/mrp/dismiss', { ids }),
    leadTimes: () => get<LeadTime[]>('/mrp/lead-times'),
    setLeadTime: (supplierId: string, days: number | null) => put<void>(`/mrp/lead-times/${id(supplierId)}`, { days }),
    capacity: (query: Query) => get<CapacityRow[]>('/capacity', query),

    // Quality
    qualityPlans: () => get<QualityPlan[]>('/quality/plans'),
    addQualityPlan: (plan: QualityPlan) => post<QualityPlan>('/quality/plans', plan),
    saveQualityPlan: (planId: string, plan: QualityPlan) => put<QualityPlan>(`/quality/plans/${id(planId)}`, plan),
    deleteQualityPlan: (planId: string) => del(`/quality/plans/${id(planId)}`),
    inspections: (query: Query) => get<Inspection[]>('/quality/inspections', query),
    receivedOrders: (query: Query) => get<ReceivedOrder[]>('/quality/receipts', query),
    inspectReceipt: (purchaseOrderId: string, body: IncomingInspectionRequest) => post<Inspection>(`/quality/receipts/${id(purchaseOrderId)}/inspect`, body),

    // Reports
    reports: () => get<ReportInfo[]>('/production/reports'),
    reportResult: (key: string, query: Query) => get<ProductionReportResult>(`/production/reports/${id(key)}`, query),
    reportCsv: (key: string, query: Query) => client.blob(`/production/reports/${id(key)}/csv`, { query }),
  };
}

export type ProductionApi = ReturnType<typeof createProductionApi>;

/** The app's manufacturing client. */
export const productionApi: ProductionApi = createProductionApi(appApi);
