/**
 * What the manufacturing pages work out for themselves: labels and chip tones, the checks of the bill editor (the server
 * checks again), the exploded tree's rows, the capacity grid, and the pass/fail preview of an inspection.
 */
import type { ChipTone } from '@/components/ui/page';
import type {
  BomDetail,
  CapacityRow,
  CheckAnswer,
  ExplodedLine,
  ProductionBoardColumn,
  ProductionOrderStatus,
  QualityCheck,
  RoutingOperation,
} from './production-types';

export const ORDER_STATUS: Record<ProductionOrderStatus, { label: string; tone: ChipTone }> = {
  Planned: { label: 'Planned', tone: 'plain' },
  Released: { label: 'Released', tone: 'warn' },
  InProgress: { label: 'In progress', tone: 'dark' },
  Completed: { label: 'Completed', tone: 'ok' },
  Closed: { label: 'Closed', tone: 'plain' },
  Cancelled: { label: 'Cancelled', tone: 'danger' },
};

/** The board's columns, left to right. */
export const BOARD_ORDER: readonly ProductionOrderStatus[] = ['Planned', 'Released', 'InProgress', 'Completed', 'Closed', 'Cancelled'];

/** The board's columns in their order; statuses the server did not send are empty columns. */
export function boardColumns(columns: readonly ProductionBoardColumn[] | undefined): ProductionBoardColumn[] {
  return BOARD_ORDER.map((status) => columns?.find((c) => c.status === status) ?? { status, total: 0, orders: [] });
}

export const MOVEMENT_LABELS: Record<string, string> = {
  ProductionIssue: 'Issued',
  ProductionReceipt: 'Received',
  ProductionScrap: 'Thrown away',
};

/** "Issued" for a positive issue is a negative movement; a positive ProductionIssue is material given back. */
export function movementLabel(kind: string, quantity: number): string {
  if (kind === 'ProductionIssue' && quantity > 0) return 'Given back';
  if (kind === 'ProductionReceipt' && quantity < 0) return 'Moved to hold';
  return MOVEMENT_LABELS[kind] ?? kind;
}

export type BomErrors = Partial<Record<'productCode' | 'yieldQuantity' | 'overheadPercent' | 'lines' | 'byProducts' | 'operations' | 'dates', string>>;

/** The checks of the bill editor, the same as the server's, so mistakes show before saving. */
export function checkBom(bom: BomDetail): BomErrors {
  const errors: BomErrors = {};
  const code = bom.productCode.trim().toUpperCase();
  if (!code) errors.productCode = 'Enter the stock code of the product.';
  else if (code.length > 16) errors.productCode = 'A stock code has at most 16 characters.';
  if (!(bom.yieldQuantity > 0)) errors.yieldQuantity = 'The yield must be above zero.';
  if (bom.overheadPercent < 0 || bom.overheadPercent > 1000) errors.overheadPercent = 'Overhead is 0 to 1000 % of material.';
  if (bom.effectiveFrom && bom.effectiveTo && bom.effectiveTo < bom.effectiveFrom) errors.dates = 'It stops being in force before it starts.';
  const sequences = new Set(bom.operations.map((o) => o.sequence));
  if (bom.lines.length === 0) errors.lines = 'Add at least one component.';
  else
    for (const line of bom.lines) {
      const c = line.componentCode.trim().toUpperCase();
      if (!c) errors.lines = 'Every component needs a stock code.';
      else if (c === code) errors.lines = `${code} cannot be a component of itself.`;
      else if (!(line.quantity > 0)) errors.lines = `${c}: the quantity must be above zero.`;
      else if (line.scrapPercent < 0 || line.scrapPercent >= 100) errors.lines = `${c}: scrap is 0 to 99.9 %.`;
      else if (line.operationSequence !== null && !sequences.has(line.operationSequence)) errors.lines = `${c} is used at operation ${line.operationSequence}, which is not in the routing.`;
      if (errors.lines) break;
    }
  for (const b of bom.byProducts) {
    if (!b.stockCode.trim()) errors.byProducts = 'Every by-product needs a stock code.';
    else if (!(b.quantity > 0)) errors.byProducts = `${b.stockCode.trim().toUpperCase()}: the quantity must be above zero.`;
    if (errors.byProducts) break;
  }
  if (!errors.byProducts && bom.byProducts.reduce((s, b) => s + b.costSharePercent, 0) >= 100)
    errors.byProducts = 'The by-products must leave some of the cost to the product (below 100 % together).';
  const ops = checkRouting(bom.operations);
  if (ops) errors.operations = ops;
  return errors;
}

