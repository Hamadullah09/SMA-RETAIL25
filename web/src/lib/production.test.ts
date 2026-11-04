import { describe, expect, it, vi } from 'vitest';
import { createApi, CSRF_HEADER, CSRF_VALUE } from './api';
import {
  answerPasses,
  boardColumns,
  capacityGrid,
  checkBom,
  checkRouting,
  loadTone,
  movementLabel,
  newBom,
  nextSequence,
  rangeText,
  treeRows,
  visibleTreeRows,
} from './production';
import { createProductionApi } from './production-api';
import type { CapacityRow, ExplodedLine, QualityCheck, RoutingOperation } from './production-types';

const op = (sequence: number, extra: Partial<RoutingOperation> = {}): RoutingOperation => ({
  sequence,
  workCentreId: 'w-1',
  workCentreCode: 'OVEN',
  name: 'Bake',
  setupMinutes: 0,
  runMinutesPerUnit: 6,
  instructions: null,
  ...extra,
});

describe('the bill editor checks', () => {
  it('accepts a good bill', () => {
    const bom = { ...newBom('CAKE'), yieldQuantity: 10, lines: [{ componentCode: 'FLOUR', description: null, quantity: 5, scrapPercent: 0, issueMethod: 'Manual' as const, operationSequence: 10 }], operations: [op(10)] };
    expect(checkBom(bom)).toEqual({});
  });

  it('names what is wrong', () => {
    expect(checkBom(newBom('')).productCode).toBe('Enter the stock code of the product.');
    expect(checkBom(newBom('CAKE')).lines).toBe('Add at least one component.');
    expect(checkBom({ ...newBom('CAKE'), yieldQuantity: 0 }).yieldQuantity).toBe('The yield must be above zero.');
    const line = { componentCode: 'cake', description: null, quantity: 1, scrapPercent: 0, issueMethod: 'Manual' as const, operationSequence: null };
    expect(checkBom({ ...newBom('CAKE'), lines: [line] }).lines).toBe('CAKE cannot be a component of itself.');
    expect(checkBom({ ...newBom('CAKE'), lines: [{ ...line, componentCode: 'EGG', operationSequence: 20 }] }).lines).toContain('operation 20');
    expect(checkBom({ ...newBom('CAKE'), lines: [{ ...line, componentCode: 'EGG', scrapPercent: 100 }] }).lines).toContain('scrap');
    const byProducts = [{ stockCode: 'CRUMB', description: null, quantity: 1, costSharePercent: 60 }, { stockCode: 'BRAN', description: null, quantity: 1, costSharePercent: 40 }];
    expect(checkBom({ ...newBom('CAKE'), lines: [{ ...line, componentCode: 'EGG' }], byProducts }).byProducts).toContain('below 100 %');
    expect(checkBom({ ...newBom('CAKE'), effectiveFrom: '2026-12-01', effectiveTo: '2026-11-01' }).dates).toBeDefined();
  });

  it('checks the routing and numbers the next operation', () => {
    expect(checkRouting([op(10), op(20)])).toBeUndefined();
    expect(checkRouting([op(10), op(10)])).toBe('Two operations have the number 10.');
    expect(checkRouting([op(0)])).toBe('Operation numbers start at 1.');
    expect(checkRouting([op(10, { workCentreId: '' })])).toBe('Operation 10 needs a work centre.');
    expect(checkRouting([op(10, { runMinutesPerUnit: -1 })])).toContain('below zero');
    expect(nextSequence([])).toBe(10);
    expect(nextSequence([op(10), op(30)])).toBe(40);
  });
});

describe('the exploded tree', () => {
  const line = (path: string, level: number): ExplodedLine => ({
    level, path, stockCode: 'S' + path, description: '', quantityPerUnit: 1, scrapPercent: 0, issueMethod: 'Manual', operationSequence: null, isMade: false,
    billOfMaterialsId: null, unitCost: 1, cost: 1,
  });

  it('knows each line\'s parent and whether lines are under it, and folds them away', () => {
    const rows = treeRows([line('1', 1), line('1.1', 2), line('1.1.1', 3), line('2', 1)]);
    expect(rows.map((r) => [r.path, r.parent, r.hasChildren])).toEqual([
      ['1', null, true],
      ['1.1', '1', true],
      ['1.1.1', '1.1', false],
      ['2', null, false],
    ]);
    expect(visibleTreeRows(rows, new Set(['1'])).map((r) => r.path)).toEqual(['1', '2']);
    expect(visibleTreeRows(rows, new Set(['1.1'])).map((r) => r.path)).toEqual(['1', '1.1', '2']);
  });
});

