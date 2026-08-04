'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { FormSection, TextField } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { cn } from '@/lib/utils';
import type { Matrix, MatrixDimension } from '@/types/masters';

/**
 * The matrix grid (guide p.39–40): up to three dimensions whose cross product becomes the variants.
 *
 * The user edits names and comma-separated values; the server generates the combinations. Typing
 * every cell by hand is how a catalogue ends up with "Med" and "Medium" as different variants of the
 * same shirt — generating them makes the codes consistent by construction. Regeneration is additive:
 * a variant that has ever been sold keeps its identity, because sale history names it.
 */
export function MatrixEditor({ productId, canWrite }: { productId: string; canWrite: boolean }) {
  const [matrix, setMatrix] = useState<Matrix | null>(null);
  const [drafts, setDrafts] = useState<Array<{ name: string; values: string }>>([]);
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState(false);

  const load = useCallback(async () => {
    try {
      const current = await mastersApi.matrix.get(productId);
      setMatrix(current);
      setDrafts(toDrafts(current.dimensions));
    } catch {
      // No grid yet — an empty editor is the correct starting state, not an error.
      setMatrix(null);
      setDrafts([{ name: 'Colour', values: '' }]);
    } finally {
      setLoaded(true);
    }
  }, [productId]);

  useEffect(() => {
    void load();
  }, [load]);

  const combinations = useMemo(
    () => drafts.reduce((total, d) => total * Math.max(1, splitValues(d.values).length), 1),
    [drafts],
  );

  const generate = async () => {
    const dimensions: MatrixDimension[] = drafts
      .map((draft, index) => ({
        position: index + 1,
        name: draft.name.trim(),
        values: splitValues(draft.values),
      }))
      .filter((d) => d.name && d.values.length > 0);

    if (dimensions.length === 0) {
      toast({ title: 'Nothing to generate', description: 'Give at least one dimension a name and some values.' });
      return;
    }

    setBusy(true);

    try {
      const generated = await mastersApi.matrix.define(productId, dimensions);
      setMatrix(generated);
      setDrafts(toDrafts(generated.dimensions));
      toast({ title: 'Grid generated', description: `${generated.variants.length} variants.` });
    } catch (error) {
      toast({
        title: 'Grid not generated',
        description: error instanceof PosApiError ? error.problem.detail : 'Something went wrong.',
        variant: 'destructive',
      });
    } finally {
      setBusy(false);
    }
  };

  if (!loaded) {
    return null;
  }

  return (
    <FormSection
      title="Matrix grid"
      hint="Values are comma-separated. Generating adds new combinations and retires removed ones — a variant that has been sold is never destroyed."
      actions={
        canWrite ? (
          <button type="button" className="underline" disabled={busy} onClick={() => void generate()}>
            {matrix ? 'Regenerate' : 'Generate'}
          </button>
        ) : null
      }
    >
      {drafts.map((draft, index) => (
        <div key={index} className="grid grid-cols-[8rem_1fr_auto] items-end gap-2">
          <TextField
            label={`Dimension ${index + 1}`}
            value={draft.name}
            disabled={!canWrite}
            onChange={(name) => setDrafts((current) => current.map((d, i) => (i === index ? { ...d, name } : d)))}
          />
          <TextField
            label="Values"
            value={draft.values}
            placeholder="S, M, L, XL"
            disabled={!canWrite}
            onChange={(values) => setDrafts((current) => current.map((d, i) => (i === index ? { ...d, values } : d)))}
          />
          {canWrite && drafts.length > 1 ? (
            <button
              type="button"
              className="pos-button mb-0.5"
              onClick={() => setDrafts((current) => current.filter((_, i) => i !== index))}
            >
              Remove
            </button>
          ) : null}
        </div>
      ))}

      {canWrite && drafts.length < 3 ? (
        <button type="button" className="pos-button" onClick={() => setDrafts((current) => [...current, { name: '', values: '' }])}>
          Add a dimension
        </button>
      ) : null}

      <p className="text-label text-ink-muted">
        {combinations} combination{combinations === 1 ? '' : 's'} will exist after generating.
        {combinations > 2000 ? ' That is more than a grid can usefully hold — check the values.' : ''}
      </p>

      {matrix && matrix.variants.length > 0 ? (
        <div className="max-h-64 overflow-y-auto">
          <table className="w-full text-label">
            <thead className="text-ink-muted">
              <tr>
                <th className="text-left">Variant</th>
                {matrix.dimensions.map((d) => (
                  <th key={d.position} className="text-left">
                    {d.name}
                  </th>
                ))}
                <th className="text-right">On hand</th>
              </tr>
            </thead>
            <tbody>
              {matrix.variants.map((variant) => (
                <tr key={String(variant.id)} className={cn(!variant.isActive && 'text-ink-muted line-through')}>
                  <td className="pos-amount">{variant.variantCode}</td>
                  <td>{variant.dim1Value}</td>
                  {matrix.dimensions.length > 1 ? <td>{variant.dim2Value ?? ''}</td> : null}
                  {matrix.dimensions.length > 2 ? <td>{variant.dim3Value ?? ''}</td> : null}
                  <td className="pos-amount text-right">{variant.onHand}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </FormSection>
  );
}

function toDrafts(dimensions: MatrixDimension[]): Array<{ name: string; values: string }> {
  return dimensions.length > 0
    ? dimensions.map((d) => ({ name: d.name, values: d.values.join(', ') }))
    : [{ name: 'Colour', values: '' }];
}

function splitValues(raw: string): string[] {
  return [...new Set(raw.split(',').map((v) => v.trim()).filter(Boolean))];
}
