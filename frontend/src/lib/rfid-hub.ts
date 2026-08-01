'use client';

import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';

/**
 * The reader feed (`/hubs/rfid`).
 *
 * Separate from {@link PosHub} because it answers a different question. That hub is about a cart —
 * a line went on, a tag was refused. This one is about the antenna field: what is in front of the
 * reader right now, whether the reader is alive, and how hard it is working. A goods-in bench or a
 * stock count wants the second and has no cart at all.
 *
 * Listen-only. There is nothing to invoke here but a subscription — tags enter the system through
 * the terminal agent's own channel, so a browser cannot inject a read.
 */

/** One tag as the server saw it, after the debounce window closed. */
export interface ObservedTag {
  epc: string;
  /** Which of the reader's antennas saw it. Four on a D2184; 0 when the reader did not say. */
  antenna: number;
  /** Signal strength in dBm — roughly, how close. Null when the reader omits it. */
  rssi: number | null;
  /** How many raw reads collapsed into this one observation. */
  readCount: number;
  firstSeenAt: string;
  lastSeenAt: string;
  /** Resolved item, when the EPC is one we know. Null for an unmapped tag. */
  productId: string | null;
  stockCode: string | null;
  name: string | null;
}

export interface RfidReaderStatus {
  connected: boolean;
  /** Raw reads off the antenna, before debounce. The figure that reveals a dead antenna. */
  readsPerSecond: number;
  distinctTagsInField: number;
  detail: string | null;
}

export interface RfidHubHandlers {
  onTagsObserved?: (tags: ObservedTag[], stationId: string) => void;
  onReaderStatus?: (status: RfidReaderStatus, stationId: string) => void;
  onConnectionChanged?: (connected: boolean) => void;
}

async function fetchHubTicket(stationId: string): Promise<string> {
  const response = await fetch('/api/auth/hub-ticket', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ stationId }),
    cache: 'no-store',
  });

  if (!response.ok) {
    throw new Error('Could not obtain a hub ticket.');
  }

  const payload = (await response.json()) as { ticket: string };
  return payload.ticket;
}

export class RfidHub {
  private connection: HubConnection | null = null;

  private handlers: RfidHubHandlers = {};

  private stationId: string | null = null;

  private locationId: string | null = null;

  async connect(stationId: string, locationId: string, handlers: RfidHubHandlers): Promise<void> {
    this.handlers = handlers;
    this.stationId = stationId;
    this.locationId = locationId;

    if (this.connection) {
      await this.disconnect();
    }

    const connection = new HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/rfid`, {
        accessTokenFactory: () => fetchHubTicket(stationId),
      })
      // The same backoff the POS hub uses: a flapping switch must not produce a thundering herd of
      // tills all retrying on the same tick.
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) => {
          const base = [0, 2000, 5000, 10000][context.previousRetryCount] ?? 20000;
          return base + Math.floor(Math.random() * 1000);
        },
      })
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('TagsObserved', (payload: { stationId: string; tags: ObservedTag[] }) =>
      this.handlers.onTagsObserved?.(payload.tags, payload.stationId),
    );

    connection.on('ReaderStatus', (payload: { stationId: string; status: RfidReaderStatus }) =>
      this.handlers.onReaderStatus?.(payload.status, payload.stationId),
    );

    connection.onreconnected(async () => {
      handlers.onConnectionChanged?.(true);
      await this.resubscribe();
    });

    connection.onreconnecting(() => handlers.onConnectionChanged?.(false));
    connection.onclose(() => handlers.onConnectionChanged?.(false));

    await connection.start();
    this.connection = connection;

    await this.resubscribe();
    handlers.onConnectionChanged?.(true);
  }

  /**
   * Both scopes: the station's own reader, and every reader in the store.
   *
   * A till mostly cares about its own antenna, but a supervisor watching one screen while someone
   * walks the floor with a handheld wants the store. SignalR de-duplicates by connection, so being
   * in both groups still delivers one copy.
   */
  private async resubscribe(): Promise<void> {
    if (this.connection?.state !== HubConnectionState.Connected) return;

    if (this.stationId) {
      await this.connection.invoke('SubscribeToStation', this.stationId);
    }

    if (this.locationId) {
      await this.connection.invoke('SubscribeToLocation', this.locationId);
    }
  }

  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = null;

    if (connection) {
      await connection.stop().catch(() => {
        // Already gone. Nothing to do and nothing worth telling the cashier.
      });
    }
  }
}
