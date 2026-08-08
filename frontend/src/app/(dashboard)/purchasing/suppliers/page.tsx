'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { BrowseFormShell, FormSection, LiveBadge, TextField } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { useLiveGrid } from '@/lib/inventory-hub';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import type { Address, ContactDetails, SupplierForm, SupplierRow, SupplierSort } from '@/types/masters';

/**
 * The id a form holds while it is creating rather than editing.
 *
 * Zero, because that is what the domain means by it too: an entity that has not been saved has no
 * id yet, and no row can ever be 0 — the sequence starts at 1. A string sentinel would have to be
 * kept out of every type that says this is a record key.
 */
const NEW_RECORD = 0;

/** Supplier Browse + Form View (guide p.59–62). */
export default function SuppliersPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canWrite = auth.can('purchasing.write');

  const [search, setSearch] = useState('');
  const [sort, setSort] = useState<SupplierSort>('Company');
  const [rows, setRows] = useState<SupplierRow[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);

  const load = useCallback(
    async (append: boolean, from: string | null) => {
      if (!locationId) return;

      setLoading(true);

      try {
        const page = await mastersApi.suppliers.browse(locationId, {
          search,
          sort,
          cursor: from ?? undefined,
          pageSize: 100,
        });

        setRows((current) => (append ? [...current, ...page.items] : page.items));
        setCursor(page.nextCursor);
        setHasMore(page.hasMore);
      } catch (error) {
        toast({ title: 'Could not load suppliers', description: describe(error), variant: 'destructive' });
      } finally {
        setLoading(false);
      }
    },
    [locationId, search, sort],
  );

  useEffect(() => {
    const timer = window.setTimeout(() => void load(false, null), 200);
    return () => window.clearTimeout(timer);
  }, [load]);

  const { connected, changed } = useLiveGrid('supplier', locationId, setRows);

  const columns = useMemo<DataGridColumn<SupplierRow>[]>(
    () => [
      { key: 'number', header: 'No.', width: 90, render: (r) => r.supplierNumber, sortValue: (r) => r.supplierNumber },
      { key: 'company', header: 'Company', width: 260, render: (r) => r.company, sortValue: (r) => r.company },
      { key: 'contact', header: 'Contact', width: 180, render: (r) => r.contactName ?? '—' },
      { key: 'city', header: 'City', width: 140, render: (r) => r.city ?? '—' },
      { key: 'phone', header: 'Phone', width: 130, render: (r) => r.phone ?? '—' },
      { key: 'email', header: 'Email', width: 200, render: (r) => r.email ?? '—' },
      {
        key: 'items',
        header: 'Items',
        width: 70,
        numeric: true,
        render: (r) => r.suppliedItemCount,
        sortValue: (r) => r.suppliedItemCount,
      },
    ],
    [],
  );

  return (
    <BrowseFormShell
      title="Suppliers"
      toolbar={
        <>
          <LiveBadge connected={connected} />
          {canWrite ? (
            <button type="button" className="pos-button-primary" onClick={() => setSelectedId(NEW_RECORD)}>
              New supplier
            </button>
          ) : null}
        </>
      }
      filters={
        <>
          <input
            className="pos-input w-64"
            placeholder="Company, number, contact or phone"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />

          <select
            className="pos-input"
            value={sort}
            onChange={(event) => setSort(event.target.value as SupplierSort)}
          >
            <option value="Company">Sort: company</option>
            <option value="Number">Sort: number</option>
          </select>
        </>
      }
      grid={
        <DataGrid
          gridId="suppliers"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          recentlyChanged={changed}
          onRowActivate={(row) => setSelectedId(row.id)}
          emptyMessage={loading ? 'Loading…' : 'No suppliers match these filters.'}
        />
      }
      form={
        selectedId !== null && locationId ? (
          <SupplierFormPanel
            key={String(selectedId)}
            supplierId={selectedId === NEW_RECORD ? null : selectedId}
            locationId={locationId}
            canWrite={canWrite}
            onClose={() => setSelectedId(null)}
            onSaved={() => void load(false, null)}
          />
        ) : null
      }
      status={
        <span className="flex items-center gap-3">
          <span>
            {rows.length} loaded{hasMore ? ' of more' : ''}
          </span>
          {hasMore ? (
            <button type="button" className="underline" onClick={() => void load(true, cursor)} disabled={loading}>
              Load more
            </button>
          ) : null}
        </span>
      }
    />
  );
}

function describe(error: unknown): string {
  return error instanceof PosApiError ? error.problem.detail : 'Something went wrong.';
}

const emptySupplier: SupplierForm = {
  id: 0,
  locationId: 0,
  supplierNumber: '',
  company: '',
  contactFirstName: null,
  contactLastName: null,
  title: null,
  address: {},
  contact: {},
  suppliedItemCount: 0,
  isDeleted: false,
  createdAt: '',
  modifiedAt: null,
};

