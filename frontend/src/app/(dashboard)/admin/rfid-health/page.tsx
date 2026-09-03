'use client';

import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '@/lib/auth-config';
import { apiClient } from '@/lib/api-client';
import { PageHeader } from '@/components/shell/page-header';

/**
 * Every antenna in the shop, and which layer is stopping the ones that are not working.
 *
 * A single "Connected" light cannot answer the question somebody actually has, which is where to
 * walk. The agent being reachable says nothing about the reader; the reader answering says nothing
 * about whether anybody pointed its antenna at a till. So the four facts are shown as four, and the
 * summary counts them separately — an estate of 252 is read by its counts, not by its rows.
 *
 * Refreshed on a timer matching the agent heartbeat rather than by the operator pressing anything.
 * It is not a push: this deployment cannot upgrade a WebSocket, so a poll is what actually works
 * here, and calling it live when it is a five-second poll would be a claim the network cannot keep.
 */

type Health =
  | 'Operational'
  | 'Unassigned'
  | 'Disabled'
  | 'AgentOffline'
  | 'ReaderOffline'
  | 'ReaderUnclaimed';

interface StationHealthRow {
  stationId: number | null;
  stationCode: string | null;
  readerKey: string;
  antennaNumber: number;
  deviceKey: string | null;
  agentOnline: boolean;
  readerOnline: boolean;
  health: Health;
  agentLastSeen: string | null;
  readerLastSeen: string | null;
}

interface Summary {
  total: number;
  operational: number;
  unassigned: number;
  disabled: number;
  agentOffline: number;
  readerOffline: number;
  readerUnclaimed: number;
}

interface Dashboard {
  summary: Summary;
  stations: StationHealthRow[];
}

const REFRESH_MS = 5_000;

/** Said as what to do about it, not as a state name. */
const EXPLANATION: Record<Health, string> = {
  Operational: 'Reading',
  Unassigned: 'No till assigned — reads go nowhere',
  Disabled: 'Switched off deliberately',
  AgentOffline: 'The PC has not checked in',
  ReaderOffline: 'The PC is up; the reader is not answering it',
  ReaderUnclaimed: 'No PC is driving this reader',
};

const TONE: Record<Health, string> = {
  Operational: 'text-positive',
  Unassigned: 'text-warning',
  Disabled: 'text-ink-muted',
  AgentOffline: 'text-destructive',
  ReaderOffline: 'text-destructive',
  ReaderUnclaimed: 'text-warning',
};

export default function RfidHealthPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;

  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [updatedAt, setUpdatedAt] = useState<Date | null>(null);

  const load = useCallback(async () => {
    if (!locationId) return;

    try {
      const { data } = await apiClient.get(`/rfid-topology/dashboard?locationId=${locationId}`);
      setDashboard(data as Dashboard);
      setUpdatedAt(new Date());
      setError(null);
    } catch {
      // Kept on screen with the failure noted rather than blanked. A dashboard that empties itself
      // when the network blips tells somebody their whole estate died, which is worse than stale.
      setError('Could not reach the server. Showing the last reading.');
    }
  }, [locationId]);

  useEffect(() => {
    void load();

    const timer = window.setInterval(() => void load(), REFRESH_MS);
    return () => window.clearInterval(timer);
  }, [load]);

  const summary = dashboard?.summary;

  return (
    <div className="flex flex-col">
      <PageHeader
        title="RFID health"
        description={`Every antenna in the shop, and which layer is stopping the ones that are not reading. Refreshes every ${REFRESH_MS / 1000} seconds.${updatedAt ? ` Last read at ${updatedAt.toLocaleTimeString()}.` : ''}`}
      />

      <div className="flex flex-col gap-4 px-page py-panel">
        {error ? <p className="text-body text-warning">{error}</p> : null}

      {summary ? (
        <div className="flex flex-wrap gap-2">
          <Tile label="Antennas" value={summary.total} />
          <Tile label="Reading" value={summary.operational} tone="text-positive" />
          <Tile label="No till assigned" value={summary.unassigned} tone="text-warning" />
          <Tile label="PC offline" value={summary.agentOffline} tone="text-destructive" />
          <Tile label="Reader offline" value={summary.readerOffline} tone="text-destructive" />
          <Tile label="Unclaimed readers" value={summary.readerUnclaimed} tone="text-warning" />
          <Tile label="Switched off" value={summary.disabled} />
        </div>
      ) : null}

      {dashboard === null ? (
        <p className="text-sm text-ink-muted">Loading…</p>
      ) : dashboard.stations.length === 0 ? (
        <p className="text-sm text-ink-muted">
          No readers registered yet. Add them under Administration → Settings → RFID.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="pos-table">
            <thead>
              <tr className="border-b border-subtle text-left text-label text-ink-muted">
                <th className="px-2 py-1">Till</th>
                <th className="px-2 py-1">Reader</th>
                <th className="px-2 py-1">Antenna</th>
                <th className="px-2 py-1">PC</th>
                <th className="px-2 py-1">PC state</th>
                <th className="px-2 py-1">Reader state</th>
                <th className="px-2 py-1">Overall</th>
              </tr>
            </thead>
            <tbody>
              {dashboard.stations.map((row) => (
                <tr key={`${row.readerKey}-${row.antennaNumber}`} className="border-b border-subtle">
                  <td className="px-2 py-1 font-medium">{row.stationCode ?? '—'}</td>
                  <td className="px-2 py-1">{row.readerKey}</td>
                  <td className="px-2 py-1">{row.antennaNumber}</td>
                  <td className="px-2 py-1">{row.deviceKey ?? '—'}</td>

                  {/* The two layers stated independently, because that is the whole point: a green
                      PC beside a red reader is a different job from both being red. */}
                  <td className={`px-2 py-1 ${row.agentOnline ? 'text-positive' : 'text-ink-muted'}`}>
                    {row.agentOnline ? 'Online' : 'Silent'}
                  </td>
                  <td className={`px-2 py-1 ${row.readerOnline ? 'text-positive' : 'text-ink-muted'}`}>
                    {row.readerOnline ? 'Answering' : 'Not answering'}
                  </td>

                  <td className={`px-2 py-1 ${TONE[row.health]}`}>{EXPLANATION[row.health]}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      </div>
    </div>
  );
}

function Tile({ label, value, tone }: { label: string; value: number; tone?: string }) {
  return (
    <div className="rounded-md border border-subtle bg-panel-sunken px-3 py-2">
      <div className={`text-heading tabular-nums ${tone ?? ''}`}>{value}</div>
      <div className="text-label text-ink-muted">{label}</div>
    </div>
  );
}
