'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { toast } from '@/components/ui/toaster';
import { apiClient } from '@/lib/api-client';
import { rfidApi } from '@/lib/rfid-api';
import { RfidHub } from '@/lib/rfid-hub';
import { cn } from '@/lib/utils';
import type { StationSettings as StationOption } from '@/types/masters';

/**
 * Which machine drives which reader, and what each antenna stands for.
 *
 * The screen exists because the model behind it changed: a reader used to *be* a station, so there
 * was nothing to configure beyond an address. Now one reader's four antennas can serve four
 * different tills, and that mapping is the thing an installer sets up and the thing that goes wrong.
 *
 * Laid out reader by reader rather than as a flat list of assignments, because that is the shape of
 * the hardware in front of the person configuring it: they are stood at a box with four sockets, and
 * the question they are answering is which till each socket watches.
 */

interface AntennaRow {
  antennaNumber: number;
  stationId: number | null;
  stationCode: string | null;
  enabled: boolean;
}

/**
 * Where a reader stands between "something answered on the network" and "tags are arriving".
 *
 * Five states rather than a connected light because they need five different responses: switch it
 * on, assign its antennas, go and look at the PC, go and look at the reader, or nothing at all. The
 * server decides which one applies — the same rule for every screen that asks.
 */
type ReaderState = 'Discovered' | 'Connected' | 'Offline' | 'Error' | 'Disabled';

interface ReaderRow {
  id: number;
  readerKey: string;
  serialNumber: string | null;
  model: string | null;
  host: string;
  port: number;
  protocol: string;
  antennaCount: number;
  deviceId: number | null;
  deviceKey: string | null;
  isEnabled: boolean;
  lastSeen: string | null;
  antennas: AntennaRow[];
  state: ReaderState;
  deviceOnline: boolean;
}

interface DeviceRow {
  id: number;
  deviceKey: string;
  name: string | null;
  hostname: string | null;
  localIpAddresses: string | null;
  agentVersion: string | null;
  isOnline: boolean;
  lastHeartbeat: string | null;
  readerCount: number;
}

interface ReaderStateSummary {
  total: number;
  connected: number;
  offline: number;
  error: number;
  discovered: number;
  disabled: number;
}

interface Topology {
  devices: DeviceRow[];
  readers: ReaderRow[];

  /**
   * Optional only because a browser can outlive a deployment: a page loaded against the previous
   * API and left open will fetch from it until it is reloaded, and a missing count should hide a
   * line rather than blank the screen an installer is working in.
   */
  summary?: ReaderStateSummary;
}

/**
 * What each state means to the person reading it, in the words they would use.
 *
 * The hint is the instruction, not a definition of the word: somebody looking at a red row wants to
 * know where to walk, and "Offline" on its own does not say whether that is the PC or the reader.
 */
const STATE_LABELS: Record<ReaderState, { label: string; hint: string; tone: string }> = {
  Connected: {
    label: 'Connected',
    hint: 'Held by its machine. Tags read here.',
    tone: 'text-positive-text',
  },
  Discovered: {
    label: 'Discovered',
    hint: 'Found on the network. Assign its antennas below to put it to work.',
    tone: 'text-accent-text',
  },
  Error: {
    label: 'Not answering',
    hint: 'Its machine is running and cannot reach the reader. Check power, cable and switch port.',
    tone: 'text-negative-text',
  },
  Offline: {
    label: 'Machine offline',
    hint: 'The PC driving this reader has stopped checking in. Nothing can be said about the reader itself.',
    tone: 'text-warning-text',
  },
  Disabled: {
    label: 'Switched off',
    hint: 'Taken out of service deliberately.',
    tone: 'text-ink-muted',
  },
};

