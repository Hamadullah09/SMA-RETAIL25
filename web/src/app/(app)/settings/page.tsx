'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ImageOff, ImageUp, Save, Trash2 } from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { ConflictDialog } from '@/components/masters';
import { Button } from '@/components/ui/button';
import { ConfirmDialog } from '@/components/ui/dialog';
import { Field, Input, Select, TextArea } from '@/components/ui/field';
import { ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { checkEmail } from '@/lib/forms';
import { useHotkey } from '@/lib/hotkeys';
import { useStore } from '@/lib/store';
import type { ImageKind, StoreSettings } from '@/lib/types';

type Tab = 'business' | 'receipt' | 'branding';
const TABS: ReadonlyArray<{ key: Tab; label: string }> = [
  { key: 'business', label: 'Business' },
  { key: 'receipt', label: 'Receipt' },
  { key: 'branding', label: 'Branding' },
];

type TextKey = Exclude<keyof StoreSettings, 'version'>;

const EMPTY: StoreSettings = {
  version: null,
  companyName: '',
  address1: '',
  address2: '',
  city: '',
  province: '',
  postalCode: '',
  country: '',
  phone: '',
  fax: '',
  email: '',
  website: '',
  taxNumber: '',
  currencySymbol: '',
  receiptHeader: '',
  receiptFooter: '',
};

/** Setup: the store's business details, receipt texts and branding, as the desktop's Setup › Business and Branding. */
export default function SettingsPage() {
  const { stores, defaultStoreId, storeId: pickedStore } = useStore();
  const [storeId, setStoreId] = useState<string | null>(defaultStoreId);
  const [tab, setTab] = useState<Tab>('business');
  useEffect(() => {
    if (pickedStore) setStoreId(pickedStore);
  }, [pickedStore]);
  useEffect(() => {
    if (!storeId && defaultStoreId) setStoreId(defaultStoreId);
  }, [storeId, defaultStoreId]);

  return (
    <div>
      <header className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <h1 className="page-title">Setup</h1>
        {stores.length > 1 && (
          <label className="flex items-center gap-2.5">
            <span className="text-muted">Store</span>
            <Select className="w-[260px]" value={storeId ?? ''} onChange={(e) => setStoreId(e.target.value)} aria-label="Store">
              {stores.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.code} · {s.name}
                </option>
              ))}
            </Select>
          </label>
        )}
      </header>
      {storeId ? (
        <StoreSetup key={storeId} storeId={storeId} tab={tab} tabs={<Tabs label="Setup" tabs={TABS} value={tab} onChange={setTab} />} />
      ) : (
        <Skeleton className="h-96 rounded-card" />
      )}
    </div>
  );
}

