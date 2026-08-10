import { apiClient } from '@/lib/api-client';

/**
 * The RFID reader's configuration and its live diagnostics.
 *
 * Two different sources, deliberately kept apart. Configuration is the server's — it lives in the
 * database, it is permission-checked, and it survives a reader being swapped for a spare. Diagnostics
 * come from the terminal agent on this machine, because they are questions about the hardware in this
 * room right now, and a server that answered them would report "unknown" whenever the till was
 * offline — which is exactly when somebody is stood at it wondering why nothing reads.
 */

export type RadioRegion = 'Etsi' | 'Fcc' | 'Chn';

export type RfLinkProfile = 'Fm0_40kHz' | 'Miller4_250kHz' | 'Miller4_300kHz' | 'Fm0_400kHz';

export type BeeperMode = 'Quiet' | 'AfterInventory' | 'EveryTag';

export type ReaderProtocol = 'Llrp' | 'Http' | 'Mqtt' | 'Simulator' | 'UhfSerial';

export interface ReaderProfile {
  id: number;
  locationId: number;
  stationId: number | null;
  name: string;
  host: string;
  port: number;
  protocol: ReaderProtocol;
  antennaZones: string;
  rssiThresholdDbm: number;
  minimumReadCount: number;
  debounceMs: number;
  coalesceMs: number;
  flushIntervalMs: number;
  maxBatchSize: number;
  autoAcceptBatches: boolean;
  continuousMode: boolean;
  outputPowerDbm: string;
  region: RadioRegion;
  frequencyStartIndex: number;
  frequencyEndIndex: number;
  /** Computed server-side so the screen and the validator cannot disagree about what a channel means. */
  frequencyStartMhz: number;
  frequencyEndMhz: number;
  regionMaxChannel: number;
  linkProfile: RfLinkProfile;
  beeper: BeeperMode;
  antennaReturnLossThresholdDb: number;
  impinjFastTid: boolean;
  denseReaderMode: boolean;
  deviceAddress: number;
  isActive: boolean;
}

/** What the device says about itself. Every field optional: readers differ in what they answer. */
/**
 * What the agent reports about itself. Shaped by its loopback `/status`.
 *
 * `reader.online` is the answer to the question people actually ask — is it going to read a tag —
 * and it is deliberately separate from whether the agent is running at all. A missing agent and a
 * reader that will not answer look identical from the till and need completely different fixes.
 */
/** One reader the server is holding, and whether it is answering right now. */
export interface ServerReaderConnection {
  profileId: number;
  name: string;
  endpoint: string;
  stationId: number;
  connected: boolean;
}

/**
 * What the server says about the readers it holds.
 *
 * `serverHosted` false means this deployment does not hold reader connections at all — the tills
 * run agents — and the screen should ask the agent instead. It is not a failure state.
 */
export interface ReaderConnectionSnapshot {
  serverHosted: boolean;
  readers: ServerReaderConnection[];
}

export interface AgentStatus {
  agentVersion: string;
  station: string;
  serverConnected: boolean;
  readerMode: 'Off' | 'OnDemand' | 'Continuous';
  reader: { online: boolean; device: string; buffered: number };
  printer?: { online: boolean };
  scale?: { online: boolean };
}

export interface ReaderDiagnostics {
  firmwareVersion?: string | null;
  temperatureCelsius?: number | null;
  outputPowerDbm?: number[] | null;
  region?: string | null;
  frequencyStartIndex?: number | null;
  frequencyEndIndex?: number | null;
  linkProfile?: string | null;
  workAntenna?: number | null;
  antennaReturnLossThresholdDb?: number | null;
  impinjFastTid?: boolean | null;
  gpioInputs?: boolean[] | null;
  returnLossDb?: Record<string, number> | null;
  unavailable: string[];
}

/**
 * The terminal agent's loopback API. Same machine as the browser, by definition — this is a till.
 * A failure here means the agent is not running, which is a normal state worth reporting plainly
 * rather than an error worth throwing.
 */
const AGENT = process.env.NEXT_PUBLIC_AGENT_URL ?? 'http://127.0.0.1:8477';

