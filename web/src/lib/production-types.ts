/**
 * The manufacturing shapes of docs/API.md ("Manufacturing (phase E5)"): work centres, bills of materials and routings,
 * production orders, planning, capacity, quality and the production reports. Kept apart from `types.ts` so the ERP
 * phases do not edit one list at the same time; the conventions are the same (camelCase, enums as strings).
 */
import type { Paged, ReportCell, ReportColumn } from './types';

export type IssueMethod = 'Manual' | 'Backflush';
export type ProductionOrderStatus = 'Planned' | 'Released' | 'InProgress' | 'Completed' | 'Closed' | 'Cancelled';
export type OperationStatus = 'Waiting' | 'Started' | 'Done';
export type ProductionLineKind = 'Component' | 'ByProduct';
export type SuggestionKind = 'Make' | 'Buy';
export type SuggestionStatus = 'Open' | 'Firmed' | 'Dismissed' | 'Superseded';
export type QualityCheckKind = 'PassFail' | 'Number' | 'Text';
export type InspectionKind = 'Production' | 'Incoming';
export type InspectionDisposition = 'None' | 'Hold' | 'Scrap';
export type StockMovementKindName = string;

// Work centres, bills and routings ------------------------------------------------------------------------------------

export interface WorkCentre {
  id: string;
  version: string;
  storeId: string;
  storeCode: string;
  locationId: string;
  locationCode: string;
  code: string;
  name: string;
  labourCostPerHour: number;
  machineCostPerHour: number;
  overheadPerHour: number;
  capacityHoursPerDay: number;
  daysPerWeek: number;
  isActive: boolean;
  notes: string | null;
}

export interface WorkCentreRequest {
  version: string | null;
  locationId: string;
  code: string;
  name: string;
  labourCostPerHour: number;
  machineCostPerHour: number;
  overheadPerHour: number;
  capacityHoursPerDay: number;
  daysPerWeek: number;
  isActive: boolean;
  notes: string | null;
}

export interface BomListRow {
  id: string;
  version: string;
  productCode: string;
  productName: string;
  revision: number;
  isActive: boolean;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  yieldQuantity: number;
  lines: number;
  byProducts: number;
  operations: number;
  inForce: boolean;
}

export interface BomLine {
  componentCode: string;
  description: string | null;
  quantity: number;
  scrapPercent: number;
  issueMethod: IssueMethod;
  operationSequence: number | null;
}

export interface BomByProduct {
  stockCode: string;
  description: string | null;
  quantity: number;
  costSharePercent: number;
}

export interface RoutingOperation {
  sequence: number;
  workCentreId: string;
  workCentreCode: string | null;
  name: string;
  setupMinutes: number;
  runMinutesPerUnit: number;
  instructions: string | null;
}

export interface BomDetail {
  id: string | null;
  version: string | null;
  productCode: string;
  productName: string | null;
  revision: number;
  isActive: boolean;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  yieldQuantity: number;
  overheadPercent: number;
  notes: string | null;
  lines: BomLine[];
  byProducts: BomByProduct[];
  operations: RoutingOperation[];
}

export interface BomDifference {
  part: string;
  key: string;
  change: 'Added' | 'Removed' | 'Changed' | (string & {});
  before: string | null;
  after: string | null;
}

export interface ExplodedLine {
  level: number;
  path: string;
  stockCode: string;
  description: string;
  quantityPerUnit: number;
  scrapPercent: number;
  issueMethod: IssueMethod;
  operationSequence: number | null;
  isMade: boolean;
  billOfMaterialsId: string | null;
  unitCost: number;
  cost: number;
}

export interface CostLine {
  stockCode: string;
  description: string;
  quantity: number;
  unitCost: number;
  cost: number;
  isMade: boolean;
}

export interface CostOperation {
  sequence: number;
  name: string;
  workCentre: string;
  minutes: number;
  labour: number;
  machine: number;
  overhead: number;
}

export interface CostRollUp {
  billOfMaterialsId: string;
  productCode: string;
  revision: number;
  lotQuantity: number;
  material: number;
  labour: number;
  machine: number;
  overhead: number;
  productSharePercent: number;
  total: number;
  unitCost: number;
  lines: CostLine[];
  operations: CostOperation[];
}

// Production orders ---------------------------------------------------------------------------------------------------

