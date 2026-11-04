/**
 * Manufacturing answers of the server that follow docs/API.md, for the end-to-end tests. Shapes come from
 * src/lib/production-types.ts, so a contract change that breaks them fails `npm run typecheck`.
 */
import type {
  BomDetail,
  BomDifference,
  BomListRow,
  CapacityRow,
  CostRollUp,
  ExplodedLine,
  Inspection,
  LeadTime,
  MrpView,
  ProductionBoardColumn,
  ProductionOrderDetail,
  ProductionOrderRow,
  ProductionReportResult,
  QualityPlan,
  ReceivedOrder,
  WorkCentre,
} from '../src/lib/production-types';
import type { ReportInfo } from '../src/lib/types';

const today = (() => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
})();
const monday = (() => {
  const d = new Date();
  d.setDate(d.getDate() - ((d.getDay() + 6) % 7));
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
})();
const nextMonday = (() => {
  const d = new Date(monday + 'T00:00:00');
  d.setDate(d.getDate() + 7);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
})();

export const productionPermissions = [
  ['production.view', 'Production', 'See bills of materials, production orders and planning'],
  ['production.plan', 'Production', 'Bills of materials, work centres, production orders, planning and quality'],
  ['production.floor', 'Production', 'Work on production orders at the shop floor: operations, materials, output'],
  ['production.costing', 'Production', 'Production costs and variances, and posting them to the ledger'],
] as const;

export const workCentres: WorkCentre[] = [
  {
    id: 'w-1', version: 'v-1', storeId: 's-main', storeCode: 'MAIN', locationId: 'l-main-pos', locationCode: 'POS', code: 'OVEN', name: 'Deck oven', labourCostPerHour: 60,
    machineCostPerHour: 30, overheadPerHour: 12, capacityHoursPerDay: 8, daysPerWeek: 6, isActive: true, notes: null,
  },
  {
    id: 'w-2', version: 'v-1', storeId: 's-main', storeCode: 'MAIN', locationId: 'l-main-pos', locationCode: 'POS', code: 'PACK', name: 'Packing bench', labourCostPerHour: 25,
    machineCostPerHour: 0, overheadPerHour: 5, capacityHoursPerDay: 8, daysPerWeek: 6, isActive: true, notes: null,
  },
];

export const boms: BomListRow[] = [
  { id: 'b-1', version: 'v-2', productCode: 'CAKE', productName: 'Chocolate cake', revision: 2, isActive: true, effectiveFrom: null, effectiveTo: null, yieldQuantity: 10, lines: 3, byProducts: 1, operations: 2, inForce: true },
  { id: 'b-0', version: 'v-1', productCode: 'CAKE', productName: 'Chocolate cake', revision: 1, isActive: false, effectiveFrom: null, effectiveTo: null, yieldQuantity: 10, lines: 2, byProducts: 0, operations: 1, inForce: false },
  { id: 'b-2', version: 'v-1', productCode: 'GIFT', productName: 'Gift pack', revision: 1, isActive: true, effectiveFrom: null, effectiveTo: null, yieldQuantity: 1, lines: 2, byProducts: 0, operations: 0, inForce: true },
];

export function bomDetail(id: string): BomDetail {
  const row = boms.find((b) => b.id === id) ?? boms[0];
  return {
    id: row.id, version: row.version, productCode: row.productCode, productName: row.productName, revision: row.revision, isActive: row.isActive, effectiveFrom: null,
    effectiveTo: null, yieldQuantity: row.yieldQuantity, overheadPercent: 5, notes: null,
    lines: [
      { componentCode: 'FLOUR', description: 'Flour 1 kg', quantity: 5, scrapPercent: 0, issueMethod: 'Manual', operationSequence: 10 },
      { componentCode: 'SUGAR', description: 'Sugar 1 kg', quantity: 2, scrapPercent: 10, issueMethod: 'Manual', operationSequence: null },
      { componentCode: 'BOX', description: 'Cake box', quantity: 10, scrapPercent: 0, issueMethod: 'Backflush', operationSequence: 20 },
    ],
    byProducts: [{ stockCode: 'CRUMB', description: 'Crumb bag', quantity: 1, costSharePercent: 10 }],
    operations: [
      { sequence: 10, workCentreId: 'w-1', workCentreCode: 'OVEN', name: 'Mix and bake', setupMinutes: 30, runMinutesPerUnit: 6, instructions: 'Bake at 180 degrees for 40 minutes.' },
      { sequence: 20, workCentreId: 'w-2', workCentreCode: 'PACK', name: 'Box', setupMinutes: 0, runMinutesPerUnit: 1, instructions: null },
    ],
  };
}

