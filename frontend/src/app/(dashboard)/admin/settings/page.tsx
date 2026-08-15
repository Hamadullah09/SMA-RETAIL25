'use client';

import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useSearchParams } from 'next/navigation';
import { CheckField, Field, NumberField, PasswordField, SelectField, TextField } from '@/components/masters/browse-form';
import { BrandingTab } from '@/components/layout/branding-settings';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { useLiveGrid } from '@/lib/inventory-hub';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { cn } from '@/lib/utils';
import {
  ArrowDown,
  ArrowUp,
  Building2,
  Check,
  Coins,
  Cpu,
  CreditCard,
  FolderTree,
  Hash,
  ListOrdered,
  Loader2,
  Monitor,
  PackageOpen,
  Palette,
  Percent,
  Plus,
  Printer,
  ShoppingCart,
  Users,
  type LucideIcon,
} from 'lucide-react';
import type {
  BusinessSettings,
  CurrencySettings,
  NumberSequenceSettings,
  PoleDisplaySettings,
  PosSettings,
  PricingRuleSettings,
  PrinterSettings,
  ReaderSettings,
  ReferenceRow,
  ScaleSettings,
  SettingsSnapshot,
  StationSettings,
  TaxSettings,
  TenderSettings,
} from '@/types/masters';

/**
 * The Setup screen (guide p.76–84).
 *
 * The tabs are the legacy ones, in the legacy order, because fifteen years of muscle memory is worth
 * more than a tidier taxonomy. Each tab saves on its own — a half-finished edit on Hardware must not
 * be able to write over Taxes.
 *
 * Thirteen tabs is more than a row of buttons can carry. The strip is a real tablist: it scrolls
 * sideways rather than wrapping into a second and third line that move under the pointer as the
 * window resizes, it answers the arrow keys, and the selected tab is marked by weight, an underline
 * and `aria-selected` as well as by colour.
 */

const TABS = [
  'Business ID',
  'Branding',
  'Taxes',
  'POS',
  'Groupings',
  'Printers',
  'Hardware',
  'Stations',
  'Tenders',
  'Currencies',
  'Numbering',
  'Pricing',
  'Users',
] as const;

type Tab = (typeof TABS)[number];

/**
 * An icon per tab.
 *
 * The strip scrolls, so a tab is often found by scanning rather than by reading a label that is
 * half off the edge — a shape at the head of each one is what makes that scan quick. It is also the
 * second non-colour cue on the selected tab.
 */
const TAB_ICONS: Record<Tab, LucideIcon> = {
  'Business ID': Building2,
  Branding: Palette,
  Taxes: Percent,
  POS: ShoppingCart,
  Groupings: FolderTree,
  Printers: Printer,
  Hardware: Cpu,
  Stations: Monitor,
  Tenders: CreditCard,
  Currencies: Coins,
  Numbering: Hash,
  Pricing: ListOrdered,
  Users,
};

/** Tab names carry spaces; DOM ids may not. */
const slug = (name: Tab) => name.toLowerCase().replace(/[^a-z0-9]+/g, '-');
const tabDomId = (name: Tab) => `setup-tab-${slug(name)}`;
const panelDomId = (name: Tab) => `setup-panel-${slug(name)}`;

export default function SettingsPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canWrite = auth.can('settings.write');
  const canTaxes = auth.can('settings.taxes');
  const canHardware = auth.can('settings.hardware');
  const canUsers = auth.can('users.manage');

  // Which tab to open can be asked for in the address, so a link can lead to the thing it names
  // rather than to the front of a stack of thirteen tabs the reader then has to search.
  const searchParams = useSearchParams();
  const requestedTab = searchParams.get('tab');

  const [tab, setTab] = useState<Tab>(
    () => (TABS as readonly string[]).includes(requestedTab ?? '') ? (requestedTab as Tab) : 'Business ID',
  );
  const [settings, setSettings] = useState<SettingsSnapshot | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if ((TABS as readonly string[]).includes(requestedTab ?? '')) {
      setTab(requestedTab as Tab);
    }
  }, [requestedTab]);

  const load = useCallback(async () => {
    if (!locationId) return;

    try {
      setSettings(await mastersApi.settings.get(locationId));
    } catch (error) {
      toast({ title: 'Could not load settings', description: describe(error), variant: 'destructive' });
    } finally {
      setLoading(false);
    }
  }, [locationId]);

  useEffect(() => {
    void load();
  }, [load]);

  // A second administrator saving a tab reloads this one. Settings are small and rarely edited by
  // two people at once; reloading is simpler and safer than merging two half-edited forms.
  useLiveGrid<{ id: number }>('settings', locationId, () => {}, {
    onSettingsChanged: () => void load(),
  });

  if (!locationId) {
    return (
      <div className="p-6">
        <PageHeading />
        <div className="mt-5 max-w-3xl">
          <EmptyState
            title="No location is attached to this session"
            hint="Setup is held per location. Sign in against a till or ask an administrator to attach your account to one."
          />
        </div>
      </div>
    );
  }

  return (
    <div className="pb-12">
      <div className="px-6 pb-4 pt-6">
        <PageHeading />
      </div>

      {/*
        Pinned under the shell header rather than scrolling away. A thirteen-tab screen is one people
        move around in constantly, and a tab strip you have to scroll back up to reach is a strip
        that gets used once per visit.
      */}
      <div className="sticky top-[var(--header-height)] z-20 border-b border-subtle bg-surface/95 backdrop-blur">
        <TabBar current={tab} onSelect={setTab} disabled={loading} />
      </div>

      <div className="max-w-5xl px-6 pt-5">
        {loading || !settings ? (
          <SettingsSkeleton />
        ) : (
          <div key={tab} id={panelDomId(tab)} role="tabpanel" aria-labelledby={tabDomId(tab)} className="space-y-4">
            {tab === 'Business ID' ? <BusinessTab locationId={locationId} value={settings.business} canWrite={canWrite} onSaved={load} /> : null}
            {tab === 'Branding' ? <BrandingTab locationId={locationId} canWrite={canWrite} /> : null}
            {tab === 'Taxes' ? <TaxesTab locationId={locationId} rows={settings.taxes} canWrite={canTaxes} onSaved={load} /> : null}
            {tab === 'POS' ? <PosTab locationId={locationId} value={settings.pos} tenders={settings.tenders} canWrite={canWrite} onSaved={load} /> : null}
            {tab === 'Groupings' ? <GroupingsTab locationId={locationId} canWrite={auth.can('catalog.write')} canDelete={auth.can('catalog.delete')} /> : null}
            {tab === 'Printers' ? <PrintersTab locationId={locationId} rows={settings.printers} canWrite={canHardware} onSaved={load} /> : null}
            {tab === 'Hardware' ? (
              <HardwareTab
                locationId={locationId}
                scales={settings.scales}
                readers={settings.readers}
                poleDisplays={settings.poleDisplays}
                canWrite={canHardware}
                onSaved={load}
              />
            ) : null}
            {tab === 'Stations' ? <StationsTab locationId={locationId} settings={settings} canWrite={canHardware} onSaved={load} /> : null}
            {tab === 'Tenders' ? <TendersTab locationId={locationId} rows={settings.tenders} canWrite={canWrite} onSaved={load} /> : null}
            {tab === 'Currencies' ? <CurrenciesTab locationId={locationId} rows={settings.currencies} canWrite={canWrite} onSaved={load} /> : null}
            {tab === 'Numbering' ? <NumberingTab locationId={locationId} rows={settings.numbering} canWrite={canWrite} onSaved={load} /> : null}
            {tab === 'Pricing' ? <PricingTab locationId={locationId} rows={settings.pricingRules} canWrite={canWrite} onSaved={load} /> : null}
            {tab === 'Users' ? <UsersTab locationId={locationId} rows={settings.staff} canWrite={canUsers} onSaved={load} /> : null}
          </div>
        )}
      </div>
    </div>
  );
}

function PageHeading() {
  return (
    <header className="max-w-3xl space-y-1">
      <h1>Setup</h1>
      <p className="text-body-lg text-ink-muted">
        Everything the store’s behaviour is read from — taxes, tills, printers, tenders and the people who use them.
        Each tab is saved on its own.
      </p>
    </header>
  );
}

// --- The tab strip -------------------------------------------------------------------------------

function TabBar({ current, onSelect, disabled }: { current: Tab; onSelect: (tab: Tab) => void; disabled?: boolean }) {
  const buttons = useRef(new Map<Tab, HTMLButtonElement | null>());

  // A tab reached from the address bar, or from the far end of the strip by keyboard, may be off
  // screen. `nearest` moves the strip and nothing else — the page does not jump.
  useEffect(() => {
    buttons.current.get(current)?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }, [current]);

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const index = TABS.indexOf(current);

    const target =
      event.key === 'ArrowRight'
        ? (index + 1) % TABS.length
        : event.key === 'ArrowLeft'
          ? (index - 1 + TABS.length) % TABS.length
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? TABS.length - 1
              : -1;

    if (target < 0) return;

    event.preventDefault();
    const name = TABS[target];
    onSelect(name);
    buttons.current.get(name)?.focus();
  };

  return (
    <div
      role="tablist"
      aria-label="Setup sections"
      onKeyDown={onKeyDown}
      // Scrolls rather than wraps. Thirteen buttons on three lines reflow every time the sidebar is
      // collapsed, and a control that moves is a control that gets mis-clicked.
      className="-mb-px flex gap-1 overflow-x-auto px-6 pt-1"
    >
      {TABS.map((name) => {
        const selected = name === current;
        const Icon = TAB_ICONS[name];

        return (
          <button
            key={name}
            ref={(node) => {
              buttons.current.set(name, node);
            }}
            id={tabDomId(name)}
            type="button"
            role="tab"
            aria-selected={selected}
            aria-controls={panelDomId(name)}
            // Roving tab index: the strip is one stop in the tab order, and the arrow keys move
            // within it. Thirteen separate tab stops in front of the form is what the plain row of
            // buttons cost a keyboard user.
            tabIndex={selected ? 0 : -1}
            disabled={disabled}
            onClick={() => onSelect(name)}
            className={cn(
              'inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap rounded-t-md border-b-2 px-3 py-2 text-body transition-colors duration-150',
              'disabled:cursor-not-allowed disabled:opacity-50',
              selected
                ? 'border-accent bg-accent-soft font-semibold text-accent-text'
                : 'border-transparent font-medium text-ink-muted hover:bg-panel-hover hover:text-ink',
            )}
          >
            <Icon className="h-3.5 w-3.5" aria-hidden />
            {name}
          </button>
        );
      })}
    </div>
  );
}

// --- Shared presentation -------------------------------------------------------------------------

/**
 * One card of settings: a heading, one line saying what it is for, the fields, and — at the bottom,
 * in the same place on every card — the button that saves them.
 *
 * The save used to be a text link in the card's title bar, which put it above the fields it applied
 * to and moved as titles changed length. A footer bar is where the eye ends up after the last field,
 * and it is in the same place whichever tab you are on.
 */
