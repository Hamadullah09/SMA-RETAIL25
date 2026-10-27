'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Save } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';
import { ConflictDialog, MasterToolbar, useDeleteUndelete } from '@/components/masters';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { Field, Input, TextArea } from '@/components/ui/field';
import { PageHeader, Pager } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { cx } from '@/lib/cx';
import { formatCount, formatMoney } from '@/lib/format';
import { checkEmail, checkNumber, numberText, parseNumber, textOrNull, type Errors } from '@/lib/forms';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { ClientDetail, ClientRow } from '@/lib/types';

const PAGE_SIZE = 50;

export default function ClientsPage() {
  return (
    <Suspense>
      <Clients />
    </Suspense>
  );
}

function Clients() {
  const { can } = useSession();
  const symbol = useStore().currencySymbol;
  const params = useSearchParams();
  const router = useRouter();
  const [text, setText] = useState('');
  const [deleted, setDeleted] = useState(false);
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<ClientRow | null>(null);
  const [editing, setEditing] = useState<string | 'new' | null>(null);
  const search = useDebounced(text.trim());
  const edit = can(P.clientsEdit);

  useEffect(() => setPage(1), [search, deleted]);
  useEffect(() => setSelected(null), [deleted]);
  useEffect(() => {
    if (params.get('new') === '1' && edit) {
      setEditing('new');
      router.replace('/clients/');
    }
  }, [params, edit, router]);

  const query = useQuery({
    queryKey: ['clients', { search, deleted }, page],
    queryFn: ({ signal }) => api.clients({ search, deleted: deleted || undefined, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  const actions = useDeleteUndelete({ listKey: 'clients', remove: api.deleteClient, restore: api.undeleteClient });

  return (
    <div>
      <PageHeader title="Clients" aside={query.data && `${formatCount(query.data.items.length)} shown · ${formatCount(query.data.total)} ${deleted ? 'deleted clients' : 'clients'}`} />
      <MasterToolbar
        text={text}
        onText={setText}
        searchPlaceholder="Name, company, phone or number"
        deleted={deleted}
        onDeleted={setDeleted}
        canEdit={edit}
        hasSelection={!!selected}
        newLabel="New client"
        onNew={() => setEditing('new')}
        onEdit={() => selected && setEditing(selected.id)}
        onDelete={() => selected && actions.askDelete(selected.id, selected.name)}
        onUndelete={() => selected && actions.askUndelete(selected.id, selected.name)}
      />
      <DataTable<ClientRow>
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(c) => c.id}
        selectedKey={selected?.id}
        onRowClick={(c) => {
          if (selected?.id === c.id && !deleted) setEditing(c.id);
          else setSelected(c);
        }}
        caption="Clients"
        empty={<EmptyState title={deleted ? 'No deleted clients' : 'No clients found'}>{search ? 'Nothing matches the search.' : null}</EmptyState>}
        columns={[
          { key: 'n', header: 'Number', cell: (c) => c.number, hideBelow: 'md' },
          { key: 'name', header: 'Name', cell: (c) => <span className="font-semibold">{c.name}</span> },
          { key: 'company', header: 'Company', cell: (c) => c.company ?? '', hideBelow: 'lg' },
          { key: 'phone', header: 'Phone', cell: (c) => c.phone ?? '', hideBelow: 'sm' },
          { key: 'email', header: 'E-mail', cell: (c) => c.email ?? '', hideBelow: 'xl' },
          { key: 'bal', header: 'Balance', numeric: true, cell: (c) => <span className={cx(c.balance > 0 && 'font-semibold')}>{formatMoney(c.balance, symbol)}</span> },
          { key: 'pts', header: 'Points', numeric: true, cell: (c) => formatCount(c.points), hideBelow: 'md' },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="clients" />}
      />
      <p className="mt-2 px-1 text-ink">Choose a client, then {edit ? 'Edit' : 'Open'} (or choose it again). Clients added at a till show up after its next upload.</p>
      {editing && <ClientDialog id={editing === 'new' ? null : editing} readOnly={!edit} onClose={() => setEditing(null)} />}
      {actions.dialog}
    </div>
  );
}

interface Form {
  firstName: string;
  lastName: string;
  company: string;
  address1: string;
  address2: string;
  city: string;
  province: string;
  postalCode: string;
  country: string;
  phone: string;
  phone2: string;
  email: string;
  creditLimit: string;
  notes: string;
}
type Key = keyof Form;

function toForm(c: ClientDetail | null): Form {
  return {
    firstName: c?.firstName ?? '',
    lastName: c?.lastName ?? '',
    company: c?.company ?? '',
    address1: c?.address1 ?? '',
    address2: c?.address2 ?? '',
    city: c?.city ?? '',
    province: c?.province ?? '',
    postalCode: c?.postalCode ?? '',
    country: c?.country ?? '',
    phone: c?.phone ?? '',
    phone2: c?.phone2 ?? '',
    email: c?.email ?? '',
    creditLimit: numberText(c?.creditLimit ?? 0),
    notes: c?.notes ?? '',
  };
}

function ClientDialog({ id, readOnly, onClose }: { id: string | null; readOnly: boolean; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const symbol = useStore().currencySymbol;
  const detail = useQuery({ queryKey: ['client', id], queryFn: () => api.client(id!), enabled: !!id, staleTime: 0 });
  const [form, setForm] = useState<Form>(toForm(null));
  const [loadedFrom, setLoadedFrom] = useState<string | null>(null);
  const [errors, setErrors] = useState<Errors<Key>>({});
  const [conflict, setConflict] = useState(false);

  useEffect(() => {
    if (detail.data && detail.data.version !== loadedFrom) {
      setForm(toForm(detail.data));
      setLoadedFrom(detail.data.version);
    }
  }, [detail.data, loadedFrom]);

  const save = useMutation({
    mutationFn: (body: ClientDetail) => (id ? api.saveClient(id, body) : api.addClient(body)),
    onSuccess: (saved) => {
      toast.ok(`${[saved.firstName, saved.lastName].filter(Boolean).join(' ') || saved.company || 'The client'} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['clients'] });
      onClose();
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });

  function set(key: Key, value: string) {
    setForm((f) => ({ ...f, [key]: value }));
    if (errors[key]) setErrors((e) => ({ ...e, [key]: undefined }));
  }

  function submit() {
    if (readOnly) return onClose();
    const found: Errors<Key> = {};
    if (!form.firstName.trim() && !form.lastName.trim() && !form.company.trim()) found.lastName = 'Type a name or a company.';
    found.email = checkEmail(form.email);
    found.creditLimit = checkNumber(form.creditLimit, { label: 'The credit limit' });
    for (const k of Object.keys(found) as Key[]) if (!found[k]) delete found[k];
    setErrors(found);
    if (Object.keys(found).length) return;
    save.mutate({
      id,
      version: detail.data?.version ?? null,
      number: detail.data?.number ?? null,
      firstName: textOrNull(form.firstName),
      lastName: textOrNull(form.lastName),
      company: textOrNull(form.company),
      address1: textOrNull(form.address1),
      address2: textOrNull(form.address2),
      city: textOrNull(form.city),
      province: textOrNull(form.province),
      postalCode: textOrNull(form.postalCode),
      country: textOrNull(form.country),
      phone: textOrNull(form.phone),
      phone2: textOrNull(form.phone2),
      email: textOrNull(form.email),
      creditLimit: parseNumber(form.creditLimit) ?? 0,
      balance: detail.data?.balance ?? 0,
      points: detail.data?.points ?? 0,
      notes: textOrNull(form.notes),
    });
  }

  const box = (key: Key, label: string, extra: { type?: string; inputMode?: 'decimal' | 'tel' | 'email'; className?: string } = {}) => (
    <Field label={label} htmlFor={`client-${key}`} error={errors[key]}>
      <Input
        id={`client-${key}`}
        type={extra.type}
        inputMode={extra.inputMode}
        className={extra.className}
        value={form[key]}
        readOnly={readOnly}
        invalid={!!errors[key]}
        onChange={(e) => set(key, e.target.value)}
      />
    </Field>
  );

  const loading = !!id && !detail.data && !detail.error;
  return (
    <Dialog
      open
      width="lg"
      title={id ? (detail.data ? `Client ${detail.data.number ?? ''}` : 'Client') : 'New client'}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          {!readOnly && (
            <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending} disabled={loading}>
              Save
            </Button>
          )}
          <Button onClick={onClose} shortcut="Esc">
            {readOnly ? 'Close' : 'Cancel'}
          </Button>
        </>
      }
    >
      {detail.error ? (
        <ErrorState error={detail.error} onRetry={() => void detail.refetch()} />
      ) : loading ? (
        <div className="space-y-3">
          {[0, 1, 2, 3, 4, 5].map((i) => (
            <Skeleton key={i} className="h-11" />
          ))}
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-x-8 gap-y-3 md:grid-cols-2">
          <div className="space-y-3">
            {box('firstName', 'First name')}
            {box('lastName', 'Last name')}
            {box('company', 'Company')}
            {box('address1', 'Address')}
            {box('address2', '')}
            {box('city', 'City')}
            {box('province', 'State / province')}
            {box('postalCode', 'Zip / postal code')}
            {box('country', 'Country')}
          </div>
          <div className="space-y-3">
            {box('phone', 'Phone', { type: 'tel', inputMode: 'tel' })}
            {box('phone2', 'Other phone', { type: 'tel', inputMode: 'tel' })}
            {box('email', 'E-mail', { type: 'email', inputMode: 'email' })}
            {box('creditLimit', 'Credit limit', { inputMode: 'decimal', className: 'max-w-[180px]' })}
            <Field label="Balance" htmlFor="client-balance" hint="From invoices and payments at the tills.">
              <p id="client-balance" className="pt-2.5 font-semibold tabular-nums">
                {formatMoney(detail.data?.balance ?? 0, symbol)}
              </p>
            </Field>
            <Field label="Points" htmlFor="client-points">
              <p id="client-points" className="pt-2.5 font-semibold tabular-nums">
                {formatCount(detail.data?.points ?? 0)}
              </p>
            </Field>
            <Field label="Notes" htmlFor="client-notes">
              <TextArea id="client-notes" value={form.notes} readOnly={readOnly} onChange={(e) => set('notes', e.target.value)} />
            </Field>
          </div>
        </div>
      )}
      <FormError error={save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null} />
      <ConflictDialog
        open={conflict}
        what="client"
        onReload={() => {
          setConflict(false);
          setLoadedFrom(null);
          save.reset();
          void detail.refetch();
        }}
        onClose={() => setConflict(false)}
      />
    </Dialog>
  );
}