export interface ProductionOrderRow {
  id: string;
  storeCode: string;
  number: number;
  status: ProductionOrderStatus;
  productCode: string;
  productName: string;
  quantity: number;
  received: number;
  materialLocation: string;
  outputLocation: string;
  plannedStart: string | null;
  plannedEnd: string | null;
  priority: number;
  madeToOrder: boolean;
  bomRevision: number;
}

export type ProductionOrderPage = Paged<ProductionOrderRow>;

export interface ProductionBoardColumn {
  status: ProductionOrderStatus;
  total: number;
  orders: ProductionOrderRow[];
}

export interface ProductionOrderRequest {
  version: string | null;
  productCode: string;
  quantity: number;
  materialLocationId: string;
  outputLocationId: string;
  billOfMaterialsId: string | null;
  plannedStart: string | null;
  plannedEnd: string | null;
  priority: number;
  customerOrderId: string | null;
  notes: string | null;
}

export interface LineProgress {
  lineId: string;
  lineNumber: number;
  kind: ProductionLineKind;
  stockCode: string;
  description: string;
  planned: number;
  done: number;
  reserved: number;
  issueMethod: IssueMethod;
  operationSequence: number | null;
  standardUnitCost: number;
}

export interface ProductionOperation {
  sequence: number;
  name: string;
  workCentre: string;
  status: OperationStatus;
  setupMinutes: number;
  runMinutesPerUnit: number;
  standardMinutes: number;
  minutes: number;
  good: number;
  scrap: number;
  startedAt: string | null;
  finishedAt: string | null;
  instructions: string | null;
}

export interface ProductionMovement {
  at: string;
  kind: StockMovementKindName;
  stockCode: string;
  quantity: number;
  unitCost: number;
  value: number;
  reason: string | null;
  user: string | null;
}

export interface ProductionReport {
  at: string;
  operationSequence: number;
  minutes: number;
  good: number;
  scrap: number;
  userName: string | null;
  cost: number;
  note: string | null;
}

export interface OrderCosts {
  material: number;
  outputScrap: number;
  labour: number;
  machine: number;
  hourlyOverhead: number;
  materialOverhead: number;
  received: number;
  conversion: number;
  charged: number;
  workInProgress: number;
}

export interface MaterialUsage {
  stockCode: string;
  description: string;
  standardQuantity: number;
  actualQuantity: number;
  standardUnitCost: number;
  standardCost: number;
  actualCost: number;
  usageVariance: number;
  priceVariance: number;
}

export interface OrderVariances {
  outputQuantity: number;
  standardMaterial: number;
  actualMaterial: number;
  standardLabour: number;
  actualLabour: number;
  standardMachine: number;
  actualMachine: number;
  standardOverhead: number;
  actualOverhead: number;
  materials: MaterialUsage[];
  materialUsageVariance: number;
  materialPriceVariance: number;
  labourVariance: number;
  machineVariance: number;
  overheadVariance: number;
  standardTotal: number;
  actualTotal: number;
  totalVariance: number;
}

export interface ProductionPosting {
  step: string;
  entryId: string;
  number: number;
  date: string;
  amount: number;
  memo: string;
}

export interface ProductionOrderDetail {
  id: string;
  version: string;
  storeId: string;
  storeCode: string;
  number: number;
  status: ProductionOrderStatus;
  productCode: string;
  productName: string;
  quantity: number;
  billOfMaterialsId: string | null;
  bomRevision: number;
  materialLocationId: string;
  materialLocation: string;
  outputLocationId: string;
  outputLocation: string;
  plannedStart: string | null;
  plannedEnd: string | null;
  priority: number;
  customerOrderId: string | null;
  customerOrder: string | null;
  notes: string | null;
  standardUnitCost: number;
  createdAt: string;
  createdBy: string | null;
  releasedAt: string | null;
  releasedBy: string | null;
  startedAt: string | null;
  completedAt: string | null;
  completedBy: string | null;
  closedAt: string | null;
  closedBy: string | null;
  received: number;
  outputScrapped: number;
  reportedScrap: number;
  lines: LineProgress[];
  operations: ProductionOperation[];
  movements: ProductionMovement[];
  reports: ProductionReport[];
  costs: OrderCosts | null;
  variances: OrderVariances | null;
  postings: ProductionPosting[] | null;
}