export const exploded: ExplodedLine[] = [
  { level: 1, path: '1', stockCode: 'CAKE', description: 'Chocolate cake', quantityPerUnit: 2, scrapPercent: 0, issueMethod: 'Manual', operationSequence: null, isMade: true, billOfMaterialsId: 'b-1', unitCost: 16.23, cost: 32.45 },
  { level: 2, path: '1.1', stockCode: 'FLOUR', description: 'Flour 1 kg', quantityPerUnit: 1, scrapPercent: 0, issueMethod: 'Manual', operationSequence: 10, isMade: false, billOfMaterialsId: null, unitCost: 2, cost: 2 },
  { level: 2, path: '1.2', stockCode: 'SUGAR', description: 'Sugar 1 kg', quantityPerUnit: 0.44, scrapPercent: 10, issueMethod: 'Manual', operationSequence: null, isMade: false, billOfMaterialsId: null, unitCost: 5, cost: 2.2 },
  { level: 1, path: '2', stockCode: 'RIBBON', description: 'Ribbon', quantityPerUnit: 1, scrapPercent: 0, issueMethod: 'Manual', operationSequence: null, isMade: false, billOfMaterialsId: null, unitCost: 1, cost: 1 },
];

export const cost: CostRollUp = {
  billOfMaterialsId: 'b-1', productCode: 'CAKE', revision: 2, lotQuantity: 10, material: 26, labour: 90, machine: 45, overhead: 19.3, productSharePercent: 90, total: 180.3,
  unitCost: 16.227,
  lines: [
    { stockCode: 'FLOUR', description: 'Flour 1 kg', quantity: 5, unitCost: 2, cost: 10, isMade: false },
    { stockCode: 'SUGAR', description: 'Sugar 1 kg', quantity: 2.2, unitCost: 5, cost: 11, isMade: false },
    { stockCode: 'BOX', description: 'Cake box', quantity: 10, unitCost: 0.5, cost: 5, isMade: false },
  ],
  operations: [{ sequence: 10, name: 'Mix and bake', workCentre: 'OVEN', minutes: 90, labour: 90, machine: 45, overhead: 18 }],
};

export const differences: BomDifference[] = [
  { part: 'Component', key: 'BOX', change: 'Added', before: null, after: '10 · scrap 0 % · Backflush' },
  { part: 'Component', key: 'SUGAR', change: 'Changed', before: '1.5 · scrap 0 % · Manual', after: '2 · scrap 10 % · Manual' },
];

const order = (id: string, number: number, status: ProductionOrderRow['status'], productCode: string, productName: string, quantity: number, received: number, priority = 5): ProductionOrderRow => ({
  id, storeCode: 'MAIN', number, status, productCode, productName, quantity, received, materialLocation: 'POS', outputLocation: 'POS', plannedStart: today, plannedEnd: today, priority,
  madeToOrder: id === 'o-2', bomRevision: 2,
});

export const orders: ProductionOrderRow[] = [
  order('o-1', 100000001, 'Released', 'CAKE', 'Chocolate cake', 20, 0, 1),
  order('o-2', 100000002, 'Planned', 'CAKE', 'Chocolate cake', 10, 0),
  order('o-3', 100000003, 'InProgress', 'GIFT', 'Gift pack', 5, 2),
  order('o-4', 100000004, 'Completed', 'CAKE', 'Chocolate cake', 10, 10),
];

export const board: ProductionBoardColumn[] = (['Planned', 'Released', 'InProgress', 'Completed', 'Closed', 'Cancelled'] as const).map((status) => ({
  status,
  total: orders.filter((o) => o.status === status).length,
  orders: orders.filter((o) => o.status === status),
}));

