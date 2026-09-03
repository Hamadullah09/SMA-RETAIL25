'use client';

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import {
  Activity,
  AlertTriangle,
  CheckCircle2,
  HelpCircle,
  Plus,
  PlugZap,
  Radio,
  RadioTower,
  RefreshCw,
  Send,
  WifiOff,
  type LucideIcon,
} from 'lucide-react';
import {
  BEEPER_LABELS,
  LINK_PROFILE_LABELS,
  REGION_LABELS,
  rfidApi,
  type AgentStatus,
  type BeeperMode,
  type RadioRegion,
  type ReaderConnectionSnapshot,
  type ReaderDiagnostics,
  type ReaderProfile,
  type RfLinkProfile,
} from '@/lib/rfid-api';
import { CheckField, FormSection, NumberField, SelectField, TextField } from '@/components/masters/browse-form';
import { TagImportSection } from '@/components/masters/tag-import';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { cn } from '@/lib/utils';
import { PageHeader as SharedPageHeader } from '@/components/shell/page-header';

/**
 * RFID reader configuration.
 *
 * Everything the reader's own Windows utility can set, set from here instead. The point is not that
 * the vendor's tool is bad — it is that a reader configured by hand is configured on one device. A
 * shop that swaps a failed unit for the spare in the drawer then has a till nobody can explain, and
 * the settings that made the old one work exist only in somebody's memory. Here they are a row in
 * the database, and the terminal agent pushes them into whatever hardware it finds on every connect.
 *
 * The right-hand column is the opposite: what the device actually reports, read live. The two
 * together answer the question that matters when a basket will not scan — not "what did we ask for"
 * but "what is it doing".
 */