function SettingsSection({
  title,
  description,
  icon: Icon,
  action,
  footer,
  footerNote,
  children,
}: {
  title: ReactNode;
  description?: string;
  icon?: LucideIcon;
  action?: ReactNode;
  footer?: ReactNode;
  footerNote?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="pos-panel overflow-hidden">
      <header className="pos-panel-header">
        <span className="pos-panel-title">
          {Icon ? <Icon aria-hidden /> : null}
          <span className="min-w-0 truncate">{title}</span>
        </span>
        {action ? <span className="normal-case flex shrink-0 items-center gap-2">{action}</span> : null}
      </header>

      <div className="px-4 py-4">
        {description ? <p className="mb-4 max-w-2xl text-label text-ink-muted">{description}</p> : null}

        {/* Groups within a card are separated by a hairline rather than by whitespace alone, so a
            long form reads as a handful of named things instead of forty fields in a column. */}
        <div className="space-y-5 [&>*+*]:border-t [&>*+*]:border-subtle [&>*+*]:pt-5">{children}</div>
      </div>

      {footer || footerNote ? (
        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-subtle bg-panel-sunken px-4 py-3">
          <p className="text-caption text-ink-muted">{footerNote}</p>
          <div className="flex flex-wrap items-center gap-2">{footer}</div>
        </div>
      ) : null}
    </section>
  );
}

/** A named group of fields inside a card. */
function FieldGroup({
  title,
  hint,
  children,
  columns = 2,
}: {
  title: string;
  hint?: string;
  children: ReactNode;
  columns?: 1 | 2 | 3;
}) {
  return (
    <div>
      <h3 className="text-label font-semibold uppercase tracking-wide text-ink-muted">{title}</h3>
      {hint ? <p className="mt-0.5 max-w-2xl text-caption text-ink-muted">{hint}</p> : null}

      {/* Side by side once there is room for two readable columns, stacked below it. */}
      <div
        className={cn(
          'mt-3 grid gap-x-4 gap-y-3',
          columns === 2 ? 'sm:grid-cols-2' : columns === 3 ? 'sm:grid-cols-2 lg:grid-cols-3' : null,
        )}
      >
        {children}
      </div>
    </div>
  );
}

/**
 * A field that needs the full width of the group — an address line, a list of antenna zones.
 *
 * `col-span-full` rather than a column count, because the group is two columns at one breakpoint and
 * three at another; a fixed span would invent an implicit fourth column in the narrower of the two.
 */
function Wide({ children }: { children: ReactNode }) {
  return <div className="col-span-full">{children}</div>;
}

/** The heading above a tab that holds a list of cards rather than a single form. */
function TabIntro({ title, description, action }: { title: string; description: string; action?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div className="max-w-2xl space-y-0.5">
        <h2 className="text-h3 font-semibold text-ink">{title}</h2>
        <p className="text-body text-ink-muted">{description}</p>
      </div>
      {action ? <div className="flex shrink-0 items-center gap-2">{action}</div> : null}
    </div>
  );
}

function EmptyState({ icon: Icon = PackageOpen, title, hint }: { icon?: LucideIcon; title: string; hint?: string }) {
  return (
    <div className="pos-panel flex flex-col items-center gap-2 px-6 py-12 text-center">
      <span aria-hidden className="inline-flex h-11 w-11 items-center justify-center rounded-full bg-panel-sunken text-ink-faint">
        <Icon className="h-5 w-5" />
      </span>
      <p className="text-body-lg font-medium text-ink">{title}</p>
      {hint ? <p className="max-w-md text-body text-ink-muted">{hint}</p> : null}
    </div>
  );
}

/**
 * What the screen shows while the snapshot is in flight.
 *
 * A single line of "Loading settings…" gives no clue how much is coming or where it will land, so
 * the page appears to jump when it arrives. This holds the shape.
 */
function SettingsSkeleton() {
  return (
    <div className="space-y-4" aria-busy="true">
      <span className="sr-only" role="status">
        Loading settings…
      </span>

      <div className="pos-panel overflow-hidden">
        <div className="pos-panel-header">
          <span className="h-4 w-44 animate-pulse rounded bg-subtle" />
        </div>
        <div className="space-y-4 px-4 py-4">
          <span className="block h-3 w-2/3 animate-pulse rounded bg-subtle" />
          <div className="grid gap-x-4 gap-y-3 sm:grid-cols-2">
            {Array.from({ length: 6 }).map((_, index) => (
              <span key={index} className="block space-y-1.5">
                <span className="block h-2.5 w-24 animate-pulse rounded bg-subtle" />
                <span className="block h-8 w-full animate-pulse rounded-sm bg-subtle" />
              </span>
            ))}
          </div>
        </div>
        <div className="flex justify-end border-t border-subtle bg-panel-sunken px-4 py-3">
          <span className="h-8 w-20 animate-pulse rounded bg-subtle" />
        </div>
      </div>
    </div>
  );
}

/** A read-only value that looks like the field it is not. */
function ReadOnlyValue({ children }: { children: ReactNode }) {
  return <p className="pos-input flex w-full items-center bg-panel-sunken text-ink-muted">{children}</p>;
}

function describe(error: unknown): string {
  return error instanceof PosApiError ? error.problem.detail : 'Something went wrong.';
}

