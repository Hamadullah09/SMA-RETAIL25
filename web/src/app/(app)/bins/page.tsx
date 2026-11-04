'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ClipboardList, ListChecks, MoveRight, Pencil, Plus, Save, Trash2, Truck } from 'lucide-react';
import { useState } from 'react';
import { ConflictDialog } from '@/components/masters';
import { BinListDialog, LocationSelect } from '@/components/supply';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { formatCount } from '@/lib/format';
import { textOrNull } from '@/lib/forms';
import { useDebounced } from '@/lib/hooks';
import { useStore } from '@/lib/store';
import type { BinRow, ItemRow } from '@/lib/types';

type ListKind = { kind: 'count'; zone: string | null; bin: string | null } | { kind: 'orders' } | { kind: 'transfer'; id: string };

export default function BinsPage() {
  const { defaultStoreId } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [locationId, setLocationId] = useState<string | null>(null);
  const [selected, setSelected] = useState<BinRow | null>(null);
  const [editing, setEditing] = useState<BinRow | 'new' | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [assigning, setAssigning] = useState(false);
  const [list, setList] = useState<ListKind | null>(null);
  const [choosingTransfer, setChoosingTransfer] = useState(false);

  const query = useQuery({ queryKey: ['bins', locationId], queryFn: () => api.bins({ locationId }), enabled: !!locationId });
  const remove = useMutation({
    mutationFn: (bin: BinRow) => api.deleteBin(bin.id),
    onSuccess: () => {
      toast.ok(`Bin ${selected?.code ?? ''} is deleted.`);
      setDeleting(false);
      setSelected(null);
      void queryClient.invalidateQueries({ queryKey: ['bins'] });
    },
    onError: (e: Error) => {
      setDeleting(false);
      toast.error(e.message);
    },
  });
  const rows = query.data;
  const zones = [...new Set((rows ?? []).map((b) => b.zone).filter((z): z is string => !!z))];

  return (
    <div>
      <PageHeader title="Bins" aside={rows && `${formatCount(rows.length)} bins`} />
      <Toolbar>
        <LocationSelect storeId={defaultStoreId} value={locationId} onChange={setLocationId} />
        <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')} disabled={!locationId}>
          New bin
        </Button>
        <Button icon={Pencil} shortcut="Ctrl+O" disabled={!selected} onClick={() => selected && setEditing(selected)}>
          Edit
        </Button>
        <Button icon={Trash2} shortcut="Del" disabled={!selected} onClick={() => setDeleting(true)}>
          Delete
        </Button>
        <Button icon={MoveRight} disabled={!locationId} onClick={() => setAssigning(true)}>
          Put items in a bin
        </Button>
      </Toolbar>
      <Toolbar>
        <Button icon={ListChecks} disabled={!locationId} onClick={() => setList({ kind: 'count', zone: null, bin: selected?.code ?? null })}>
          {selected ? `Count sheet of ${selected.code}` : 'Count sheet'}
        </Button>
        {zones.length > 0 && (
          <>
            <label className="sr-only" htmlFor="zone-sheet">
              Count sheet of a zone
            </label>
            <Select id="zone-sheet" className="w-[220px]" value="" onChange={(e) => e.target.value && setList({ kind: 'count', zone: e.target.value, bin: null })}>
              <option value="">Count sheet of a zone…</option>
              {zones.map((z) => (
                <option key={z} value={z}>
                  {z}
                </option>
              ))}
            </Select>
          </>
        )}
        <Button icon={ClipboardList} disabled={!locationId} onClick={() => setList({ kind: 'orders' })}>
          Pick list: customer orders
        </Button>
        <Button icon={Truck} disabled={!defaultStoreId} onClick={() => setChoosingTransfer(true)}>
          Pick list: transfer
        </Button>
      </Toolbar>
      <DataTable<BinRow>
        rows={locationId ? rows : []}
        loading={!!locationId && query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(b) => b.id}
        selectedKey={selected?.id}
        onRowClick={(b) => (selected?.id === b.id ? setEditing(b) : setSelected(b))}
        caption="Bins"
        empty={<EmptyState title="No bins at this location">Add the shelves and bays goods are kept in; each item then names its bin.</EmptyState>}
        columns={[
          { key: 'code', header: 'Bin', cell: (b) => <span className="font-semibold">{b.code}</span> },
          { key: 'zone', header: 'Zone', cell: (b) => b.zone ?? '' },
          { key: 'desc', header: 'Description', cell: (b) => b.description ?? '', className: 'wrap', hideBelow: 'md' },
          { key: 'items', header: 'Items', numeric: true, cell: (b) => formatCount(b.items) },
          { key: 'active', header: 'Status', cell: (b) => (b.isActive ? <Chip tone="ok">In use</Chip> : <Chip>Inactive</Chip>), hideBelow: 'sm' },
        ]}
      />
      {editing && locationId && <BinDialog bin={editing === 'new' ? null : editing} locationId={locationId} onClose={() => setEditing(null)} />}
      {assigning && locationId && <AssignDialog bins={(rows ?? []).filter((b) => b.isActive)} onClose={() => setAssigning(false)} />}
      {choosingTransfer && (
        <TransferChooser
          onClose={() => setChoosingTransfer(false)}
          onChoose={(id) => {
            setChoosingTransfer(false);
            setList({ kind: 'transfer', id });
          }}
        />
      )}
      {list && locationId && <ListDialog list={list} locationId={locationId} onClose={() => setList(null)} />}
      <ConfirmDialog
        open={deleting && !!selected}
        title={`Delete bin ${selected?.code ?? ''}?`}
        message="A bin that items are kept in cannot be deleted: move them first, or make the bin inactive."
        confirmLabel="Delete"
        cancelLabel="Cancel"
        danger
        busy={remove.isPending}
        onConfirm={() => selected && remove.mutate(selected)}
        onClose={() => setDeleting(false)}
      />
    </div>
  );
}

