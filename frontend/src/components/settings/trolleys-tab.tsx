'use client';

import { useCallback, useEffect, useState } from 'react';
import { toast } from '@/components/ui/toaster';
import { apiClient } from '@/lib/api-client';

/**
 * The self-checkout trolleys, and what each one weighs empty.
 *
 * Here rather than on a screen of its own because a trolley is a fixture of the shop, like a till or
 * a printer, and the people who weigh them are the people who set the shop up.
 *
 * The weight matters because the fleet is not uniform — these run about 2.2 to 2.5 kg — and anything
 * that checks a basket against a scale has to subtract the right one. A single fleet-wide figure
 * would start every trolley up to 150g out before a single item was in it.
 */

interface TrolleyRow {
  id: number;
  code: string;
  label: string | null;
  stationId: number;
  isActive: boolean;
  tareWeightKg: number | null;
}

export function TrolleysTab({ locationId, canWrite }: { locationId?: number; canWrite: boolean }) {
  const [rows, setRows] = useState<TrolleyRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [draft, setDraft] = useState<Record<number, string>>({});
  const [saving, setSaving] = useState<number | null>(null);

  const load = useCallback(async () => {
    if (!locationId) return;

    setLoading(true);

    try {
      const { data } = await apiClient.get(`/trolleys?locationId=${locationId}`);
      const list = data as TrolleyRow[];

      setRows(list);

      // Seeded from what is stored, so the box shows the current figure rather than a blank that
      // looks like "no weight on file".
      setDraft(
        Object.fromEntries(list.map((t) => [t.id, t.tareWeightKg === null ? '' : String(t.tareWeightKg)])),
      );
    } catch {
      toast({ title: 'Could not load the trolleys', variant: 'destructive' });
    } finally {
      setLoading(false);
    }
  }, [locationId]);

  useEffect(() => {
    void load();
  }, [load]);

  const save = async (trolley: TrolleyRow) => {
    const typed = (draft[trolley.id] ?? '').trim();

    // An empty box clears the weight rather than storing zero. Unknown and "weighs nothing" are
    // different claims, and a trolley that has been rebuilt is better recorded as unweighed than as
    // whatever the sticker used to say.
    const value = typed.length === 0 ? null : Number(typed);

    if (value !== null && (!Number.isFinite(value) || value <= 0)) {
      toast({
        title: 'That is not a weight',
        description: 'Enter a number of kilograms, or clear the box to mark it unknown.',
        variant: 'destructive',
      });
      return;
    }

    setSaving(trolley.id);

    try {
      await apiClient.put(`/trolleys/${trolley.id}/tare`, { tareWeightKg: value });
      toast({ title: `Trolley ${trolley.code} saved` });
      await load();
    } catch (error) {
      const problem = (error as { response?: { data?: { detail?: string } } })?.response?.data;
      toast({
        title: 'Could not save it',
        description: problem?.detail ?? 'Something went wrong.',
        variant: 'destructive',
      });
    } finally {
      setSaving(null);
    }
  };

  const unweighed = rows.filter((t) => t.tareWeightKg === null).length;

  // Lays the whole block down at once. Counters were only ever created when a shopper was issued
  // one, which is fine for running a shop and useless for setting one up: nobody wants to wait for
  // two hundred shoppers before the fleet appears on this screen.
  const provision = async () => {
    setLoading(true);

    try {
      const { data } = await apiClient.post('/trolleys/provision', {});
      const result = data as { created: number; alreadyThere: number; weighed: number; failed: number };

      toast({
        title: result.created > 0 ? `${result.created} counters added` : 'Nothing to add',
        description:
          result.failed > 0
            ? `${result.weighed} given a starting weight. ${result.failed} could not be created.`
            : `${result.weighed} given a starting weight.`,
        variant: result.failed > 0 ? 'destructive' : undefined,
      });

      await load();
    } catch (error) {
      const problem = (error as { response?: { data?: { detail?: string } } })?.response?.data;
      toast({
        title: 'Could not add the counters',
        description: problem?.detail ?? 'Something went wrong.',
        variant: 'destructive',
      });
    } finally {
      setLoading(false);
    }
  };

  return (
    <section className="flex flex-col gap-3">
      <header>
        <h2 className="text-heading">Trolleys</h2>
        <p className="text-sm text-ink-muted">
          What each self-checkout trolley weighs empty, in kilograms. A scale checking a basket
          subtracts this, so it is worth being accurate to the gram — the fleet varies by around
          300g and a single shared figure would start every trolley out by more than many items
          weigh.
        </p>
        <p className="mt-1 text-sm text-ink-muted">
          Leave a box empty to mark a trolley as never weighed. That is not the same as zero, and it
          is the right answer for one that has been rebuilt.
        </p>

        {canWrite ? (
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <button type="button" className="pos-button" disabled={loading} onClick={() => void provision()}>
              {loading ? 'Working…' : 'Add every counter in the block'}
            </button>
            <span className="text-xs text-ink-muted">
              Creates any counter in the self-checkout range that does not exist yet and gives each a
              starting weight spread across the band. Safe to press twice; it never overwrites a
              weight already recorded.
            </span>
          </div>
        ) : null}
      </header>

      {loading ? (
        <p className="text-sm text-ink-muted">Loading…</p>
      ) : rows.length === 0 ? (
        <p className="text-sm text-ink-muted">
          No trolleys yet. One is registered the first time a shopper is issued a counter in the
          self-checkout range.
        </p>
      ) : (
        <>
          {unweighed > 0 ? (
            <p className="rounded-md border border-subtle bg-panel-sunken p-2 text-xs text-ink-muted">
              {unweighed} of {rows.length} have never been weighed and are running on the assumed
              figure.
            </p>
          ) : null}

          <div className="overflow-x-auto">
            <table className="pos-table">
              <thead>
                <tr className="border-b border-subtle text-left text-label text-ink-muted">
                  <th className="px-2 py-1">Counter</th>
                  <th className="px-2 py-1">Label</th>
                  <th className="px-2 py-1">In service</th>
                  <th className="px-2 py-1 text-right">Empty weight (kg)</th>
                  <th className="px-2 py-1" />
                </tr>
              </thead>
              <tbody>
                {rows.map((trolley) => (
                  <tr key={trolley.id} className="border-b border-subtle">
                    <td className="px-2 py-1 font-medium">{trolley.code}</td>
                    <td className="px-2 py-1">{trolley.label ?? '—'}</td>
                    <td className="px-2 py-1">{trolley.isActive ? 'Yes' : 'No'}</td>
                    <td className="px-2 py-1 text-right">
                      <input
                        type="number"
                        step="0.001"
                        min="0"
                        inputMode="decimal"
                        aria-label={`Empty weight of trolley ${trolley.code} in kilograms`}
                        className="pos-input w-28 text-right"
                        placeholder="not weighed"
                        disabled={!canWrite}
                        value={draft[trolley.id] ?? ''}
                        onChange={(e) => setDraft((d) => ({ ...d, [trolley.id]: e.target.value }))}
                      />
                    </td>
                    <td className="px-2 py-1">
                      <button
                        type="button"
                        className="pos-button"
                        disabled={!canWrite || saving === trolley.id}
                        onClick={() => void save(trolley)}
                      >
                        {saving === trolley.id ? 'Saving…' : 'Save'}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  );
}