function useSaver(onSaved: () => void | Promise<void>) {
  const [busy, setBusy] = useState(false);

  const run = async (action: () => Promise<unknown>, title = 'Saved') => {
    setBusy(true);

    try {
      await action();
      await onSaved();
      toast({ title });
    } catch (error) {
      toast({ title: 'Not saved', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return { busy, run };
}

function SaveButton({
  busy,
  onClick,
  label = 'Save',
  variant = 'primary',
}: {
  busy: boolean;
  onClick: () => void;
  label?: string;

  /** A card's own save is the primary action; a save that belongs to one row of many is not. */
  variant?: 'primary' | 'secondary';
}) {
  return (
    <button
      type="button"
      className={variant === 'primary' ? 'pos-button-primary' : 'pos-button'}
      disabled={busy}
      onClick={onClick}
    >
      {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden /> : <Check className="h-3.5 w-3.5" aria-hidden />}
      {busy ? 'Saving…' : label}
    </button>
  );
}

// --- Business ID ---------------------------------------------------------------------------------

function BusinessTab({
  locationId,
  value,
  canWrite,
  onSaved,
}: {
  locationId: number;
  value: BusinessSettings;
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const [form, setForm] = useState(value);
  const { busy, run } = useSaver(onSaved);

  useEffect(() => setForm(value), [value]);

  const patch = (changes: Partial<BusinessSettings>) => setForm((current) => ({ ...current, ...changes }));

  return (
    <SettingsSection
      title="Business identity"
      icon={Building2}
      description="This is what prints at the top of every receipt, invoice and statement."
      footerNote={canWrite ? null : 'You do not have permission to change these.'}
      footer={
        canWrite ? (
          <SaveButton
            busy={busy}
            onClick={() =>
              void run(() =>
                mastersApi.settings.business({
                  locationId,
                  businessName: form.businessName,
                  address: form.address,
                  contact: form.contact,
                  licenceNumber: form.licenceNumber,
                  taxRegistrationNumber: form.taxRegistrationNumber,
                  locationName: form.locationName,
                  timeZoneId: form.timeZoneId,
                  businessDayStart: form.businessDayStart,
                }),
              )
            }
          />
        ) : null
      }
    >
      <FieldGroup title="Registered details">
        <Wide>
          <TextField label="Business name" value={form.businessName} onChange={(v) => patch({ businessName: v })} disabled={!canWrite} />
        </Wide>
        <TextField label="Licence number" value={form.licenceNumber ?? ''} onChange={(v) => patch({ licenceNumber: v })} disabled={!canWrite} />
        <TextField
          label="Tax registration number"
          value={form.taxRegistrationNumber ?? ''}
          onChange={(v) => patch({ taxRegistrationNumber: v })}
          disabled={!canWrite}
        />
      </FieldGroup>

      <FieldGroup title="Address">
        <Wide>
          <TextField
            label="Address line 1"
            value={form.address.line1 ?? ''}
            onChange={(v) => patch({ address: { ...form.address, line1: v } })}
            disabled={!canWrite}
          />
        </Wide>
        <TextField
          label="City"
          value={form.address.city ?? ''}
          onChange={(v) => patch({ address: { ...form.address, city: v } })}
          disabled={!canWrite}
        />
        <TextField
          label="State / province"
          value={form.address.stateOrProvince ?? ''}
          onChange={(v) => patch({ address: { ...form.address, stateOrProvince: v } })}
          disabled={!canWrite}
        />
        <TextField
          label="Postcode"
          value={form.address.postalCode ?? ''}
          onChange={(v) => patch({ address: { ...form.address, postalCode: v } })}
          disabled={!canWrite}
        />
      </FieldGroup>

      <FieldGroup title="Contact">
        <TextField
          label="Phone"
          value={form.contact.phone ?? ''}
          onChange={(v) => patch({ contact: { ...form.contact, phone: v } })}
          disabled={!canWrite}
        />
        <TextField
          label="Email"
          value={form.contact.email ?? ''}
          onChange={(v) => patch({ contact: { ...form.contact, email: v } })}
          disabled={!canWrite}
        />
      </FieldGroup>

      <FieldGroup title="This location" hint="Two of these are fixed at creation and are shown so the values are not a mystery.">
        <TextField label="Location name" value={form.locationName} onChange={(v) => patch({ locationName: v })} disabled={!canWrite} />
        <Field label="Location code" hint="Fixed at creation, because migrated data and old reports refer to it.">
          <ReadOnlyValue>{form.legacyCode}</ReadOnlyValue>
        </Field>
        <TextField
          label="Time zone"
          value={form.timeZoneId}
          onChange={(v) => patch({ timeZoneId: v })}
          disabled={!canWrite}
          hint="An IANA or Windows id the server can resolve, e.g. America/Toronto."
        />
        <TextField
          label="Business day starts at"
          value={form.businessDayStart}
          onChange={(v) => patch({ businessDayStart: v })}
          disabled={!canWrite}
          hint="A store trading past midnight sets this to its closing time, so takings group the way staff expect."
        />
        <Field label="Base currency" hint="Every ledger is denominated in it and it cannot be changed here.">
          <ReadOnlyValue>{form.baseCurrencyCode}</ReadOnlyValue>
        </Field>
      </FieldGroup>
    </SettingsSection>
  );
}

// --- Taxes ---------------------------------------------------------------------------------------

function TaxesTab({
  locationId,
  rows,
  canWrite,
  onSaved,
}: {
  locationId: number;
  rows: TaxSettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const current = rows.find((r) => r.isCurrent) ?? rows[0];
  const [form, setForm] = useState<TaxSettings | undefined>(current);
  const [effectiveFrom, setEffectiveFrom] = useState(() => new Date().toISOString().slice(0, 10));
  const { busy, run } = useSaver(onSaved);

  useEffect(() => setForm(current), [current]);

  if (!form) {
    return (
      <EmptyState
        icon={Percent}
        title="No tax configuration yet"
        hint="Nothing can be rung up until a rate exists — a till that silently charges nothing is worse than one that stops."
      />
    );
  }

  const patch = (changes: Partial<TaxSettings>) => setForm((c) => (c ? { ...c, ...changes } : c));

  return (
    <>
      <SettingsSection
        title="Taxes and charges"
        icon={Percent}
        description="Saving schedules a new effective-dated row. The current one is kept so a reprint of an old invoice still shows the tax that was charged."
        footerNote={canWrite ? 'The change is scheduled, not applied on the spot.' : 'You do not have permission to change taxes.'}
        footer={
          canWrite ? (
            <SaveButton
              busy={busy}
              label="Schedule change"
              onClick={() =>
                void run(
                  () =>
                    mastersApi.settings.taxes({
                      locationId,
                      effectiveFrom,
                      tax1Enabled: form.tax1Enabled,
                      tax1Name: form.tax1Name,
                      tax1Rate: form.tax1Rate,
                      tax2Enabled: form.tax2Enabled,
                      tax2Name: form.tax2Name,
                      tax2Rate: form.tax2Rate,
                      tax2Compound: form.tax2Compound,
                      addOnChargeEnabled: form.addOnChargeEnabled,
                      addOnChargeName: form.addOnChargeName,
                      addOnChargeRate: form.addOnChargeRate,
                      addOnChargeTaxable: form.addOnChargeTaxable,
                      taxationType: form.taxationType,
                      registrationNumber: form.registrationNumber,
                    }),
                  'Tax change scheduled',
                )
              }
            />
          ) : null
        }
      >
        <FieldGroup title="When it takes effect">
          <TextField
            label="Takes effect on"
            value={effectiveFrom}
            onChange={setEffectiveFrom}
            disabled={!canWrite}
            hint="Cannot be backdated: sales already rung would change retroactively."
          />
        </FieldGroup>

        <FieldGroup title="Tax 1">
          <Wide>
            <CheckField label="Tax 1 enabled" checked={form.tax1Enabled} onChange={(v) => patch({ tax1Enabled: v })} disabled={!canWrite} />
          </Wide>
          <TextField label="Tax 1 name" value={form.tax1Name} onChange={(v) => patch({ tax1Name: v })} disabled={!canWrite} />
          <NumberField label="Tax 1 rate %" value={form.tax1Rate} onChange={(v) => patch({ tax1Rate: v })} step="0.0001" disabled={!canWrite} />
        </FieldGroup>

        <FieldGroup title="Tax 2">
          <Wide>
            <CheckField label="Tax 2 enabled" checked={form.tax2Enabled} onChange={(v) => patch({ tax2Enabled: v })} disabled={!canWrite} />
          </Wide>
          <TextField label="Tax 2 name" value={form.tax2Name} onChange={(v) => patch({ tax2Name: v })} disabled={!canWrite} />
          <NumberField label="Tax 2 rate %" value={form.tax2Rate} onChange={(v) => patch({ tax2Rate: v })} step="0.0001" disabled={!canWrite} />
          <Wide>
            <CheckField
              label="Tax 2 compounds on tax 1"
              checked={form.tax2Compound}
              onChange={(v) => patch({ tax2Compound: v })}
              disabled={!canWrite}
              hint="Unusual, but required in some jurisdictions."
            />
          </Wide>
        </FieldGroup>

        <FieldGroup title="Add-on charge">
          <Wide>
            <CheckField
              label="Add-on charge enabled"
              checked={form.addOnChargeEnabled}
              onChange={(v) => patch({ addOnChargeEnabled: v })}
              disabled={!canWrite}
            />
          </Wide>
          <TextField label="Add-on charge name" value={form.addOnChargeName} onChange={(v) => patch({ addOnChargeName: v })} disabled={!canWrite} />
          <NumberField
            label="Add-on charge rate %"
            value={form.addOnChargeRate}
            onChange={(v) => patch({ addOnChargeRate: v })}
            step="0.0001"
            disabled={!canWrite}
          />
          <Wide>
            <CheckField
              label="Add-on charge is itself taxable"
              checked={form.addOnChargeTaxable}
              onChange={(v) => patch({ addOnChargeTaxable: v })}
              disabled={!canWrite}
            />
          </Wide>
        </FieldGroup>

        <FieldGroup title="How prices are shown">
          <SelectField
            label="Shelf prices"
            value={form.taxationType}
            options={[
              { value: 'Exclusive', label: 'Exclude tax — added at the till' },
              { value: 'Inclusive', label: 'Include tax — backed out for reporting' },
            ]}
            onChange={(v) => patch({ taxationType: (v || 'Exclusive') as TaxSettings['taxationType'] })}
            disabled={!canWrite}
          />
          <TextField
            label="Registration number"
            value={form.registrationNumber ?? ''}
            onChange={(v) => patch({ registrationNumber: v })}
            disabled={!canWrite}
          />
        </FieldGroup>
      </SettingsSection>

      <SettingsSection title="History" description="Every rate that has ever applied, and when.">
        <div className="-mx-4 -mb-4 overflow-x-auto border-t border-subtle">
          <table className="w-full min-w-[34rem] text-body">
            <thead>
              <tr className="border-b border-subtle bg-panel-sunken text-label text-ink-muted">
                <th className="px-4 py-2 text-left font-medium">From</th>
                <th className="px-4 py-2 text-left font-medium">To</th>
                <th className="px-4 py-2 text-right font-medium">Tax 1</th>
                <th className="px-4 py-2 text-right font-medium">Tax 2</th>
                <th className="px-4 py-2 text-left font-medium">Pricing</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-subtle">
              {rows.map((row) => (
                <tr key={row.id ?? row.effectiveFrom} className={row.isCurrent ? 'font-medium text-ink' : 'text-ink-muted'}>
                  <td className="whitespace-nowrap px-4 py-2">
                    {row.effectiveFrom}
                    {/* The current row is named, not merely emboldened — weight alone is the kind of
                        distinction that survives neither a glance nor a printout. */}
                    {row.isCurrent ? <span className="pos-badge ml-2 text-accent-text">Current</span> : null}
                  </td>
                  <td className="whitespace-nowrap px-4 py-2">{row.effectiveTo ?? '—'}</td>
                  <td className="pos-amount px-4 py-2 text-right">{row.tax1Enabled ? `${row.tax1Rate}%` : 'off'}</td>
                  <td className="pos-amount px-4 py-2 text-right">{row.tax2Enabled ? `${row.tax2Rate}%` : 'off'}</td>
                  <td className="px-4 py-2">{row.taxationType}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </SettingsSection>
    </>
  );
}

// --- POS -----------------------------------------------------------------------------------------

function PosTab({
  locationId,
  value,
  tenders,
  canWrite,
  onSaved,
}: {
  locationId: number;
  value: PosSettings;
  tenders: TenderSettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const [form, setForm] = useState(value);
  const { busy, run } = useSaver(onSaved);

  useEffect(() => setForm(value), [value]);

  const patch = (changes: Partial<PosSettings>) => setForm((current) => ({ ...current, ...changes }));

  return (
    <SettingsSection
      title="Point of sale"
      icon={ShoppingCart}
      description="Store-wide defaults. A station can override the selling switches on the Stations tab."
      footerNote={canWrite ? null : 'You do not have permission to change these.'}
      footer={
        canWrite ? (
          <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.pos({ locationId, settings: form }))} />
        ) : null
      }
    >
      <FieldGroup title="Tax at the till">
        <CheckField label="Charge tax 1" checked={form.applyTax1} onChange={(v) => patch({ applyTax1: v })} disabled={!canWrite} />
        <CheckField label="Charge tax 2" checked={form.applyTax2} onChange={(v) => patch({ applyTax2: v })} disabled={!canWrite} />
        <CheckField
          label="Allow tax override at the till"
          checked={form.allowTaxOverride}
          onChange={(v) => patch({ allowTaxOverride: v })}
          disabled={!canWrite}
          hint="When off the keys are hidden and the server refuses the field."
        />
        <CheckField label="Apply the add-on charge" checked={form.applyAddOnCharge} onChange={(v) => patch({ applyAddOnCharge: v })} disabled={!canWrite} />
      </FieldGroup>

      <FieldGroup title="Scanning and selling">
        <CheckField
          label="Fast scan mode"
          checked={form.fastScanMode}
          onChange={(v) => patch({ fastScanMode: v })}
          disabled={!canWrite}
          hint="Suppresses the item-detail window so scanning is uninterrupted."
        />
        <CheckField label="Auto-save sales" checked={form.autoSaveSales} onChange={(v) => patch({ autoSaveSales: v })} disabled={!canWrite} />
        <CheckField
          label="Confirm before saving a sale"
          checked={form.confirmBeforeSavingSales}
          onChange={(v) => patch({ confirmBeforeSavingSales: v })}
          disabled={!canWrite}
        />
        <CheckField
          label="Read random-weight barcodes"
          checked={form.scanRandomWeightBarcodes}
          onChange={(v) => patch({ scanRandomWeightBarcodes: v })}
          disabled={!canWrite}
          hint="Type 2 embedded-price labels. Only useful where a scale actually feeds the till."
        />
        <CheckField label="Staff may give discounts" checked={form.staffMayDiscount} onChange={(v) => patch({ staffMayDiscount: v })} disabled={!canWrite} />
        <CheckField label="Allow free-text lines on an invoice" checked={form.allowItemListEdit} onChange={(v) => patch({ allowItemListEdit: v })} disabled={!canWrite} />
      </FieldGroup>

      <FieldGroup title="Staff and supervision">
        <CheckField label="Attribute every sale to a staff member" checked={form.trackStaffSales} onChange={(v) => patch({ trackStaffSales: v })} disabled={!canWrite} />
        <CheckField
          label="A supervisor must approve a void"
          checked={form.requireSupervisorToVoid}
          onChange={(v) => patch({ requireSupervisorToVoid: v })}
          disabled={!canWrite}
        />
        <CheckField label="Use the employee time clock" checked={form.useEmployeeTimeClock} onChange={(v) => patch({ useEmployeeTimeClock: v })} disabled={!canWrite} />
      </FieldGroup>

      <FieldGroup title="What prints, and what carries over">
        <CheckField
          label="Print the signature line on card sales"
          checked={form.printCreditCardSignatureLine}
          onChange={(v) => patch({ printCreditCardSignatureLine: v })}
          disabled={!canWrite}
        />
        <CheckField
          label="Print the customer's name on the slip"
          checked={form.printClientNameOnSalesSlip}
          onChange={(v) => patch({ printClientNameOnSalesSlip: v })}
          disabled={!canWrite}
        />
        <CheckField
          label="Carry city, state and postcode into a new customer"
          checked={form.carryOverCityStateZip}
          onChange={(v) => patch({ carryOverCityStateZip: v })}
          disabled={!canWrite}
        />
      </FieldGroup>

      <FieldGroup title="Defaults">
        <SelectField
          label="Default tender"
          value={form.defaultTenderTypeId ?? ''}
          options={[{ value: '', label: '— none —' }, ...tenders.map((t) => ({ value: t.id, label: t.displayName }))]}
          onChange={(v) => patch({ defaultTenderTypeId: v || null })}
          disabled={!canWrite}
        />
        <NumberField
          label="Abandon an untouched cart after (minutes)"
          value={form.abandonedCartTimeoutMinutes}
          onChange={(v) => patch({ abandonedCartTimeoutMinutes: v })}
          step="1"
          disabled={!canWrite}
          hint="Suspended carts are never expired by this — only carts nobody came back to."
        />
      </FieldGroup>
    </SettingsSection>
  );
}

// --- Printers ------------------------------------------------------------------------------------

function PrintersTab({
  locationId,
  rows,
  canWrite,
  onSaved,
}: {
  locationId: number;
  rows: PrinterSettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(rows);

  useEffect(() => setDrafts(rows), [rows]);

  const patch = (id: number, changes: Partial<PrinterSettings>) =>
    setDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  const addPrinter = (
    <button
      type="button"
      className="pos-button"
      disabled={busy}
      onClick={() =>
        void run(
          () =>
            mastersApi.settings.printer({
              locationId,
              profile: {
                id: '00000000-0000-0000-0000-000000000000',
                stationId: null,
                name: 'New printer',
                setupCommand: null,
                cutterCommand: null,
                redCommand: null,
                blackCommand: null,
                port: 'COM1',
                defaultCopies: 1,
                pageEject: false,
                extraCopyOnCard: false,
                initializeSerial: false,
                output: 'Slip40',
                columns: 40,
                drawerTrigger: '27,112,0,50,250',
                drawerRepeat: 1,
                openDrawerOnPrint: false,
                isActive: true,
              },
            }),
          'Printer added',
        )
      }
    >
      <Plus className="h-3.5 w-3.5" aria-hidden />
      Add a printer
    </button>
  );

  return (
    <>
      <TabIntro
        title="Printers"
        description="One profile per physical printer. Every escape sequence is decimal ASCII — Epson cuts with 27,105; Star with 27,100,48."
        action={canWrite ? addPrinter : null}
      />

      {drafts.length === 0 ? (
        <EmptyState
          icon={Printer}
          title="No printer profile yet"
          hint={canWrite ? 'Add one and give it the port the printer is on.' : 'Ask someone with hardware permission to add one.'}
        />
      ) : null}

      {drafts.map((printer) => (
        <SettingsSection
          key={String(printer.id)}
          title={printer.name}
          icon={Printer}
          action={
            printer.isActive ? (
              <span className="pos-badge text-positive">Active</span>
            ) : (
              <span className="pos-badge text-ink-muted">Inactive</span>
            )
          }
          footer={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.printer({ locationId, profile: printer }))} /> : null}
          footerNote={canWrite ? null : 'You do not have permission to change hardware.'}
        >
          <FieldGroup title="Identity and output">
            <TextField label="Name" value={printer.name} onChange={(v) => patch(printer.id, { name: v })} disabled={!canWrite} />
            <TextField label="Port" value={printer.port ?? ''} onChange={(v) => patch(printer.id, { port: v })} disabled={!canWrite} hint="COM1, LPT1, a UNC share, or host:port." />
            <SelectField
              label="Output"
              value={printer.output}
              options={[
                { value: 'Invoice', label: 'Full-page invoice' },
                { value: 'Slip40', label: '40-column slip' },
                { value: 'Slip20', label: '20-column slip' },
              ]}
              onChange={(v) => patch(printer.id, { output: (v || 'Slip40') as PrinterSettings['output'] })}
              disabled={!canWrite}
            />
            <NumberField label="Columns" value={printer.columns} onChange={(v) => patch(printer.id, { columns: v })} step="1" disabled={!canWrite} />
          </FieldGroup>

          <FieldGroup title="Escape sequences" hint="Decimal ASCII, comma separated. Test them before a shift, not during one.">
            <TextField label="Setup command" value={printer.setupCommand ?? ''} onChange={(v) => patch(printer.id, { setupCommand: v })} disabled={!canWrite} />
            <TextField label="Cutter command" value={printer.cutterCommand ?? ''} onChange={(v) => patch(printer.id, { cutterCommand: v })} disabled={!canWrite} />
            <TextField
              label="Drawer kick"
              value={printer.drawerTrigger}
              onChange={(v) => patch(printer.id, { drawerTrigger: v })}
              disabled={!canWrite}
              hint="27,112,0,50,250 is the Epson pulse."
            />
            <NumberField label="Drawer repeat" value={printer.drawerRepeat} onChange={(v) => patch(printer.id, { drawerRepeat: v })} step="1" disabled={!canWrite} />
          </FieldGroup>

          <FieldGroup title="Copies and drawer">
            <NumberField label="Default copies" value={printer.defaultCopies} onChange={(v) => patch(printer.id, { defaultCopies: v })} step="1" disabled={!canWrite} />
            <CheckField label="Extra copy on card sales" checked={printer.extraCopyOnCard} onChange={(v) => patch(printer.id, { extraCopyOnCard: v })} disabled={!canWrite} />
            <CheckField label="Open the drawer on every print" checked={printer.openDrawerOnPrint} onChange={(v) => patch(printer.id, { openDrawerOnPrint: v })} disabled={!canWrite} />
            <CheckField label="Active" checked={printer.isActive} onChange={(v) => patch(printer.id, { isActive: v })} disabled={!canWrite} />
          </FieldGroup>
        </SettingsSection>
      ))}
    </>
  );
}

// --- Hardware ------------------------------------------------------------------------------------

function HardwareTab({
  locationId,
  scales,
  readers,
  poleDisplays,
  canWrite,
  onSaved,
}: {
  locationId: number;
  scales: ScaleSettings[];
  readers: ReaderSettings[];
  poleDisplays: PoleDisplaySettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [scaleDrafts, setScaleDrafts] = useState(scales);
  const [readerDrafts, setReaderDrafts] = useState(readers);
  const [poleDrafts, setPoleDrafts] = useState(poleDisplays);

  useEffect(() => setScaleDrafts(scales), [scales]);
  useEffect(() => setReaderDrafts(readers), [readers]);
  useEffect(() => setPoleDrafts(poleDisplays), [poleDisplays]);

  const patchScale = (id: number, changes: Partial<ScaleSettings>) =>
    setScaleDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  const patchReader = (id: number, changes: Partial<ReaderSettings>) =>
    setReaderDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  const patchPole = (id: number, changes: Partial<PoleDisplaySettings>) =>
    setPoleDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  const empty = scaleDrafts.length === 0 && readerDrafts.length === 0 && poleDrafts.length === 0;

  return (
    <>
      <TabIntro
        title="Peripherals"
        description="The scales, tag readers and customer displays a till can be pointed at. A station picks which of these it uses on the Stations tab."
      />

      {empty ? (
        <EmptyState
          icon={Cpu}
          title="No scale, reader or pole display profile yet"
          hint="Peripheral profiles are created with the terminal agent, then tuned here."
        />
      ) : null}

      {scaleDrafts.map((scale) => (
        <SettingsSection
          key={String(scale.id)}
          title={`Scale — ${scale.name}`}
          icon={Cpu}
          description="Scales from different makers answer to different letters; a Mettler-Toledo weighs on W."
          footer={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.scale({ locationId, profile: scale }))} /> : null}
          footerNote={canWrite ? null : 'You do not have permission to change hardware.'}
        >
          <FieldGroup title="Serial port" columns={3}>
            <TextField label="Port" value={scale.port} onChange={(v) => patchScale(scale.id, { port: v })} disabled={!canWrite} />
            <NumberField label="Baud rate" value={scale.baudRate} onChange={(v) => patchScale(scale.id, { baudRate: v })} step="1" disabled={!canWrite} />
            <NumberField label="Data bits" value={scale.dataBits} onChange={(v) => patchScale(scale.id, { dataBits: v })} step="1" disabled={!canWrite} />
            <TextField label="Parity" value={scale.parity} onChange={(v) => patchScale(scale.id, { parity: v })} disabled={!canWrite} />
            <TextField label="Stop bits" value={scale.stopBits} onChange={(v) => patchScale(scale.id, { stopBits: v })} disabled={!canWrite} />
          </FieldGroup>

          <FieldGroup title="Commands" columns={3}>
            <TextField label="Weigh command" value={scale.getWeightCommand} onChange={(v) => patchScale(scale.id, { getWeightCommand: v })} disabled={!canWrite} />
            <TextField label="Zero command" value={scale.zeroCommand} onChange={(v) => patchScale(scale.id, { zeroCommand: v })} disabled={!canWrite} />
            <TextField label="Unit" value={scale.unit} onChange={(v) => patchScale(scale.id, { unit: v })} disabled={!canWrite} />
          </FieldGroup>
        </SettingsSection>
      ))}

      {readerDrafts.map((reader) => (
        <SettingsSection
          key={String(reader.id)}
          title={`RFID reader — ${reader.name}`}
          icon={Cpu}
          description="These thresholds are what keep the shelf behind the till out of the basket. They are found by trial on site."
          footer={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.reader({ locationId, profile: reader }))} /> : null}
          footerNote={canWrite ? null : 'You do not have permission to change hardware.'}
        >
          <FieldGroup title="Connection" columns={3}>
            <TextField label="Host" value={reader.host} onChange={(v) => patchReader(reader.id, { host: v })} disabled={!canWrite} />
            <NumberField label="Port" value={reader.port} onChange={(v) => patchReader(reader.id, { port: v })} step="1" disabled={!canWrite} />
            <SelectField
              label="Protocol"
              value={reader.protocol}
              options={[
                { value: 'Llrp', label: 'LLRP' },
                { value: 'Http', label: 'HTTP' },
                { value: 'Mqtt', label: 'MQTT' },
                { value: 'UhfSerial', label: 'UHF Serial (D2184B and relatives)' },
                { value: 'Simulator', label: 'Simulator' },
              ]}
              onChange={(v) => patchReader(reader.id, { protocol: (v || 'Simulator') as ReaderSettings['protocol'] })}
              disabled={!canWrite}
            />
            <Wide>
              <TextField
                label="Antenna zones"
                value={reader.antennaZones}
                onChange={(v) => patchReader(reader.id, { antennaZones: v })}
                disabled={!canWrite}
                hint="e.g. 1=Checkout;2=Checkout;9=Exit. Only a Checkout antenna may put items in a cart."
              />
            </Wide>
          </FieldGroup>

          <FieldGroup title="What counts as a read" columns={3}>
            <NumberField
              label="RSSI floor (dBm)"
              value={reader.rssiThresholdDbm}
              onChange={(v) => patchReader(reader.id, { rssiThresholdDbm: v })}
              step="1"
              disabled={!canWrite}
              hint="A tag on the next shelf reads weaker than one in the basket."
            />
            <NumberField label="Minimum reads" value={reader.minimumReadCount} onChange={(v) => patchReader(reader.id, { minimumReadCount: v })} step="1" disabled={!canWrite} />
            <NumberField label="Debounce (ms)" value={reader.debounceMs} onChange={(v) => patchReader(reader.id, { debounceMs: v })} step="1" disabled={!canWrite} />
            <NumberField label="Coalesce (ms)" value={reader.coalesceMs} onChange={(v) => patchReader(reader.id, { coalesceMs: v })} step="1" disabled={!canWrite} />
            <NumberField label="Batch size" value={reader.maxBatchSize} onChange={(v) => patchReader(reader.id, { maxBatchSize: v })} step="1" disabled={!canWrite} />
            <Wide>
              <CheckField
                label="Accept batches without confirmation"
                checked={reader.autoAcceptBatches}
                onChange={(v) => patchReader(reader.id, { autoAcceptBatches: v })}
                disabled={!canWrite}
                hint="Only for a well-shielded read zone."
              />
            </Wide>
          </FieldGroup>
        </SettingsSection>
      ))}

      {poleDrafts.map((pole) => (
        <SettingsSection
          key={String(pole.id)}
          title={`Pole display — ${pole.name}`}
          icon={Monitor}
          description="Line lengths differ by model: a PD3000 scrolls 45 characters on line 1 and holds 19 fixed on line 2."
          action={
            pole.isActive ? (
              <span className="pos-badge text-positive">Active</span>
            ) : (
              <span className="pos-badge text-ink-muted">Inactive</span>
            )
          }
          footer={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.poleDisplay({ locationId, profile: pole }))} /> : null}
          footerNote={canWrite ? null : 'You do not have permission to change hardware.'}
        >
          <FieldGroup title="Port and geometry" columns={3}>
            <TextField label="Port" value={pole.port} onChange={(v) => patchPole(pole.id, { port: v })} disabled={!canWrite} />
            <NumberField label="Baud rate" value={pole.baudRate} onChange={(v) => patchPole(pole.id, { baudRate: v })} step="1" disabled={!canWrite} />
            <NumberField label="Line 1 width" value={pole.line1Width} onChange={(v) => patchPole(pole.id, { line1Width: v })} step="1" disabled={!canWrite} />
            <NumberField label="Line 2 width" value={pole.line2Width} onChange={(v) => patchPole(pole.id, { line2Width: v })} step="1" disabled={!canWrite} />
          </FieldGroup>

          <FieldGroup title="What the customer sees">
            <TextField
              label="Idle line 1"
              value={pole.idleLine1}
              onChange={(v) => patchPole(pole.id, { idleLine1: v })}
              disabled={!canWrite}
              hint="What the customer sees between sales."
            />
            <TextField label="Idle line 2" value={pole.idleLine2} onChange={(v) => patchPole(pole.id, { idleLine2: v })} disabled={!canWrite} />
          </FieldGroup>

          <FieldGroup title="Commands" columns={3}>
            <TextField label="Clear command" value={pole.clearCommand} onChange={(v) => patchPole(pole.id, { clearCommand: v })} disabled={!canWrite} />
            <TextField label="Cursor to line 1" value={pole.line1Command} onChange={(v) => patchPole(pole.id, { line1Command: v })} disabled={!canWrite} />
            <TextField label="Cursor to line 2" value={pole.line2Command} onChange={(v) => patchPole(pole.id, { line2Command: v })} disabled={!canWrite} />
            <Wide>
              <CheckField label="Active" checked={pole.isActive} onChange={(v) => patchPole(pole.id, { isActive: v })} disabled={!canWrite} />
            </Wide>
          </FieldGroup>
        </SettingsSection>
      ))}
    </>
  );
}

// --- Groupings -----------------------------------------------------------------------------------

/**
 * Departments and categories (guide p.31).
 *
 * They live in Setup rather than on the item form because they are shared: renaming a department
 * from inside one item, while twenty other items sit in it, hides the consequence. The item count
 * next to each one is there so the consequence is visible before the change is made.
 */
function GroupingsTab({
  locationId,
  canWrite,
  canDelete,
}: {
  locationId: number;
  canWrite: boolean;
  canDelete: boolean;
}) {
  return (
    <>
      <TabIntro
        title="Departments and categories"
        description="The two lists every item is filed under. Both are shared, so a rename here reaches every item already in the group."
      />

      <ReferenceList
        title="Departments"
        hint="Every item is filed under one department, and every sales report groups by it."
        locationId={locationId}
        canWrite={canWrite}
        canDelete={canDelete}
        api={mastersApi.departments}
      />
      <ReferenceList
        title="Categories"
        hint="A second, looser grouping for filtering the browse."
        locationId={locationId}
        canWrite={canWrite}
        canDelete={canDelete}
        api={mastersApi.categories}
      />
    </>
  );
}

function ReferenceList({
  title,
  hint,
  locationId,
  canWrite,
  canDelete,
  api,
}: {
  title: string;
  hint: string;
  locationId: number;
  canWrite: boolean;
  canDelete: boolean;
  api: {
    list: (locationId: number, includeInactive?: boolean) => Promise<ReferenceRow[]>;
    save: (body: unknown) => Promise<ReferenceRow>;
    remove: (id: number) => Promise<void>;
  };
}) {
  const [rows, setRows] = useState<ReferenceRow[]>([]);
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState(false);

  const load = useCallback(() => {
    void api
      .list(locationId, true)
      .then(setRows)
      .catch((error) => toast({ title: `Could not load ${title.toLowerCase()}`, description: describe(error), variant: 'destructive' }))
      .finally(() => setLoaded(true));
  }, [api, locationId, title]);

  useEffect(load, [load]);

  const patch = (id: number, changes: Partial<ReferenceRow>) =>
    setRows((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  const act = async (action: () => Promise<unknown>, success: string) => {
    setBusy(true);

    try {
      await action();
      load();
      toast({ title: success });
    } catch (error) {
      toast({ title: 'Not saved', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <SettingsSection
      title={title}
      icon={FolderTree}
      description={hint}
      action={
        canWrite ? (
          <button
            type="button"
            className="pos-button"
            disabled={busy}
            onClick={() => {
              const name = window.prompt(`Name of the new ${title.slice(0, -1).toLowerCase()}`);
              if (!name) return;

              void act(
                () => api.save({ locationId, id: null, name, code: null, sortOrder: (rows.length + 1) * 10, isActive: true }),
                'Added',
              );
            }}
          >
            <Plus className="h-3.5 w-3.5" aria-hidden />
            Add
          </button>
        ) : null
      }
    >
      {rows.length === 0 ? (
        <p className="rounded-md bg-panel-sunken px-4 py-8 text-center text-body text-ink-muted">
          {loaded ? `No ${title.toLowerCase()} yet.` : `Loading ${title.toLowerCase()}…`}
        </p>
      ) : (
        <div className="divide-y divide-subtle">
          {rows.map((row) => (
            <div key={String(row.id)} className="py-3 first:pt-0 last:pb-0">
              <div className="flex flex-wrap items-end gap-3">
                <div className="grid min-w-0 flex-1 gap-x-3 gap-y-2 sm:grid-cols-[minmax(0,1fr)_7rem_5rem]">
                  <TextField label="Name" value={row.name} onChange={(v) => patch(row.id, { name: v })} disabled={!canWrite} />
                  <TextField label="Code" value={row.code ?? ''} onChange={(v) => patch(row.id, { code: v })} disabled={!canWrite} />
                  <NumberField label="Order" value={row.sortOrder} onChange={(v) => patch(row.id, { sortOrder: v })} step="1" disabled={!canWrite} />
                </div>

                <div className="flex flex-wrap items-center gap-2">
                  <label className="flex items-center gap-1.5 whitespace-nowrap text-label text-ink">
                    <input
                      type="checkbox"
                      checked={row.isActive}
                      disabled={!canWrite}
                      onChange={(event) => patch(row.id, { isActive: event.target.checked })}
                    />
                    Active
                  </label>

                  {canWrite ? (
                    <SaveButton
                      busy={busy}
                      variant="secondary"
                      onClick={() =>
                        void act(
                          () =>
                            api.save({
                              locationId,
                              id: row.id,
                              name: row.name,
                              code: row.code,
                              sortOrder: row.sortOrder,
                              isActive: row.isActive,
                            }),
                          'Saved',
                        )
                      }
                    />
                  ) : null}

                  {canDelete ? (
                    <button
                      type="button"
                      className="pos-button-danger"
                      disabled={busy}
                      onClick={() => void act(() => api.remove(row.id), 'Deleted')}
                      // Refused by the server while items are still filed under it, rather than silently
                      // orphaning them — the count next to the name says whether that will happen.
                      title={row.usageCount > 0 ? `${row.usageCount} items are still filed under this` : undefined}
                    >
                      Delete
                    </button>
                  ) : null}
                </div>
              </div>

              <p className="mt-1.5 text-caption text-ink-muted">
                {row.usageCount} item{row.usageCount === 1 ? '' : 's'} filed under this
              </p>
            </div>
          ))}
        </div>
      )}
    </SettingsSection>
  );
}

// --- Stations ------------------------------------------------------------------------------------

function StationsTab({
  locationId,
  settings,
  canWrite,
  onSaved,
}: {
  locationId: number;
  settings: SettingsSnapshot;
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(settings.stations);

  useEffect(() => setDrafts(settings.stations), [settings.stations]);

  const patch = (id: number, changes: Partial<StationSettings>) =>
    setDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  const body = (station: StationSettings) => ({
    locationId,
    id: station.id || null,
    stationCode: station.stationCode,
    name: station.name,
    fastScanMode: station.fastScanMode,
    autoSaveSales: station.autoSaveSales,
    confirmBeforeSaving: station.confirmBeforeSaving,
    scanRandomWeightBarcodes: station.scanRandomWeightBarcodes,
    defaultTenderTypeId: station.defaultTenderTypeId,
    printerProfileId: station.printerProfileId,
    readerProfileId: station.readerProfileId,
    scaleProfileId: station.scaleProfileId,
    poleDisplayProfileId: station.poleDisplayProfileId,
    readerMode: station.readerMode,
    isActive: station.isActive,
  });

  /** Three states, not two: null defers to the store policy, true and false override it. */
  const tri = (value: boolean | null): '' | 'true' | 'false' => (value === null ? '' : value ? 'true' : 'false');
  const untri = (value: string): boolean | null => (value === '' ? null : value === 'true');

  const overrideOptions: Array<{ value: '' | 'true' | 'false'; label: string }> = [
    { value: '', label: 'Use the store setting' },
    { value: 'true', label: 'On' },
    { value: 'false', label: 'Off' },
  ];

  const addStation = (
    <button
      type="button"
      className="pos-button"
      disabled={busy}
      onClick={() => {
        const code = window.prompt('Station code (1–3 digits)');
        if (!code) return;

        void run(
          () =>
            mastersApi.settings.station({
              locationId,
              id: null,
              stationCode: code,
              name: null,
              fastScanMode: null,
              autoSaveSales: null,
              confirmBeforeSaving: null,
              scanRandomWeightBarcodes: null,
              defaultTenderTypeId: null,
              printerProfileId: null,
              readerProfileId: null,
              scaleProfileId: null,
              poleDisplayProfileId: null,
              readerMode: 'OnDemand',
              isActive: true,
            }),
          'Station added',
        );
      }}
    >
      <Plus className="h-3.5 w-3.5" aria-hidden />
      Add a station
    </button>
  );

  return (
    <>
      <TabIntro
        title="Stations"
        description="One card per till. A station may override the store's selling switches, and it names the peripherals that till is wired to."
        action={canWrite ? addStation : null}
      />

      {drafts.length === 0 ? (
        <EmptyState
          icon={Monitor}
          title="No station yet"
          hint={canWrite ? 'Add one with the code the till reports.' : 'Ask someone with hardware permission to add one.'}
        />
      ) : null}

      {drafts.map((station) => (
        <SettingsSection
          key={String(station.id)}
          title={`Station ${station.stationCode}${station.name ? ` — ${station.name}` : ''}`}
          icon={Monitor}
          description={
            station.agentOnline
              ? `Agent ${station.agentVersion ?? 'unknown'} online.`
              : 'No agent has checked in recently — peripherals on this till will not respond.'
          }
          action={
            <>
              {station.isActive ? null : <span className="pos-badge text-ink-muted">Retired</span>}
              {station.agentOnline ? (
                <span className="pos-badge text-positive">Agent online</span>
              ) : (
                <span className="pos-badge text-warning">Agent offline</span>
              )}
            </>
          }
          footerNote={canWrite ? null : 'You do not have permission to change stations.'}
          footer={
            canWrite ? (
              <>
                {station.isActive ? (
                  <button
                    type="button"
                    className="pos-button-danger"
                    disabled={busy}
                    onClick={() => void run(() => mastersApi.settings.deactivateStation(station.id), 'Station retired')}
                  >
                    Retire this station
                  </button>
                ) : null}
                <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.station(body(station)))} />
              </>
            ) : null
          }
        >
          <FieldGroup title="Identity">
            <TextField label="Station code" value={station.stationCode} onChange={(v) => patch(station.id, { stationCode: v })} disabled={!canWrite} />
            <TextField label="Name" value={station.name ?? ''} onChange={(v) => patch(station.id, { name: v })} disabled={!canWrite} />
          </FieldGroup>

          <FieldGroup title="Overrides" hint="Left on the store setting unless this till genuinely differs." columns={3}>
            <SelectField
              label="Fast scan mode"
              value={tri(station.fastScanMode)}
              options={overrideOptions}
              onChange={(v) => patch(station.id, { fastScanMode: untri(v) })}
              disabled={!canWrite}
            />
            <SelectField
              label="Auto-save sales"
              value={tri(station.autoSaveSales)}
              options={overrideOptions}
              onChange={(v) => patch(station.id, { autoSaveSales: untri(v) })}
              disabled={!canWrite}
            />
            <SelectField
              label="Random-weight barcodes"
              value={tri(station.scanRandomWeightBarcodes)}
              options={overrideOptions}
              onChange={(v) => patch(station.id, { scanRandomWeightBarcodes: untri(v) })}
              disabled={!canWrite}
            />
          </FieldGroup>

          <FieldGroup title="Peripherals" columns={3}>
            <SelectField
              label="Printer"
              value={station.printerProfileId ?? ''}
              options={[{ value: '', label: '— none —' }, ...settings.printers.map((p) => ({ value: p.id, label: p.name }))]}
              onChange={(v) => patch(station.id, { printerProfileId: v || null })}
              disabled={!canWrite}
            />
            <SelectField
              label="RFID reader"
              value={station.readerProfileId ?? ''}
              options={[{ value: '', label: '— none —' }, ...settings.readers.map((r) => ({ value: r.id, label: r.name }))]}
              onChange={(v) => patch(station.id, { readerProfileId: v || null })}
              disabled={!canWrite}
            />
            <SelectField
              label="Scale"
              value={station.scaleProfileId ?? ''}
              options={[{ value: '', label: '— none —' }, ...settings.scales.map((s) => ({ value: s.id, label: s.name }))]}
              onChange={(v) => patch(station.id, { scaleProfileId: v || null })}
              disabled={!canWrite}
            />
            <SelectField
              label="Pole display"
              value={station.poleDisplayProfileId ?? ''}
              options={[{ value: '', label: '— none —' }, ...settings.poleDisplays.map((p) => ({ value: p.id, label: p.name }))]}
              onChange={(v) => patch(station.id, { poleDisplayProfileId: v || null })}
              disabled={!canWrite}
            />
            <SelectField
              label="Reader mode"
              value={station.readerMode}
              options={[
                { value: 'Off', label: 'Off' },
                { value: 'OnDemand', label: 'On demand' },
                { value: 'Continuous', label: 'Continuous' },
              ]}
              onChange={(v) => patch(station.id, { readerMode: (v || 'OnDemand') as StationSettings['readerMode'] })}
              disabled={!canWrite}
            />
          </FieldGroup>
        </SettingsSection>
      ))}
    </>
  );
}

// --- Tenders -------------------------------------------------------------------------------------

function TendersTab({
  locationId,
  rows,
  canWrite,
  onSaved,
}: {
  locationId: number;
  rows: TenderSettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(rows);

  useEffect(() => setDrafts(rows), [rows]);

  const patch = (id: number, changes: Partial<TenderSettings>) =>
    setDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  return (
    <>
      <TabIntro
        title="Tenders"
        description="The buttons on the pay screen, and what each one does to the drawer, to change and to the day's close."
      />

      {drafts.length === 0 ? <EmptyState icon={CreditCard} title="No tender types yet" /> : null}

      {drafts.map((tender) => (
        <SettingsSection
          key={String(tender.id)}
          title={`${tender.displayName} (${tender.code})`}
          icon={CreditCard}
          description="The accounting key must match the account name in the accounting system exactly."
          action={
            tender.isActive ? (
              <span className="pos-badge text-positive">Active</span>
            ) : (
              <span className="pos-badge text-ink-muted">Inactive</span>
            )
          }
          footerNote={canWrite ? null : 'You do not have permission to change tenders.'}
          footer={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.tender({ locationId, tender }))} /> : null}
        >
          <FieldGroup title="The button">
            <TextField label="Label on the button" value={tender.displayName} onChange={(v) => patch(tender.id, { displayName: v })} disabled={!canWrite} />
            <NumberField label="Sort order" value={tender.sortOrder} onChange={(v) => patch(tender.id, { sortOrder: v })} step="1" disabled={!canWrite} />
          </FieldGroup>

          <FieldGroup title="What it does">
            <CheckField label="Opens the cash drawer" checked={tender.opensCashDrawer} onChange={(v) => patch(tender.id, { opensCashDrawer: v })} disabled={!canWrite} />
            <CheckField label="Accepts over-tender and gives change" checked={tender.allowsOverTender} onChange={(v) => patch(tender.id, { allowsOverTender: v })} disabled={!canWrite} />
            <CheckField label="Rounds to the smallest coin" checked={tender.roundsToMinimumTender} onChange={(v) => patch(tender.id, { roundsToMinimumTender: v })} disabled={!canWrite} />
            <CheckField label="Counts as cash at close" checked={tender.countsTowardsDrawerCash} onChange={(v) => patch(tender.id, { countsTowardsDrawerCash: v })} disabled={!canWrite} />
            <CheckField label="Requires a reference" checked={tender.requiresReference} onChange={(v) => patch(tender.id, { requiresReference: v })} disabled={!canWrite} />
            <CheckField label="Prints a signature copy" checked={tender.printsSignatureCopy} onChange={(v) => patch(tender.id, { printsSignatureCopy: v })} disabled={!canWrite} />
            <CheckField label="Allowed for refunds" checked={tender.allowedForRefunds} onChange={(v) => patch(tender.id, { allowedForRefunds: v })} disabled={!canWrite} />
          </FieldGroup>

          <FieldGroup title="Accounting">
            <TextField
              label="Accounting key"
              value={tender.externalAccountingKey ?? ''}
              onChange={(v) => patch(tender.id, { externalAccountingKey: v })}
              disabled={!canWrite}
            />
            <CheckField label="Active" checked={tender.isActive} onChange={(v) => patch(tender.id, { isActive: v })} disabled={!canWrite} />
          </FieldGroup>
        </SettingsSection>
      ))}
    </>
  );
}

// --- Currencies ----------------------------------------------------------------------------------

function CurrenciesTab({
  locationId,
  rows,
  canWrite,
  onSaved,
}: {
  locationId: number;
  rows: CurrencySettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(rows);

  useEffect(() => setDrafts(rows), [rows]);

  const patch = (id: number, changes: Partial<CurrencySettings>) =>
    setDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  return (
    <>
      <TabIntro
        title="Currencies"
        description="How money is written and what it rounds to. The base currency is the one every ledger is denominated in."
      />

      {drafts.length === 0 ? <EmptyState icon={Coins} title="No currencies yet" /> : null}

      {drafts.map((currency) => (
        <SettingsSection
          key={String(currency.id)}
          title={`${currency.code} — ${currency.name}`}
          icon={Coins}
          description={
            currency.isBaseCurrency
              ? 'The minimum tender is what cash and change round to. A store that abolished the penny sets 0.05 here.'
              : undefined
          }
          action={
            <>
              {currency.isBaseCurrency ? <span className="pos-badge text-accent-text">Base currency</span> : null}
              {currency.isActive ? (
                <span className="pos-badge text-positive">Active</span>
              ) : (
                <span className="pos-badge text-ink-muted">Inactive</span>
              )}
            </>
          }
          footerNote={canWrite ? null : 'You do not have permission to change currencies.'}
          footer={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.currency({ locationId, currency }))} /> : null}
        >
          <FieldGroup title="How it is written" columns={3}>
            {/*
              The code is editable, and on the base currency that is a bigger deal than it looks —
              hence a hint that says what changing it does and, just as importantly, what it does
              not. It renames the money; it does not revalue a single stored amount. A shop moving
              from one currency to another needs its prices converted, and nothing here does that.

              Uppercased as it is typed rather than on save, so the field shows what will be stored.
            */}
            <TextField
              label="Code"
              value={currency.code}
              onChange={(v) => patch(currency.id, { code: v.toUpperCase() })}
              hint={
                currency.isBaseCurrency
                  ? 'Three letters (ISO 4217). Renaming the base currency renames what this shop calls its money — no price or ledger figure changes value.'
                  : 'Three letters (ISO 4217).'
              }
              placeholder="PKR"
              disabled={!canWrite}
            />
            <TextField label="Name" value={currency.name} onChange={(v) => patch(currency.id, { name: v })} disabled={!canWrite} />
            <TextField label="Symbol" value={currency.symbol} onChange={(v) => patch(currency.id, { symbol: v })} disabled={!canWrite} />
            <NumberField label="Decimal places" value={currency.scale} onChange={(v) => patch(currency.id, { scale: v })} step="1" disabled={!canWrite} />
          </FieldGroup>

          <FieldGroup title="How it rounds" columns={3}>
            <SelectField
              label="Rounding"
              value={currency.rounding}
              options={[
                { value: 'AwayFromZero', label: 'Away from zero (retail convention)' },
                { value: 'ToEven', label: "To even (banker's)" },
                { value: 'Down', label: 'Toward zero' },
                { value: 'Up', label: 'Away from zero, always' },
              ]}
              onChange={(v) => patch(currency.id, { rounding: (v || 'AwayFromZero') as CurrencySettings['rounding'] })}
              disabled={!canWrite}
            />
            <NumberField label="Minimum tender" value={currency.minimumTender} onChange={(v) => patch(currency.id, { minimumTender: v })} disabled={!canWrite} />
            {!currency.isBaseCurrency ? (
              <NumberField
                label="Exchange rate"
                value={currency.exchangeRate}
                onChange={(v) => patch(currency.id, { exchangeRate: v })}
                step="0.0001"
                disabled={!canWrite}
                hint="Units of this currency per one unit of the base currency."
              />
            ) : null}
            <Wide>
              <CheckField label="Active" checked={currency.isActive} onChange={(v) => patch(currency.id, { isActive: v })} disabled={!canWrite} />
            </Wide>
          </FieldGroup>
        </SettingsSection>
      ))}
    </>
  );
}

// --- Numbering -----------------------------------------------------------------------------------

function NumberingTab({
  locationId,
  rows,
  canWrite,
  onSaved,
}: {
  locationId: number;
  rows: NumberSequenceSettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(rows);

  useEffect(() => setDrafts(rows), [rows]);

  const patch = (id: number, changes: Partial<NumberSequenceSettings>) =>
    setDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  return (
    <SettingsSection
      title="Document numbering"
      icon={Hash}
      description="A migrated store sets these to its old counters so customer 4,182 is followed by 4,183. A number already issued cannot be reused."
    >
      {drafts.length === 0 ? (
        <p className="rounded-md bg-panel-sunken px-4 py-8 text-center text-body text-ink-muted">No sequences yet.</p>
      ) : (
        // Each sequence saves on its own, so the save sits at the end of its own row rather than at
        // the foot of the card — a single footer button here would look as though it saved all eight.
        <div className="divide-y divide-subtle">
          {drafts.map((sequence) => (
            <div key={String(sequence.id)} className="py-4 first:pt-0 last:pb-0">
              <div className="flex flex-wrap items-end gap-3">
                <span className="w-28 shrink-0 pb-1.5 text-body font-medium text-ink">{sequence.kind}</span>

                <div className="grid min-w-0 flex-1 gap-x-3 gap-y-2 sm:grid-cols-3">
                  <TextField label="Prefix" value={sequence.prefix} onChange={(v) => patch(sequence.id, { prefix: v })} disabled={!canWrite} />
                  <NumberField label="Pad width" value={sequence.padWidth} onChange={(v) => patch(sequence.id, { padWidth: v })} step="1" disabled={!canWrite} />
                  <NumberField label="Next number" value={sequence.nextNumber} onChange={(v) => patch(sequence.id, { nextNumber: v })} step="1" disabled={!canWrite} />
                </div>

                {canWrite ? (
                  <SaveButton
                    busy={busy}
                    variant="secondary"
                    onClick={() =>
                      void run(() =>
                        mastersApi.settings.numbering({
                          locationId,
                          kind: sequence.kind,
                          prefix: sequence.prefix,
                          padWidth: sequence.padWidth,
                          nextNumber: sequence.nextNumber,
                        }),
                      )
                    }
                  />
                ) : null}
              </div>

              <p className="mt-2 text-caption text-ink-muted">
                Next will read <span className="pos-amount text-ink">{sequence.sample}</span>
                {sequence.highWaterMark > 0 ? ` · highest issued ${sequence.highWaterMark}` : ''}
              </p>
            </div>
          ))}
        </div>
      )}
    </SettingsSection>
  );
}

// --- Pricing ladder ------------------------------------------------------------------------------

const RULE_LABELS: Record<string, string> = {
  manual: 'Manual price override',
  randomWeight: 'Weighed barcode price',
  bonus: 'Buy X get Y',
  break: 'Volume break point',
  requestedLevel: 'Price level chosen at the till',
  clientLevel: "Customer's price level",
  sale: 'Sale window',
  regular: 'Regular price',
};

function PricingTab({
  locationId,
  rows,
  canWrite,
  onSaved,
}: {
  locationId: number;
  rows: PricingRuleSettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(rows);

  useEffect(() => setDrafts(rows), [rows]);

  const move = (index: number, delta: number) => {
    const target = index + delta;
    if (target < 0 || target >= drafts.length) return;

    const next = [...drafts];
    [next[index], next[target]] = [next[target], next[index]];
    setDrafts(next.map((rule, position) => ({ ...rule, order: (position + 1) * 10 })));
  };

  return (
    <SettingsSection
      title="Price precedence"
      icon={ListOrdered}
      description="The first rule that matches sets the unit price. Reordering these rows is how the store changes its pricing policy — there is no code change behind it."
      footerNote={canWrite ? 'The order is not saved until you say so.' : 'You do not have permission to change pricing.'}
      footer={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.pricingLadder({ locationId, rules: drafts }))} /> : null}
    >
      <ol className="divide-y divide-subtle">
        {drafts.map((rule, index) => {
          const label = RULE_LABELS[rule.ruleKey] ?? rule.ruleKey;

          return (
            <li key={String(rule.id)} className="flex flex-wrap items-center gap-x-3 gap-y-2 py-2.5 first:pt-0 last:pb-0">
              <span
                aria-hidden
                className="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-panel-sunken text-caption font-semibold tabular-nums text-ink-muted"
              >
                {index + 1}
              </span>

              <span className={cn('min-w-0 flex-1 text-body', rule.enabled ? 'text-ink' : 'text-ink-muted line-through')}>
                {label}
              </span>

              {/* Off is said in words as well as drawn: a struck-through label alone is a distinction
                  a reader can miss, and this is the list that decides what a customer is charged. */}
              {rule.enabled ? null : <span className="pos-badge text-ink-muted">Never consulted</span>}

              <label className="flex items-center gap-1.5 whitespace-nowrap text-label text-ink">
                <input
                  type="checkbox"
                  checked={rule.enabled}
                  disabled={!canWrite}
                  onChange={(event) =>
                    setDrafts((current) =>
                      current.map((r) => (r.id === rule.id ? { ...r, enabled: event.target.checked } : r)),
                    )
                  }
                />
                Enabled
              </label>

              <span className="flex items-center gap-1">
                <button
                  type="button"
                  className="pos-button px-2"
                  aria-label={`Move ${label} up`}
                  disabled={!canWrite || index === 0}
                  onClick={() => move(index, -1)}
                >
                  <ArrowUp className="h-3.5 w-3.5" aria-hidden />
                </button>
                <button
                  type="button"
                  className="pos-button px-2"
                  aria-label={`Move ${label} down`}
                  disabled={!canWrite || index === drafts.length - 1}
                  onClick={() => move(index, 1)}
                >
                  <ArrowDown className="h-3.5 w-3.5" aria-hidden />
                </button>
              </span>
            </li>
          );
        })}
      </ol>
    </SettingsSection>
  );
}

// --- Users ---------------------------------------------------------------------------------------

const LEVEL_LABELS = ['0 — Trainee', '1 — Cashier', '2 — Senior cashier', '3 — Supervisor', '4 — Administrator'];

const BLANK_COLLEAGUE = {
  email: '',
  firstName: '',
  lastName: '',
  staffCode: '',
  password: '',
  role: '',
  accessLevel: 1,
  pin: '',
};

/**
 * Onboarding a colleague.
 *
 * Creates the sign-in and the staff record in one go, because one without the other is useless: a
 * sign-in with no staff profile cannot be attributed a sale, and a staff profile with no sign-in
 * cannot get to a till.
 */
function NewColleague({ locationId, onCreated }: { locationId: number; onCreated: () => void | Promise<void> }) {
  const { busy, run } = useSaver(onCreated);
  const [form, setForm] = useState(BLANK_COLLEAGUE);
  const [roles, setRoles] = useState<import('@/types/masters').AssignableRole[]>([]);
  const [open, setOpen] = useState(false);

  const patch = (changes: Partial<typeof BLANK_COLLEAGUE>) => setForm((current) => ({ ...current, ...changes }));

  const chosenRole = roles.find((role) => role.name === form.role);

  // The roles come from the server rather than a constant here, so a deployment that adds one does
  // not need a rebuilt front end to be able to assign it.
  useEffect(() => {
    if (!open || roles.length > 0) return;

    let cancelled = false;

    void mastersApi.staff
      .roles()
      .then((found) => {
        if (cancelled) return;
        setRoles(found);
        setForm((current) => (current.role ? current : { ...current, role: found[0]?.name ?? '' }));
      })
      .catch(() => {
        /* The picker falls back to a free-text role name; the server is the one that validates it. */
      });

    return () => {
      cancelled = true;
    };
  }, [open, roles.length]);

  if (!open) {
    return (
      <SettingsSection
        title="Add a colleague"
        icon={Users}
        description="Creates a sign-in and a staff record together, so the new person can both log in and be attributed a sale."
        action={
          <button type="button" className="pos-button-primary" onClick={() => setOpen(true)}>
            Add a colleague
          </button>
        }
      >
        {null}
      </SettingsSection>
    );
  }

  const submit = () =>
    void run(async () => {
      await mastersApi.staff.create({
        email: form.email,
        firstName: form.firstName,
        lastName: form.lastName,
        staffCode: form.staffCode,
        password: form.password,
        role: form.role,
        accessLevel: form.accessLevel,
        locationId,
        pin: form.pin.trim() === '' ? null : form.pin.trim(),
      });

      // Cleared on success only. A failed attempt keeps what was typed, so a rejected password does
      // not cost the person the other seven fields.
      setForm(BLANK_COLLEAGUE);
      setOpen(false);
    }, 'Colleague added');

  return (
    <SettingsSection
      title="Add a colleague"
      icon={Users}
      description="They can sign in as soon as this is saved. Give them the password in person and ask them to change it."
      action={
        <button type="button" className="pos-button" onClick={() => setOpen(false)} disabled={busy}>
          Cancel
        </button>
      }
      footer={<SaveButton busy={busy} onClick={submit} label="Create" />}
    >
      <FieldGroup title="Identity" columns={3}>
        <TextField label="First name" value={form.firstName} onChange={(v) => patch({ firstName: v })} autoFocus />
        <TextField label="Last name" value={form.lastName} onChange={(v) => patch({ lastName: v })} />
        <TextField
          label="Staff code"
          value={form.staffCode}
          onChange={(v) => patch({ staffCode: v })}
          hint="Short, and printed on receipts — e.g. SK."
        />
      </FieldGroup>

      <FieldGroup title="Sign-in" columns={2}>
        <TextField
          label="Email"
          value={form.email}
          onChange={(v) => patch({ email: v })}
          placeholder="sam@yourshop.com"
          hint="This is what they sign in with."
        />
        <PasswordField
          label="Temporary password"
          value={form.password}
          onChange={(v) => patch({ password: v })}
          hint="They should change it once they are in."
        />
      </FieldGroup>

      {/*
        One control, not two.
        <p>
          Role and Access level asked the same question twice. Every seeded role carries the level it
          means — Trainee is 0, Cashier 1, up to Administrator at 4 — and picking a role already moved
          the level to match. So the pair could be left disagreeing: choose Cashier, then set the
          level to Manager, and the form showed a Cashier while the server was told something else.
          Which one won was not visible from the screen.
        </p>
        <p>
          The role is the question worth asking, because it is the one phrased in terms of the shop —
          what this person is allowed to do — rather than a number from the old system. The level is
          derived and shown, so nothing is hidden, just no longer separately editable.
        </p>
      */}
      <FieldGroup title="Access" columns={2}>
        <SelectField
          label="Role"
          value={form.role}
          options={
            roles.length > 0
              ? roles.map((role) => ({ value: role.name, label: role.description ? `${role.name} — ${role.description}` : role.name }))
              : [{ value: form.role, label: form.role || 'Loading…' }]
          }
          onChange={(v) => {
            const chosen = roles.find((role) => role.name === v);
            patch({ role: v, accessLevel: chosen?.legacyLevel ?? form.accessLevel });
          }}
          hint={
            chosenRole?.legacyLevel != null
              ? `What they are allowed to do. Access level ${chosenRole.legacyLevel} — ${LEVEL_LABELS[chosenRole.legacyLevel] ?? ''}.`
              : 'What they are allowed to do.'
          }
        />
        <TextField
          label="Till PIN (optional)"
          value={form.pin}
          onChange={(v) => patch({ pin: v.replace(/\D/g, '') })}
          hint="Four digits or more, for fast-switching at a till."
        />

        {/*
          A role added later that maps to no level cannot have one derived, and the server still needs
          one. Rather than guess, the picker comes back — but only for the role that actually needs it,
          instead of sitting on the form permanently contradicting the roles that do not.
        */}
        {chosenRole != null && chosenRole.legacyLevel == null ? (
          <SelectField
            label="Access level"
            value={String(form.accessLevel)}
            options={LEVEL_LABELS.map((label, level) => ({ value: String(level), label }))}
            onChange={(v) => patch({ accessLevel: Number(v) || 0 })}
            hint="This role does not imply a level, so choose one."
          />
        ) : null}
      </FieldGroup>
    </SettingsSection>
  );
}

/** An administrator setting someone's password for them — the answer to a locked-out cashier. */
/**
 * Taking somebody's access away, behind a confirmation.
 *
 * Two clicks rather than one, and the second one names the person. This sits directly under a
 * password field on a screen an administrator uses in a hurry, and "are you sure?" that does not
 * say who is a question nobody reads.
 *
 * The word is "Revoke access" rather than "Delete" because that is what happens. The staff record
 * stays — it is what a sale is attributed to and what an audit entry points at — and the server
 * refuses two cases outright: removing your own access, and removing the last administrator. Both
 * end with nobody able to sign in.
 */
function RevokeAccess({
  staff,
  onDone,
}: {
  staff: import('@/types/masters').StaffSettings;
  onDone: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onDone);
  const [confirming, setConfirming] = useState(false);

  if (!staff.isActive) {
    return (
      <FieldGroup title="Access">
        <p className="text-body text-ink-muted">
          This person cannot sign in. Their record and history are kept.
        </p>
        <SaveButton
          busy={busy}
          variant="secondary"
          label="Restore access"
          onClick={() => void run(() => mastersApi.staff.reactivate(staff.id), 'Access restored')}
        />
      </FieldGroup>
    );
  }

  return (
    <FieldGroup title="Access">
      {confirming ? (
        <div className="rounded border border-negative/40 bg-negative/10 p-3">
          <p className="text-body font-semibold text-ink">
            Remove access for {staff.firstName} {staff.lastName}?
          </p>
          <p className="mt-0.5 text-body text-ink-muted">
            They will not be able to sign in. Their sales, hours and audit history are kept, and you
            can restore access later.
          </p>

          <div className="mt-2 flex gap-2">
            <button
              type="button"
              className="pos-button-danger px-3"
              disabled={busy}
              onClick={() =>
                void run(async () => {
                  await mastersApi.staff.deactivate(staff.id);
                  setConfirming(false);
                }, 'Access removed')
              }
            >
              {busy ? 'Removing…' : 'Remove access'}
            </button>
            <button type="button" className="pos-button px-3" disabled={busy} onClick={() => setConfirming(false)}>
              Cancel
            </button>
          </div>
        </div>
      ) : (
        <button type="button" className="pos-button px-3 text-negative" onClick={() => setConfirming(true)}>
          Remove access…
        </button>
      )}
    </FieldGroup>
  );
}

function ResetPassword({ staffId, onDone }: { staffId: number; onDone: () => void | Promise<void> }) {
  const { busy, run } = useSaver(onDone);
  const [password, setPassword] = useState('');

  return (
    <FieldGroup title="Password">
      <PasswordField
        label="Set a new password"
        value={password}
        onChange={setPassword}
        hint="Ends any session they currently have. Leave blank to make no change."
      />
      <SaveButton
        busy={busy}
        variant="secondary"
        label="Set password"
        onClick={() =>
          void run(async () => {
            await mastersApi.staff.resetPassword(staffId, password);
            setPassword('');
          }, 'Password set')
        }
      />
    </FieldGroup>
  );
}

/**
 * The sign-in behind a member of staff, read-only.
 *
 * Everything here is state rather than a secret, and that is the design rather than an oversight.
 * An administrator asks four questions about an account — what does this person sign in with, what
 * are they allowed to do, can they get in, and if not why not — and none of the answers requires
 * showing anything that would let somebody else sign in as them. There is no reveal control because
 * there is nothing behind it: the password is a hash the server will not project, and the PIN is
 * the same. The way to give somebody access is to set a new password, not to look up the old one.
 *
 * Read-only on purpose too. The email and the role are changed through the flows that also update
 * Identity; an editable field here would be a text box that silently disagreed with what the person
 * actually signs in with.
 */
function AccountSummary({ staff }: { staff: import('@/types/masters').StaffSettings }) {
  const lockedUntil = staff.lockedOutUntil ? new Date(staff.lockedOutUntil) : null;

  return (
    <FieldGroup title="Sign-in">
      <dl className="grid gap-x-6 gap-y-2 text-body sm:grid-cols-[auto_1fr]">
        <dt className="text-ink-muted">Signs in with</dt>
        <dd className="text-ink">
          {staff.email ?? <span className="text-ink-muted">No sign-in — this person cannot log in at all.</span>}
          {staff.email && !staff.emailConfirmed ? (
            <span className="pos-badge ml-2 text-warning">Unconfirmed</span>
          ) : null}
        </dd>

        <dt className="text-ink-muted">Allowed to</dt>
        <dd className="text-ink">
          {staff.roles && staff.roles.length > 0 ? (
            staff.roles.join(', ')
          ) : (
            <span className="text-ink-muted">No role</span>
          )}
        </dd>

        <dt className="text-ink-muted">Status</dt>
        <dd>
          {/*
            Locked out and disabled both mean "cannot get in", and saying which matters: one clears
            itself in a quarter of an hour, the other needs somebody to act. Telling a manager only
            that access is refused sends them resetting a password that was never the problem.
          */}
          {lockedUntil ? (
            <span className="text-negative">
              Locked out until {lockedUntil.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} after
              too many wrong passwords. It clears itself.
            </span>
          ) : staff.canSignIn ? (
            <span className="text-positive">Can sign in</span>
          ) : (
            <span className="text-ink-muted">Cannot sign in. Access was revoked, not locked.</span>
          )}
        </dd>
      </dl>

      <p className="text-caption text-ink-muted">
        Passwords and PINs are stored hashed and cannot be read back — not here, not anywhere. If
        someone is locked out, set them a new password below.
      </p>
    </FieldGroup>
  );
}

function UsersTab({
  locationId,
  rows,
  canWrite,
  onSaved,
}: {
  locationId: number;
  rows: import('@/types/masters').StaffSettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(rows);

  useEffect(() => setDrafts(rows), [rows]);

  const patch = (id: number, changes: Partial<import('@/types/masters').StaffSettings>) =>
    setDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  return (
    <>
      <TabIntro
        title="Users and access"
        description="Staff codes, access levels and PIN state. Authorisation is always by permission; the level is only a preset."
      />

      {canWrite ? <NewColleague locationId={locationId} onCreated={onSaved} /> : null}

      {drafts.length === 0 ? (
        <EmptyState icon={Users} title="No staff profiles yet" hint="Everyone who rings a sale needs one, so the sale can be attributed." />
      ) : null}

      {drafts.map((staff) => (
        <SettingsSection
          key={String(staff.id)}
          title={`${staff.staffCode} — ${staff.firstName} ${staff.lastName}`}
          icon={Users}
          description={
            staff.pinLocked
              ? 'PIN locked after too many wrong attempts. A supervisor can unlock it from the till.'
              : staff.hasPin
                ? 'A PIN is set. It is stored hashed and can never be read back.'
                : 'No PIN set — this person cannot fast-switch at a till.'
          }
          action={
            <>
              {staff.pinLocked ? (
                <span className="pos-badge text-negative">PIN locked</span>
              ) : staff.hasPin ? (
                <span className="pos-badge text-positive">PIN set</span>
              ) : (
                <span className="pos-badge text-warning">No PIN</span>
              )}
              {staff.lockedOutUntil ? <span className="pos-badge text-negative">Locked out</span> : null}
              {staff.isActive ? null : <span className="pos-badge text-ink-muted">Inactive</span>}
            </>
          }
          footerNote={canWrite ? null : 'You do not have permission to change users.'}
          footer={
            canWrite ? (
              <SaveButton
                busy={busy}
                onClick={() =>
                  void run(() =>
                    mastersApi.settings.staff({
                      locationId,
                      id: staff.id,
                      userId: staff.userId,
                      staffCode: staff.staffCode,
                      firstName: staff.firstName,
                      lastName: staff.lastName,
                      accessLevel: staff.accessLevel,
                      isActive: staff.isActive,
                    }),
                  )
                }
              />
            ) : null
          }
        >
          <AccountSummary staff={staff} />

          <FieldGroup title="Identity" columns={3}>
            <TextField label="Staff code" value={staff.staffCode} onChange={(v) => patch(staff.id, { staffCode: v })} disabled={!canWrite} />
            <TextField label="First name" value={staff.firstName} onChange={(v) => patch(staff.id, { firstName: v })} disabled={!canWrite} />
            <TextField label="Last name" value={staff.lastName} onChange={(v) => patch(staff.id, { lastName: v })} disabled={!canWrite} />
          </FieldGroup>

          <FieldGroup title="Access">
            <SelectField
              label="Access level"
              value={String(staff.accessLevel)}
              options={LEVEL_LABELS.map((label, level) => ({ value: String(level), label }))}
              onChange={(v) => patch(staff.id, { accessLevel: Number(v) || 0 })}
              disabled={!canWrite}
              hint="A preset for the permission set. Authorisation is always by permission, never by level."
            />
            <CheckField label="Active" checked={staff.isActive} onChange={(v) => patch(staff.id, { isActive: v })} disabled={!canWrite} />
          </FieldGroup>

          {canWrite ? <ResetPassword staffId={staff.id} onDone={onSaved} /> : null}
          {canWrite ? <RevokeAccess staff={staff} onDone={onSaved} /> : null}
        </SettingsSection>
      ))}
    </>
  );
}