// Stations come from the settings payload the page already holds rather than from a fetch of their
// own: there is no stations endpoint, and adding one to avoid passing a prop would be a round trip
// bought with an API surface.
export function RfidTopologyTab({
  locationId,
  canWrite,
  stations,
}: {
  locationId?: number;
  canWrite: boolean;
  stations: readonly StationOption[];
}) {
  const [topology, setTopology] = useState<Topology | null>(null);
  const [busy, setBusy] = useState(false);
  const [scanning, setScanning] = useState(false);
  const [scanResult, setScanResult] = useState<string | null>(null);
  const [nothingToBringAcross, setNothingToBringAcross] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!locationId) return;

    try {
      const { data } = await apiClient.get(`/rfid-topology?locationId=${locationId}`);

      setTopology(data as Topology);
    } catch {
      toast({ title: 'Could not load the RFID topology', variant: 'destructive' });
    }
  }, [locationId]);

  useEffect(() => {
    void load();
  }, [load]);

  /**
   * Live updates, so a reader plugged in on the shop floor appears here without anybody pressing
   * anything.
   *
   * Watches the store rather than a till: a reader that has just been discovered belongs to no
   * station yet, which is the entire reason this screen exists. The push carries no rows — it says
   * the list has moved, and the rows are then fetched through the endpoint that is already
   * permission-checked.
   *
   * Failing to connect is not worth a toast. The page works without it; it just stops updating on
   * its own, and an administrator on a laptop with a flaky VPN does not need a red box for that.
   */
  const loadRef = useRef(load);
  loadRef.current = load;

  useEffect(() => {
    if (!locationId) return undefined;

    const hub = new RfidHub();
    let live = true;

    void hub
      .connect(null, locationId, {
        onTopologyChanged: () => {
          if (live) void loadRef.current();
        },
      })
      .catch(() => {
        // Nothing to say. The screen still loads and the refresh button still works.
      });

    return () => {
      live = false;
      void hub.disconnect();
    };
  }, [locationId]);

  const assign = async (reader: ReaderRow, antenna: number, stationId: number | null) => {
    setBusy(true);

    try {
      await apiClient.put(`/rfid-topology/readers/${reader.id}/antennas/${antenna}`, {
        stationId,
        enabled: true,
      });

      await load();
    } catch (error) {
      const problem = (error as { response?: { data?: { detail?: string } } })?.response?.data;
      toast({
        title: 'Could not assign that antenna',
        description: problem?.detail ?? 'Something went wrong.',
        variant: 'destructive',
      });
    } finally {
      setBusy(false);
    }
  };

  /**
   * Asks the agent on *this* machine to sweep *its* network now.
   *
   * The scan cannot be run from the server, and saying so plainly is most of what this button does.
   * The readers are on the shop's own LAN behind its router; a server at pos.sma-techno.net has no
   * route to 192.168.x.x and would be scanning its own neighbours if it tried. So the browser asks
   * the agent installed beside it, the agent reports its findings to the server over its own
   * authenticated channel, and this screen reloads to show what was recorded.
   */
  const scan = async () => {
    setScanning(true);
    setScanResult(null);

    try {
      const result = await rfidApi.scan();

      if (result === null) {
        setScanResult(
          'No terminal agent is answering on this machine, so there is nothing here that can see the '
          + 'shop network. Run the scan from a till that has the agent installed, or add the reader '
          + 'by hand if you know its address.',
        );

        return;
      }

      setScanResult(
        result.found === 0
          ? 'The sweep finished and found no readers that were not already connected. Readers in use '
            + 'are skipped deliberately — this family of reader accepts one client at a time, and '
            + 'probing a reader mid-sale would take it away from the till.'
          : `Found ${result.found} reader(s). Any that were new are listed below.`,
      );

      await load();
    } finally {
      setScanning(false);
    }
  };

  const backfill = async () => {
    setBusy(true);

    try {
      const { data } = await apiClient.post('/rfid-topology/backfill', { locationId, dryRun: false });

      const result = data as {
        readersCreated: number;
        assignmentsCreated: number;
        profilesWithoutStation: number;
        skipped: string[];
      };

      // Nothing happening has a reason, and the reason is the whole message.
      //
      // The first live run of this created nothing and said "brought across", because the count that
      // explained it — a reader profile with no till assigned — was returned by the server and
      // dropped here. Correct behaviour that looks like a broken button is worse than an error.
      if (result.readersCreated === 0) {
        setNothingToBringAcross(
          result.profilesWithoutStation > 0
            ? `${result.profilesWithoutStation} reader profile(s) under Hardware have no till assigned, so there was `
              + 'nothing to bring across. Set a station on the reader under Hardware first, or add the '
              + 'reader here directly.'
            : result.skipped.length > 0
              ? `Already brought across: ${result.skipped.join(', ')}.`
              : 'There are no reader profiles under Hardware to bring across.',
        );
      } else {
        setNothingToBringAcross(null);

        toast({
          title: 'Existing readers brought across',
          description: `${result.readersCreated} reader(s), ${result.assignmentsCreated} antenna assignment(s).`,
        });
      }

      await load();
    } catch {
      toast({ title: 'Could not bring the existing readers across', variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const unassigned = (topology?.readers ?? []).flatMap((r) =>
    r.antennas.filter((a) => a.stationId === null).map((a) => `${r.readerKey}/${a.antennaNumber}`),
  );

  return (
    <section className="flex flex-col gap-4">
      <header className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h2 className="text-heading">RFID topology</h2>
          <p className="text-caption text-ink-muted">
            Which machine drives which reader, and which till each antenna watches. One reader with four
            antennas can serve four separate tills — the antenna is what decides where a tag read
            lands, not the reader.
          </p>
        </div>

        {canWrite ? (
          <button type="button" className="pos-button" disabled={scanning} onClick={() => void scan()}>
            {scanning ? 'Scanning…' : 'Scan for readers'}
          </button>
        ) : null}
      </header>

      {scanResult ? (
        <p className="rounded-md border border-subtle bg-panel-sunken p-2 text-caption text-ink-muted">{scanResult}</p>
      ) : null}

      {topology === null ? (
        <p className="text-caption text-ink-muted">Loading…</p>
      ) : (
        <>
          {/* The count a shop actually asks for, and then the ones that explain the difference.
              Counted on the server so two screens cannot answer it differently. */}
          {topology.summary && topology.summary.total > 0 ? (
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-body font-semibold">
                {topology.summary.connected} of {topology.summary.total} reader
                {topology.summary.total === 1 ? '' : 's'} connected
              </span>

              {(['Error', 'Offline', 'Discovered', 'Disabled'] as const)
                .map((state) => ({
                  state,
                  count: {
                    Error: topology.summary?.error ?? 0,
                    Offline: topology.summary?.offline ?? 0,
                    Discovered: topology.summary?.discovered ?? 0,
                    Disabled: topology.summary?.disabled ?? 0,
                  }[state],
                }))
                .filter((entry) => entry.count > 0)
                .map((entry) => (
                  <span
                    key={entry.state}
                    title={STATE_LABELS[entry.state].hint}
                    className={cn('pos-badge tabular-nums', STATE_LABELS[entry.state].tone)}
                  >
                    {entry.count} {STATE_LABELS[entry.state].label.toLowerCase()}
                  </span>
                ))}
            </div>
          ) : null}

          {/* Machines. Liveness is a property of the machine, so it is stated once here rather than
              repeated against every station it happens to serve. */}
          <div>
            <h3 className="text-body font-semibold">Machines</h3>

            {topology.devices.length === 0 ? (
              <p className="mt-1 text-caption text-ink-muted">
                None yet. A machine registers itself the first time its agent checks in.
              </p>
            ) : (
              <table className="pos-table mt-2">
                <thead>
                  <tr className="border-b border-subtle text-left text-label text-ink-muted">
                    <th className="px-2 py-1">Machine</th>
                    <th className="px-2 py-1">Host name</th>
                    <th className="px-2 py-1">Address</th>
                    <th className="px-2 py-1">Agent</th>
                    <th className="px-2 py-1">Readers</th>
                    <th className="px-2 py-1">State</th>
                  </tr>
                </thead>
                <tbody>
                  {topology.devices.map((device) => (
                    <tr key={device.id} className="border-b border-subtle">
                      <td className="px-2 py-1 font-medium">{device.deviceKey}</td>
                      <td className="px-2 py-1">{device.hostname ?? '—'}</td>
                      <td className="px-2 py-1">{device.localIpAddresses ?? '—'}</td>
                      <td className="px-2 py-1">{device.agentVersion ?? '—'}</td>
                      <td className="px-2 py-1">{device.readerCount}</td>
                      <td className={`px-2 py-1 ${device.isOnline ? 'text-positive' : 'text-warning'}`}>
                        {device.isOnline ? 'Online' : 'Offline'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          {/* The count is stated because an unassigned antenna reads nothing and says nothing at the
              till — it is the most common commissioning mistake and the hardest to spot from a list. */}
          {unassigned.length > 0 ? (
            <p className="rounded-md border border-subtle bg-panel-sunken p-2 text-caption text-ink-muted">
              {unassigned.length} antenna(s) have no till assigned and will read nothing:{' '}
              {unassigned.slice(0, 8).join(', ')}
              {unassigned.length > 8 ? '…' : ''}
            </p>
          ) : null}

          <div>
            <h3 className="text-body font-semibold">Readers and antennas</h3>

            {topology.readers.length === 0 ? (
              <div className="mt-1 flex flex-col gap-2">
                <p className="text-caption text-ink-muted">
                  No readers registered. Scan for readers above if a till on this network has the agent
                  installed. If this shop already had a reader configured under Hardware, bring it
                  across instead — it keeps working exactly as it does now, on antenna 1.
                </p>

                {canWrite ? (
                  <div className="flex flex-col gap-2">
                    <div>
                      <button type="button" className="pos-button" disabled={busy} onClick={() => void backfill()}>
                        Bring existing readers across
                      </button>
                    </div>

                    {nothingToBringAcross ? (
                      <p className="rounded-md border border-subtle bg-panel-sunken p-2 text-caption text-ink-muted">
                        {nothingToBringAcross}
                      </p>
                    ) : null}
                  </div>
                ) : null}
              </div>
            ) : (
              <div className="mt-2 flex flex-col gap-4">
                {topology.readers.map((reader) => (
                  <div key={reader.id} className="rounded-md border border-subtle p-3">
                    <div className="flex flex-wrap items-baseline justify-between gap-2">
                      <span className="flex items-center gap-2">
                        <span className="font-semibold">{reader.readerKey}</span>
                        {STATE_LABELS[reader.state] ? (
                          <span
                            title={STATE_LABELS[reader.state].hint}
                            className={cn('pos-badge', STATE_LABELS[reader.state].tone)}
                          >
                            {STATE_LABELS[reader.state].label}
                          </span>
                        ) : null}
                      </span>
                      <span className="text-caption text-ink-muted">
                        {reader.protocol} · {reader.host}:{reader.port}
                        {reader.serialNumber ? ` · serial ${reader.serialNumber}` : ' · no serial reported'}
                        {reader.deviceKey ? ` · driven by ${reader.deviceKey}` : ' · not assigned to a machine'}
                      </span>
                    </div>

                    {/* Said once, under the reader it applies to. A state on its own tells somebody
                        that a reader is down; this tells them which end of the shop to walk to. */}
                    {reader.state !== 'Connected' && STATE_LABELS[reader.state] ? (
                      <p className="mt-1 text-caption text-ink-muted">{STATE_LABELS[reader.state].hint}</p>
                    ) : null}

                    <table className="pos-table mt-2">
                      <thead>
                        <tr className="border-b border-subtle text-left text-label text-ink-muted">
                          <th className="px-2 py-1 w-24">Antenna</th>
                          <th className="px-2 py-1">Till</th>
                        </tr>
                      </thead>
                      <tbody>
                        {reader.antennas.map((antenna) => (
                          <tr key={antenna.antennaNumber} className="border-b border-subtle">
                            <td className="px-2 py-1">{antenna.antennaNumber}</td>
                            <td className="px-2 py-1">
                              <select
                                className="pos-input w-64"
                                disabled={!canWrite || busy}
                                value={antenna.stationId ?? ''}
                                onChange={(e) =>
                                  void assign(
                                    reader,
                                    antenna.antennaNumber,
                                    e.target.value === '' ? null : Number(e.target.value),
                                  )
                                }
                              >
                                <option value="">Not assigned — reads nothing</option>
                                {stations.map((station) => (
                                  <option key={station.id} value={station.id}>
                                    {station.stationCode}
                                    {station.name ? ` — ${station.name}` : ''}
                                  </option>
                                ))}
                              </select>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ))}
              </div>
            )}
          </div>
        </>
      )}
    </section>
  );
}
