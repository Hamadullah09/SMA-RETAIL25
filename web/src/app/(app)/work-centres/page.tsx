'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, Save, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { LocationSelect, StoreField, useWorkingStore } from '@/components/production';
import { Button } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, TextArea } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar } from '@/components/ui/page';
import { EmptyState, FormError } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { ApiError } from '@/lib/api';
import { formatCount, formatMoney, formatQuantity } from '@/lib/format';
import { numberText, parseNumber, textOrNull } from '@/lib/forms';
import { P } from '@/lib/nav';
import { productionApi } from '@/lib/production-api';
import type { WorkCentre } from '@/lib/production-types';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';

/** The work centres of the store: where work is done, what an hour costs there, and how many hours a day it can work. */
export default function WorkCentresPage() {
  const { can } = useSession();
  const { storeId, currencySymbol: symbol } = useStore();
  const [editing, setEditing] = useState<WorkCentre | 'new' | null>(null);
  const query = useQuery({ queryKey: ['work-centres', storeId], queryFn: () => productionApi.workCentres({ storeId }) });
  const edit = can(P.productionPlan);
  const money = (n: number) => formatMoney(n, symbol);
  return (
    <div>
      <PageHeader title="Work centres" aside={query.data && `${formatCount(query.data.length)} work centres`} />
      <Toolbar>
        {edit && (
          <Button variant="sun" icon={Plus} shortcut="Ctrl+N" onClick={() => setEditing('new')}>
            New work centre
          </Button>
        )}
        <span className="text-ink">Machines, lines or teams. Their hourly rates cost the routings’ time.</span>
      </Toolbar>
      <DataTable<WorkCentre>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(w) => w.id}
        caption="Work centres"
        onRowClick={edit ? (w) => setEditing(w) : undefined}
        empty={<EmptyState title="No work centres yet" />}
        columns={[
          { key: 'code', header: 'Code', cell: (w) => <span className="font-semibold">{w.code}</span> },
          { key: 'name', header: 'Name', cell: (w) => w.name },
          { key: 'at', header: 'Location', cell: (w) => `${w.storeCode} · ${w.locationCode}`, hideBelow: 'md' },
          { key: 'lab', header: 'Labour an hour', numeric: true, cell: (w) => money(w.labourCostPerHour) },
          { key: 'mac', header: 'Machine an hour', numeric: true, cell: (w) => money(w.machineCostPerHour), hideBelow: 'sm' },
          { key: 'ovh', header: 'Overhead an hour', numeric: true, cell: (w) => money(w.overheadPerHour), hideBelow: 'lg' },
          { key: 'cap', header: 'Hours a week', numeric: true, cell: (w) => formatQuantity(w.capacityHoursPerDay * w.daysPerWeek), hideBelow: 'sm' },
          { key: 'state', header: '', cell: (w) => (w.isActive ? '' : <Chip>Inactive</Chip>) },
        ]}
      />
      {editing && <CentreDialog centre={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function CentreDialog({ centre, onClose }: { centre: WorkCentre | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const working = useWorkingStore();
  const [store, setStore] = useState<string | null>(centre?.storeId ?? working);
  const [location, setLocation] = useState(centre?.locationId ?? '');
  const [code, setCode] = useState(centre?.code ?? '');
  const [name, setName] = useState(centre?.name ?? '');
  const [labour, setLabour] = useState(numberText(centre?.labourCostPerHour ?? 0));
  const [machine, setMachine] = useState(numberText(centre?.machineCostPerHour ?? 0));
  const [overhead, setOverhead] = useState(numberText(centre?.overheadPerHour ?? 0));
  const [hours, setHours] = useState(numberText(centre?.capacityHoursPerDay ?? 8));
  const [days, setDays] = useState(numberText(centre?.daysPerWeek ?? 6));
  const [active, setActive] = useState(centre?.isActive ?? true);
  const [notes, setNotes] = useState(centre?.notes ?? '');
  const [problem, setProblem] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);
  const done = () => {
    void queryClient.invalidateQueries({ queryKey: ['work-centres'] });
    onClose();
  };
  const save = useMutation({
    mutationFn: () => {
      const body = {
        version: centre?.version ?? null, locationId: location, code: code.trim().toUpperCase(), name: name.trim(), labourCostPerHour: parseNumber(labour) ?? 0,
        machineCostPerHour: parseNumber(machine) ?? 0, overheadPerHour: parseNumber(overhead) ?? 0, capacityHoursPerDay: parseNumber(hours) ?? 0,
        daysPerWeek: parseNumber(days) ?? 0, isActive: active, notes: textOrNull(notes),
      };
      return centre ? productionApi.saveWorkCentre(centre.id, body) : productionApi.addWorkCentre(body);
    },
    onSuccess: (w) => {
      toast.ok(`${w.code} ${w.name} is saved.`);
      done();
    },
  });
  const remove = useMutation({
    mutationFn: () => productionApi.deleteWorkCentre(centre!.id),
    onSuccess: () => {
      toast.ok('The work centre is deleted.');
      done();
    },
    onError: (e: Error) => {
      setDeleting(false);
      toast.error(e instanceof ApiError ? e.message : 'It was not deleted.');
    },
  });
  function submit() {
    const text = !location ? 'Choose the location it is at.' : !code.trim() ? 'Enter a short code.' : !name.trim() ? 'Enter its name.' : null;
    setProblem(text);
    if (!text) save.mutate();
  }
  return (
    <Dialog
      open
      title={centre ? `Change ${centre.code}` : 'New work centre'}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          {centre && (
            <Button icon={Trash2} onClick={() => setDeleting(true)} className="mr-auto">
              Delete
            </Button>
          )}
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
        {!centre && <StoreField id="centre-store" value={store} onChange={setStore} />}
        <LocationSelect id="centre-location" label="Location" storeId={store} value={location} onChange={setLocation} />
        <Field label="Code" htmlFor="centre-code">
          <Input id="centre-code" value={code} maxLength={10} className="max-w-[160px] uppercase" onChange={(e) => setCode(e.target.value)} />
        </Field>
        <Field label="Name" htmlFor="centre-name">
          <Input id="centre-name" value={name} maxLength={50} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="Labour an hour" htmlFor="centre-labour">
          <Input id="centre-labour" inputMode="decimal" value={labour} className="max-w-[160px] text-right" onChange={(e) => setLabour(e.target.value)} />
        </Field>
        <Field label="Machine an hour" htmlFor="centre-machine">
          <Input id="centre-machine" inputMode="decimal" value={machine} className="max-w-[160px] text-right" onChange={(e) => setMachine(e.target.value)} />
        </Field>
        <Field label="Overhead an hour" htmlFor="centre-overhead" hint="Rent, supervision and the like, absorbed for each hour worked here.">
          <Input id="centre-overhead" inputMode="decimal" value={overhead} className="max-w-[160px] text-right" onChange={(e) => setOverhead(e.target.value)} />
        </Field>
        <Field label="Hours a day" htmlFor="centre-hours">
          <Input id="centre-hours" inputMode="decimal" value={hours} className="max-w-[120px] text-right" onChange={(e) => setHours(e.target.value)} />
        </Field>
        <Field label="Days a week" htmlFor="centre-days">
          <Input id="centre-days" inputMode="numeric" value={days} className="max-w-[120px] text-right" onChange={(e) => setDays(e.target.value)} />
        </Field>
        <Checkbox label="Active" checked={active} onChange={setActive} />
        <Field label="Notes" htmlFor="centre-notes">
          <TextArea id="centre-notes" value={notes} maxLength={400} onChange={(e) => setNotes(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? save.error} />
      <ConfirmDialog open={deleting} title={`Delete ${centre?.code}?`} message="A work centre a routing uses cannot be deleted; make it inactive instead." danger busy={remove.isPending} onConfirm={() => remove.mutate()} onClose={() => setDeleting(false)} />
    </Dialog>
  );
}