export const rfidApi = {
  /**
   * Which readers the *server* is holding, if it holds any.
   *
   * There are two places a reader connection can live now, and they are alternatives: a server on
   * the shop's own network can open the connections itself, or each till runs an agent. Asking the
   * server first is what lets this screen tell "no reader" from "not this machine's job" — the
   * distinction that made it report "Agent not running" on a shop whose reader was working fine.
   */
  serverConnections: async (): Promise<ReaderConnectionSnapshot | null> => {
    try {
      const response = await apiClient.get('/terminals/reader-connections');
      return response.data as ReaderConnectionSnapshot;
    } catch {
      return null;
    }
  },

  list: async (locationId: number): Promise<ReaderProfile[]> => {
    const response = await apiClient.get(`/terminals/readers?locationId=${locationId}`);
    return response.data as ReaderProfile[];
  },

  save: async (profile: ReaderProfile): Promise<ReaderProfile> => {
    const response = await apiClient.put(`/terminals/readers/${profile.id}`, profile);
    return response.data as ReaderProfile;
  },

  /** Null when the agent is not reachable, so the caller can say so rather than show a spinner forever. */
  diagnostics: async (): Promise<ReaderDiagnostics | null> => {
    try {
      const response = await fetch(`${AGENT}/reader/diagnostics`, { cache: 'no-store' });
      return response.ok ? ((await response.json()) as ReaderDiagnostics) : null;
    } catch {
      return null;
    }
  },

  /**
   * Creates a reader. The settings endpoint treats id 0 as "new", which is the only way to add one
   * — the terminals endpoint updates an existing profile and cannot mint one.
   */
  create: async (locationId: number, profile: Partial<ReaderProfile> & { name: string }): Promise<ReaderProfile> => {
    const response = await apiClient.post('/settings/readers', {
      locationId,
      profile: { id: 0, ...profile },
    });

    return response.data as ReaderProfile;
  },

  /**
   * What the agent on this machine currently sees.
   *
   * Distinct from diagnostics: that asks the reader questions and needs it answering. This says
   * whether the agent is running at all and whether it is holding the reader — which are the two
   * different reasons tags stop arriving, and telling them apart is the whole point.
   */
  status: async (): Promise<AgentStatus | null> => {
    try {
      const response = await fetch(`${AGENT}/status`, { cache: 'no-store' });
      return response.ok ? ((await response.json()) as AgentStatus) : null;
    } catch {
      return null;
    }
  },

  /**
   * Asks the agent to take the reader again.
   *
   * There is no connect command on the wire: the agent retries on its own every few seconds. What
   * this does is set the mode, which makes it re-evaluate the session immediately rather than on
   * the next tick — and, more usefully, it returns a status the caller can report.
   */
  connect: async (mode: 'OnDemand' | 'Continuous' = 'OnDemand'): Promise<AgentStatus | null> => {
    try {
      await fetch(`${AGENT}/reader/mode`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ mode }),
        cache: 'no-store',
      });
    } catch {
      return null;
    }

    await new Promise((resolve) => setTimeout(resolve, 1200));

    try {
      const response = await fetch(`${AGENT}/status`, { cache: 'no-store' });
      return response.ok ? ((await response.json()) as AgentStatus) : null;
    } catch {
      return null;
    }
  },

  /** Re-pushes the saved profile into the device. Returns what the reader would not accept. */
  applyToDevice: async (): Promise<{ applied: boolean; refused: string[] } | null> => {
    try {
      const response = await fetch(`${AGENT}/reader/apply-settings`, { method: 'POST', cache: 'no-store' });
      return response.ok ? ((await response.json()) as { applied: boolean; refused: string[] }) : null;
    } catch {
      return null;
    }
  },
};

/** Labels that say what a setting does, not what it is called in the protocol. */
export const REGION_LABELS: Record<RadioRegion, string> = {
  Fcc: 'FCC — North America (902.75–927.25 MHz)',
  Etsi: 'ETSI — Europe (865.1–868.1 MHz)',
  Chn: 'China (920.125–924.875 MHz)',
};

export const LINK_PROFILE_LABELS: Record<RfLinkProfile, string> = {
  Fm0_40kHz: 'FM0 40 kHz — slowest, most robust',
  Miller4_250kHz: 'Miller-4 250 kHz — recommended',
  Miller4_300kHz: 'Miller-4 300 kHz',
  Fm0_400kHz: 'FM0 400 kHz — fastest, shortest range',
};

export const BEEPER_LABELS: Record<BeeperMode, string> = {
  Quiet: 'Silent — the till gives its own feedback',
  AfterInventory: 'One beep per read round',
  EveryTag: 'A beep per tag — for stocktaking, not a checkout',
};
