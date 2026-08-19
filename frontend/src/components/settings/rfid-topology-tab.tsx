'use client';

import { useCallback, useEffect, useState } from 'react';
import { toast } from '@/components/ui/toaster';
import { apiClient } from '@/lib/api-client';
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

interface Topology {
  devices: DeviceRow[];
  readers: ReaderRow[];
}

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

  const backfill = async () => {
    setBusy(true);

    try {
      const { data } = await apiClient.post('/rfid-topology/backfill', { locationId, dryRun: false });
      const result = data as { readersCreated: number; assignmentsCreated: number };

      toast({
        title: 'Existing readers brought across',
        description: `${result.readersCreated} reader(s), ${result.assignmentsCreated} antenna assignment(s).`,
      });

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
      <header>
        <h2 className="text-heading">RFID topology</h2>
        <p className="text-sm text-ink-muted">
          Which machine drives which reader, and which till each antenna watches. One reader with four
          antennas can serve four separate tills — the antenna is what decides where a tag read
          lands, not the reader.
        </p>
      </header>

      {topology === null ? (
        <p className="text-sm text-ink-muted">Loading…</p>
      ) : (
        <>
          {/* Machines. Liveness is a property of the machine, so it is stated once here rather than
              repeated against every station it happens to serve. */}
          <div>
            <h3 className="text-body font-semibold">Machines</h3>

            {topology.devices.length === 0 ? (
              <p className="mt-1 text-sm text-ink-muted">
                None yet. A machine registers itself the first time its agent checks in.
              </p>
            ) : (
              <table className="mt-2 w-full text-sm">
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
            <p className="rounded-md border border-subtle bg-panel-sunken p-2 text-xs text-ink-muted">
              {unassigned.length} antenna(s) have no till assigned and will read nothing:{' '}
              {unassigned.slice(0, 8).join(', ')}
              {unassigned.length > 8 ? '…' : ''}
            </p>
          ) : null}

          <div>
            <h3 className="text-body font-semibold">Readers and antennas</h3>

            {topology.readers.length === 0 ? (
              <div className="mt-1 flex flex-col gap-2">
                <p className="text-sm text-ink-muted">
                  No readers registered. If this shop already had a reader configured under Hardware,
                  bring it across — it keeps working exactly as it does now, on antenna 1.
                </p>

                {canWrite ? (
                  <div>
                    <button type="button" className="pos-button" disabled={busy} onClick={() => void backfill()}>
                      Bring existing readers across
                    </button>
                  </div>
                ) : null}
              </div>
            ) : (
              <div className="mt-2 flex flex-col gap-4">
                {topology.readers.map((reader) => (
                  <div key={reader.id} className="rounded-md border border-subtle p-3">
                    <div className="flex flex-wrap items-baseline justify-between gap-2">
                      <span className="font-semibold">{reader.readerKey}</span>
                      <span className="text-xs text-ink-muted">
                        {reader.protocol} · {reader.host}:{reader.port}
                        {reader.serialNumber ? ` · serial ${reader.serialNumber}` : ' · no serial reported'}
                        {reader.deviceKey ? ` · driven by ${reader.deviceKey}` : ' · not assigned to a machine'}
                      </span>
                    </div>

                    <table className="mt-2 w-full text-sm">
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
