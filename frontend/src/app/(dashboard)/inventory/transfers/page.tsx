'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { BrowseFormShell, FormSection, NumberField, TextField } from '@/components/masters/browse-form';
import { RecordPicker } from '@/components/masters/record-picker';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { formatCurrency } from '@/lib/utils';
import type { Transfer, TransferRow, TransferStatus } from '@/types/masters';
import { describeError } from '@/lib/errors';
import { ConfirmDialog, useConfirm } from '@/components/ui/confirm-dialog';

const filterClass =
  'pos-input';

const statuses: TransferStatus[] = ['Draft', 'InTransit', 'Received', 'Cancelled'];

/** How each state reads on screen. "In transit" is the one that matters — the goods are nowhere. */
const statusLabel: Record<TransferStatus, string> = {
  Draft: 'Draft',
  InTransit: 'In transit',
  Received: 'Received',
  Cancelled: 'Cancelled',
};

/**
 * Stock transfers between stores (guide p.20–21).
 *
 * Stock leaves the source when the van does and arrives when someone opens the box. Between those
 * two moments it belongs to neither store, and this screen is where that gap is visible.
 */
export default function TransfersPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canTransfer = auth.can('inventory.transfer');

  const [status, setStatus] = useState<TransferStatus | ''>('');
  const [includeInbound, setIncludeInbound] = useState(true);
  const [rows, setRows] = useState<TransferRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);

  const { data: destinations = [] } = useQuery({
    queryKey: ['transfer-destinations', locationId],
    queryFn: () => mastersApi.transfers.destinations(locationId!),
    enabled: Boolean(locationId),
  });

  const load = useCallback(async () => {
    if (!locationId) return;
    setLoading(true);

    try {
      setRows(await mastersApi.transfers.browse(locationId, {
        status: status || undefined,
        includeInbound,
      }));
    } catch (error) {
      toast({ title: 'Could not load transfers', description: describeError(error), variant: 'destructive' });
    } finally {
      setLoading(false);
    }
  }, [locationId, status, includeInbound]);

  useEffect(() => {
    void load();
  }, [load]);

  const columns = useMemo<DataGridColumn<TransferRow>[]>(
    () => [
      { key: 'number', header: 'Transfer', width: 100, render: (r) => `TR-${r.transferNumber}` },
      { key: 'from', header: 'From', width: 160, render: (r) => r.fromLocationName },
      { key: 'to', header: 'To', width: 160, render: (r) => r.toLocationName },
      {
        key: 'status',
        header: 'Status',
        width: 110,
        // Shape as well as colour: in transit is the state that needs attention, and a colour-only
        // signal is invisible to a good fraction of the people using this.
        render: (r) => (
          <span className={r.status === 'InTransit' ? 'font-semibold' : undefined}>
            {r.status === 'InTransit' ? '→ ' : ''}
            {statusLabel[r.status]}
          </span>
        ),
      },
      { key: 'lines', header: 'Items', width: 70, numeric: true, render: (r) => r.lineCount },
      {
        key: 'value',
        header: 'Value',
        width: 100,
        numeric: true,
        render: (r) => formatCurrency(r.totalValue),
        sortValue: (r) => r.totalValue,
      },
      {
        key: 'shipped',
        header: 'Shipped',
        width: 150,
        render: (r) => (r.shippedAt ? new Date(r.shippedAt).toLocaleString() : '—'),
      },
    ],
    [],
  );

  const create = async (toLocationId: number) => {
    if (!locationId) return;

    try {
      const created = await mastersApi.transfers.create(locationId, toLocationId);
      setSelectedId(created.id);
      await load();
    } catch (error) {
      toast({ title: 'Could not start a transfer', description: describeError(error), variant: 'destructive' });
    }
  };

  return (
    <BrowseFormShell
      title="Stock transfers"
      toolbar={
        canTransfer && destinations.length > 0 ? (
          <select aria-label="New transfer to…"
            className={filterClass}
            value=""
            onChange={(event) => {
              if (event.target.value) void create(Number(event.target.value));
            }}
          >
            <option value="">New transfer to…</option>
            {destinations.map((destination) => (
              <option key={String(destination.id)} value={destination.id}>
                {destination.name} ({destination.code})
              </option>
            ))}
          </select>
        ) : destinations.length === 0 ? (
          <span className="text-label text-ink-muted">
            There is only one location, so there is nowhere to transfer to.
          </span>
        ) : null
      }
      filters={
        <>
          <select aria-label="All states"
            className={filterClass}
            value={status}
            onChange={(event) => setStatus(event.target.value as TransferStatus | '')}
          >
            <option value="">All states</option>
            {statuses.map((option) => (
              <option key={option} value={option}>
                {statusLabel[option]}
              </option>
            ))}
          </select>

          <label className="flex items-center gap-1.5">
            <input
              type="checkbox"
              checked={includeInbound}
              onChange={(event) => setIncludeInbound(event.target.checked)}
            />
            Include transfers coming here
          </label>
        </>
      }
      grid={
        <DataGrid
          gridId="transfers"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          onRowActivate={(row) => setSelectedId(row.id)}
          loading={loading}
          emptyMessage="No transfers match these filters."
        />
      }
      form={
        selectedId !== null && locationId ? (
          <TransferPanel
            key={String(selectedId)}
            transferId={selectedId}
            locationId={locationId}
            canTransfer={canTransfer}
            onClose={() => setSelectedId(null)}
            onChanged={() => void load()}
          />
        ) : null
      }
      status={
        <span className="flex items-center gap-3">
          <span>{rows.length} transfer(s)</span>
        </span>
      }
    />
  );
}