function StoreSetup({ storeId, tab, tabs }: { storeId: string; tab: Tab; tabs: ReactNode }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['settings', storeId], queryFn: () => api.settings(storeId), staleTime: 0 });
  const [form, setForm] = useState<StoreSettings>(EMPTY);
  const [loadedFrom, setLoadedFrom] = useState<string | null | undefined>(undefined);
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (query.data && query.data.version !== loadedFrom) {
      setForm({ ...EMPTY, ...query.data });
      setLoadedFrom(query.data.version);
      setDirty(false);
    }
  }, [query.data, loadedFrom]);

  const save = useMutation({
    mutationFn: (body: StoreSettings) => api.saveSettings(storeId, body),
    onSuccess: (saved) => {
      toast.ok('The settings are saved. The tills get them with their next download.');
      setDirty(false);
      if (saved) {
        queryClient.setQueryData(['settings', storeId], saved);
        setLoadedFrom(saved.version);
      }
      void queryClient.invalidateQueries({ queryKey: ['settings', storeId] });
      // The currency symbol comes with the store list.
      void queryClient.invalidateQueries({ queryKey: ['stores'] });
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });

  function set(key: TextKey, value: string) {
    setForm((f) => ({ ...f, [key]: value }));
    setDirty(true);
  }

  function submit() {
    const text = !form.companyName?.trim() ? 'Type the business name.' : checkEmail(form.email ?? '') ?? ((form.currencySymbol ?? '').length > 3 ? 'A currency symbol has at most 3 characters.' : null);
    setProblem(text);
    if (text) return;
    // Empty boxes go as null; the receipt texts keep their spacing, everything else is trimmed.
    const body: Record<string, string | null> = { version: form.version };
    for (const key of Object.keys(form) as TextKey[]) {
      const v = form[key] ?? '';
      body[key] = v.trim() === '' ? null : key === 'receiptHeader' || key === 'receiptFooter' ? v : v.trim();
    }
    save.mutate(body as unknown as StoreSettings);
  }

  useHotkey('Ctrl+S', submit, !save.isPending && !!query.data);

  if (query.error) {
    return (
      <div className="card">
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </div>
    );
  }

  const box = (key: TextKey, label: string, extra: { hint?: string; className?: string; type?: string; maxLength?: number } = {}) => (
    <Field label={label} htmlFor={`settings-${key}`} hint={extra.hint}>
      <Input id={`settings-${key}`} type={extra.type} value={(form[key] as string | null) ?? ''} maxLength={extra.maxLength} className={extra.className} onChange={(e) => set(key, e.target.value)} />
    </Field>
  );

  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
    >
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        {tabs}
        {tab !== 'branding' && (
          <div className="flex flex-wrap items-center gap-3">
            {dirty && <span className="font-semibold text-ink">Not saved yet</span>}
            <Button type="submit" variant="sun" icon={Save} shortcut="Ctrl+S" busy={save.isPending} disabled={!query.data}>
              Save settings
            </Button>
          </div>
        )}
      </div>
      <div className="card p-5 sm:p-6">
        {!query.data ? (
          <div className="space-y-4">
            {[0, 1, 2, 3, 4, 5].map((i) => (
              <Skeleton key={i} className="h-11 max-w-xl" />
            ))}
          </div>
        ) : (
          <>
            <div hidden={tab !== 'business'} className="grid grid-cols-1 gap-x-10 gap-y-3 xl:grid-cols-2">
              <div className="space-y-3">
                {box('companyName', 'Business name', { maxLength: 30 })}
                {box('address1', 'Address')}
                {box('address2', '')}
                {box('city', 'City')}
                {box('province', 'State / province')}
                {box('postalCode', 'Zip / postal code', { className: 'max-w-[220px]' })}
                {box('country', 'Country')}
              </div>
              <div className="space-y-3">
                {box('phone', 'Phone', { type: 'tel' })}
                {box('fax', 'Fax', { type: 'tel' })}
                {box('email', 'E-mail', { type: 'email' })}
                {box('website', 'Website')}
                {box('taxNumber', 'Tax number')}
                {box('currencySymbol', 'Currency symbol', { className: 'max-w-[120px]', maxLength: 3, hint: 'Printed before money: $, €, GH₵.' })}
              </div>
            </div>
            <div hidden={tab !== 'receipt'} className="grid grid-cols-1 gap-x-10 gap-y-3 xl:grid-cols-2">
              <div className="space-y-3">
                <Field label="Receipt header" htmlFor="settings-receiptHeader" hint="Printed at the top of receipts and invoices, under the business name.">
                  <TextArea id="settings-receiptHeader" rows={4} value={form.receiptHeader ?? ''} onChange={(e) => set('receiptHeader', e.target.value)} className="font-mono" />
                </Field>
                <Field label="Receipt footer" htmlFor="settings-receiptFooter" hint="Printed at the bottom of receipts and invoices: returns, thanks, opening hours.">
                  <TextArea id="settings-receiptFooter" rows={4} value={form.receiptFooter ?? ''} onChange={(e) => set('receiptFooter', e.target.value)} className="font-mono" />
                </Field>
              </div>
              <ReceiptPreview settings={form} />
            </div>
            <div hidden={tab !== 'branding'}>
              <Branding storeId={storeId} />
            </div>
          </>
        )}
        <FormError error={problem ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
      </div>
      <ConflictDialog
        open={conflict}
        what="store's settings"
        onReload={() => {
          setConflict(false);
          setLoadedFrom(undefined);
          save.reset();
          void query.refetch();
        }}
        onClose={() => setConflict(false)}
      />
    </form>
  );
}

/** How the top and bottom of a receipt will read. */
function ReceiptPreview({ settings }: { settings: StoreSettings }) {
  const lines = [settings.companyName, settings.address1, settings.address2, [settings.city, settings.province, settings.postalCode].filter(Boolean).join(' '), settings.phone].filter(
    (l): l is string => !!l && l.trim() !== '',
  );
  return (
    <div>
      <p className="mb-2 text-muted">On the receipt</p>
      <div className="mx-auto max-w-[320px] rounded-control border border-line bg-cream px-5 py-4 text-center font-mono text-note leading-5 text-ink">
        {lines.map((l, i) => (
          <p key={i} className={i === 0 ? 'font-bold' : undefined}>
            {l}
          </p>
        ))}
        {settings.receiptHeader && <p className="mt-2 whitespace-pre-wrap">{settings.receiptHeader}</p>}
        <p className="my-3 tracking-widest text-muted">· · · items · · ·</p>
        {settings.receiptFooter && <p className="whitespace-pre-wrap">{settings.receiptFooter}</p>}
        {settings.taxNumber && <p className="mt-2">Tax no. {settings.taxNumber}</p>}
      </div>
    </div>
  );
}