export function orderDetail(id: string, status?: ProductionOrderDetail['status']): ProductionOrderDetail {
  const row = orders.find((o) => o.id === id) ?? orders[0];
  const released = (status ?? row.status) !== 'Planned';
  return {
    id: row.id, version: 'v-1', storeId: 's-west', storeCode: 'WEST', number: row.number, status: status ?? row.status, productCode: row.productCode, productName: row.productName,
    quantity: row.quantity, billOfMaterialsId: 'b-1', bomRevision: 2, materialLocationId: 'l-west-pos', materialLocation: 'POS', outputLocationId: 'l-west-pos', outputLocation: 'POS',
    plannedStart: row.plannedStart, plannedEnd: row.plannedEnd, priority: row.priority, customerOrderId: null, customerOrder: null, notes: 'For the weekend market', standardUnitCost: 15.48,
    createdAt: `${today}T08:00:00`, createdBy: 'sara', releasedAt: released ? `${today}T08:30:00` : null, releasedBy: released ? 'sara' : null, startedAt: null, completedAt: null,
    completedBy: null, closedAt: null, closedBy: null, received: row.received, outputScrapped: 0, reportedScrap: 0,
    lines: released
      ? [
          { lineId: 'pl-1', lineNumber: 1, kind: 'Component', stockCode: 'FLOUR', description: 'Flour 1 kg', planned: 10, done: 4, reserved: 10, issueMethod: 'Manual', operationSequence: 10, standardUnitCost: 2 },
          { lineId: 'pl-2', lineNumber: 2, kind: 'Component', stockCode: 'SUGAR', description: 'Sugar 1 kg', planned: 4.4, done: 0, reserved: 3, issueMethod: 'Manual', operationSequence: null, standardUnitCost: 5 },
          { lineId: 'pl-3', lineNumber: 3, kind: 'Component', stockCode: 'BOX', description: 'Cake box', planned: 20, done: 0, reserved: 20, issueMethod: 'Backflush', operationSequence: 20, standardUnitCost: 0.5 },
          { lineId: 'pl-4', lineNumber: 1, kind: 'ByProduct', stockCode: 'CRUMB', description: 'Crumb bag', planned: 2, done: 0, reserved: 0, issueMethod: 'Manual', operationSequence: null, standardUnitCost: 0 },
        ]
      : [],
    operations: released
      ? [
          { sequence: 10, name: 'Mix and bake', workCentre: 'Deck oven', status: 'Waiting', setupMinutes: 30, runMinutesPerUnit: 6, standardMinutes: 150, minutes: 0, good: 0, scrap: 0, startedAt: null, finishedAt: null, instructions: 'Bake at 180 degrees for 40 minutes.' },
          { sequence: 20, name: 'Box', workCentre: 'Packing bench', status: 'Waiting', setupMinutes: 0, runMinutesPerUnit: 1, standardMinutes: 20, minutes: 0, good: 0, scrap: 0, startedAt: null, finishedAt: null, instructions: null },
        ]
      : [],
    movements: released ? [{ at: `${today}T09:00:00`, kind: 'ProductionIssue', stockCode: 'FLOUR', quantity: -4, unitCost: 2, value: -8, reason: 'Issued to production', user: 'baker' }] : [],
    reports: [],
    costs: { material: 8, outputScrap: 0, labour: 0, machine: 0, hourlyOverhead: 0, materialOverhead: 0.4, received: 0, conversion: 0.4, charged: 8.4, workInProgress: 8.4 },
    variances: {
      outputQuantity: 0, standardMaterial: 0, actualMaterial: 8, standardLabour: 0, actualLabour: 0, standardMachine: 0, actualMachine: 0, standardOverhead: 0, actualOverhead: 0.4,
      materials: [{ stockCode: 'FLOUR', description: 'Flour 1 kg', standardQuantity: 0, actualQuantity: 4, standardUnitCost: 2, standardCost: 0, actualCost: 8, usageVariance: 8, priceVariance: 0 }],
      materialUsageVariance: 8, materialPriceVariance: 0, labourVariance: 0, machineVariance: 0, overheadVariance: 0.4, standardTotal: 0, actualTotal: 8.4, totalVariance: 8.4,
    },
    postings: [{ step: 'issue', entryId: 'j-1', number: 10000000041, date: today, amount: 8, memo: `Production order ${row.number} (CAKE): material issued` }],
  };
}

export const mrp: MrpView = {
  run: { id: 'run-1', runAtUtc: new Date().toISOString().replace(/\.\d{3}Z$/, 'Z'), runBy: 'sara', locationCode: 'POS', horizonDays: 60, defaultLeadDays: 7, suggestions: 3 },
  suggestions: [
    { id: 'sg-1', kind: 'Make', status: 'Open', stockCode: 'CAKE', description: 'Chocolate cake', storeCode: 'MAIN', locationId: 'l-main-pos', locationCode: 'POS', quantity: 4, needBy: today, startBy: today, leadDays: 1, supplierName: null, level: 0, reason: 'Customer order 10000077 (Akua Owusu)', firmedDocument: null, late: false, fromRelease: false },
    { id: 'sg-2', kind: 'Buy', status: 'Open', stockCode: 'SUGAR', description: 'Sugar 1 kg', storeCode: 'MAIN', locationId: 'l-main-pos', locationCode: 'POS', quantity: 12, needBy: today, startBy: '2026-01-02', leadDays: 5, supplierName: 'Sweet Supplies', level: 1, reason: 'To make 4 x CAKE', firmedDocument: null, late: true, fromRelease: false },
    { id: 'sg-3', kind: 'Buy', status: 'Open', stockCode: 'BOX', description: 'Cake box', storeCode: 'MAIN', locationId: 'l-main-pos', locationCode: 'POS', quantity: 40, needBy: today, startBy: today, leadDays: 7, supplierName: null, level: 1, reason: 'Short for production order 100000001 (CAKE)', firmedDocument: null, late: false, fromRelease: true },
  ],
};

