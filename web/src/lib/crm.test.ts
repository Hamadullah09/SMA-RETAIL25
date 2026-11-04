import { describe, expect, it } from 'vitest';
import { checkWhatsApp, groupTasks, joinLocal, moveCard, normalizeWhatsApp, openTotals, relatedTo, splitLocal, stageTotals } from './crm';
import type { ActivityRow, OpportunityBoard, OpportunityRow } from './types';

function opp(id: string, stageId: string, value: number, probability: number): OpportunityRow {
  return {
    id, version: 'v-1', title: `Deal ${id}`, clientId: 'cl-1', clientName: 'Akua Owusu', leadId: null, leadName: null, stageId, stage: stageId,
    outcome: 'Open', expectedValue: value, probability, weightedValue: (value * probability) / 100, expectedCloseOn: null, ownerId: null, ownerName: null,
    storeId: 's-main', storeCode: 'MAIN', lostReason: null, closedOn: null, notes: null, createdAtUtc: '2026-11-01T10:00:00Z',
  };
}

function board(): OpportunityBoard {
  const stage = (id: string, name: string, outcome: 'Open' | 'Won' | 'Lost', probability: number, opportunities: OpportunityRow[]) => ({
    id, name, outcome, probability, opportunities, ...stageTotals({ opportunities }),
  });
  return {
    stages: [
      stage('new', 'New', 'Open', 10, [opp('a', 'new', 1000, 10), opp('b', 'new', 500, 10)]),
      stage('proposal', 'Proposal', 'Open', 60, [opp('c', 'proposal', 2000, 60)]),
      stage('won', 'Won', 'Won', 100, []),
      stage('lost', 'Lost', 'Lost', 0, []),
    ],
  };
}

function task(id: string, due: ActivityRow['due'], dueAt: string | null, createdAtUtc = '2026-11-01T10:00:00Z', isDone = false): ActivityRow {
  return {
    id, version: 'v-1', kind: 'Task', subject: id, notes: null, clientId: null, clientName: null, leadId: null, leadName: null, opportunityId: null,
    opportunityTitle: null, dueAt, isDone, doneAtUtc: null, ownerId: 'u-1', ownerName: 'sara', storeId: 's-main', storeCode: 'MAIN', due, createdAtUtc,
  };
}

describe('the pipeline board', () => {
  it('adds up a stage and the open stages', () => {
    const b = board();
    expect(b.stages[0]).toMatchObject({ count: 2, value: 1500, weighted: 150 });
    expect(openTotals(b)).toEqual({ count: 3, value: 3500, weighted: 1350 });
  });

  it('moves a card with the stage probability and new totals in both columns', () => {
    const moved = moveCard(board(), 'a', 'proposal');
    expect(moved.stages[0]).toMatchObject({ count: 1, value: 500, weighted: 50 });
    expect(moved.stages[1]).toMatchObject({ count: 2, value: 3000, weighted: 1800 });
    expect(moved.stages[1].opportunities[0]).toMatchObject({ id: 'a', stageId: 'proposal', stage: 'Proposal', probability: 60, weightedValue: 600 });
  });

  it('keeps the reason of a lost card and drops it when it opens again', () => {
    const lost = moveCard(board(), 'c', 'lost', 'Too expensive');
    expect(lost.stages[3].opportunities[0]).toMatchObject({ outcome: 'Lost', lostReason: 'Too expensive', weightedValue: 0 });
    expect(openTotals(lost)).toEqual({ count: 2, value: 1500, weighted: 150 });
    const back = moveCard(lost, 'c', 'new');
    expect(back.stages[0].opportunities[0]).toMatchObject({ id: 'c', outcome: 'Open', lostReason: null });
  });

  it('leaves the board alone for the same stage or an unknown card', () => {
    const b = board();
    expect(moveCard(b, 'a', 'new')).toBe(b);
    expect(moveCard(b, 'zz', 'won')).toBe(b);
  });
});

describe('my tasks', () => {
  it('puts overdue first (oldest first), then today, later and no date, and leaves out done ones', () => {
    const groups = groupTasks([
      task('later', 'later', '2026-12-01T09:00:00'),
      task('none-new', 'none', null, '2026-11-02T10:00:00Z'),
      task('over-new', 'overdue', '2026-10-30T09:00:00'),
      task('today', 'today', '2026-11-03T15:00:00'),
      task('over-old', 'overdue', '2026-10-01T09:00:00'),
      task('none-old', 'none', null, '2026-10-01T10:00:00Z'),
      task('done', 'done', '2026-10-01T09:00:00', undefined, true),
    ]);
    expect(groups.map((g) => g.title)).toEqual(['Overdue', 'Today', 'Later', 'No date']);
    expect(groups.map((g) => g.items.map((i) => i.id))).toEqual([['over-old', 'over-new'], ['today'], ['later'], ['none-old', 'none-new']]);
  });

  it('leaves empty groups out', () => {
    expect(groupTasks([task('t', 'today', '2026-11-03T15:00:00')]).map((g) => g.key)).toEqual(['today']);
    expect(groupTasks([])).toEqual([]);
  });

  it('says what an activity is about', () => {
    expect(relatedTo({ clientName: 'Akua Owusu', leadName: null, opportunityTitle: 'Shop fit-out' })).toBe('Opportunity Shop fit-out · Client Akua Owusu');
    expect(relatedTo({ clientName: null, leadName: 'Kwame', opportunityTitle: null })).toBe('Lead Kwame');
  });
});

describe('due times', () => {
  it('splits and joins the store-local time', () => {
    expect(splitLocal('2026-11-03T15:30:00')).toEqual({ date: '2026-11-03', time: '15:30' });
    expect(splitLocal(null)).toEqual({ date: '', time: '' });
    expect(joinLocal('2026-11-03', '15:30')).toBe('2026-11-03T15:30:00');
    expect(joinLocal('2026-11-03', '')).toBe('2026-11-03T09:00:00');
    expect(joinLocal('', '15:30')).toBeNull();
  });
});

describe('WhatsApp numbers', () => {
  it('keeps the international form', () => {
    expect(normalizeWhatsApp('+92 300-123 4567')).toBe('+923001234567');
    expect(normalizeWhatsApp('0092 (300) 1234567')).toBe('+923001234567');
    expect(normalizeWhatsApp('  ')).toBeNull();
  });

  it('refuses a number without a country code', () => {
    expect(checkWhatsApp('+923001234567')).toBeUndefined();
    expect(checkWhatsApp('')).toBeUndefined();
    expect(checkWhatsApp('03001234567')).toMatch(/country code/);
    expect(checkWhatsApp('+12')).toMatch(/country code/);
  });
});
