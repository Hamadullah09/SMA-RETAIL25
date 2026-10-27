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
import { formatCount } from '@/lib/format';
import { checkEmail, textOrNull, type Errors } from '@/lib/forms';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import type { SupplierDetail, SupplierRow } from '@/lib/types';

const PAGE_SIZE = 50;

export default function SuppliersPage() {
  return (
    <Suspense>
      <Suppliers />
    </Suspense>
  );
}

function Suppliers() {
  const { can, canChangeShared } = useSession();
  const params = useSearchParams();
  const router = useRouter();
  const [text, setText] = useState('');
  const [deleted, setDeleted] = useState(false);
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<SupplierRow | null>(null);
  const [editing, setEditing] = useState<string | 'new' | null>(null);
  const search = useDebounced(text.trim());
  // Suppliers are company-wide: changed with suppliers.edit by those who may change shared records.
  const edit = can(P.suppliersEdit) && canChangeShared;

  useEffect(() => setPage(1), [search, deleted]);
  useEffect(() => setSelected(null), [deleted]);
  useEffect(() => {
    if (params.get('new') === '1' && edit) {
      setEditing('new');
      router.replace('/suppliers/');
    }
  }, [params, edit, router]);

  const query = useQuery({
    queryKey: ['suppliers', { search, deleted }, page],
    queryFn: ({ signal }) => api.suppliers({ search, deleted: deleted || undefined, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  const actions = useDeleteUndelete({ listKey: 'suppliers', remove: api.deleteSupplier, restore: api.undeleteSupplier });

  return (
    <div>
      <PageHeader title="Suppliers" aside={query.data && `${formatCount(query.data.items.length)} shown · ${formatCount(query.data.total)} ${deleted ? 'deleted suppliers' : 'suppliers'}`} />
      <MasterToolbar
        text={text}
        onText={setText}
        searchPlaceholder="Code, company or contact"
        deleted={deleted}
        onDeleted={setDeleted}
        canEdit={edit}
        hasSelection={!!selected}
        newLabel="New supplier"
        onNew={() => setEditing('new')}
        onEdit={() => selected && setEditing(selected.id)}
        onDelete={() => selected && actions.askDelete(selected.id, selected.company)}
        onUndelete={() => selected && actions.askUndelete(selected.id, selected.company)}
      />
      <DataTable<SupplierRow>
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(s) => s.id}
        selectedKey={selected?.id}
        onRowClick={(s) => {
          if (selected?.id === s.id && !deleted) setEditing(s.id);
          else setSelected(s);
        }}
        caption="Suppliers"
        empty={<EmptyState title={deleted ? 'No deleted suppliers' : 'No suppliers found'}>{search ? 'Nothing matches the search.' : null}</EmptyState>}
        columns={[
          { key: 'code', header: 'Code', cell: (s) => <span className="font-semibold">{s.code}</span> },
          { key: 'company', header: 'Company', cell: (s) => s.company },
          { key: 'contact', header: 'Contact', cell: (s) => s.contact ?? '', hideBelow: 'md' },
          { key: 'phone', header: 'Phone', cell: (s) => s.phone ?? '', hideBelow: 'sm' },
          { key: 'email', header: 'E-mail', cell: (s) => s.email ?? '', hideBelow: 'lg' },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="suppliers" />}
      />
      {can(P.suppliersEdit) && !canChangeShared && <p className="mt-2 px-1 text-ink">Suppliers are shared by every store: a super administrator changes them.</p>}
      {editing && <SupplierDialog id={editing === 'new' ? null : editing} readOnly={!edit} onClose={() => setEditing(null)} />}
      {actions.dialog}
    </div>
  );
}

interface Form {
  code: string;
  company: string;
  contact: string;
  address1: string;
  address2: string;
  city: string;
  province: string;
  postalCode: string;
  country: string;
  phone: string;
  fax: string;
  email: string;
  notes: string;
}
type Key = keyof Form;

function toForm(s: SupplierDetail | null): Form {
  return {
    code: s?.code ?? '',
    company: s?.company ?? '',
    contact: s?.contact ?? '',
    address1: s?.address1 ?? '',
    address2: s?.address2 ?? '',
    city: s?.city ?? '',
    province: s?.province ?? '',
    postalCode: s?.postalCode ?? '',
    country: s?.country ?? '',
    phone: s?.phone ?? '',
    fax: s?.fax ?? '',
    email: s?.email ?? '',
    notes: s?.notes ?? '',
  };
}

function SupplierDialog({ id, readOnly, onClose }: { id: string | null; readOnly: boolean; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const detail = useQuery({ queryKey: ['supplier', id], queryFn: () => api.supplier(id!), enabled: !!id, staleTime: 0 });
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
    mutationFn: (body: SupplierDetail) => (id ? api.saveSupplier(id, body) : api.addSupplier(body)),
    onSuccess: (saved) => {
      toast.ok(`${saved.company || 'The supplier'} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['suppliers'] });
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
    if (!form.code.trim()) found.code = 'A code is needed.';
    else if (form.code.trim().length > 10) found.code = 'A code has at most 10 characters.';
    if (!form.company.trim()) found.company = 'The company name is needed.';
    found.email = checkEmail(form.email);
    for (const k of Object.keys(found) as Key[]) if (!found[k]) delete found[k];
    setErrors(found);
    if (Object.keys(found).length) return;
    save.mutate({
      id,
      version: detail.data?.version ?? null,
      code: form.code.trim().toUpperCase(),
      company: form.company.trim(),
      contact: textOrNull(form.contact),
      address1: textOrNull(form.address1),
      address2: textOrNull(form.address2),
      city: textOrNull(form.city),
      province: textOrNull(form.province),
      postalCode: textOrNull(form.postalCode),
      country: textOrNull(form.country),
      phone: textOrNull(form.phone),
      fax: textOrNull(form.fax),
      email: textOrNull(form.email),
      notes: textOrNull(form.notes),
    });
  }

  const box = (key: Key, label: string, extra: { type?: string; inputMode?: 'tel' | 'email'; maxLength?: number } = {}) => (
    <Field label={label} htmlFor={`supplier-${key}`} error={errors[key]}>
      <Input
        id={`supplier-${key}`}
        type={extra.type}
        inputMode={extra.inputMode}
        maxLength={extra.maxLength}
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
      title={id ? detail.data?.company ?? 'Supplier' : 'New supplier'}
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
          {[0, 1, 2, 3, 4].map((i) => (
            <Skeleton key={i} className="h-11" />
          ))}
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-x-8 gap-y-3 md:grid-cols-2">
          <div className="space-y-3">
            {box('code', 'Code', { maxLength: 10 })}
            {box('company', 'Company')}
            {box('contact', 'Contact')}
            {box('address1', 'Address')}
            {box('address2', '')}
            {box('city', 'City')}
            {box('province', 'State / province')}
          </div>
          <div className="space-y-3">
            {box('postalCode', 'Zip / postal code')}
            {box('country', 'Country')}
            {box('phone', 'Phone', { type: 'tel', inputMode: 'tel' })}
            {box('fax', 'Fax', { type: 'tel', inputMode: 'tel' })}
            {box('email', 'E-mail', { type: 'email', inputMode: 'email' })}
            <Field label="Notes" htmlFor="supplier-notes">
              <TextArea id="supplier-notes" value={form.notes} readOnly={readOnly} onChange={(e) => set('notes', e.target.value)} />
            </Field>
          </div>
        </div>
      )}
      <FormError error={save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null} />
      <ConflictDialog
        open={conflict}
        what="supplier"
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