function Branding({ storeId }: { storeId: string }) {
  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
      <BrandImage storeId={storeId} kind="logo" title="Logo" hint="Printed at the top of receipts and reports. The server keeps it as the desktop does: a JPEG at most 800 pixels a side." />
      <BrandImage storeId={storeId} kind="watermark" title="Watermark" hint="Printed faintly behind invoices and statements." />
    </div>
  );
}

function BrandImage({ storeId, kind, title, hint }: { storeId: string; kind: ImageKind; title: string; hint: string }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [removing, setRemoving] = useState(false);
  const [url, setUrl] = useState<string | null>(null);

  const image = useQuery({
    queryKey: ['image', storeId, kind],
    queryFn: async () => {
      try {
        return await api.image(storeId, kind);
      } catch (e) {
        if (e instanceof ApiError && e.isNotFound) return null;
        throw e;
      }
    },
    staleTime: 60_000,
  });

  useEffect(() => {
    if (!image.data) {
      setUrl(null);
      return;
    }
    const next = URL.createObjectURL(image.data);
    setUrl(next);
    return () => URL.revokeObjectURL(next);
  }, [image.data]);

  async function upload(file: File) {
    if (!file.type.startsWith('image/')) {
      toast.error('Choose a picture (JPEG, PNG, GIF, BMP or WebP).');
      return;
    }
    if (file.size > 20 * 1024 * 1024) {
      toast.error('The picture is larger than 20 MB.');
      return;
    }
    setProgress(0);
    try {
      await api.uploadImage(storeId, kind, file, setProgress);
      toast.ok(`The ${title.toLowerCase()} is saved.`);
      await queryClient.invalidateQueries({ queryKey: ['image', storeId, kind] });
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : 'The picture did not upload.');
    } finally {
      setProgress(null);
      if (input.current) input.current.value = '';
    }
  }

  const remove = useMutation({
    mutationFn: () => api.deleteImage(storeId, kind),
    onSuccess: () => {
      toast.ok(`The ${title.toLowerCase()} is removed.`);
      setRemoving(false);
      queryClient.setQueryData(['image', storeId, kind], null);
    },
    onError: (e: Error) => {
      setRemoving(false);
      toast.error(e.message);
    },
  });

  return (
    <section aria-label={title}>
      <h2 className="section-title">{title}</h2>
      <p className="mb-3 text-muted">{hint}</p>
      <div
        className="grid h-[220px] place-items-center overflow-hidden rounded-card border border-line bg-cream"
        onDragOver={(e) => e.preventDefault()}
        onDrop={(e) => {
          e.preventDefault();
          const file = e.dataTransfer.files?.[0];
          if (file) void upload(file);
        }}
      >
        {image.isLoading ? (
          <Skeleton className="h-full w-full rounded-none" />
        ) : image.error ? (
          <ErrorState error={image.error} onRetry={() => void image.refetch()} />
        ) : url ? (
          // eslint-disable-next-line @next/next/no-img-element -- a blob from the API, not a static asset
          <img src={url} alt={`The store's ${title.toLowerCase()}`} className="max-h-[200px] max-w-full object-contain" />
        ) : (
          <div className="flex flex-col items-center gap-2 text-center text-muted">
            <ImageOff className="h-8 w-8" strokeWidth={1.5} />
            <p>
              No {title.toLowerCase()} yet.
              <br />
              Choose one, or drop one here.
            </p>
          </div>
        )}
      </div>
      {progress !== null && (
        <div className="mt-3 h-2.5 overflow-hidden rounded-full bg-stone-light" role="progressbar" aria-label={`${title} upload`} aria-valuenow={Math.round(progress * 100)} aria-valuemin={0} aria-valuemax={100}>
          <div className="h-full rounded-full bg-sun-dark transition-[width]" style={{ width: `${Math.max(4, progress * 100)}%` }} />
        </div>
      )}
      <input
        ref={input}
        type="file"
        accept="image/jpeg,image/png,image/gif,image/bmp,image/webp"
        className="sr-only"
        aria-label={`${title} file`}
        onChange={(e) => {
          const file = e.target.files?.[0];
          if (file) void upload(file);
        }}
      />
      <div className="mt-3 flex flex-wrap gap-2.5">
        <Button icon={ImageUp} onClick={() => input.current?.click()} busy={progress !== null}>
          Choose picture
        </Button>
        <Button icon={Trash2} onClick={() => setRemoving(true)} disabled={!url}>
          Remove picture
        </Button>
      </div>
      <ConfirmDialog
        open={removing}
        title={`Remove the ${title.toLowerCase()}?`}
        message="Receipts and reports print without it from the tills' next download."
        confirmLabel="Remove"
        cancelLabel="Keep it"
        danger
        busy={remove.isPending}
        onConfirm={() => remove.mutate()}
        onClose={() => setRemoving(false)}
      />
    </section>
  );
}
