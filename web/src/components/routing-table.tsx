'use client';

import { X } from 'lucide-react';
import { IconButton } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/field';
import type { RoutingOperation } from '@/lib/production-types';

/** A row of numbers kept as typed, so "0." survives while typing. */
export type Text<T> = { [K in keyof T]: T[K] extends number ? string : T[K] extends number | null ? string : T[K] };

export type OpText = Text<RoutingOperation>;

/** A routing as typed rows, from its operations. */
export function routingText(operations: readonly RoutingOperation[]): OpText[] {
  return operations.map((o) => ({ ...o, sequence: String(o.sequence), setupMinutes: String(o.setupMinutes), runMinutesPerUnit: String(o.runMinutesPerUnit) }));
}

/** The routing's rows, editable: the bill editor's Routing tab and the Routings page. */
export function OperationsTable({ ops, setOps, editable, centres }: { ops: OpText[]; setOps(ops: OpText[]): void; editable: boolean; centres: Array<{ id: string; code: string; name: string; storeCode: string }> }) {
  return (
    <table className="grid-table" aria-label="Routing">
      <thead>
        <tr>
          <th className="num">Step</th>
          <th>Operation</th>
          <th>Work centre</th>
          <th className="num">Setup min</th>
          <th className="num">Min a unit</th>
          <th className="max-lg:hidden">Instructions</th>
          <th />
        </tr>
      </thead>
      <tbody>
        {ops.map((o, i) => {
          const set = (patch: Partial<OpText>) => setOps(ops.map((x, j) => (j === i ? { ...x, ...patch } : x)));
          return (
            <tr key={i}>
              <td><Input aria-label={`Operation ${i + 1} step`} inputMode="numeric" value={o.sequence} disabled={!editable} className="w-[80px] text-right" onChange={(e) => set({ sequence: e.target.value })} /></td>
              <td><Input aria-label={`Operation ${i + 1} name`} value={o.name} disabled={!editable} className="min-w-[150px]" onChange={(e) => set({ name: e.target.value })} /></td>
              <td>
                <Select aria-label={`Operation ${i + 1} work centre`} value={o.workCentreId} disabled={!editable} className="min-w-[170px]" onChange={(e) => set({ workCentreId: e.target.value })}>
                  <option value="">Choose…</option>
                  {centres.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.code} {c.name} ({c.storeCode})
                    </option>
                  ))}
                </Select>
              </td>
              <td><Input aria-label={`Operation ${i + 1} setup`} inputMode="decimal" value={o.setupMinutes} disabled={!editable} className="w-[100px] text-right" onChange={(e) => set({ setupMinutes: e.target.value })} /></td>
              <td><Input aria-label={`Operation ${i + 1} run`} inputMode="decimal" value={o.runMinutesPerUnit} disabled={!editable} className="w-[100px] text-right" onChange={(e) => set({ runMinutesPerUnit: e.target.value })} /></td>
              <td className="max-lg:hidden"><Input aria-label={`Operation ${i + 1} instructions`} value={o.instructions ?? ''} disabled={!editable} className="min-w-[200px]" onChange={(e) => set({ instructions: e.target.value || null })} /></td>
              <td>{editable && <IconButton icon={X} label={`Remove operation ${i + 1}`} onClick={() => setOps(ops.filter((_, j) => j !== i))} />}</td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

