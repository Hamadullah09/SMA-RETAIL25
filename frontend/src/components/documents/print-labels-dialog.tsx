'use client';

import { useEffect, useMemo, useState } from 'react';
import { Dialog } from '@/components/ui/dialog';
import { toast } from '@/components/ui/toaster';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { downloadPdf, printPdf } from '@/lib/print';
import { formatCurrency } from '@/lib/utils';
import type { LabelStock, LabelStockOption } from '@/types/masters';

/** The minimum an item has to tell us to appear in the run. */
export interface PrintableItem {
  id: number;
  stockCode: string;
  name: string;
  regularPrice: number;
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

  const build = async () => {
    const body = { locationId, lines, stock, showBarcode, skipLabels };

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

          <div className="max-h-72 overflow-y-auto border border-subtle">
            <table className="w-full text-body">
              <thead className="sticky top-0 bg-panel text-label">
                <tr>
                  <th className="px-2 py-1 text-left">Code</th>
                  <th className="px-2 py-1 text-left">Description</th>
                  <th className="px-2 py-1 text-right">Price</th>
                  <th className="px-2 py-1 text-right">Copies</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={String(item.id)} className="border-t border-subtle">
                    <td className="px-2 py-1">{item.stockCode}</td>
                    <td className="px-2 py-1">{item.name}</td>
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
