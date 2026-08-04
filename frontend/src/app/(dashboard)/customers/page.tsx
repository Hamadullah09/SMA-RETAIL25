'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import {
  BrowseFormShell,
  CheckField,
  FormSection,
  LiveBadge,
  NumberField,
  SelectField,
  TextField,
} from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { useLiveGrid } from '@/lib/inventory-hub';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { formatCurrency } from '@/lib/utils';
import type { Address, ContactDetails, CustomerForm, CustomerRow, CustomerSort } from '@/types/masters';

/**
 * The id a form holds while it is creating rather than editing.
 *
 * Zero, because that is what the domain means by it too: an entity that has not been saved has no
 * id yet, and no row can ever be 0 — the sequence starts at 1. A string sentinel would have to be
 * kept out of every type that says this is a record key.
 */
const NEW_RECORD = 0;

/**
 * Customer Browse + Form View (guide p.46–52).
 *
 * The account and pricing fields sit on the same screen as the name and address because they are
 * what changes a sale: attaching this customer at the till applies their price level, their usual
 * discount and their tax exemptions to the whole basket.
 */
export default function CustomersPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canWrite = auth.can('customer.write');
  const canDelete = auth.can('customer.delete');

  const [search, setSearch] = useState('');
  const [clientType, setClientType] = useState('');
  const [withBalanceOnly, setWithBalanceOnly] = useState(false);
  const [sort, setSort] = useState<CustomerSort>('Number');
  const [rows, setRows] = useState<CustomerRow[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);

  const { data: clientTypes = [] } = useQuery({
    queryKey: ['client-types', locationId],
    queryFn: () => mastersApi.customers.clientTypes(locationId!),
    enabled: Boolean(locationId),
  });

  const load = useCallback(
    async (append: boolean, from: string | null) => {
      if (!locationId) return;

      setLoading(true);

      try {
        const page = await mastersApi.customers.browse(locationId, {
          search,
          clientType,
          withBalanceOnly,
          sort,
          cursor: from ?? undefined,
          pageSize: 100,
        });

        setRows((current) => (append ? [...current, ...page.items] : page.items));
        setCursor(page.nextCursor);
        setHasMore(page.hasMore);
      } catch (error) {
        toast({ title: 'Could not load customers', description: describe(error), variant: 'destructive' });
      } finally {
        setLoading(false);
      }
    },
    [locationId, search, clientType, withBalanceOnly, sort],
  );

  useEffect(() => {
    const timer = window.setTimeout(() => void load(false, null), 200);
    return () => window.clearTimeout(timer);
  }, [load]);

  const { connected, changed } = useLiveGrid('customer', locationId, setRows);

  const columns = useMemo<DataGridColumn<CustomerRow>[]>(
    () => [
      {
        key: 'number',
        header: 'No.',
        width: 80,
        numeric: true,
        render: (r) => r.customerNumber,
        sortValue: (r) => r.customerNumber,
      },
      { key: 'name', header: 'Name', width: 240, render: (r) => r.displayName, sortValue: (r) => r.displayName },
      { key: 'city', header: 'City', width: 140, render: (r) => r.city ?? '—' },
      { key: 'phone', header: 'Phone', width: 130, render: (r) => r.phone ?? '—' },
      { key: 'email', header: 'Email', width: 200, render: (r) => r.email ?? '—' },
      { key: 'clientType', header: 'Type', width: 110, render: (r) => r.clientType ?? '—' },
      { key: 'level', header: 'Level', width: 60, numeric: true, render: (r) => r.priceLevel },
      {
        key: 'balance',
        header: 'Balance',
        width: 100,
        numeric: true,
        // Money owed is the reason most people open this screen, so it is coloured rather than left
        // to be spotted among identical figures.
        render: (r) => (
          <span className={r.balanceDue > 0 ? 'text-negative' : undefined}>
            {formatCurrency(r.balanceDue)}
          </span>
        ),
        sortValue: (r) => r.balanceDue,
      },
      {
        key: 'limit',
        header: 'Credit limit',
        width: 100,
        numeric: true,
        render: (r) => (r.creditLimit === 0 ? 'Unlimited' : formatCurrency(r.creditLimit)),
      },
      { key: 'lastPurchase', header: 'Last purchase', width: 120, render: (r) => r.lastPurchaseOn ?? '—' },
    ],
    [],
  );

  return (
    <BrowseFormShell
      title="Customers"
      toolbar={
        <>
          <LiveBadge connected={connected} />
          {canWrite ? (
            <button type="button" className="pos-button-primary" onClick={() => setSelectedId(NEW_RECORD)}>
              New customer
            </button>
          ) : null}
        </>
      }
      filters={
        <>
          <input
            className="pos-input w-64"
            placeholder="Name, company, phone or email"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />

          <select
            className="pos-input"
            value={clientType}
            onChange={(event) => setClientType(event.target.value)}
          >
            <option value="">All client types</option>
            {clientTypes.map((type) => (
              <option key={type} value={type}>
                {type}
              </option>
            ))}
          </select>

          <select
            className="pos-input"
            value={sort}
            onChange={(event) => setSort(event.target.value as CustomerSort)}
          >
            <option value="Number">Sort: number</option>
            <option value="Name">Sort: surname</option>
            <option value="Company">Sort: company</option>
          </select>

          <label className="flex items-center gap-1.5">
            <input
              type="checkbox"
              checked={withBalanceOnly}
              onChange={(event) => setWithBalanceOnly(event.target.checked)}
            />
            Owing money
          </label>
        </>
      }
      grid={
        <DataGrid
          gridId="customers"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          recentlyChanged={changed}
          onRowActivate={(row) => setSelectedId(row.id)}
          emptyMessage={loading ? 'Loading…' : 'No customers match these filters.'}
        />
      }
      form={
        selectedId && locationId ? (
          <CustomerFormPanel
            key={String(selectedId)}
            customerId={selectedId === NEW_RECORD ? null : selectedId}
            locationId={locationId}
            canWrite={canWrite}
            canDelete={canDelete}
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

const emptyCustomer: CustomerForm = {
  id: 0,
  locationId: 0,
  customerNumber: 0,
  firstName: '',
  lastName: '',
  company: null,
  title: null,
  billingAddress: {},
  shipToAddress: {},
  contact: {},
  clientType: null,
  birthday: null,
  notes: null,
  lastPurchaseOn: null,
  lastMailingOn: null,
  accountNumber: 0,
  creditLimit: 0,
  balanceDue: 0,
  usualDiscountPct: 0,
  priceLevel: 1,
  exemptTax1: false,
  exemptTax2: false,
  rewardPoints: 0,
  isDeleted: false,
  createdAt: '',
  modifiedAt: null,
};

function CustomerFormPanel({
  customerId,
  locationId,
  canWrite,
  canDelete,
  onClose,
  onSaved,
}: {
  customerId: number | null;
  locationId: number;
  canWrite: boolean;
  canDelete: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState<CustomerForm>(emptyCustomer);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!customerId) {
      setForm({ ...emptyCustomer, locationId });
      return;
    }

    void mastersApi.customers
      .get(customerId)
      .then(setForm)
      .catch((error) =>
        toast({ title: 'Could not open the customer', description: describe(error), variant: 'destructive' }),
      );
  }, [customerId, locationId]);

  const patch = (changes: Partial<CustomerForm>) => setForm((current) => ({ ...current, ...changes }));

  const address = (key: 'billingAddress' | 'shipToAddress', changes: Partial<Address>) =>
    patch({ [key]: { ...form[key], ...changes } } as Partial<CustomerForm>);

  const contact = (changes: Partial<ContactDetails>) => patch({ contact: { ...form.contact, ...changes } });

  const identity = () => ({
    firstName: form.firstName,
    lastName: form.lastName,
    company: form.company,
    title: form.title,
    clientType: form.clientType,
    birthday: form.birthday,
    notes: form.notes,
  });

  const addresses = () => ({
    billingAddress: form.billingAddress,
    shipToAddress: form.shipToAddress,
    contact: form.contact,
  });

  const account = () => ({
    creditLimit: form.creditLimit,
    usualDiscountPct: form.usualDiscountPct,
    priceLevel: form.priceLevel,
    exemptTax1: form.exemptTax1,
    exemptTax2: form.exemptTax2,
  });

  const save = async (sections: Record<string, unknown>) => {
    setBusy(true);

    try {
      const saved = customerId
        ? await mastersApi.customers.update(customerId, { customerId, ...sections })
        : await mastersApi.customers.create({ locationId, identity: identity(), addresses: addresses(), account: account() });

      setForm(saved);
      onSaved();
      toast({ title: customerId ? 'Saved' : 'Customer created' });
    } catch (error) {
      toast({ title: 'Not saved', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!customerId) return;

    setBusy(true);

    try {
      await mastersApi.customers.remove(customerId);
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

  const saveButton = (sections: () => Record<string, unknown>) =>
    canWrite ? (
      <button type="button" className="underline" disabled={busy} onClick={() => void save(sections())}>
        Save
      </button>
    ) : null;

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">
          {customerId ? `#${form.customerNumber} ${form.company || `${form.firstName} ${form.lastName}`}` : 'New customer'}
          {form.isDeleted ? <span className="pos-badge ml-2 text-negative">Deleted</span> : null}
        </h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Close
        </button>
      </div>

      <FormSection
        title="Identity"
        hint="The customer number is issued by the store's counter and is not editable."
        actions={saveButton(() => ({ identity: identity() }))}
      >
        <TextField label="First name" value={form.firstName} onChange={(v) => patch({ firstName: v })} disabled={disabled} autoFocus />
        <TextField label="Last name" value={form.lastName} onChange={(v) => patch({ lastName: v })} disabled={disabled} />
        <TextField
          label="Company"
          value={form.company ?? ''}
          onChange={(v) => patch({ company: v })}
          disabled={disabled}
          hint="When set, this is the name the browse and the receipt show."
        />
        <TextField label="Title" value={form.title ?? ''} onChange={(v) => patch({ title: v })} disabled={disabled} />
        <TextField
          label="Client type"
          value={form.clientType ?? ''}
          onChange={(v) => patch({ clientType: v })}
          disabled={disabled}
          hint="Free text; it becomes a filter on the browse."
        />
        <TextField label="Notes" value={form.notes ?? ''} onChange={(v) => patch({ notes: v })} disabled={disabled} />
      </FormSection>

      <FormSection title="Address and contact" actions={saveButton(() => ({ addresses: addresses() }))}>
        <TextField
          label="Billing line 1"
          value={form.billingAddress.line1 ?? ''}
          onChange={(v) => address('billingAddress', { line1: v })}
          disabled={disabled}
        />
        <TextField
          label="Billing line 2"
          value={form.billingAddress.line2 ?? ''}
          onChange={(v) => address('billingAddress', { line2: v })}
          disabled={disabled}
        />
        <TextField
          label="City"
          value={form.billingAddress.city ?? ''}
          onChange={(v) => address('billingAddress', { city: v })}
          disabled={disabled}
        />
        <TextField
          label="State / province"
          value={form.billingAddress.stateOrProvince ?? ''}
          onChange={(v) => address('billingAddress', { stateOrProvince: v })}
          disabled={disabled}
        />
        <TextField
          label="Postcode"
          value={form.billingAddress.postalCode ?? ''}
          onChange={(v) => address('billingAddress', { postalCode: v })}
          disabled={disabled}
        />
        <TextField
          label="Ship-to line 1"
          value={form.shipToAddress.line1 ?? ''}
          onChange={(v) => address('shipToAddress', { line1: v })}
          disabled={disabled}
          hint="Leave blank to ship to the billing address."
        />
        <TextField
          label="Ship-to city"
          value={form.shipToAddress.city ?? ''}
          onChange={(v) => address('shipToAddress', { city: v })}
          disabled={disabled}
        />
        <TextField label="Phone" value={form.contact.phone ?? ''} onChange={(v) => contact({ phone: v })} disabled={disabled} />
        <TextField label="Mobile" value={form.contact.mobile ?? ''} onChange={(v) => contact({ mobile: v })} disabled={disabled} />
        <TextField label="Email" value={form.contact.email ?? ''} onChange={(v) => contact({ email: v })} disabled={disabled} />
      </FormSection>

      <FormSection
        title="Account and pricing"
        hint="These follow the customer onto every sale: attaching them reprices the whole basket."
        actions={saveButton(() => ({ account: account() }))}
      >
        <NumberField
          label="Credit limit"
          value={form.creditLimit}
          onChange={(v) => patch({ creditLimit: v })}
          disabled={disabled}
          hint="Zero means unlimited, as in the legacy system."
        />
        <NumberField
          label="Usual discount %"
          value={form.usualDiscountPct}
          onChange={(v) => patch({ usualDiscountPct: v })}
          disabled={disabled}
        />
        <SelectField
          label="Price level"
          value={String(form.priceLevel)}
          options={[1, 2, 3, 4].map((level) => ({ value: String(level), label: `Level ${level}` }))}
          onChange={(v) => patch({ priceLevel: Number(v) || 1 })}
          disabled={disabled}
        />
        <CheckField label="Exempt from tax 1" checked={form.exemptTax1} onChange={(v) => patch({ exemptTax1: v })} disabled={disabled} />
        <CheckField label="Exempt from tax 2" checked={form.exemptTax2} onChange={(v) => patch({ exemptTax2: v })} disabled={disabled} />

        {customerId ? (
          <p className="text-label text-ink-muted">
            Balance {formatCurrency(form.balanceDue)} · {form.rewardPoints} reward points — both derived from the ledgers
            and not editable here.
          </p>
        ) : null}
      </FormSection>

      {canDelete && customerId ? (
        <div className="mb-6">
          <button type="button" className="pos-button text-negative" onClick={() => void remove()} disabled={busy}>
            Delete
          </button>
        </div>
      ) : null}
    </div>
  );
}
