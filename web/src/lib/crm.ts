/**
 * The CRM pages' rules that need no server: labels for the enums, the board's totals and the move of a card before the
 * server answers, the grouping of "My tasks", due times as the store's local time, and WhatsApp numbers.
 */
import type { ChipTone } from '@/components/ui/page';
import type { ActivityDue, ActivityKind, ActivityRow, BoardStage, LeadStatus, OpportunityBoard, StageOutcome } from './types';

export const ACTIVITY_KINDS: ReadonlyArray<{ key: ActivityKind; label: string }> = [
  { key: 'Call', label: 'Call' },
  { key: 'Visit', label: 'Visit' },
  { key: 'Meeting', label: 'Meeting' },
  { key: 'Note', label: 'Note' },
  { key: 'Task', label: 'Task' },
];

export const LEAD_STATUS: Record<LeadStatus, { label: string; tone: ChipTone }> = {
  Open: { label: 'Open', tone: 'warn' },
  Converted: { label: 'Client', tone: 'ok' },
  Dropped: { label: 'Dropped', tone: 'plain' },
};

export const OUTCOMES: ReadonlyArray<{ key: StageOutcome; label: string; tone: ChipTone }> = [
  { key: 'Open', label: 'Open', tone: 'plain' },
  { key: 'Won', label: 'Won', tone: 'ok' },
  { key: 'Lost', label: 'Lost', tone: 'danger' },
];

export function outcomeTone(outcome: StageOutcome): ChipTone {
  return OUTCOMES.find((o) => o.key === outcome)?.tone ?? 'plain';
}

export const DUE: Record<ActivityDue, { label: string; tone: ChipTone }> = {
  overdue: { label: 'Overdue', tone: 'danger' },
  today: { label: 'Today', tone: 'warn' },
  later: { label: 'Later', tone: 'plain' },
  none: { label: 'No date', tone: 'plain' },
  done: { label: 'Done', tone: 'ok' },
};

// Totals ------------------------------------------------------------------------------------------------------------

export interface Totals {
  count: number;
  value: number;
  weighted: number;
}

const round2 = (n: number) => Math.round(n * 100) / 100;

/** A stage's count, value and weighted value from its cards. */
export function stageTotals(stage: Pick<BoardStage, 'opportunities'>): Totals {
  return stage.opportunities.reduce(
    (t, o) => ({ count: t.count + 1, value: round2(t.value + o.expectedValue), weighted: round2(t.weighted + o.weightedValue) }),
    { count: 0, value: 0, weighted: 0 },
  );
}

/** What is still open on the board: the open stages only. */
export function openTotals(board: OpportunityBoard): Totals {
  return board.stages
    .filter((s) => s.outcome === 'Open')
    .reduce((t, s) => ({ count: t.count + s.count, value: round2(t.value + s.value), weighted: round2(t.weighted + s.weighted) }), { count: 0, value: 0, weighted: 0 });
}

/**
 * The board after a card moved, shown while the server saves it: the card takes the stage's name, outcome and
 * probability (as the server does), and both columns' totals are worked out again.
 */
export function moveCard(board: OpportunityBoard, opportunityId: string, toStageId: string, lostReason?: string | null): OpportunityBoard {
  const from = board.stages.find((s) => s.opportunities.some((o) => o.id === opportunityId));
  const to = board.stages.find((s) => s.id === toStageId);
  if (!from || !to || from.id === to.id) return board;
  const card = from.opportunities.find((o) => o.id === opportunityId)!;
  const moved = {
    ...card,
    stageId: to.id,
    stage: to.name,
    outcome: to.outcome,
    probability: to.probability,
    weightedValue: round2((card.expectedValue * to.probability) / 100),
    lostReason: to.outcome === 'Lost' ? lostReason ?? card.lostReason : null,
  };
  const stages = board.stages.map((s) => {
    if (s.id === from.id) {
      const opportunities = s.opportunities.filter((o) => o.id !== opportunityId);
      return { ...s, opportunities, ...stageTotals({ opportunities }) };
    }
    if (s.id === to.id) {
      const opportunities = [moved, ...s.opportunities];
      return { ...s, opportunities, ...stageTotals({ opportunities }) };
    }
    return s;
  });
  return { stages };
}

// My tasks ----------------------------------------------------------------------------------------------------------

export type TaskGroupKey = 'overdue' | 'today' | 'later' | 'none';

export interface TaskGroup {
  key: TaskGroupKey;
  title: string;
  items: ActivityRow[];
}

const GROUPS: ReadonlyArray<{ key: TaskGroupKey; title: string }> = [
  { key: 'overdue', title: 'Overdue' },
  { key: 'today', title: 'Today' },
  { key: 'later', title: 'Later' },
  { key: 'none', title: 'No date' },
];

/**
 * Open activities in the order to do them: overdue first (the oldest first), then today's, later ones, and those
 * without a date (oldest added first). Groups with nothing in them are left out; done ones are never listed.
 */
export function groupTasks(items: readonly ActivityRow[]): TaskGroup[] {
  const byDue = (a: ActivityRow, b: ActivityRow) => (a.dueAt ?? '').localeCompare(b.dueAt ?? '') || a.createdAtUtc.localeCompare(b.createdAtUtc);
  return GROUPS.map((g) => ({
    ...g,
    items: items.filter((a) => !a.isDone && a.due === g.key).sort(g.key === 'none' ? (a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc) : byDue),
  })).filter((g) => g.items.length > 0);
}

/** "Client Akua Owusu", "Lead Kwame", "Opportunity Shop fit-out": what an activity is about. */
export function relatedTo(a: Pick<ActivityRow, 'clientName' | 'leadName' | 'opportunityTitle'>): string {
  const parts: string[] = [];
  if (a.opportunityTitle) parts.push(`Opportunity ${a.opportunityTitle}`);
  if (a.clientName) parts.push(`Client ${a.clientName}`);
  else if (a.leadName) parts.push(`Lead ${a.leadName}`);
  return parts.join(' · ');
}

// Due times ---------------------------------------------------------------------------------------------------------

/** A store-local `yyyy-MM-ddTHH:mm:ss` split for a date box and a time box. */
export function splitLocal(value: string | null | undefined): { date: string; time: string } {
  const match = /^(\d{4}-\d{2}-\d{2})(?:[T ](\d{2}:\d{2}))?/.exec(value ?? '');
  return match ? { date: match[1], time: match[2] ?? '' } : { date: '', time: '' };
}

/** A date box and a time box as the store-local time the server keeps; null without a date. */
export function joinLocal(date: string, time: string): string | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) return null;
  const hm = /^\d{2}:\d{2}$/.test(time) ? time : '09:00';
  return `${date}T${hm}:00`;
}

// WhatsApp numbers --------------------------------------------------------------------------------------------------

/**
 * A WhatsApp number as the server keeps it: international form, spaces, dashes, dots and brackets dropped, `00` read as
 * `+`. Null when nothing was typed.
 */
export function normalizeWhatsApp(text: string | null | undefined): string | null {
  const t = (text ?? '').replace(/[\s\-().]/g, '');
  if (!t) return null;
  return t.startsWith('00') ? '+' + t.slice(2) : t;
}

/** Why a typed WhatsApp number would be refused, or undefined when it is fine (or empty). */
export function checkWhatsApp(text: string | null | undefined): string | undefined {
  const n = normalizeWhatsApp(text);
  if (!n) return undefined;
  return /^\+[1-9]\d{7,14}$/.test(n) ? undefined : 'Type the number with its country code, like +923001234567.';
}