export const leadTimes: LeadTime[] = [
  { supplierId: 'sup-1', supplier: 'Cotton Co. Ltd', days: 5 },
  { supplierId: 'sup-2', supplier: 'Sweet Supplies', days: null },
];

export const capacity: CapacityRow[] = [
  { workCentreId: 'w-1', storeCode: 'MAIN', code: 'OVEN', name: 'Deck oven', weekStart: monday, capacityHours: 48, loadHours: 52, loadPercent: 108.3 },
  { workCentreId: 'w-1', storeCode: 'MAIN', code: 'OVEN', name: 'Deck oven', weekStart: nextMonday, capacityHours: 48, loadHours: 12, loadPercent: 25 },
  { workCentreId: 'w-2', storeCode: 'MAIN', code: 'PACK', name: 'Packing bench', weekStart: monday, capacityHours: 48, loadHours: 42, loadPercent: 87.5 },
  { workCentreId: 'w-2', storeCode: 'MAIN', code: 'PACK', name: 'Packing bench', weekStart: nextMonday, capacityHours: 48, loadHours: 0, loadPercent: 0 },
];

export const qualityPlans: QualityPlan[] = [
  {
    id: 'qp-1', version: 'v-1', productCode: 'CAKE', name: 'Cake checks', isActive: true, forProduction: true, forIncoming: false, notes: null,
    checks: [
      { lineNumber: 1, name: 'Weight', kind: 'Number', minimum: 900, maximum: 1100, unit: 'g', required: true },
      { lineNumber: 2, name: 'Looks right', kind: 'PassFail', minimum: null, maximum: null, unit: null, required: true },
    ],
  },
  {
    id: 'qp-2', version: 'v-1', productCode: 'SUGAR', name: 'Sugar on arrival', isActive: true, forProduction: false, forIncoming: true, notes: null,
    checks: [{ lineNumber: 1, name: 'Bags sealed', kind: 'PassFail', minimum: null, maximum: null, unit: null, required: true }],
  },
];

export const inspections: Inspection[] = [
  {
    id: 'in-1', kind: 'Production', storeCode: 'MAIN', documentNumber: 100000004, productionOrderId: 'o-4', purchaseOrderId: null, stockCode: 'CAKE', description: 'Chocolate cake',
    quantityInspected: 10, quantityFailed: 1, disposition: 'Hold', holdLocation: 'HOLD', passed: false, inspectedAt: `${today}T11:00:00`, inspectedBy: 'sara', notes: 'One cracked',
    results: [
      { lineNumber: 1, checkName: 'Weight', kind: 'Number', minimum: 900, maximum: 1100, required: true, passFail: null, numberValue: 1010, textValue: null, passed: true },
      { lineNumber: 2, checkName: 'Looks right', kind: 'PassFail', minimum: null, maximum: null, required: true, passFail: true, numberValue: null, textValue: null, passed: true },
    ],
  },
];

export const receivedOrders: ReceivedOrder[] = [
  { id: 'po-1', number: 10000031, storeCode: 'MAIN', locationCode: 'POS', receivedAt: `${today}T10:00:00`, lines: [{ plu: 'SUGAR', description: 'Sugar 1 kg', received: 25, hasPlan: true, inspections: 0 }] },
];

export const productionReports: ReportInfo[] = [
  { key: 'production-status', group: 'Production', title: 'Production orders by status', usesDates: false },
  { key: 'production-costs', group: 'Production', title: 'Production cost and variances', usesDates: true },
  { key: 'production-scrap', group: 'Production', title: 'Scrap', usesDates: true },
];

export const statusReport: ProductionReportResult = {
  title: 'Production orders by status', subtitle: null, from: today, to: today,
  columns: [
    { header: 'Status', kind: 'Text', total: false },
    { header: 'Orders', kind: 'Count', total: true },
    { header: 'Quantity', kind: 'Quantity', total: true },
    { header: 'Made', kind: 'Quantity', total: true },
    { header: 'Oldest planned start', kind: 'Date', total: false },
  ],
  rows: [
    ['planned', 1, 10, 0, today],
    ['released', 1, 20, 0, today],
    ['in progress', 1, 5, 2, today],
    ['completed', 1, 10, 10, today],
  ],
  totals: ['Total', 4, 45, 12, null],
};
