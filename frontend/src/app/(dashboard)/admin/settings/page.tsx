'use client';

import { useCallback, useEffect, useState } from 'react';
import { CheckField, Field, FormSection, NumberField, SelectField, TextField } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { useLiveGrid } from '@/lib/inventory-hub';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { cn } from '@/lib/utils';
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
 */

const TABS = [
  'Business ID',
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

export default function SettingsPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canWrite = auth.can('settings.write');
  const canTaxes = auth.can('settings.taxes');
  const canHardware = auth.can('settings.hardware');
  const canUsers = auth.can('users.manage');

  const [tab, setTab] = useState<Tab>('Business ID');
  const [settings, setSettings] = useState<SettingsSnapshot | null>(null);
  const [loading, setLoading] = useState(true);

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
  useLiveGrid<{ id: string }>('settings', locationId, () => {}, {
    onSettingsChanged: () => void load(),
  });

  if (!locationId) {
    return <p className="text-body text-ink-muted">No location is attached to this session.</p>;
  }

  if (loading || !settings) {
    return <p className="text-body text-ink-muted">Loading settings…</p>;
  }

  return (
    <div className="space-y-3">
      <h1 className="text-h3 font-semibold">Setup</h1>

      <nav className="flex flex-wrap gap-1 border-b border-subtle">
        {TABS.map((name) => (
          <button
            key={name}
            type="button"
            onClick={() => setTab(name)}
            className={cn(
              'px-3 py-1.5 text-body',
              tab === name
                ? 'border-b-2 border-accent font-medium'
                : 'text-ink-muted hover:text-ink',
            )}
          >
            {name}
          </button>
        ))}
      </nav>

      <div className="max-w-3xl">
        {tab === 'Business ID' ? <BusinessTab locationId={locationId} value={settings.business} canWrite={canWrite} onSaved={load} /> : null}
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
    </div>
  );
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

function SaveButton({ busy, onClick, label = 'Save' }: { busy: boolean; onClick: () => void; label?: string }) {
  return (
    <button type="button" className="underline" disabled={busy} onClick={onClick}>
      {label}
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
  locationId: string;
  value: BusinessSettings;
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const [form, setForm] = useState(value);
  const { busy, run } = useSaver(onSaved);

  useEffect(() => setForm(value), [value]);

  const patch = (changes: Partial<BusinessSettings>) => setForm((current) => ({ ...current, ...changes }));

  return (
    <FormSection
      title="Business identity"
      hint="This is what prints at the top of every receipt, invoice and statement."
      actions={
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
      <TextField label="Business name" value={form.businessName} onChange={(v) => patch({ businessName: v })} disabled={!canWrite} />
      <TextField
        label="Address line 1"
        value={form.address.line1 ?? ''}
        onChange={(v) => patch({ address: { ...form.address, line1: v } })}
        disabled={!canWrite}
      />
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
      <TextField label="Licence number" value={form.licenceNumber ?? ''} onChange={(v) => patch({ licenceNumber: v })} disabled={!canWrite} />
      <TextField
        label="Tax registration number"
        value={form.taxRegistrationNumber ?? ''}
        onChange={(v) => patch({ taxRegistrationNumber: v })}
        disabled={!canWrite}
      />

      <hr className="border-subtle" />

      <TextField label="Location name" value={form.locationName} onChange={(v) => patch({ locationName: v })} disabled={!canWrite} />
      <Field label="Location code">
        <p className="text-body">{form.legacyCode} — fixed at creation, because migrated data and old reports refer to it.</p>
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
      <Field label="Base currency">
        <p className="text-body">{form.baseCurrencyCode} — every ledger is denominated in it and it cannot be changed here.</p>
      </Field>
    </FormSection>
  );
}

// --- Taxes ---------------------------------------------------------------------------------------

function TaxesTab({
  locationId,
  rows,
  canWrite,
  onSaved,
}: {
  locationId: string;
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
    return <p className="text-body text-ink-muted">No tax configuration yet.</p>;
  }

  const patch = (changes: Partial<TaxSettings>) => setForm((c) => (c ? { ...c, ...changes } : c));

  return (
    <>
      <FormSection
        title="Taxes and charges"
        hint="Saving schedules a new effective-dated row. The current one is kept so a reprint of an old invoice still shows the tax that was charged."
        actions={
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
        <TextField
          label="Takes effect on"
          value={effectiveFrom}
          onChange={setEffectiveFrom}
          disabled={!canWrite}
          hint="Cannot be backdated: sales already rung would change retroactively."
        />

        <CheckField label="Tax 1 enabled" checked={form.tax1Enabled} onChange={(v) => patch({ tax1Enabled: v })} disabled={!canWrite} />
        <TextField label="Tax 1 name" value={form.tax1Name} onChange={(v) => patch({ tax1Name: v })} disabled={!canWrite} />
        <NumberField label="Tax 1 rate %" value={form.tax1Rate} onChange={(v) => patch({ tax1Rate: v })} step="0.0001" disabled={!canWrite} />

        <CheckField label="Tax 2 enabled" checked={form.tax2Enabled} onChange={(v) => patch({ tax2Enabled: v })} disabled={!canWrite} />
        <TextField label="Tax 2 name" value={form.tax2Name} onChange={(v) => patch({ tax2Name: v })} disabled={!canWrite} />
        <NumberField label="Tax 2 rate %" value={form.tax2Rate} onChange={(v) => patch({ tax2Rate: v })} step="0.0001" disabled={!canWrite} />
        <CheckField
          label="Tax 2 compounds on tax 1"
          checked={form.tax2Compound}
          onChange={(v) => patch({ tax2Compound: v })}
          disabled={!canWrite}
          hint="Unusual, but required in some jurisdictions."
        />

        <CheckField
          label="Add-on charge enabled"
          checked={form.addOnChargeEnabled}
          onChange={(v) => patch({ addOnChargeEnabled: v })}
          disabled={!canWrite}
        />
        <TextField label="Add-on charge name" value={form.addOnChargeName} onChange={(v) => patch({ addOnChargeName: v })} disabled={!canWrite} />
        <NumberField
          label="Add-on charge rate %"
          value={form.addOnChargeRate}
          onChange={(v) => patch({ addOnChargeRate: v })}
          step="0.0001"
          disabled={!canWrite}
        />
        <CheckField
          label="Add-on charge is itself taxable"
          checked={form.addOnChargeTaxable}
          onChange={(v) => patch({ addOnChargeTaxable: v })}
          disabled={!canWrite}
        />

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
      </FormSection>

      <FormSection title="History" hint="Every rate that has ever applied, and when.">
        <table className="w-full text-label">
          <thead className="text-ink-muted">
            <tr>
              <th className="text-left">From</th>
              <th className="text-left">To</th>
              <th className="text-right">Tax 1</th>
              <th className="text-right">Tax 2</th>
              <th className="text-left">Pricing</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id ?? row.effectiveFrom} className={row.isCurrent ? 'font-medium' : undefined}>
                <td>{row.effectiveFrom}</td>
                <td>{row.effectiveTo ?? '—'}</td>
                <td className="pos-amount text-right">{row.tax1Enabled ? `${row.tax1Rate}%` : 'off'}</td>
                <td className="pos-amount text-right">{row.tax2Enabled ? `${row.tax2Rate}%` : 'off'}</td>
                <td>{row.taxationType}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </FormSection>
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
  locationId: string;
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
    <FormSection
      title="Point of sale"
      hint="Store-wide defaults. A station can override the selling switches on the Stations tab."
      actions={
        canWrite ? (
          <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.pos({ locationId, settings: form }))} />
        ) : null
      }
    >
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

      <hr className="border-subtle" />

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

      <hr className="border-subtle" />

      <CheckField label="Attribute every sale to a staff member" checked={form.trackStaffSales} onChange={(v) => patch({ trackStaffSales: v })} disabled={!canWrite} />
      <CheckField
        label="A supervisor must approve a void"
        checked={form.requireSupervisorToVoid}
        onChange={(v) => patch({ requireSupervisorToVoid: v })}
        disabled={!canWrite}
      />
      <CheckField label="Use the employee time clock" checked={form.useEmployeeTimeClock} onChange={(v) => patch({ useEmployeeTimeClock: v })} disabled={!canWrite} />

      <hr className="border-subtle" />

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
    </FormSection>
  );
}

// --- Printers ------------------------------------------------------------------------------------

function PrintersTab({
  locationId,
  rows,
  canWrite,
  onSaved,
}: {
  locationId: string;
  rows: PrinterSettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(rows);

  useEffect(() => setDrafts(rows), [rows]);

  const patch = (id: string, changes: Partial<PrinterSettings>) =>
    setDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  return (
    <>
      {drafts.map((printer) => (
        <FormSection
          key={printer.id}
          title={printer.name}
          hint="Every escape sequence is decimal ASCII. Epson cuts with 27,105; Star with 27,100,48."
          actions={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.printer({ locationId, profile: printer }))} /> : null}
        >
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
          <TextField label="Setup command" value={printer.setupCommand ?? ''} onChange={(v) => patch(printer.id, { setupCommand: v })} disabled={!canWrite} />
          <TextField label="Cutter command" value={printer.cutterCommand ?? ''} onChange={(v) => patch(printer.id, { cutterCommand: v })} disabled={!canWrite} />
          <TextField
            label="Drawer kick"
            value={printer.drawerTrigger}
            onChange={(v) => patch(printer.id, { drawerTrigger: v })}
            disabled={!canWrite}
            hint="27,112,0,50,250 is the Epson pulse. Test it before a shift, not during one."
          />
          <NumberField label="Drawer repeat" value={printer.drawerRepeat} onChange={(v) => patch(printer.id, { drawerRepeat: v })} step="1" disabled={!canWrite} />
          <NumberField label="Default copies" value={printer.defaultCopies} onChange={(v) => patch(printer.id, { defaultCopies: v })} step="1" disabled={!canWrite} />
          <CheckField label="Extra copy on card sales" checked={printer.extraCopyOnCard} onChange={(v) => patch(printer.id, { extraCopyOnCard: v })} disabled={!canWrite} />
          <CheckField label="Open the drawer on every print" checked={printer.openDrawerOnPrint} onChange={(v) => patch(printer.id, { openDrawerOnPrint: v })} disabled={!canWrite} />
          <CheckField label="Active" checked={printer.isActive} onChange={(v) => patch(printer.id, { isActive: v })} disabled={!canWrite} />
        </FormSection>
      ))}

      {canWrite ? (
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
          Add a printer
        </button>
      ) : null}
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
  locationId: string;
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

  const patchScale = (id: string, changes: Partial<ScaleSettings>) =>
    setScaleDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  const patchReader = (id: string, changes: Partial<ReaderSettings>) =>
    setReaderDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  const patchPole = (id: string, changes: Partial<PoleDisplaySettings>) =>
    setPoleDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  return (
    <>
      {scaleDrafts.map((scale) => (
        <FormSection
          key={scale.id}
          title={`Scale — ${scale.name}`}
          hint="Scales from different makers answer to different letters; a Mettler-Toledo weighs on W."
          actions={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.scale({ locationId, profile: scale }))} /> : null}
        >
          <TextField label="Port" value={scale.port} onChange={(v) => patchScale(scale.id, { port: v })} disabled={!canWrite} />
          <NumberField label="Baud rate" value={scale.baudRate} onChange={(v) => patchScale(scale.id, { baudRate: v })} step="1" disabled={!canWrite} />
          <NumberField label="Data bits" value={scale.dataBits} onChange={(v) => patchScale(scale.id, { dataBits: v })} step="1" disabled={!canWrite} />
          <TextField label="Parity" value={scale.parity} onChange={(v) => patchScale(scale.id, { parity: v })} disabled={!canWrite} />
          <TextField label="Stop bits" value={scale.stopBits} onChange={(v) => patchScale(scale.id, { stopBits: v })} disabled={!canWrite} />
          <TextField label="Weigh command" value={scale.getWeightCommand} onChange={(v) => patchScale(scale.id, { getWeightCommand: v })} disabled={!canWrite} />
          <TextField label="Zero command" value={scale.zeroCommand} onChange={(v) => patchScale(scale.id, { zeroCommand: v })} disabled={!canWrite} />
          <TextField label="Unit" value={scale.unit} onChange={(v) => patchScale(scale.id, { unit: v })} disabled={!canWrite} />
        </FormSection>
      ))}

      {readerDrafts.map((reader) => (
        <FormSection
          key={reader.id}
          title={`RFID reader — ${reader.name}`}
          hint="These thresholds are what keep the shelf behind the till out of the basket. They are found by trial on site."
          actions={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.reader({ locationId, profile: reader }))} /> : null}
        >
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
          <TextField
            label="Antenna zones"
            value={reader.antennaZones}
            onChange={(v) => patchReader(reader.id, { antennaZones: v })}
            disabled={!canWrite}
            hint="e.g. 1=Checkout;2=Checkout;9=Exit. Only a Checkout antenna may put items in a cart."
          />
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
          <CheckField
            label="Accept batches without confirmation"
            checked={reader.autoAcceptBatches}
            onChange={(v) => patchReader(reader.id, { autoAcceptBatches: v })}
            disabled={!canWrite}
            hint="Only for a well-shielded read zone."
          />
        </FormSection>
      ))}

      {poleDrafts.map((pole) => (
        <FormSection
          key={pole.id}
          title={`Pole display — ${pole.name}`}
          hint="Line lengths differ by model: a PD3000 scrolls 45 characters on line 1 and holds 19 fixed on line 2."
          actions={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.poleDisplay({ locationId, profile: pole }))} /> : null}
        >
          <TextField label="Port" value={pole.port} onChange={(v) => patchPole(pole.id, { port: v })} disabled={!canWrite} />
          <NumberField label="Baud rate" value={pole.baudRate} onChange={(v) => patchPole(pole.id, { baudRate: v })} step="1" disabled={!canWrite} />
          <NumberField label="Line 1 width" value={pole.line1Width} onChange={(v) => patchPole(pole.id, { line1Width: v })} step="1" disabled={!canWrite} />
          <NumberField label="Line 2 width" value={pole.line2Width} onChange={(v) => patchPole(pole.id, { line2Width: v })} step="1" disabled={!canWrite} />
          <TextField
            label="Idle line 1"
            value={pole.idleLine1}
            onChange={(v) => patchPole(pole.id, { idleLine1: v })}
            disabled={!canWrite}
            hint="What the customer sees between sales."
          />
          <TextField label="Idle line 2" value={pole.idleLine2} onChange={(v) => patchPole(pole.id, { idleLine2: v })} disabled={!canWrite} />
          <TextField label="Clear command" value={pole.clearCommand} onChange={(v) => patchPole(pole.id, { clearCommand: v })} disabled={!canWrite} />
          <TextField label="Cursor to line 1" value={pole.line1Command} onChange={(v) => patchPole(pole.id, { line1Command: v })} disabled={!canWrite} />
          <TextField label="Cursor to line 2" value={pole.line2Command} onChange={(v) => patchPole(pole.id, { line2Command: v })} disabled={!canWrite} />
          <CheckField label="Active" checked={pole.isActive} onChange={(v) => patchPole(pole.id, { isActive: v })} disabled={!canWrite} />
        </FormSection>
      ))}

      {scaleDrafts.length === 0 && readerDrafts.length === 0 && poleDrafts.length === 0 ? (
        <p className="text-body text-ink-muted">No scale, reader or pole display profile has been created yet.</p>
      ) : null}
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
  locationId: string;
  canWrite: boolean;
  canDelete: boolean;
}) {
  return (
    <>
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
  locationId: string;
  canWrite: boolean;
  canDelete: boolean;
  api: {
    list: (locationId: string, includeInactive?: boolean) => Promise<ReferenceRow[]>;
    save: (body: unknown) => Promise<ReferenceRow>;
    remove: (id: string) => Promise<void>;
  };
}) {
  const [rows, setRows] = useState<ReferenceRow[]>([]);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    void api
      .list(locationId, true)
      .then(setRows)
      .catch((error) => toast({ title: `Could not load ${title.toLowerCase()}`, description: describe(error), variant: 'destructive' }));
  }, [api, locationId, title]);

  useEffect(load, [load]);

  const patch = (id: string, changes: Partial<ReferenceRow>) =>
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
    <FormSection
      title={title}
      hint={hint}
      actions={
        canWrite ? (
          <button
            type="button"
            className="underline"
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
            Add
          </button>
        ) : null
      }
    >
      {rows.length === 0 ? <p className="text-label text-ink-muted">None yet.</p> : null}

      {rows.map((row) => (
        <div key={row.id} className="grid grid-cols-[1fr_7rem_5rem_auto_auto] items-end gap-2 border-b border-subtle pb-2">
          <TextField label="Name" value={row.name} onChange={(v) => patch(row.id, { name: v })} disabled={!canWrite} />
          <TextField label="Code" value={row.code ?? ''} onChange={(v) => patch(row.id, { code: v })} disabled={!canWrite} />
          <NumberField label="Order" value={row.sortOrder} onChange={(v) => patch(row.id, { sortOrder: v })} step="1" disabled={!canWrite} />

          <label className="mb-1.5 flex items-center gap-1 text-label">
            <input
              type="checkbox"
              checked={row.isActive}
              disabled={!canWrite}
              onChange={(event) => patch(row.id, { isActive: event.target.checked })}
            />
            active
          </label>

          <span className="mb-1 flex items-center gap-2 text-label">
            {canWrite ? (
              <button
                type="button"
                className="underline"
                disabled={busy}
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
              >
                Save
              </button>
            ) : null}

            {canDelete ? (
              <button
                type="button"
                className="underline text-negative"
                disabled={busy}
                onClick={() => void act(() => api.remove(row.id), 'Deleted')}
                // Refused by the server while items are still filed under it, rather than silently
                // orphaning them — the count next to the name says whether that will happen.
                title={row.usageCount > 0 ? `${row.usageCount} items are still filed under this` : undefined}
              >
                Delete
              </button>
            ) : null}
          </span>

          <span className="col-span-5 text-caption text-ink-muted">
            {row.usageCount} item{row.usageCount === 1 ? '' : 's'}
          </span>
        </div>
      ))}
    </FormSection>
  );
}