export interface Shortage {
  stockCode: string;
  description: string;
  required: number;
  available: number;
  missing: number;
  isMade: boolean;
}

export interface ReleaseResponse {
  order: ProductionOrderDetail;
  shortages: Shortage[];
}

export interface ByProductQuantity {
  stockCode: string;
  quantity: number;
}

export interface PostCostsResponse {
  orders: number;
  problems: string[];
}

export interface CustomerOrderPick {
  id: string;
  invoiceNumber: number;
  name: string | null;
  plu: string | null;
  description: string;
  quantity: number;
  orderDate: string | null;
}

// Planning and capacity ------------------------------------------------------------------------------------------------

export interface MrpRunInfo {
  id: string;
  runAtUtc: string;
  runBy: string | null;
  locationCode: string;
  horizonDays: number;
  defaultLeadDays: number;
  suggestions: number;
}

export interface Suggestion {
  id: string;
  kind: SuggestionKind;
  status: SuggestionStatus;
  stockCode: string;
  description: string;
  storeCode: string;
  locationId: string;
  locationCode: string;
  quantity: number;
  needBy: string;
  startBy: string;
  leadDays: number;
  supplierName: string | null;
  level: number;
  reason: string | null;
  firmedDocument: string | null;
  late: boolean;
  fromRelease: boolean;
}

export interface MrpView {
  run: MrpRunInfo | null;
  suggestions: Suggestion[];
}

export interface MrpRunRequest {
  storeId: string | null;
  locationId: string | null;
  horizonDays: number;
  defaultLeadDays: number;
}

export interface FirmResult {
  made: string[];
  problems: string[];
}

export interface LeadTime {
  supplierId: string;
  supplier: string;
  days: number | null;
}

export interface CapacityRow {
  workCentreId: string;
  storeCode: string;
  code: string;
  name: string;
  weekStart: string;
  capacityHours: number;
  loadHours: number;
  loadPercent: number;
}

// Quality ------------------------------------------------------------------------------------------------------------

export interface QualityCheck {
  lineNumber: number;
  name: string;
  kind: QualityCheckKind;
  minimum: number | null;
  maximum: number | null;
  unit: string | null;
  required: boolean;
}

export interface QualityPlan {
  id: string | null;
  version: string | null;
  productCode: string;
  name: string;
  isActive: boolean;
  forProduction: boolean;
  forIncoming: boolean;
  notes: string | null;
  checks: QualityCheck[];
}

export interface CheckAnswer {
  lineNumber: number;
  passFail?: boolean | null;
  number?: number | null;
  text?: string | null;
}

export interface QualityResult {
  lineNumber: number;
  checkName: string;
  kind: QualityCheckKind;
  minimum: number | null;
  maximum: number | null;
  required: boolean;
  passFail: boolean | null;
  numberValue: number | null;
  textValue: string | null;
  passed: boolean;
}

export interface Inspection {
  id: string;
  kind: InspectionKind;
  storeCode: string;
  documentNumber: number | null;
  productionOrderId: string | null;
  purchaseOrderId: string | null;
  stockCode: string;
  description: string;
  quantityInspected: number;
  quantityFailed: number;
  disposition: InspectionDisposition;
  holdLocation: string | null;
  passed: boolean;
  inspectedAt: string;
  inspectedBy: string | null;
  notes: string | null;
  results: QualityResult[];
}

export interface OutputInspectionRequest {
  quantityInspected: number;
  quantityFailed: number;
  disposition: InspectionDisposition;
  holdLocationId: string | null;
  answers: CheckAnswer[];
  notes: string | null;
}

export interface IncomingInspectionRequest {
  stockCode: string;
  quantityInspected: number;
  quantityFailed: number;
  answers: CheckAnswer[];
  notes: string | null;
}

export interface ReceivedLine {
  plu: string;
  description: string;
  received: number;
  hasPlan: boolean;
  inspections: number;
}

export interface ReceivedOrder {
  id: string;
  number: number;
  storeCode: string;
  locationCode: string;
  receivedAt: string | null;
  lines: ReceivedLine[];
}

// Reports ------------------------------------------------------------------------------------------------------------

export interface ProductionReportResult {
  title: string;
  subtitle: string | null;
  columns: ReportColumn[];
  rows: ReportCell[][];
  totals: ReportCell[] | null;
  from: string;
  to: string;
}
