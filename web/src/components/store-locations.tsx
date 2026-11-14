'use client';

/**
 * A store's locations, on the Stores page: where it keeps stock, and which one holds the goods that failed an inspection.
 * A location's code is fixed once made (a till finds the location it sells from by its code); its name can change.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MapPin, Pencil, Save } from 'lucide-react';
import { useState } from 'react';
import { api, ApiError } from '@/lib/api';
import { formatCount } from '@/lib/format';
import { locationErrors, type LocationForm } from '@/lib/locations';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import type { LocationInfo, StoreInfo } from '@/lib/types';
import { ConflictDialog } from './masters';
import { Button } from './ui/button';
import { Dialog } from './ui/dialog';
import { Checkbox, Field, Input } from './ui/field';
import { Chip } from './ui/page';
import { FormError } from './ui/states';
import { DataTable } from './ui/table';
import { useToast } from './ui/toast';

export function StoreLocations({ store }: { store: StoreInfo }) {
  const { can } = useSession();
  const manage = can(P.storesManage);
  const [editing, setEditing] = useState<LocationInfo | 'new' | null>(null);
  const locations = useQuery({ queryKey: ['locations', store.id], queryFn: () => api.locations(store.id) });
  const hold = locations.data?.find((l) => l.isHold);

  return (
    <div className="border-t border-line" aria-label={`Locations of ${store.name}`} role="region">
      <div className="flex flex-wrap items-center justify-between gap-3 px-5 pb-2 pt-4">
        <div>
          <h3 className="section-title my-0">Locations</h3>
          <p className="text-muted">
            Where the store keeps stock.{' '}
            {hold ? `Goods that fail an inspection go to ${hold.code} unless another location is chosen.` : 'Mark one as the hold location for goods that fail an inspection.'}
          </p>
        </div>
        {manage && (
          <Button icon={MapPin} onClick={() => setEditing('new')}>
            Add a location
          </Button>
        )}
      </div>
      <DataTable<LocationInfo>
        className="rounded-t-none shadow-none"
        rows={locations.data}
        loading={locations.isLoading}
        error={locations.error}
        onRetry={() => void locations.refetch()}
        rowKey={(l) => l.id}
        caption={`Locations of ${store.name}`}
        empty={<p className="px-5 pb-5 text-muted">No locations yet.</p>}
        columns={[
          { key: 'code', header: 'Code', cell: (l) => <span className="font-semibold">{l.code}</span> },
          { key: 'name', header: 'Name', cell: (l) => l.name ?? '' },
          { key: 'items', header: 'Items', numeric: true, cell: (l) => formatCount(l.items), hideBelow: 'sm' },
          { key: 'hold', header: 'Use', cell: (l) => (l.isHold ? <Chip tone="warn">Hold location</Chip> : null) },
          ...(manage
            ? [
                {
                  key: 'edit',
                  header: '',
                  cell: (l: LocationInfo) => (
                    <Button icon={Pencil} className="min-h-[40px] px-4" onClick={() => setEditing(l)} aria-label={`Change location ${l.code}`}>
                      Change
                    </Button>
                  ),
                  className: 'text-right',
                },
              ]
            : []),
        ]}
      />
      {editing && (
        <LocationDialog store={store} list={locations.data ?? []} location={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />
      )}
    </div>
  );
}

function LocationDialog({ store, list, location, onClose }: { store: StoreInfo; list: LocationInfo[]; location: LocationInfo | null; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [form, setForm] = useState<LocationForm>({ code: location?.code ?? '', name: location?.name ?? '', isHold: location?.isHold ?? false });
  const [errors, setErrors] = useState<Partial<Record<keyof LocationForm, string>>>({});
  const [conflict, setConflict] = useState(false);
  const only = !!location && list.length < 2;
  const otherHold = list.find((l) => l.isHold && l.id !== location?.id);

  const save = useMutation({
    mutationFn: () => {
      const body = { version: location?.version ?? null, code: location ? location.code : form.code.trim().toUpperCase(), name: form.name.trim() || null, isHold: form.isHold };
      return location ? api.saveLocation(location.id, body) : api.addLocation(store.id, body);
    },
    onSuccess: (saved) => {
      toast.ok(saved.isHold ? `${saved.code} is saved as the hold location of ${store.name}.` : `Location ${saved.code} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['locations', store.id] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });

  function set<K extends keyof LocationForm>(key: K, value: LocationForm[K]) {
    setForm((f) => ({ ...f, [key]: value }));
    setErrors((e) => ({ ...e, [key]: undefined }));
  }

  return (
    <Dialog
      open
      title={location ? `Change location ${location.code}` : `Add a location to ${store.name}`}
      onClose={onClose}
      onSubmit={() => {
        const found = locationErrors(form, list, location);
        setErrors(found);
        if (Object.values(found).every((v) => !v)) save.mutate();
      }}
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
        <Field
          label="Code"
          htmlFor="location-code"
          error={errors.code}
          hint={location ? 'The code stays: the tills find the location they sell from by it.' : '1 to 3 characters, on reports and transfers: BAK, HLD.'}
        >
          <Input
            id="location-code"
            value={form.code}
            maxLength={3}
            disabled={!!location}
            className="max-w-[120px] uppercase"
            invalid={!!errors.code}
            onChange={(e) => set('code', e.target.value)}
            autoFocus={!location}
          />
        </Field>
        <Field label="Name" htmlFor="location-name" error={errors.name}>
          <Input id="location-name" value={form.name} maxLength={50} invalid={!!errors.name} onChange={(e) => set('name', e.target.value)} autoFocus={!!location} />
        </Field>
        <Field label="Quality" htmlFor="location-hold" error={errors.isHold}>
          <Checkbox
            label="Hold location for goods that fail an inspection"
            hint={
              only
                ? 'The store’s only location cannot be its hold location: add another one first.'
                : otherHold && form.isHold
                  ? `${otherHold.code} stops being the hold location: a store has one.`
                  : 'Failed output goes here unless the inspection names another location.'
            }
            checked={form.isHold}
            disabled={only && !form.isHold}
            onChange={(v) => set('isHold', v)}
          />
        </Field>
      </div>
      <FormError error={save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null} />
      <ConflictDialog
        open={conflict}
        what="location"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['locations', store.id] });
          onClose();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}