/** The routing's checks: sequences above zero and unique, a name and a work centre each, no negative times. */
export function checkRouting(operations: readonly RoutingOperation[]): string | undefined {
  const seen = new Set<number>();
  for (const o of operations) {
    if (!(o.sequence > 0)) return 'Operation numbers start at 1.';
    if (seen.has(o.sequence)) return `Two operations have the number ${o.sequence}.`;
    seen.add(o.sequence);
    if (!o.name.trim()) return `Operation ${o.sequence} needs a name.`;
    if (!o.workCentreId) return `Operation ${o.sequence} needs a work centre.`;
    if (o.setupMinutes < 0 || o.runMinutesPerUnit < 0) return `Operation ${o.sequence}: times cannot be below zero.`;
  }
  return undefined;
}

/** The next operation number: ten after the highest (10, 20, 30…). */
export function nextSequence(operations: readonly RoutingOperation[]): number {
  return operations.reduce((max, o) => Math.max(max, o.sequence), 0) + 10;
}

/** An empty bill for the editor. */
export function newBom(productCode = ''): BomDetail {
  return {
    id: null,
    version: null,
    productCode,
    productName: null,
    revision: 0,
    isActive: true,
    effectiveFrom: null,
    effectiveTo: null,
    yieldQuantity: 1,
    overheadPercent: 0,
    notes: null,
    lines: [],
    byProducts: [],
    operations: [],
  };
}

export interface TreeRow extends ExplodedLine {
  /** The path of the line it is under, or null at the top. */
  parent: string | null;
  /** True when a line under it follows. */
  hasChildren: boolean;
}

/** The exploded bill as tree rows: each with the line it is under. */
export function treeRows(lines: readonly ExplodedLine[]): TreeRow[] {
  const paths = new Set(lines.map((l) => l.path));
  return lines.map((line) => {
    const cut = line.path.lastIndexOf('.');
    const parent = cut < 0 ? null : line.path.slice(0, cut);
    const hasChildren = lines.some((other) => other.path.startsWith(line.path + '.'));
    return { ...line, parent: parent && paths.has(parent) ? parent : null, hasChildren };
  });
}

/** The rows a tree shows when some lines are folded: none under a folded line. */
export function visibleTreeRows(rows: readonly TreeRow[], folded: ReadonlySet<string>): TreeRow[] {
  return rows.filter((row) => ![...folded].some((path) => row.path.startsWith(path + '.')));
}

/** A load's chip tone: fine below 85 %, nearly full to 100 %, over beyond. */
export function loadTone(percent: number): ChipTone {
  if (percent >= 100) return 'danger';
  if (percent >= 85) return 'warn';
  return 'ok';
}

export interface CapacityGrid {
  weeks: string[];
  centres: Array<{ id: string; label: string; storeCode: string; cells: Record<string, CapacityRow> }>;
}

/** The capacity rows as work centres down and weeks across. */
export function capacityGrid(rows: readonly CapacityRow[]): CapacityGrid {
  const weeks = [...new Set(rows.map((r) => r.weekStart))].sort();
  const centres = new Map<string, CapacityGrid['centres'][number]>();
  for (const r of rows) {
    let centre = centres.get(r.workCentreId);
    if (!centre) {
      centre = { id: r.workCentreId, label: `${r.code} ${r.name}`, storeCode: r.storeCode, cells: {} };
      centres.set(r.workCentreId, centre);
    }
    centre.cells[r.weekStart] = r;
  }
  return { weeks, centres: [...centres.values()].sort((a, b) => a.storeCode.localeCompare(b.storeCode) || a.label.localeCompare(b.label)) };
}

/** Whether an answer passes its check, as the server will judge it (a check left out passes only when not required). */
export function answerPasses(check: QualityCheck, answer: CheckAnswer | undefined): boolean {
  switch (check.kind) {
    case 'PassFail':
      return answer?.passFail === true || (answer?.passFail == null && !check.required);
    case 'Number': {
      const v = answer?.number;
      if (v === null || v === undefined) return !check.required;
      return (check.minimum === null || v >= check.minimum) && (check.maximum === null || v <= check.maximum);
    }
    default:
      return (answer?.text ?? '').trim().length > 0 || !check.required;
  }
}

/** The answers to send: an untouched pass/fail check is a fail (the box was not ticked), the others as given. */
export function completeAnswers(checks: readonly QualityCheck[], answers: readonly CheckAnswer[]): CheckAnswer[] {
  return checks.map((c) => {
    const a = answers.find((x) => x.lineNumber === c.lineNumber) ?? { lineNumber: c.lineNumber };
    return c.kind === 'PassFail' ? { ...a, passFail: a.passFail ?? false } : a;
  });
}

/** The range a number check allows, for its label: "90 to 110 g", "at least 5", "at most 2 mm". */
export function rangeText(check: Pick<QualityCheck, 'minimum' | 'maximum' | 'unit'>): string {
  const unit = check.unit ? ` ${check.unit}` : '';
  if (check.minimum !== null && check.maximum !== null) return `${check.minimum} to ${check.maximum}${unit}`;
  if (check.minimum !== null) return `at least ${check.minimum}${unit}`;
  if (check.maximum !== null) return `at most ${check.maximum}${unit}`;
  return check.unit ?? '';
}
