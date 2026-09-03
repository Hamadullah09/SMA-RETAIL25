'use client';

import { useState } from 'react';
import { Dialog } from '@/components/ui/dialog';
import { toast } from '@/components/ui/toaster';
import { apiClient } from '@/lib/api-client';

/**
 * Loading a shop's stock from one spreadsheet.
 *
 * The importer behind this has existed for a while and had no screen, which meant it did not exist
 * as far as anybody running a shop was concerned. That is the whole reason this file is here: a
 * capability reachable only by posting a form to an undocumented endpoint is not a feature.
 *
 * One file, not four. Items, barcodes, departments, suppliers, opening stock and RFID tags all sit
 * in the same sheet, because the alternative is asking a shopkeeper to perform a join by hand
 * across several files keyed on stock code.
 *
 * Nothing is written until the preview has been read. The first press always dry-runs: it reports
 * what the file would do and touches nothing, so a file somebody has been editing in Excel can be
 * looked at before it reaches a live catalogue.
 */

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
  problems: ImportProblem[];
}

export function ImportCatalogueDialog({
  locationId,
  open,
  onClose,
  onImported,
}: {
  locationId: number;
  open: boolean;
  onClose: () => void;
  onImported: () => void;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<ImportResult | null>(null);
  const [busy, setBusy] = useState(false);

  const reset = () => {
    setFile(null);
    setPreview(null);
  };

  const send = async (dryRun: boolean): Promise<ImportResult | null> => {
    if (!file) return null;

    const body = new FormData();
    body.append('file', file);
    body.append('locationId', String(locationId));
    body.append('dryRun', String(dryRun));

    const { data } = await apiClient.post('/serialized-units/import', body);

    return data as ImportResult;
  };

  const check = async () => {
    setBusy(true);

    try {
      setPreview(await send(true));
    } catch (error) {
      setPreview(null);
      toast({
        title: 'Could not read the file',
        description: detail(error),
        variant: 'destructive',
      });
    } finally {
      setBusy(false);
    }
  };

  const apply = async () => {
    setBusy(true);

    try {
      const result = await send(false);

      toast({ variant: 'success',
        title: 'Imported',
        description: result
          ? `${result.productsCreated} new items, ${result.productsMatched} already here, ${result.tagsCreated} tags.`
          : undefined,
      });

      onImported();
      reset();
      onClose();
    } catch (error) {
      toast({ title: 'Could not import', description: detail(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const dropped = preview?.problems.filter((p) => p.rowDropped).length ?? 0;

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (next) return;
        reset();
        onClose();
      }}
      title="Import items from a spreadsheet"
      footer={
        <>
          <button
            type="button"
            className="pos-button"
            onClick={() => {
              reset();
              onClose();
            }}
          >
            Cancel
          </button>

          {preview ? (
            <button
              type="button"
              className="pos-button-primary"
              disabled={busy || preview.productsCreated + preview.tagsCreated === 0}
              onClick={() => void apply()}
            >
              {busy ? 'Importing…' : `Import ${preview.productsCreated} items`}
            </button>
          ) : (
            <button
              type="button"
              className="pos-button-primary"
              disabled={busy || !file}
              onClick={() => void check()}
            >
              {busy ? 'Reading…' : 'Check the file'}
            </button>
          )}
        </>
      }
    >
      <div className="flex flex-col gap-3 text-sm">
        <p className="text-ink-muted">
          One row per item. A row with an EPC also creates that tag, so a shop with no RFID simply
          leaves the column out.
        </p>

        <p>
          <a className="underline" href="/api/proxy/serialized-units/import/template" download>
            Download a template
          </a>{' '}
          <span className="text-ink-muted">
            — fill it in and send it back. Headings may be written however you write them.
          </span>
        </p>

        <label className="flex flex-col gap-1">
          <span className="text-xs text-ink-muted">CSV file</span>
          <input
            type="file"
            accept=".csv,text/csv"
            className="pos-input"
            onChange={(e) => {
              setFile(e.target.files?.[0] ?? null);
              setPreview(null);
            }}
          />
        </label>

        <details className="rounded-md border border-subtle bg-panel-sunken p-3">
          <summary className="cursor-pointer text-xs text-ink-muted">Which columns are read</summary>
          <p className="mt-2 text-xs text-ink-muted">
            <strong>Stock Code</strong> is the only one required — it is what identifies an item.
            Everything else is optional: Item Name, Description, Department, Category, Supplier,
            Barcode or UPC, Cost, Price, Qty, Bin, Weight, Case Qty, Reorder Point, Reorder Qty,
            Base Stock, Tax1, Tax2, POS Message, Invoice Message, Notes, Image URL and EPC.
          </p>
          <p className="mt-2 text-xs text-ink-muted">
            Departments, categories and suppliers are matched by name and created if they are new.
            Qty becomes the opening stock. A column you leave out keeps the item&rsquo;s existing
            value rather than clearing it. Anything the importer does not recognise is ignored
            rather than rejected.
          </p>
          <p className="mt-2 text-xs text-ink-muted">
            <strong>Image URL</strong> is downloaded and stored against the item. It must be a
            public http or https address ending in a PNG, JPEG or WebP — addresses on a private
            network are refused, and a picture that cannot be fetched is reported without holding up
            the rest of the import.
          </p>
          <p className="mt-2 text-xs text-ink-muted">
            <strong>On order</strong> is not imported. It is worked out from purchase orders, so a
            number here would stop meaning anything the moment a real order existed.
          </p>
        </details>

        {preview ? (
          <div className="rounded-md border border-subtle p-3">
            {/*
              Stated as what would happen, not as what did. This panel is the output of a dry run
              and reads a press too early otherwise.
            */}
            <p className="font-semibold">Nothing has been imported yet. This file would:</p>
            <ul className="mt-2 list-disc pl-5">
              <li>create {preview.productsCreated} new items</li>
              <li>match {preview.productsMatched} that are already here, leaving them unchanged</li>
              {preview.tagsCreated > 0 ? <li>attach {preview.tagsCreated} RFID tags</li> : null}
              {preview.tagsAlreadyMapped > 0 ? (
                <li>skip {preview.tagsAlreadyMapped} tags already on an item</li>
              ) : null}
              <li className="text-ink-muted">read from {preview.rowsRead} rows</li>
            </ul>

            {dropped > 0 ? (
              <div className="mt-3">
                <p className="font-semibold text-warning">{dropped} rows would be skipped:</p>
                <ul className="mt-1 max-h-40 overflow-y-auto text-xs">
                  {preview.problems
                    .filter((p) => p.rowDropped)
                    .slice(0, 25)
                    .map((p) => (
                      <li key={`${p.lineNumber}-${p.reason}`}>
                        Line {p.lineNumber}: {p.message}
                      </li>
                    ))}
                </ul>
              </div>
            ) : null}
          </div>
        ) : null}
      </div>
    </Dialog>
  );
}

function detail(error: unknown): string {
  const problem = (error as { response?: { data?: { detail?: string; title?: string } } })?.response?.data;

  return problem?.detail ?? problem?.title ?? 'Something went wrong.';
}
