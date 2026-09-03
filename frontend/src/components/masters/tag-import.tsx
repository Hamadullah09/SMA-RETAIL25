'use client';

import { useRef, useState } from 'react';
import { FormSection } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { apiClient } from '@/lib/api-client';

/**
 * Loading a tag export: an item per stock code, a tag per EPC, in one pass.
 *
 * The rehearsal is not a nicety. These files are exports that somebody has then edited in a
 * spreadsheet — that is what they are for — and the only honest way to look at one before it
 * touches a live catalogue is to have the server parse it and say what it would do. So the rehearsal
 * runs first and by itself; committing is a second, deliberate click on numbers the operator has
 * already read.
 */

const MAXIMUM_BYTES = 8 * 1024 * 1024;

interface ImportProblem {
  lineNumber: number;
  value: string;
  reason: string;
  message: string;
  rowDropped: boolean;
}

interface ImportResult {
  rowsRead: number;
  tagsCreated: number;
  tagsAlreadyMapped: number;
  productsCreated: number;
  productsMatched: number;
  stockCodes: string[];
  problems: ImportProblem[];
}

export function TagImportSection({ locationId, canWrite }: { locationId: number; canWrite: boolean }) {
  const inputRef = useRef<HTMLInputElement>(null);

  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [rehearsal, setRehearsal] = useState<ImportResult | null>(null);
  const [committed, setCommitted] = useState<ImportResult | null>(null);

  const send = async (dryRun: boolean): Promise<ImportResult | null> => {
    if (!file) return null;

    const body = new FormData();
    body.append('file', file);
    body.append('locationId', String(locationId));
    body.append('dryRun', String(dryRun));

    // No explicit Content-Type: the browser has to set the multipart boundary itself, and naming
    // the type here overwrites it with one that has none.
    const response = await apiClient.post<ImportResult>('/serialized-units/import', body);

    return response.data;
  };

  const run = async (dryRun: boolean) => {
    setBusy(true);

    try {
      const result = await send(dryRun);

      if (!result) return;

      if (dryRun) {
        setRehearsal(result);
        setCommitted(null);
      } else {
        setCommitted(result);
        toast({
          title: 'Import finished',
          description: `${result.tagsCreated} tags on ${result.productsCreated + result.productsMatched} items.`,
        });
      }
    } catch (error) {
      toast({
        title: dryRun ? 'Could not read the file' : 'Import failed',
        description: error instanceof Error ? error.message : 'The request was refused.',
        variant: 'destructive',
      });
    } finally {
      setBusy(false);
    }
  };

  const shown = committed ?? rehearsal;

  return (
    <FormSection
      title="Import a tag export"
      hint="A CSV with one row per tag: the EPC, and the stock code of the item it belongs to. Items that already exist are matched rather than overwritten, and running the same file twice adds nothing."
    >
      <div className="flex flex-wrap items-center gap-3">
        <input
          ref={inputRef}
          type="file"
          accept=".csv,text/csv"
          className="sr-only"
          disabled={!canWrite || busy}
          onChange={(event) => {
            const chosen = event.target.files?.[0] ?? null;

            if (chosen && chosen.size > MAXIMUM_BYTES) {
              toast({
                title: 'That file is too large',
                description: `It is ${(chosen.size / 1024 / 1024).toFixed(1)} MB; the limit is 8 MB.`,
                variant: 'destructive',
              });
              return;
            }

            setFile(chosen);
            setRehearsal(null);
            setCommitted(null);
          }}
        />

        <button
          type="button"
          className="pos-button"
          disabled={!canWrite || busy}
          onClick={() => inputRef.current?.click()}
        >
          Choose a file
        </button>

        <span className="text-body text-ink-muted">{file ? file.name : 'No file chosen'}</span>

        <button
          type="button"
          className="pos-button"
          disabled={!canWrite || busy || !file}
          onClick={() => void run(true)}
        >
          {busy ? 'Working…' : 'Check the file'}
        </button>

        {/*
          Only offered once the file has been read. Importing something nobody has looked at is the
          one action this panel should not make easy.
        */}
        <button
          type="button"
          className="pos-button"
          disabled={!canWrite || busy || !rehearsal || committed !== null}
          onClick={() => void run(false)}
        >
          Import
        </button>
      </div>

      {shown ? <ImportSummary result={shown} committed={committed !== null} /> : null}
    </FormSection>
  );
}

function ImportSummary({ result, committed }: { result: ImportResult; committed: boolean }) {
  const dropped = result.problems.filter((p) => p.rowDropped);
  const noted = result.problems.filter((p) => !p.rowDropped);

  return (
    <div className="mt-3 flex flex-col gap-3 rounded border border-subtle bg-panel-sunken p-3">
      <p className="text-body">
        {committed ? 'Imported' : 'This file would import'}{' '}
        <strong>{result.tagsCreated}</strong> {result.tagsCreated === 1 ? 'tag' : 'tags'} across{' '}
        <strong>{result.productsCreated}</strong> new{' '}
        {result.productsCreated === 1 ? 'item' : 'items'} and <strong>{result.productsMatched}</strong> already in the
        catalogue, from {result.rowsRead} rows.
      </p>

      {result.tagsAlreadyMapped > 0 ? (
        <p className="text-label text-ink-muted">
          {result.tagsAlreadyMapped} {result.tagsAlreadyMapped === 1 ? 'tag is' : 'tags are'} already on an item here
          and {result.tagsAlreadyMapped === 1 ? 'was' : 'were'} left alone.
        </p>
      ) : null}

      {noted.length > 0 ? (
        <p className="text-label text-ink-muted">{noted.length} rows needed a default and were imported anyway.</p>
      ) : null}

      {dropped.length > 0 ? (
        <details>
          <summary className="cursor-pointer text-label font-medium text-warning">
            {dropped.length} {dropped.length === 1 ? 'row was' : 'rows were'} not imported
          </summary>

          {/*
            Every rejected row, not a count. A file that quietly loses eleven tags is a shop that
            discovers eleven items will not scan, one customer at a time.
          */}
          <div className="mt-2 max-h-60 overflow-auto">
            <table className="pos-table">
              <thead className="text-ink-muted">
                <tr>
                  <th className="px-2 py-1 text-left font-medium">Line</th>
                  <th className="px-2 py-1 text-left font-medium">Value</th>
                  <th className="px-2 py-1 text-left font-medium">Why</th>
                </tr>
              </thead>
              <tbody>
                {dropped.map((problem) => (
                  <tr key={`${problem.lineNumber}-${problem.value}`} className="border-t border-subtle">
                    <td className="px-2 py-1 tabular-nums">{problem.lineNumber}</td>
                    <td className="px-2 py-1 font-mono">{problem.value}</td>
                    <td className="px-2 py-1">{problem.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      ) : null}
    </div>
  );
}