function TransferPanel({
  transferId,
  locationId,
  canTransfer,
  onClose,
  onChanged,
}: {
  transferId: number;
  locationId: number;
  canTransfer: boolean;
  onClose: () => void;
  onChanged: () => void;
}) {
  const confirmer = useConfirm();

  const askShip = () => {
    if (!transfer) return;

    confirmer.ask(
      {
        subject: `Transfer ${transfer.transferNumber}`,
        consequence:
          'The stock comes off the shelf here straight away and is in transit until the other '
          + 'branch receives it.',
        verb: 'Ship transfer',
      },
      () => run(() => mastersApi.transfers.ship(transfer.id), 'Shipped'),
    );
  };

  const askCancelTransfer = () => {
    if (!transfer) return;

    confirmer.ask(
      {
        subject: `Transfer ${transfer.transferNumber}`,
        consequence: 'The transfer is abandoned. Nothing moves.',
        verb: 'Cancel transfer',
      },
      () => run(() => mastersApi.transfers.cancel(transfer.id), 'Cancelled'),
    );
  };

  const [transfer, setTransfer] = useState<Transfer | null>(null);
  const [busy, setBusy] = useState(false);
  const [quantity, setQuantity] = useState(1);
  const [receiving, setReceiving] = useState<Record<string, number>>({});

  const refresh = useCallback(async () => {
    try {
      setTransfer(await mastersApi.transfers.get(transferId));
    } catch (error) {
      toast({ title: 'Could not open the transfer', description: describeError(error), variant: 'destructive' });
    }
  }, [transferId]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const run = async (action: () => Promise<Transfer>, success: string) => {
    setBusy(true);

    try {
      setTransfer(await action());
      onChanged();
      toast({ title: success });
    } catch (error) {
      toast({ title: 'Not done', description: describeError(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  if (!transfer) {
    return <p className="px-1 text-label text-ink-muted">Loading…</p>;
  }

  const isDraft = transfer.status === 'Draft';
  const isInTransit = transfer.status === 'InTransit';
  const isOutbound = transfer.fromLocationId === locationId;

  // The receiving store is the one that opens the box, so only it gets the receive controls.
  const canReceiveHere = canTransfer && isInTransit && transfer.toLocationId === locationId;

  const receiveAll = () => run(() => mastersApi.transfers.receive(transfer.id), 'Received');

  const receiveSome = () => {
    const lines = Object.entries(receiving)
      .filter(([, qty]) => qty > 0)
      // Object.entries always yields string keys, whatever was used to write them, so the line id
      // has to be turned back into a number here rather than assumed to have survived the round trip.
      .map(([lineId, quantity]) => ({ lineId: Number(lineId), quantity }));

    if (lines.length === 0) {
      toast({ title: 'Nothing entered', description: 'Type a quantity against at least one line.' });
      return;
    }

    return run(async () => {
      const updated = await mastersApi.transfers.receive(transfer.id, lines);
      setReceiving({});
      return updated;
    }, 'Received');
  };

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">
          TR-{transfer.transferNumber} — {transfer.fromLocationName} → {transfer.toLocationName}
        </h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Close
        </button>
      </div>

      <FormSection
        title="Status"
        hint={
          isInTransit
            ? 'The stock has left the sending store and is not yet on the receiving store’s shelf.'
            : undefined
        }
      >
        <p className="text-body">
          <span className="font-semibold">{statusLabel[transfer.status]}</span>
          {transfer.shippedAt ? ` · shipped ${new Date(transfer.shippedAt).toLocaleString()}` : ''}
          {transfer.receivedAt ? ` · received ${new Date(transfer.receivedAt).toLocaleString()}` : ''}
        </p>
        <p className="text-label text-ink-muted">
          {transfer.lines.length} item(s), {formatCurrency(transfer.totalValue)} at cost.
        </p>
      </FormSection>

      <FormSection
        title="Items"
        hint={isDraft ? 'Quantities can be changed until the transfer is shipped.' : undefined}
      >
        <table className="w-full text-body">
          <thead className="text-label">
            <tr>
              <th className="py-1 text-left">Code</th>
              <th className="py-1 text-left">Description</th>
              <th className="py-1 text-right">Sending</th>
              <th className="py-1 text-right">{isInTransit || transfer.status === 'Received' ? 'Received' : 'On hand'}</th>
              {canReceiveHere ? <th className="py-1 text-right">Receive</th> : null}
              {isDraft && canTransfer ? <th /> : null}
            </tr>
          </thead>
          <tbody>
            {transfer.lines.map((line) => (
              <tr key={String(line.id)} className="border-t border-subtle">
                <td className="py-1">{line.stockCode}</td>
                <td className="py-1">{line.productName}</td>
                <td className="py-1 text-right">{line.quantity}</td>
                <td className="py-1 text-right">
                  {isInTransit || transfer.status === 'Received'
                    ? `${line.quantityReceived}${line.outstanding > 0 ? ` (${line.outstanding} to come)` : ''}`
                    : line.sourceOnHand}
                </td>
                {canReceiveHere ? (
                  <td className="py-1 text-right">
                    <input
                      type="number"
                      min={0}
                      max={line.outstanding}
                      aria-label={`Receive ${line.stockCode}`}
                      className={`${filterClass} w-20 text-right`}
                      value={receiving[line.id] ?? ''}
                      onChange={(event) =>
                        setReceiving((current) => ({ ...current, [line.id]: Number(event.target.value) || 0 }))
                      }
                    />
                  </td>
                ) : null}
                {isDraft && canTransfer ? (
                  <td className="py-1 text-right">
                    <button
                      type="button"
                      className="text-label underline"
                      disabled={busy}
                      onClick={() => void run(() => mastersApi.transfers.removeLine(transfer.id, line.id), 'Removed')}
                    >
                      Remove
                    </button>
                  </td>
                ) : null}
              </tr>
            ))}
          </tbody>
        </table>

        {transfer.lines.length === 0 ? (
          <p className="text-label text-ink-muted">Nothing on this transfer yet.</p>
        ) : null}

        {isDraft && canTransfer && isOutbound ? (
          <>
            <NumberField label="Quantity" value={quantity} step="1" onChange={setQuantity} />
            <RecordPicker
              label="Add an item"
              value={null}
              aria-label="Search" placeholder="Code or description"
              search={(term) =>
                mastersApi.products
                  .browse(locationId, { search: term, pageSize: 15 })
                  .then((page) => page.items.map((item) => ({ id: item.id, code: item.stockCode, name: item.name })))
              }
              onChange={(picked) => {
                if (!picked) return;
                void run(
                  () => mastersApi.transfers.upsertLine(transfer.id, picked.id, Math.max(1, quantity)),
                  'Added',
                );
              }}
            />
          </>
        ) : null}
      </FormSection>

      <div className="mb-6 flex flex-wrap gap-2">
        {canTransfer && isDraft && isOutbound ? (
          <button
            type="button"
            className="pos-button-primary"
            disabled={busy || transfer.lines.length === 0}
            onClick={askShip}
          >
            Ship — take the stock off the shelf
          </button>
        ) : null}

        {canReceiveHere ? (
          <>
            <button type="button" className="pos-button-primary" disabled={busy} onClick={() => void receiveAll()}>
              Receive everything
            </button>
            <button type="button" className="pos-button" disabled={busy} onClick={() => void receiveSome()}>
              Receive what was typed
            </button>
          </>
        ) : null}

        {canTransfer && isDraft ? (
          <button
            type="button"
            className="pos-button text-negative"
            disabled={busy}
            onClick={askCancelTransfer}
          >
            Cancel
          </button>
        ) : null}
      </div>

      {isInTransit && !canReceiveHere ? (
        <p className="px-1 text-label text-ink-muted">
          {transfer.toLocationName} books this in when the box arrives there.
        </p>
      ) : null}

      <ConfirmDialog
        request={confirmer.request}
        open={confirmer.open}
        onOpenChange={confirmer.setOpen}
        onConfirm={confirmer.confirm}
        busy={confirmer.busy}
      />
    </div>
  );
}