function ListDialog({ list, locationId, onClose }: { list: ListKind; locationId: string; onClose(): void }) {
  const query = useQuery({
    queryKey: ['bin-list', list, locationId],
    queryFn: () =>
      list.kind === 'count'
        ? api.countSheet({ locationId, zone: list.zone, bin: list.bin })
        : list.kind === 'orders'
          ? api.pickOrders({ locationId })
          : api.pickTransfer(list.id),
    staleTime: 0,
  });
  const title = list.kind === 'count' ? `Count sheet${list.bin ? ` of bin ${list.bin}` : list.zone ? ` of ${list.zone}` : ''}` : undefined;
  return <BinListDialog list={query.data} loading={query.isLoading} error={query.error} title={title} quantityHeader={list.kind === 'count' ? 'Counted' : 'Pick'} onClose={onClose} />;
}

function BinDialog({ bin, locationId, onClose }: { bin: BinRow | null; locationId: string; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [code, setCode] = useState(bin?.code ?? '');
  const [zone, setZone] = useState(bin?.zone ?? '');
  const [description, setDescription] = useState(bin?.description ?? '');
  const [active, setActive] = useState(bin?.isActive ?? true);
  const [error, setError] = useState<unknown>(null);
  const [conflict, setConflict] = useState(false);
  const save = useMutation({
    mutationFn: () => {
      const body = { version: bin?.version ?? null, locationId, code: code.trim(), zone: textOrNull(zone), description: textOrNull(description), isActive: active };
      return bin ? api.saveBin(bin.id, body) : api.addBin(body);
    },
    onSuccess: (saved) => {
      toast.ok(`Bin ${saved.code} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['bins'] });
      onClose();
    },
    onError: (e) => (e instanceof ApiError && e.isConflict ? setConflict(true) : setError(e)),
  });
  return (
    <Dialog
      open
      title={bin ? `Bin ${bin.code}` : 'New bin'}
      onClose={onClose}
      onSubmit={() => (code.trim() ? save.mutate() : setError(new Error('Enter the bin’s code.')))}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Save
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Code" htmlFor="bin-code" hint="As written on the shelf, e.g. A-01-03. Renaming a bin moves its items with it.">
          <Input id="bin-code" maxLength={25} value={code} onChange={(e) => setCode(e.target.value)} />
        </Field>
        <Field label="Zone" htmlFor="bin-zone" hint="The area it is in: back room, cold room, aisle 3.">
          <Input id="bin-zone" maxLength={30} value={zone} onChange={(e) => setZone(e.target.value)} />
        </Field>
        <Field label="Description" htmlFor="bin-description">
          <Input id="bin-description" maxLength={100} value={description} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <Checkbox label="In use" checked={active} onChange={setActive} />
      </div>
      <FormError error={error} />
      <ConflictDialog open={conflict} what="bin" onReload={onClose} onClose={() => setConflict(false)} />
    </Dialog>
  );
}

/** Makes a bin the default bin of the items chosen. */
function AssignDialog({ bins, onClose }: { bins: BinRow[]; onClose(): void }) {
  const { storeId } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [binCode, setBinCode] = useState(bins[0]?.code ?? '');
  const [text, setText] = useState('');
  const [chosen, setChosen] = useState<Map<string, ItemRow>>(new Map());
  const search = useDebounced(text.trim());
  const items = useQuery({ queryKey: ['items', 'bin-picker', storeId, search], queryFn: () => api.items({ storeId, search, pageSize: 20 }), enabled: search.length > 0 });
  const assign = useMutation({
    mutationFn: () => api.assignBin([...chosen.keys()], binCode || null),
    onSuccess: (result) => {
      toast.ok(`${result.changed} items are now kept in ${binCode || 'no bin'}.`);
      void queryClient.invalidateQueries({ queryKey: ['bins'] });
      onClose();
    },
  });
  return (
    <Dialog
      open
      width="lg"
      title="Put items in a bin"
      onClose={onClose}
      onSubmit={() => chosen.size > 0 && assign.mutate()}
      footer={
        <>
          <Button type="submit" variant="sun" icon={MoveRight} shortcut="Enter" busy={assign.isPending} disabled={chosen.size === 0}>
            Put {chosen.size} items in {binCode || 'no bin'}
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Bin" htmlFor="assign-bin">
          <Select id="assign-bin" className="max-w-[260px]" value={binCode} onChange={(e) => setBinCode(e.target.value)}>
            {bins.map((b) => (
              <option key={b.id} value={b.code}>
                {b.code}
                {b.zone ? ` · ${b.zone}` : ''}
              </option>
            ))}
            <option value="">No bin</option>
          </Select>
        </Field>
        <Field label="Items" htmlFor="assign-search" hint="Find items by stock code or description and tick them.">
          <Input id="assign-search" value={text} onChange={(e) => setText(e.target.value)} placeholder="Stock code or description" />
        </Field>
        <ul className="max-h-[40vh] space-y-1 overflow-y-auto" aria-label="Items to put in the bin">
          {[...new Map([...chosen, ...(items.data?.items ?? []).map((i) => [i.id, i] as const)]).values()].map((i) => (
            <li key={i.id}>
              <Checkbox
                label={`${i.plu} · ${i.name}${i.storeCode ? ` · ${i.storeCode}` : ''}`}
                checked={chosen.has(i.id)}
                onChange={(on) =>
                  setChosen((m) => {
                    const next = new Map(m);
                    if (on) next.set(i.id, i);
                    else next.delete(i.id);
                    return next;
                  })
                }
              />
            </li>
          ))}
        </ul>
      </div>
      <FormError error={assign.error} />
    </Dialog>
  );
}

function TransferChooser({ onClose, onChoose }: { onClose(): void; onChoose(id: string): void }) {
  const { storeId } = useStore();
  const query = useQuery({ queryKey: ['transfers', 'to-pick', storeId], queryFn: () => api.transfers({ storeId, status: 'Draft', pageSize: 50 }) });
  return (
    <Dialog
      open
      width="sm"
      title="Pick list of a transfer"
      onClose={onClose}
      footer={
        <Button onClick={onClose} shortcut="Esc">
          Cancel
        </Button>
      }
    >
      <DataTable
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        rowKey={(t) => t.id}
        onRowClick={(t) => onChoose(t.id)}
        caption="Transfers being made"
        empty={<EmptyState title="No transfers being made">Transfers are picked while they are drafts, before they are shipped.</EmptyState>}
        columns={[
          { key: 'n', header: 'Transfer', cell: (t) => <span className="font-semibold">{t.number}</span> },
          { key: 'to', header: 'To', cell: (t) => `${t.toStore} · ${t.toLocation}` },
          { key: 'lines', header: 'Lines', numeric: true, cell: (t) => formatCount(t.lines) },
        ]}
      />
    </Dialog>
  );
}
