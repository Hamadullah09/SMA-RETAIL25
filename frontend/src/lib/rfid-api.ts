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
  id: string;
  locationId: string;
  stationId: string | null;
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
  list: async (locationId: string): Promise<ReaderProfile[]> => {
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
