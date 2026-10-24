'use client';

import { useEffect, useMemo, useState } from 'react';
import { Dialog } from '@/components/ui/dialog';
import { toast } from '@/components/ui/toaster';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { downloadPdf, printPdf } from '@/lib/print';
import { formatCurrency } from '@/lib/utils';
import { useAuth } from '@/lib/auth-config';
import { describeError } from '@/lib/errors';
import { labelBarcodeKind, type BarcodeKind } from '@/lib/upc';
import { StatusBadge } from '@/components/ui/status-badge';
import type { LabelStock, LabelStockOption, LabelSymbology } from '@/types/masters';

/** The minimum an item has to tell us to appear in the run. */
export interface PrintableItem {
  id: number;
  stockCode: string;
  name: string;
  regularPrice: number;
  upc?: string | null;
}

const SYMBOLOGIES: Array<{ value: LabelSymbology; label: string }> = [
  { value: 'Auto', label: 'UPC-A where the item has one, otherwise Code 39' },
  { value: 'UpcA', label: 'UPC-A only (items without one print no barcode)' },
  { value: 'Code39', label: 'Code 39 of the stock code, as before' },
];

/** What a label in this run will carry, for the column that says so before anything is printed. */
function barcodeFor(upc: string | null | undefined, stockCode: string, symbology: LabelSymbology): BarcodeKind {
  const auto = labelBarcodeKind(upc, stockCode);

  if (symbology === 'UpcA') return auto === 'upca' ? 'upca' : 'none';
  if (symbology === 'Code39') return labelBarcodeKind(null, upc?.trim() || stockCode) === 'none' ? 'none' : 'code39';
  return auto;
}

/** How many labels fit a sheet, so the page can say how many sheets to load. Mirrors AveryLayouts. */
const perSheet: Record<LabelStock, number> = {
  Avery5160: 30,
  Avery8160: 30,
  Avery8163: 10,
  S644N: 6,
};

const inputClass =
  'pos-input';

/**
 * The label run (guide App. L).
 *
 * Copies are per item rather than one figure for the batch, because a shelf reset is almost never
 * "one of everything" — it is four of the fast movers and one of the rest.
 */