function SupplierFormPanel({
  supplierId,
  locationId,
  canWrite,
  onClose,
  onSaved,
}: {
  supplierId: number | null;
  locationId: number;
  canWrite: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState<SupplierForm>(emptySupplier);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!supplierId) {
      setForm({ ...emptySupplier, locationId });
      return;
    }

    void mastersApi.suppliers
      .get(supplierId)
      .then(setForm)
      .catch((error) =>
        toast({ title: 'Could not open the supplier', description: describe(error), variant: 'destructive' }),
      );
  }, [supplierId, locationId]);

  const patch = (changes: Partial<SupplierForm>) => setForm((current) => ({ ...current, ...changes }));
  const address = (changes: Partial<Address>) => patch({ address: { ...form.address, ...changes } });
  const contact = (changes: Partial<ContactDetails>) => patch({ contact: { ...form.contact, ...changes } });

  const details = () => ({
    company: form.company,
    contactFirstName: form.contactFirstName,
    contactLastName: form.contactLastName,
    title: form.title,
    address: form.address,
    contact: form.contact,
  });

  const save = async () => {
    setBusy(true);

    try {
      const saved = supplierId
        ? await mastersApi.suppliers.update(supplierId, details())
        : await mastersApi.suppliers.create({ locationId, details: details() });

      setForm(saved);
      onSaved();
      toast({ title: supplierId ? 'Saved' : 'Supplier created' });
    } catch (error) {
      toast({ title: 'Not saved', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!supplierId) return;

    setBusy(true);

    try {
      await mastersApi.suppliers.remove(supplierId);
      toast({ title: 'Deleted', description: 'It can be brought back from Undelete items.' });
      onSaved();
      onClose();
    } catch (error) {
      toast({ title: 'Not deleted', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const disabled = !canWrite || busy;

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">{supplierId ? form.company : 'New supplier'}</h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Close
        </button>
      </div>

      <FormSection
        title="Supplier"
        hint={supplierId ? undefined : 'Leaving the number blank draws the next one from the store’s counter.'}
        actions={
          canWrite ? (
            <button type="button" className="underline" disabled={busy} onClick={() => void save()}>
              Save
            </button>
          ) : null
        }
      >
        <TextField label="Company" value={form.company} onChange={(v) => patch({ company: v })} disabled={disabled} autoFocus />
        <TextField
          label="Supplier number"
          value={form.supplierNumber}
          onChange={(v) => patch({ supplierNumber: v })}
          disabled={disabled || Boolean(supplierId)}
        />
        <TextField
          label="Contact first name"
          value={form.contactFirstName ?? ''}
          onChange={(v) => patch({ contactFirstName: v })}
          disabled={disabled}
        />
        <TextField
          label="Contact last name"
          value={form.contactLastName ?? ''}
          onChange={(v) => patch({ contactLastName: v })}
          disabled={disabled}
        />
        <TextField label="Title" value={form.title ?? ''} onChange={(v) => patch({ title: v })} disabled={disabled} />
      </FormSection>

      <FormSection title="Address and contact">
        <TextField label="Line 1" value={form.address.line1 ?? ''} onChange={(v) => address({ line1: v })} disabled={disabled} />
        <TextField label="Line 2" value={form.address.line2 ?? ''} onChange={(v) => address({ line2: v })} disabled={disabled} />
        <TextField label="City" value={form.address.city ?? ''} onChange={(v) => address({ city: v })} disabled={disabled} />
        <TextField
          label="State / province"
          value={form.address.stateOrProvince ?? ''}
          onChange={(v) => address({ stateOrProvince: v })}
          disabled={disabled}
        />
        <TextField
          label="Postcode"
          value={form.address.postalCode ?? ''}
          onChange={(v) => address({ postalCode: v })}
          disabled={disabled}
        />
        <TextField label="Phone" value={form.contact.phone ?? ''} onChange={(v) => contact({ phone: v })} disabled={disabled} />
        <TextField label="Fax" value={form.contact.fax ?? ''} onChange={(v) => contact({ fax: v })} disabled={disabled} />
        <TextField label="Email" value={form.contact.email ?? ''} onChange={(v) => contact({ email: v })} disabled={disabled} />
        <TextField
          label="Website"
          value={form.contact.website ?? ''}
          onChange={(v) => contact({ website: v })}
          disabled={disabled}
        />
      </FormSection>

      {supplierId ? (
        <div className="mb-6 space-y-2">
          <p className="text-label text-ink-muted">
            {form.suppliedItemCount} item{form.suppliedItemCount === 1 ? '' : 's'} sourced from this supplier.
            {form.suppliedItemCount > 0 ? ' Unlink them before deleting.' : ''}
          </p>

          {canWrite ? (
            <button type="button" className="pos-button text-negative" onClick={() => void remove()} disabled={busy}>
              Delete
            </button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