describe('the board, capacity and quality helpers', () => {
  it('gives every status a column in order', () => {
    const columns = boardColumns([{ status: 'Completed', total: 1, orders: [] }]);
    expect(columns.map((c) => c.status)).toEqual(['Planned', 'Released', 'InProgress', 'Completed', 'Closed', 'Cancelled']);
    expect(columns[3].total).toBe(1);
  });

  it('names movements the way the floor thinks of them', () => {
    expect(movementLabel('ProductionIssue', -4)).toBe('Issued');
    expect(movementLabel('ProductionIssue', 1)).toBe('Given back');
    expect(movementLabel('ProductionReceipt', -2)).toBe('Moved to hold');
    expect(movementLabel('ProductionScrap', -1)).toBe('Thrown away');
  });

  it('turns capacity rows into work centres by week, with a tone for the load', () => {
    const row = (centre: string, week: string, load: number): CapacityRow => ({
      workCentreId: centre, storeCode: 'MAIN', code: centre.toUpperCase(), name: 'Centre', weekStart: week, capacityHours: 40, loadHours: load, loadPercent: (load / 40) * 100,
    });
    const grid = capacityGrid([row('b', '2026-11-09', 10), row('a', '2026-11-02', 50), row('a', '2026-11-09', 0)]);
    expect(grid.weeks).toEqual(['2026-11-02', '2026-11-09']);
    expect(grid.centres.map((c) => c.id)).toEqual(['a', 'b']);
    expect(grid.centres[0].cells['2026-11-02'].loadPercent).toBe(125);
    expect([loadTone(50), loadTone(90), loadTone(100), loadTone(140)]).toEqual(['ok', 'warn', 'danger', 'danger']);
  });

  it('judges answers like the server', () => {
    const number: QualityCheck = { lineNumber: 1, name: 'Weight', kind: 'Number', minimum: 90, maximum: 110, unit: 'g', required: true };
    expect(answerPasses(number, { lineNumber: 1, number: 100 })).toBe(true);
    expect(answerPasses(number, { lineNumber: 1, number: 120 })).toBe(false);
    expect(answerPasses(number, undefined)).toBe(false);
    expect(answerPasses({ ...number, required: false }, undefined)).toBe(true);
    const looks: QualityCheck = { ...number, kind: 'PassFail', minimum: null, maximum: null };
    expect(answerPasses(looks, { lineNumber: 1, passFail: false })).toBe(false);
    expect(answerPasses({ ...looks, kind: 'Text' }, { lineNumber: 1, text: 'ok' })).toBe(true);
    expect(rangeText(number)).toBe('90 to 110 g');
    expect(rangeText({ minimum: 5, maximum: null, unit: null })).toBe('at least 5');
  });
});

describe('the manufacturing client', () => {
  it('posts actions with the CSRF header to the order\'s address', async () => {
    const calls: Array<{ url: string; init: RequestInit }> = [];
    const fetch = vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
      calls.push({ url: String(url), init: init ?? {} });
      return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } });
    }) as unknown as typeof globalThis.fetch;
    const production = createProductionApi(createApi({ base: '', fetch }));
    await production.receive('o 1', 5, true, [{ stockCode: 'CRUMB', quantity: 2 }]);
    expect(calls[0].url).toBe('/api/production-orders/o%201/receive');
    expect(calls[0].init.method).toBe('POST');
    expect((calls[0].init.headers as Record<string, string>)[CSRF_HEADER]).toBe(CSRF_VALUE);
    expect(JSON.parse(String(calls[0].init.body))).toEqual({ quantity: 5, complete: true, byProducts: [{ stockCode: 'CRUMB', quantity: 2 }] });
    await production.startOperation('o1', 10);
    expect(calls[1].url).toBe('/api/production-orders/o1/operations/10/start');
    await production.capacity({ storeId: 's1', weeks: 8, suggestions: true });
    expect(calls[2].url).toBe('/api/capacity?storeId=s1&weeks=8&suggestions=true');
    expect(calls[2].init.method).toBe('GET');
  });
});