// --- Stations ------------------------------------------------------------------------------------

function StationsTab({
  locationId,
  settings,
  canWrite,
  onSaved,
}: {
  locationId: string;
  settings: SettingsSnapshot;
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(settings.stations);

  useEffect(() => setDrafts(settings.stations), [settings.stations]);

  const patch = (id: string, changes: Partial<StationSettings>) =>
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

  return (
    <>
      {drafts.map((station) => (
        <FormSection
          key={station.id}
          title={`Station ${station.stationCode}${station.name ? ` — ${station.name}` : ''}`}
          hint={
            station.agentOnline
              ? `Agent ${station.agentVersion ?? 'unknown'} online.`
              : 'No agent has checked in recently — peripherals on this till will not respond.'
          }
          actions={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.station(body(station)))} /> : null}
        >
          <TextField label="Station code" value={station.stationCode} onChange={(v) => patch(station.id, { stationCode: v })} disabled={!canWrite} />
          <TextField label="Name" value={station.name ?? ''} onChange={(v) => patch(station.id, { name: v })} disabled={!canWrite} />

          <SelectField
            label="Fast scan mode"
            value={tri(station.fastScanMode)}
            options={[
              { value: '', label: 'Use the store setting' },
              { value: 'true', label: 'On' },
              { value: 'false', label: 'Off' },
            ]}
            onChange={(v) => patch(station.id, { fastScanMode: untri(v) })}
            disabled={!canWrite}
          />
          <SelectField
            label="Auto-save sales"
            value={tri(station.autoSaveSales)}
            options={[
              { value: '', label: 'Use the store setting' },
              { value: 'true', label: 'On' },
              { value: 'false', label: 'Off' },
            ]}
            onChange={(v) => patch(station.id, { autoSaveSales: untri(v) })}
            disabled={!canWrite}
          />
          <SelectField
            label="Random-weight barcodes"
            value={tri(station.scanRandomWeightBarcodes)}
            options={[
              { value: '', label: 'Use the store setting' },
              { value: 'true', label: 'On' },
              { value: 'false', label: 'Off' },
            ]}
            onChange={(v) => patch(station.id, { scanRandomWeightBarcodes: untri(v) })}
            disabled={!canWrite}
          />

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

          {canWrite && station.isActive ? (
            <button
              type="button"
              className="pos-button text-negative"
              disabled={busy}
              onClick={() => void run(() => mastersApi.settings.deactivateStation(station.id), 'Station retired')}
            >
              Retire this station
            </button>
          ) : null}
        </FormSection>
      ))}

      {canWrite ? (
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
          Add a station
        </button>
      ) : null}
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
  locationId: string;
  rows: TenderSettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(rows);

  useEffect(() => setDrafts(rows), [rows]);

  const patch = (id: string, changes: Partial<TenderSettings>) =>
    setDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  return (
    <>
      {drafts.map((tender) => (
        <FormSection
          key={tender.id}
          title={`${tender.displayName} (${tender.code})`}
          hint="The accounting key must match the account name in the accounting system exactly."
          actions={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.tender({ locationId, tender }))} /> : null}
        >
          <TextField label="Label on the button" value={tender.displayName} onChange={(v) => patch(tender.id, { displayName: v })} disabled={!canWrite} />
          <NumberField label="Sort order" value={tender.sortOrder} onChange={(v) => patch(tender.id, { sortOrder: v })} step="1" disabled={!canWrite} />
          <CheckField label="Opens the cash drawer" checked={tender.opensCashDrawer} onChange={(v) => patch(tender.id, { opensCashDrawer: v })} disabled={!canWrite} />
          <CheckField label="Accepts over-tender and gives change" checked={tender.allowsOverTender} onChange={(v) => patch(tender.id, { allowsOverTender: v })} disabled={!canWrite} />
          <CheckField label="Rounds to the smallest coin" checked={tender.roundsToMinimumTender} onChange={(v) => patch(tender.id, { roundsToMinimumTender: v })} disabled={!canWrite} />
          <CheckField label="Counts as cash at close" checked={tender.countsTowardsDrawerCash} onChange={(v) => patch(tender.id, { countsTowardsDrawerCash: v })} disabled={!canWrite} />
          <CheckField label="Requires a reference" checked={tender.requiresReference} onChange={(v) => patch(tender.id, { requiresReference: v })} disabled={!canWrite} />
          <CheckField label="Prints a signature copy" checked={tender.printsSignatureCopy} onChange={(v) => patch(tender.id, { printsSignatureCopy: v })} disabled={!canWrite} />
          <CheckField label="Allowed for refunds" checked={tender.allowedForRefunds} onChange={(v) => patch(tender.id, { allowedForRefunds: v })} disabled={!canWrite} />
          <TextField
            label="Accounting key"
            value={tender.externalAccountingKey ?? ''}
            onChange={(v) => patch(tender.id, { externalAccountingKey: v })}
            disabled={!canWrite}
          />
          <CheckField label="Active" checked={tender.isActive} onChange={(v) => patch(tender.id, { isActive: v })} disabled={!canWrite} />
        </FormSection>
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
  locationId: string;
  rows: CurrencySettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(rows);

  useEffect(() => setDrafts(rows), [rows]);

  const patch = (id: string, changes: Partial<CurrencySettings>) =>
    setDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  return (
    <>
      {drafts.map((currency) => (
        <FormSection
          key={currency.id}
          title={`${currency.code} — ${currency.name}${currency.isBaseCurrency ? ' (base)' : ''}`}
          hint={
            currency.isBaseCurrency
              ? 'The minimum tender is what cash and change round to. A store that abolished the penny sets 0.05 here.'
              : undefined
          }
          actions={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.currency({ locationId, currency }))} /> : null}
        >
          <TextField label="Name" value={currency.name} onChange={(v) => patch(currency.id, { name: v })} disabled={!canWrite} />
          <TextField label="Symbol" value={currency.symbol} onChange={(v) => patch(currency.id, { symbol: v })} disabled={!canWrite} />
          <NumberField label="Decimal places" value={currency.scale} onChange={(v) => patch(currency.id, { scale: v })} step="1" disabled={!canWrite} />
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
          <CheckField label="Active" checked={currency.isActive} onChange={(v) => patch(currency.id, { isActive: v })} disabled={!canWrite} />
        </FormSection>
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
  locationId: string;
  rows: NumberSequenceSettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(rows);

  useEffect(() => setDrafts(rows), [rows]);

  const patch = (id: string, changes: Partial<NumberSequenceSettings>) =>
    setDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  return (
    <FormSection
      title="Document numbering"
      hint="A migrated store sets these to its old counters so customer 4,182 is followed by 4,183. A number already issued cannot be reused."
    >
      {drafts.map((sequence) => (
        <div key={sequence.id} className="grid grid-cols-[7rem_1fr_1fr_1fr_auto] items-end gap-2 border-b border-subtle pb-2">
          <span className="pb-1 text-body">{sequence.kind}</span>

          <TextField label="Prefix" value={sequence.prefix} onChange={(v) => patch(sequence.id, { prefix: v })} disabled={!canWrite} />
          <NumberField label="Pad width" value={sequence.padWidth} onChange={(v) => patch(sequence.id, { padWidth: v })} step="1" disabled={!canWrite} />
          <NumberField label="Next number" value={sequence.nextNumber} onChange={(v) => patch(sequence.id, { nextNumber: v })} step="1" disabled={!canWrite} />

          {canWrite ? (
            <SaveButton
              busy={busy}
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

          <span className="col-span-5 text-label text-ink-muted">
            Next will read <span className="pos-amount">{sequence.sample}</span>
            {sequence.highWaterMark > 0 ? ` · highest issued ${sequence.highWaterMark}` : ''}
          </span>
        </div>
      ))}
    </FormSection>
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
  locationId: string;
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
    <FormSection
      title="Price precedence"
      hint="The first rule that matches sets the unit price. Reordering these rows is how the store changes its pricing policy — there is no code change behind it."
      actions={canWrite ? <SaveButton busy={busy} onClick={() => void run(() => mastersApi.settings.pricingLadder({ locationId, rules: drafts }))} /> : null}
    >
      <ol className="space-y-1">
        {drafts.map((rule, index) => (
          <li key={rule.id} className="flex items-center gap-2 border-b border-subtle py-1 text-body">
            <span className="w-6 text-ink-muted">{index + 1}</span>
            <span className="flex-1">{RULE_LABELS[rule.ruleKey] ?? rule.ruleKey}</span>

            <label className="flex items-center gap-1 text-label">
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
              on
            </label>

            <button type="button" className="pos-button" disabled={!canWrite || index === 0} onClick={() => move(index, -1)}>
              ↑
            </button>
            <button
              type="button"
              className="pos-button"
              disabled={!canWrite || index === drafts.length - 1}
              onClick={() => move(index, 1)}
            >
              ↓
            </button>
          </li>
        ))}
      </ol>
    </FormSection>
  );
}

// --- Users ---------------------------------------------------------------------------------------

const LEVEL_LABELS = ['0 — Trainee', '1 — Cashier', '2 — Senior cashier', '3 — Supervisor', '4 — Administrator'];

function UsersTab({
  locationId,
  rows,
  canWrite,
  onSaved,
}: {
  locationId: string;
  rows: import('@/types/masters').StaffSettings[];
  canWrite: boolean;
  onSaved: () => void | Promise<void>;
}) {
  const { busy, run } = useSaver(onSaved);
  const [drafts, setDrafts] = useState(rows);

  useEffect(() => setDrafts(rows), [rows]);

  const patch = (id: string, changes: Partial<import('@/types/masters').StaffSettings>) =>
    setDrafts((current) => current.map((row) => (row.id === id ? { ...row, ...changes } : row)));

  return (
    <>
      {drafts.map((staff) => (
        <FormSection
          key={staff.id}
          title={`${staff.staffCode} — ${staff.firstName} ${staff.lastName}`}
          hint={
            staff.pinLocked
              ? 'PIN locked after too many wrong attempts. A supervisor can unlock it from the till.'
              : staff.hasPin
                ? 'A PIN is set. It is stored hashed and can never be read back.'
                : 'No PIN set — this person cannot fast-switch at a till.'
          }
          actions={
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
          <TextField label="Staff code" value={staff.staffCode} onChange={(v) => patch(staff.id, { staffCode: v })} disabled={!canWrite} />
          <TextField label="First name" value={staff.firstName} onChange={(v) => patch(staff.id, { firstName: v })} disabled={!canWrite} />
          <TextField label="Last name" value={staff.lastName} onChange={(v) => patch(staff.id, { lastName: v })} disabled={!canWrite} />
          <SelectField
            label="Access level"
            value={String(staff.accessLevel)}
            options={LEVEL_LABELS.map((label, level) => ({ value: String(level), label }))}
            onChange={(v) => patch(staff.id, { accessLevel: Number(v) || 0 })}
            disabled={!canWrite}
            hint="A preset for the permission set. Authorisation is always by permission, never by level."
          />
          <CheckField label="Active" checked={staff.isActive} onChange={(v) => patch(staff.id, { isActive: v })} disabled={!canWrite} />
        </FormSection>
      ))}

      {drafts.length === 0 ? <p className="text-body text-ink-muted">No staff profiles yet.</p> : null}
    </>
  );
}