export default function RfidSettingsPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canWrite = auth.can('terminals.register');

  const [readers, setReaders] = useState<ReaderProfile[]>([]);
  const [selected, setSelected] = useState<number | null>(null);
  const [draft, setDraft] = useState<ReaderProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (!locationId) return;

    setLoading(true);

    try {
      const rows = await rfidApi.list(locationId);
      setReaders(rows);
      setSelected((current) => current ?? rows[0]?.id ?? null);
    } catch (error) {
      toast({ title: 'Could not load readers', description: describe(error), variant: 'destructive' });
    } finally {
      setLoading(false);
    }
  }, [locationId]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    setDraft(readers.find((r) => r.id === selected) ?? null);
  }, [readers, selected]);

  const patch = (changes: Partial<ReaderProfile>) =>
    setDraft((current) => (current ? { ...current, ...changes } : current));

  /**
   * Adds a reader with the defaults the domain already carries, then selects it.
   *
   * Deliberately not a blank form: a reader profile has twenty fields and nineteen of them have a
   * sensible default, so asking for all twenty before anything exists is how a shop ends up with no
   * second reader. Give it a host and a port and it is a reader.
   */
  const addReader = async () => {
    if (!locationId) return;

    setBusy(true);

    try {
      const created = await rfidApi.create(locationId, {
        name: `Reader ${readers.length + 1}`,
        host: '192.168.0.1',
        port: 4001,
        protocol: 'UhfSerial',
      });

      toast({ title: `${created.name} added`, description: 'Set its address and antennas, then save.' });
      await load();
      setSelected(created.id);
    } catch (error) {
      toast({ title: 'Could not add the reader', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const save = async () => {
    if (!draft) return;

    setBusy(true);

    try {
      const saved = await rfidApi.save(draft);
      setReaders((current) => current.map((r) => (r.id === saved.id ? saved : r)));
      setDraft(saved);

      toast({
        title: 'Saved',
        description: 'Tills pick this up on their next profile refresh, or press "Send to reader now".',
      });
    } catch (error) {
      toast({ title: 'Not saved', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  if (!locationId) {
    return (
      <div className="p-4 lg:p-6">
        <PageHeader title="RFID readers" lede="Reader settings live here rather than in the device." />
        <section className="pos-panel mt-4">
          <EmptyState
            icon={RadioTower}
            title="No location is selected for this account"
            hint="A reader profile belongs to a location. Sign in against one, or ask an administrator to attach a location to your account."
          />
        </section>
      </div>
    );
  }

  return (
    <div className="space-y-4 p-4 lg:p-6">
      <PageHeader
        title="RFID readers"
        lede="These settings live here rather than in the device, so a reader can be replaced without anyone having to remember how the old one was set up."
      >
        {readers.length > 1 ? (
          <select
            className="pos-input"
            value={selected ?? ''}
            onChange={(event) => setSelected(Number(event.target.value))}
            aria-label="Reader"
          >
            {readers.map((reader) => (
              <option key={String(reader.id)} value={reader.id}>
                {reader.name}
              </option>
            ))}
          </select>
        ) : null}

        {canWrite ? (
          <button type="button" className="pos-button-primary" disabled={busy} onClick={() => void addReader()}>
            <Plus className="h-3.5 w-3.5" aria-hidden />
            Add reader
          </button>
        ) : null}
      </PageHeader>

      <AgentConnection />

      {loading ? (
        <section className="pos-panel">
          <p className="px-4 py-12 text-center text-body text-ink-muted">Loading…</p>
        </section>
      ) : !draft ? (
        <section className="pos-panel">
          <EmptyState
            icon={RadioTower}
            title="No reader is configured for this location"
            hint={
              canWrite
                ? 'Press “Add reader” at the top of the page. It starts with the defaults that suit most units — give it an address and a port and it is a reader.'
                : 'Ask someone with the terminals.register permission to add one. Until then this till cannot read a tag.'
            }
          />
        </section>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_22rem]">
          <div className="min-w-0">
            <ConnectionSection draft={draft} patch={patch} canWrite={canWrite} busy={busy} onSave={save} />
            <RadioSection draft={draft} patch={patch} canWrite={canWrite} busy={busy} onSave={save} />
            <ReadZoneSection draft={draft} patch={patch} canWrite={canWrite} busy={busy} onSave={save} />

            {/*
              Configuring a reader and giving it something to recognise are the same job in
              practice: a correctly tuned reader that reads nothing but unknown tags is indist-
              inguishable, at the counter, from a broken one.
            */}
            <TagImportSection locationId={locationId} canWrite={auth.can('inventory.commission_tags')} />
          </div>

          <DiagnosticsPanel profile={draft} canWrite={canWrite} />
        </div>
      )}
    </div>
  );
}

/* ------------------------------------------------------------------ configuration */

interface SectionProps {
  draft: ReaderProfile;
  patch: (changes: Partial<ReaderProfile>) => void;
  canWrite: boolean;
  busy: boolean;
  onSave: () => void | Promise<void>;
}

function SaveAction({ canWrite, busy, onSave }: { canWrite: boolean; busy: boolean; onSave: () => void | Promise<void> }) {
  return canWrite ? (
    <button type="button" className="pos-button" disabled={busy} onClick={() => void onSave()}>
      Save
    </button>
  ) : null;
}

/**
 * Whether this till is holding its reader, and a button to take it.
 *
 * Sits above the settings because it answers the question that brings people here. The settings say
 * what was asked for; this says whether any of it is happening — and, when it is not, which of the
 * two unrelated reasons it is, because they have nothing to do with each other:
 *
 *   the agent is not running on this machine — nothing can read, whatever the reader is doing; or
 *   the agent is running and the reader will not answer — almost always because something else has
 *   it, since the bridge in front of these units accepts exactly one connection at a time.
 *
 * Distinguishing those two is the entire value here. Before this, both looked like a dot that was
 * not green.
 */
function AgentConnection() {
  const [status, setStatus] = useState<AgentStatus | null>(null);
  const [server, setServer] = useState<ReaderConnectionSnapshot | null>(null);
  const [checked, setChecked] = useState(false);
  const [busy, setBusy] = useState(false);

  // The server is asked first, because it is the one place that knows whether a local agent is even
  // expected. Where the server holds the readers, no agent runs on this machine by design — and
  // this panel used to report that as "Agent not running", in red, on a shop whose reader was
  // reading perfectly well.
  const refresh = useCallback(async () => {
    const snapshot = await rfidApi.serverConnections();
    setServer(snapshot);
    setStatus(snapshot?.serverHosted ? null : await rfidApi.status());
    setChecked(true);
  }, []);

  useEffect(() => {
    void refresh();
    const timer = window.setInterval(() => void refresh(), 5000);

    return () => window.clearInterval(timer);
  }, [refresh]);

  const connect = async () => {
    setBusy(true);

    try {
      const next = await rfidApi.connect('OnDemand');
      setStatus(next);
      setChecked(true);

      toast(
        next?.reader.online
          ? { title: 'Reader connected', description: next.reader.device }
          : {
              title: next ? 'The reader did not answer' : 'The terminal agent is not running',
              description: next
                ? 'Check it is powered on and on the network, and that no other program is holding it.'
                : 'Start the agent on this till, then try again.',
              variant: 'destructive',
            },
      );
    } finally {
      setBusy(false);
    }
  };

  // Where the server holds the readers, its answer is the only one that matters — there is no agent
  // on this machine and none is wanted.
  const serverHosted = server?.serverHosted === true;
  const serverReaders = server?.readers ?? [];
  const serverLive = serverReaders.filter((r) => r.connected);

  const online = serverHosted ? serverLive.length > 0 : status?.reader.online === true;

  /*
   * Four cues, not one: a glyph that changes shape, a heading in words, a sentence explaining which
   * of the unrelated failures this is, and — last — a hue. "Reader not answering" and "Agent not
   * running" are different problems with different fixes, and the shape says so before the colour
   * does.
   */
  const state = !checked
    ? {
        icon: HelpCircle,
        tone: 'text-ink-faint',
        heading: 'Checking…',
        detail: 'Asking the server.',
      }
    : serverHosted
      ? serverLive.length > 0
        ? {
            icon: CheckCircle2,
            tone: 'text-positive',
            heading: serverLive.length === 1 ? 'Reader connected' : `${serverLive.length} readers connected`,
            detail: `Held by the server, so this machine needs nothing installed · ${serverLive
              .map((r) => `${r.name} (${r.endpoint})`)
              .join(', ')}`,
          }
        : {
            icon: AlertTriangle,
            tone: 'text-negative',
            heading: serverReaders.length === 0 ? 'No reader is being held' : 'Reader not answering',
            detail:
              serverReaders.length === 0
                ? 'The server holds reader connections for this shop, but has no active reader profile bound to a till. Add one below, or bind an existing one to its station.'
                : `The server is trying but the reader will not answer. Only one program can hold it at a time — close any vendor tool still connected to it. ${serverReaders
                    .map((r) => r.endpoint)
                    .join(', ')}`,
          }
      : online
      ? {
          icon: CheckCircle2,
          tone: 'text-positive',
          heading: 'Reader connected',
          detail: `${status?.reader.device} · mode ${status?.readerMode}`,
        }
      : status
        ? {
            icon: AlertTriangle,
            tone: 'text-negative',
            heading: 'Reader not answering',
            detail:
              'The agent is running but the reader will not answer. Only one program can hold it at a time — close any vendor tool still connected to it.',
          }
        : {
            icon: WifiOff,
            tone: 'text-negative',
            heading: 'Agent not running',
            detail:
              'No agent is running on this machine, so nothing can read a tag here whatever the reader is doing.',
          };

  const StateIcon = state.icon;

  return (
    <section
      className={cn(
        'pos-panel flex flex-wrap items-center justify-between gap-3 p-4',
        checked && !online && 'border-negative/35',
      )}
    >
      <div className="flex min-w-0 items-start gap-2.5">
        <StateIcon className={cn('mt-0.5 h-4 w-4 shrink-0', state.tone)} aria-hidden />

        <div className="min-w-0">
          <p className={cn('text-body-lg font-semibold', checked && !online ? state.tone : 'text-ink')}>
            {state.heading}
          </p>
          <p className="mt-0.5 max-w-[76ch] text-body leading-relaxed text-ink-muted">{state.detail}</p>
        </div>
      </div>

      <button type="button" className="pos-button-primary shrink-0" disabled={busy} onClick={() => void connect()}>
        <PlugZap className="h-3.5 w-3.5" aria-hidden />
        {busy ? 'Connecting…' : online ? 'Reconnect' : 'Connect'}
      </button>
    </section>
  );
}

function ConnectionSection({ draft, patch, canWrite, busy, onSave }: SectionProps) {
  return (
    <FormSection
      title="Connection"
      hint="How the till reaches the reader. A unit wired by RS-232 through a serial-to-Ethernet bridge is reached the same way as one with its own network port."
      actions={<SaveAction canWrite={canWrite} busy={busy} onSave={onSave} />}
    >
      <TextField label="Name" value={draft.name} onChange={(v) => patch({ name: v })} disabled={!canWrite} />

      <SelectField
        label="Protocol"
        value={draft.protocol}
        options={[
          { value: 'UhfSerial', label: 'UHF serial (R2000 family — D2184 and relatives)' },
          { value: 'Llrp', label: 'LLRP' },
          { value: 'Simulator', label: 'Simulator — no hardware' },
        ]}
        onChange={(v) => patch({ protocol: v as ReaderProfile['protocol'] })}
        disabled={!canWrite}
      />

      <TextField label="Address" value={draft.host} onChange={(v) => patch({ host: v })} disabled={!canWrite} />

      <NumberField
        label="Port"
        value={draft.port}
        onChange={(v) => patch({ port: v })}
        step="1"
        disabled={!canWrite}
      />

      <NumberField
        label="RS-485 address"
        value={draft.deviceAddress}
        onChange={(v) => patch({ deviceAddress: v })}
        step="1"
        disabled={!canWrite}
        hint="255 is the protocol's broadcast address: every reader answers to it whatever it has been given. Leave it there unless several readers share one bus."
      />
    </FormSection>
  );
}

function RadioSection({ draft, patch, canWrite, busy, onSave }: SectionProps) {
  const powers = draft.outputPowerDbm.split(',').map((p) => p.trim());
  const perAntenna = powers.length > 1;

  return (
    <FormSection
      title="Radio"
      hint="Transmit power, band and data rate. The band is a licensing matter — a reader on FCC channels in Europe is transmitting without a licence."
      actions={<SaveAction canWrite={canWrite} busy={busy} onSave={onSave} />}
    >
      <TextField
        label="Transmit power (dBm)"
        value={draft.outputPowerDbm}
        onChange={(v) => patch({ outputPowerDbm: v })}
        disabled={!canWrite}
        hint={
          perAntenna
            ? `One figure per antenna port: ${powers.join(', ')}. A checkout antenna wants only enough to reach the counter; a door antenna wants the range.`
            : 'One figure applies to every port. Enter four comma-separated values to set them individually — 0 to 33.'
        }
      />

      <SelectField
        label="Region"
        value={draft.region}
        options={(Object.keys(REGION_LABELS) as RadioRegion[]).map((value) => ({
          value,
          label: REGION_LABELS[value],
        }))}
        onChange={(v) => patch({ region: v as RadioRegion })}
        disabled={!canWrite}
      />

      <NumberField
        label="First channel"
        value={draft.frequencyStartIndex}
        onChange={(v) => patch({ frequencyStartIndex: v })}
        step="1"
        disabled={!canWrite}
        hint={`${draft.frequencyStartMhz} MHz. Channels ${draft.regionMinChannel}–${draft.regionMaxChannel} in this region.`}
      />

      <NumberField
        label="Last channel"
        value={draft.frequencyEndIndex}
        onChange={(v) => patch({ frequencyEndIndex: v })}
        step="1"
        disabled={!canWrite}
        hint={`${draft.frequencyEndMhz} MHz. Narrowing the range helps only where another radio is interfering.`}
      />

      <SelectField
        label="Link profile"
        value={draft.linkProfile}
        options={(Object.keys(LINK_PROFILE_LABELS) as RfLinkProfile[]).map((value) => ({
          value,
          label: LINK_PROFILE_LABELS[value],
        }))}
        onChange={(v) => patch({ linkProfile: v as RfLinkProfile })}
        disabled={!canWrite}
      />

      <SelectField
        label="Buzzer"
        value={draft.beeper}
        options={(Object.keys(BEEPER_LABELS) as BeeperMode[]).map((value) => ({
          value,
          label: BEEPER_LABELS[value],
        }))}
        onChange={(v) => patch({ beeper: v as BeeperMode })}
        disabled={!canWrite}
      />

      <NumberField
        label="Antenna fault threshold (dB)"
        value={draft.antennaReturnLossThresholdDb}
        onChange={(v) => patch({ antennaReturnLossThresholdDb: v })}
        step="1"
        disabled={!canWrite}
        hint="The reader refuses to transmit into a port whose return loss is worse than this. 0 turns the check off — worth leaving on, because transmitting into a disconnected antenna reflects the power back into the output stage."
      />

      <CheckField
        label="Impinj fast TID"
        checked={draft.impinjFastTid}
        onChange={(v) => patch({ impinjFastTid: v })}
        disabled={!canWrite}
      />

      <CheckField
        label="Dense reader mode"
        checked={draft.denseReaderMode}
        onChange={(v) => patch({ denseReaderMode: v })}
        disabled={!canWrite}
      />
    </FormSection>
  );
}

function ReadZoneSection({ draft, patch, canWrite, busy, onSave }: SectionProps) {
  return (
    <FormSection
      title="Read zone"
      hint="What counts as a tag worth putting on a sale. These are the settings that keep the shelf behind the till out of the basket, and the right numbers are found by trial in the actual shop."
      actions={<SaveAction canWrite={canWrite} busy={busy} onSave={onSave} />}
    >
      <TextField
        label="Antenna zones"
        value={draft.antennaZones}
        onChange={(v) => patch({ antennaZones: v })}
        disabled={!canWrite}
        hint="Port=zone pairs, e.g. 1=Checkout;2=Exit. Only Checkout antennas may add to a cart."
      />

      <NumberField
        label="Signal floor (dBm)"
        value={draft.rssiThresholdDbm}
        onChange={(v) => patch({ rssiThresholdDbm: v })}
        step="1"
        disabled={!canWrite}
        hint="Tags fainter than this are ignored. Raise it if the till reads the next counter."
      />

      <NumberField
        label="Minimum reads"
        value={draft.minimumReadCount}
        onChange={(v) => patch({ minimumReadCount: v })}
        step="1"
        disabled={!canWrite}
        hint="How many times a tag must be seen before it counts. Two rejects most stray single reads."
      />

      <NumberField
        label="Ignore repeats for (ms)"
        value={draft.debounceMs}
        onChange={(v) => patch({ debounceMs: v })}
        step="100"
        disabled={!canWrite}
      />

      <CheckField
        label="Add tags to the sale without confirmation"
        checked={draft.autoAcceptBatches}
        onChange={(v) => patch({ autoAcceptBatches: v })}
        disabled={!canWrite}
      />

      <CheckField
        label="Read continuously"
        checked={draft.continuousMode}
        onChange={(v) => patch({ continuousMode: v })}
        disabled={!canWrite}
      />
    </FormSection>
  );
}

/* ------------------------------------------------------------------ diagnostics */

/**
 * What the reader reports, as opposed to what it was told.
 *
 * Worth its own column because the gap between the two is the whole diagnosis. A shop reporting poor
 * range wants to see that the device is transmitting at 2 dBm — not that somebody typed 30 into a
 * form once and it was never accepted.
 */
function DiagnosticsPanel({ profile, canWrite }: { profile: ReaderProfile; canWrite: boolean }) {
  const [diagnostics, setDiagnostics] = useState<ReaderDiagnostics | null>(null);
  const [state, setState] = useState<'idle' | 'reading' | 'offline'>('idle');

  const read = useCallback(async () => {
    setState('reading');
    const result = await rfidApi.diagnostics();
    setDiagnostics(result);
    setState(result ? 'idle' : 'offline');
  }, []);

  useEffect(() => {
    void read();
  }, [read]);

  const send = async () => {
    const result = await rfidApi.applyToDevice();

    if (!result) {
      toast({ title: 'The till agent is not running on this machine', variant: 'destructive' });
      return;
    }

    if (result.applied) {
      toast({ title: 'Sent to the reader' });
    } else {
      toast({
        title: 'The reader would not take everything',
        description: `Refused: ${result.refused.join(', ')}.`,
        variant: 'destructive',
      });
    }

    await read();
  };

  const claimed = profile.outputPowerDbm.split(',').map((p) => Number(p.trim()));
  const actual = diagnostics?.outputPowerDbm ?? null;

  // The comparison that finds the real fault: a setting that was saved but never reached the device.
  const powerDisagrees =
    actual !== null && claimed.length > 0 && actual.some((value, index) => value !== (claimed[index] ?? claimed[0]));

  return (
    <aside className="pos-panel h-fit overflow-hidden">
      <header className="pos-panel-header">
        <span className="pos-panel-title">
          <Activity />
          <span className="truncate">This till&rsquo;s reader</span>
        </span>
        <button
          type="button"
          className="pos-button"
          onClick={() => void read()}
          disabled={state === 'reading'}
        >
          <RefreshCw className="h-3.5 w-3.5" aria-hidden />
          {state === 'reading' ? 'Reading…' : 'Refresh'}
        </button>
      </header>

      <div className="space-y-2 p-4">
        {state === 'offline' ? (
          <div className="flex items-start gap-2 text-body text-ink-muted">
            <WifiOff className="mt-0.5 h-3.5 w-3.5 shrink-0 text-ink-faint" aria-hidden />
            <p>
              The till agent is not answering on this machine. Diagnostics come from the agent, not the server, so this
              panel only works at a till.
            </p>
          </div>
        ) : diagnostics ? (
          <>
            <Reading label="Firmware" value={diagnostics.firmwareVersion} />
            <Reading
              label="Temperature"
              value={diagnostics.temperatureCelsius != null ? `${diagnostics.temperatureCelsius} °C` : null}
            />
            <Reading
              label="Transmit power"
              value={actual ? `${actual.join(', ')} dBm` : null}
              warn={powerDisagrees ? `Saved as ${profile.outputPowerDbm} — the reader is not using it` : undefined}
            />
            <Reading label="Region" value={diagnostics.region} />
            <Reading label="Link profile" value={diagnostics.linkProfile} />
            <Reading label="Working antenna" value={diagnostics.workAntenna?.toString()} />
            <Reading
              label="Fault threshold"
              value={
                diagnostics.antennaReturnLossThresholdDb != null
                  ? `${diagnostics.antennaReturnLossThresholdDb} dB`
                  : null
              }
            />
            <Reading
              label="Inputs"
              value={diagnostics.gpioInputs?.map((high, i) => `${i + 1}:${high ? 'high' : 'low'}`).join(' ')}
            />

            {diagnostics.returnLossDb ? (
              <div className="space-y-2 border-t border-subtle pt-2.5">
                <p className="flex items-center gap-1.5 text-label font-medium text-ink-muted">
                  <Radio className="h-3.5 w-3.5" aria-hidden />
                  Antenna return loss
                </p>
                {Object.entries(diagnostics.returnLossDb).map(([port, db]) => (
                  <Reading
                    key={port}
                    label={`Port ${port}`}
                    value={`${db} dB`}
                    // A port reflecting nearly everything back has a cable off or an antenna damaged.
                    warn={db < 5 ? 'Little or nothing is connected to this port' : undefined}
                  />
                ))}
              </div>
            ) : null}

            {diagnostics.unavailable.length > 0 ? (
              <p className="border-t border-subtle pt-2.5 text-caption text-ink-faint">
                Not reported by this reader: {diagnostics.unavailable.join(', ')}.
              </p>
            ) : null}
          </>
        ) : (
          <p className="text-body text-ink-muted">Reading…</p>
        )}

        {canWrite ? (
          <button type="button" className="pos-button mt-3 w-full" onClick={() => void send()}>
            <Send className="h-3.5 w-3.5" aria-hidden />
            Send settings to reader now
          </button>
        ) : null}
      </div>
    </aside>
  );
}

function Reading({ label, value, warn }: { label: string; value?: string | null; warn?: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3 text-body">
      <span className="shrink-0 text-ink-muted">{label}</span>
      <span className={cn('min-w-0 text-right tabular-nums', warn ? 'font-medium text-warning' : 'text-ink')}>
        {value ?? <span className="text-ink-faint">unknown</span>}
        {warn ? (
          <span className="mt-0.5 flex items-start justify-end gap-1 text-caption font-normal">
            <AlertTriangle className="mt-0.5 h-3 w-3 shrink-0" aria-hidden />
            <span>{warn}</span>
          </span>
        ) : null}
      </span>
    </div>
  );
}

/**
 * The server's own explanation, in preference to the transport's.
 *
 * These calls go through apiClient directly rather than through pos-api's wrapper, so what arrives
 * here is a raw axios error whose message is "Request failed with status code 400" — true, useless,
 * and identical for every possible cause. The API answers with a problem document naming the actual
 * refusal, and it was being thrown away: a save rejected because the frequency range sat outside the
 * selected region's band reported only the status code, so the one field at fault was the one thing
 * the operator could not learn.
 */
function describe(error: unknown): string {
  const problem = (error as { response?: { data?: { detail?: string; title?: string } } })?.response?.data;

  if (problem?.detail || problem?.title) {
    return problem.detail ?? problem.title!;
  }

  return error instanceof Error ? error.message : 'The request was refused.';
}

/* ------------------------------------------------------------------ page furniture */

/**
 * Delegates to the shared header.
 *
 * This was copy-pasted verbatim into six admin screens, and had already drifted: year-end
 * aligned its actions to the bottom while the other five centred them. Kept behind the local
 * name so the call sites in this file do not change.
 */
function PageHeader({ title, lede, children }: { title: string; lede: string; children?: ReactNode }) {
  return <SharedPageHeader title={title} description={lede} actions={children} />;
}

function EmptyState({ icon: Icon, title, hint }: { icon: LucideIcon; title: string; hint: string }) {
  return (
    <div className="flex flex-col items-center gap-1.5 px-4 py-12 text-center">
      <Icon className="mb-1 h-6 w-6 text-ink-faint" aria-hidden />
      <p className="text-body-lg font-medium text-ink">{title}</p>
      <p className="max-w-[52ch] text-body text-ink-muted">{hint}</p>
    </div>
  );
}