export function PrintLabelsDialog({
  locationId,
  items,
  onClose,
}: {
  locationId: number;
  items: PrintableItem[];
  onClose: () => void;
}) {
  const [stocks, setStocks] = useState<LabelStockOption[]>([]);
  const [stock, setStock] = useState<LabelStock>('Avery5160');
  const [barcodeFirst, setBarcodeFirst] = useState(false);
  const [showBarcode, setShowBarcode] = useState(true);
  const [skipLabels, setSkipLabels] = useState(0);
  const [symbology, setSymbology] = useState<LabelSymbology>('Auto');

  // The barcodes as this dialog knows them, so numbering items here shows the result at once.
  const [upcs, setUpcs] = useState<Record<string, string | null>>(() =>
    Object.fromEntries(items.map((item) => [item.id, item.upc ?? null])),
  );
  const [numbering, setNumbering] = useState(false);
  const canNumber = useAuth().can('catalog.write');
  const [copies, setCopies] = useState<Record<string, number>>(() =>
    Object.fromEntries(items.map((item) => [item.id, 1])),
  );
  const [busy, setBusy] = useState(false);

  // Escape, the focus trap and returning focus to the button that opened this are the dialog
  // primitive's job now; this component used to do all three by hand and got the trap wrong.
  useEffect(() => {
    void mastersApi.documents
      .labelStocks()
      .then(setStocks)
      .catch(() => {
        // The picker degrades to the default stock rather than blocking the print.
        setStocks([]);
      });
  }, []);

  const lines = useMemo(
    () => items.map((item) => ({ productId: item.id, copies: copies[item.id] ?? 1 })).filter((l) => l.copies > 0),
    [items, copies],
  );

  const totalLabels = lines.reduce((sum, line) => sum + line.copies, 0);
  const sheets = totalLabels === 0 ? 0 : Math.ceil((totalLabels + skipLabels) / perSheet[stock]);

  const setAll = (value: number) =>
    setCopies(Object.fromEntries(items.map((item) => [item.id, Math.max(0, value)])));

  // Items with no barcode at all: the ones an in-store UPC-A can be given to. An item carrying some
  // other barcode keeps it — it may be printed on packaging the shop does not control.
  const unnumbered = items.filter((item) => !upcs[item.id]?.trim());
  const withUpcA = items.filter((item) => labelBarcodeKind(upcs[item.id], item.stockCode) === 'upca').length;

  const number = async () => {
    setNumbering(true);

    try {
      const result = await mastersApi.products.assignInStoreUpcs(
        locationId,
        unnumbered.map((item) => item.id),
      );

      setUpcs((current) => ({
        ...current,
        ...Object.fromEntries(result.assigned.map((entry) => [entry.productId, entry.upc])),
      }));

      toast({
        variant: 'success',
        title: `${result.assigned.length} item${result.assigned.length === 1 ? '' : 's'} given a UPC-A`,
        description:
          result.skipped > 0
            ? `${result.skipped} could not be numbered because their number is already in use.`
            : 'They print as UPC-A from now on, and ring up when scanned.',
      });
    } catch (error) {
      toast({ title: 'Could not give out UPC-A numbers', description: describeError(error), variant: 'destructive' });
    } finally {
      setNumbering(false);
    }
  };

  const build = async () => {
    const body = { locationId, lines, stock, showBarcode, skipLabels, symbology };

    return barcodeFirst
      ? mastersApi.documents.printBarcodeLabels(body)
      : mastersApi.documents.printPriceTags(body);
  };

  const failed = (error: unknown, title: string) =>
    toast({
      title,
      description: error instanceof PosApiError ? error.problem.detail : 'Something went wrong.',
      variant: 'destructive',
    });

  const print = async () => {
    if (lines.length === 0) return;

    setBusy(true);

    try {
      // The print dialog, not a file in Downloads. This used to open a tab and call that printing,
      // which it was not: the document arrived as an attachment, so the tab saved it and went away.
      const outcome = await printPdf(await build());

      if (outcome === 'blocked') {
        toast({
          title: 'Pop-up blocked',
          description: 'Allow pop-ups for this site, or use Save and print the file.',
          variant: 'destructive',
        });
      } else if (outcome === 'opened') {
        toast({
          title: 'Labels opened',
          description: 'This browser would not open the print dialog. Press Ctrl+P in the new tab.',
        });
      }
    } catch (error) {
      failed(error, 'Could not print');
    } finally {
      setBusy(false);
    }
  };

  // Kept as its own button rather than left to the viewer's save icon. Printing is the common case
  // and now costs one press; wanting the file is rarer but real -- a sheet emailed to whoever has
  // the label printer -- and it should not require finding a control inside a PDF viewer.
  const save = async () => {
    if (lines.length === 0) return;

    setBusy(true);

    try {
      downloadPdf(await build(), barcodeFirst ? 'barcode-labels.pdf' : 'price-tags.pdf');
    } catch (error) {
      failed(error, 'Could not save');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      title="Print labels"
      description={`${items.length} item(s) · ${totalLabels} label(s) across ${sheets} sheet(s)`}
      size="lg"
      footer={
        <>
          <button type="button" className="pos-button" onClick={onClose}>
            Cancel
          </button>
          <button
            type="button"
            className="pos-button"
            disabled={busy || totalLabels === 0}
            onClick={() => void save()}
            title="Save the sheet as a PDF instead of printing it"
          >
            Save PDF
          </button>
          <button
            type="button"
            className="pos-button-primary"
            disabled={busy || totalLabels === 0}
            onClick={() => void print()}
          >
            {busy ? 'Building the PDF…' : 'Print'}
          </button>
        </>
      }
    >
      <div className="space-y-4">
          <div className="flex flex-wrap items-end gap-3">
            <label className="flex flex-col gap-1 text-label">
              Label stock
              <select
                className={inputClass}
                value={stock}
                onChange={(event) => setStock(event.target.value as LabelStock)}
              >
                {stocks.length > 0 ? (
                  stocks.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))
                ) : (
                  <option value="Avery5160">Avery 5160 — 30 per sheet</option>
                )}
              </select>
            </label>

            <label className="flex flex-col gap-1 text-label">
              Skip labels
              <input
                type="number"
                min={0}
                className={`${inputClass} w-24`}
                value={skipLabels}
                onChange={(event) => setSkipLabels(Math.max(0, Number(event.target.value) || 0))}
              />
            </label>

            <label className="flex items-center gap-1.5 text-label">
              <input
                type="checkbox"
                checked={showBarcode}
                onChange={(event) => setShowBarcode(event.target.checked)}
              />
              Print the barcode
            </label>

            <label className="flex flex-col gap-1 text-label">
              Barcode type
              <select
                className={inputClass}
                value={symbology}
                disabled={!showBarcode}
                onChange={(event) => setSymbology(event.target.value as LabelSymbology)}
              >
                {SYMBOLOGIES.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>

            <label className="flex items-center gap-1.5 text-label">
              <input
                type="checkbox"
                checked={barcodeFirst}
                disabled={!showBarcode}
                onChange={(event) => setBarcodeFirst(event.target.checked)}
              />
              Barcode at the top (shelf edge)
            </label>

            <label className="flex flex-col gap-1 text-label">
              Set every copy count to
              <input
                type="number"
                min={0}
                className={`${inputClass} w-24`}
                defaultValue={1}
                onChange={(event) => setAll(Number(event.target.value) || 0)}
              />
            </label>
          </div>

          <p className="text-label text-ink-muted">
            Skip counts labels already peeled off a part-used sheet, so printing resumes at the next free
            position instead of onto bare backing.
          </p>

          {/*
            Most of a shop's own goods have no UPC: their stock codes have letters in them, which UPC-A
            cannot carry. Saying how many will print as UPC-A, and offering to number the rest from
            the shop's own range, is what turns "print UPC-A barcodes" into something that happens.
          */}
          {showBarcode && symbology !== 'Code39' ? (
            <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-subtle bg-panel-sunken px-3 py-2">
              <p className="text-body text-ink">
                <strong className="font-semibold">{withUpcA}</strong> of {items.length} item
                {items.length === 1 ? '' : 's'} will print as UPC-A.
                {unnumbered.length > 0
                  ? ` ${unnumbered.length} ${unnumbered.length === 1 ? 'has' : 'have'} no barcode yet.`
                  : ''}
              </p>

              {canNumber && unnumbered.length > 0 ? (
                <button type="button" className="pos-button" disabled={numbering} onClick={() => void number()}>
                  {numbering
                    ? 'Numbering…'
                    : `Give ${unnumbered.length === 1 ? 'it' : `these ${unnumbered.length}`} a UPC-A number`}
                </button>
              ) : null}
            </div>
          ) : null}

          <div className="max-h-72 overflow-y-auto border border-subtle">
            <table className="pos-table">
              <thead className="sticky top-0 bg-panel text-label">
                <tr>
                  <th className="px-2 py-1 text-left">Code</th>
                  <th className="px-2 py-1 text-left">Description</th>
                  <th className="px-2 py-1 text-left">Barcode</th>
                  <th className="px-2 py-1 text-right">Price</th>
                  <th className="px-2 py-1 text-right">Copies</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={String(item.id)} className="border-t border-subtle">
                    <td className="px-2 py-1">{item.stockCode}</td>
                    <td className="px-2 py-1">{item.name}</td>
                    <td className="px-2 py-1">
                      <BarcodeCell
                        upc={upcs[item.id]}
                        kind={showBarcode ? barcodeFor(upcs[item.id], item.stockCode, symbology) : 'none'}
                      />
                    </td>
                    <td className="px-2 py-1 text-right">{formatCurrency(item.regularPrice)}</td>
                    <td className="px-2 py-1 text-right">
                      <input
                        type="number"
                        min={0}
                        max={500}
                        aria-label={`Copies of ${item.stockCode}`}
                        className={`${inputClass} w-20 text-right`}
                        value={copies[item.id] ?? 1}
                        onChange={(event) =>
                          setCopies((current) => ({
                            ...current,
                            [item.id]: Math.max(0, Math.min(500, Number(event.target.value) || 0)),
                          }))
                        }
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

      </div>
    </Dialog>
  );
}

/** The number a label will carry, and as what — so a sheet is never printed to find that out. */
function BarcodeCell({ upc, kind }: { upc: string | null | undefined; kind: BarcodeKind }) {
  return (
    <span className="flex flex-wrap items-center gap-1.5">
      {upc ? <span className="font-mono text-caption">{upc}</span> : null}
      {kind === 'upca' ? (
        <StatusBadge tone="positive" label="UPC-A" />
      ) : kind === 'code39' ? (
        <StatusBadge tone="neutral" label="Code 39" />
      ) : (
        <StatusBadge tone="warning" label="No barcode" />
      )}
    </span>
  );
}
