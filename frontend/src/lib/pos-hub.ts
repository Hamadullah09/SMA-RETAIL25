'use client';

import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import type { Cart, CartLine, CartTotals, DrawerTotals, PeripheralStatus } from '@/types/pos';

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';

/**
 * Asks the BFF for a single-use hub ticket (doc 07 §Topology).
 *
 * SignalR needs a credential in the query string, because a WebSocket handshake cannot carry an
 * Authorization header. Handing it the real access token would put a working API credential in
 * JavaScript and undo the whole point of the BFF, so what it gets instead is a ticket that opens one
 * connection, expires in a minute and is consumed on use.
 *
 * Fetched on every connect and reconnect rather than cached: a ticket is single-use by design.
 */
async function fetchHubTicket(stationId: number): Promise<string> {
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

export interface PosHubHandlers {
  onCartUpdated?: (cart: Cart, revision: number) => void;
  onCartLinesAdded?: (lines: CartLine[], revision: number) => void;
  onCartLineRejected?: (payload: { epc: string; reason: string; message: string }) => void;
  onTotalsChanged?: (totals: CartTotals, revision: number) => void;
  onTagStreamStatus?: (payload: { readerOnline: boolean; readRate: number }) => void;
  onPeripheralStatus?: (status: PeripheralStatus) => void;
  onDrawerStateChanged?: (drawer: DrawerTotals) => void;
  onPosMessage?: (payload: { productId: number; message: string }) => void;
  onWeightReported?: (payload: { value: number; unit: string; stable: boolean }) => void;
  onResyncRequired?: (payload: { cartId: number; serverRevision: number }) => void;
  onConnectionChanged?: (connected: boolean) => void;
}

/**
 * The till's hub connection (doc 05 §SignalR).
 *
 * Two behaviours matter more than the plumbing. Reconnect uses backoff with jitter so a switch
 * flapping does not produce a thundering herd of tills; and on every reconnect the client resyncs
 * rather than trusting what is on screen, because anything it missed while disconnected is money it
 * would otherwise be showing wrongly.
 */
export class PosHub {
  private connection: HubConnection | null = null;

  private handlers: PosHubHandlers = {};

  private joinedCartId: number | null = null;

  async connect(stationId: number, locationId: number, handlers: PosHubHandlers): Promise<void> {
    this.handlers = handlers;

    if (this.connection) {
      await this.disconnect();
    }

    const connection = new HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/pos`, {
        accessTokenFactory: () => fetchHubTicket(stationId),
      })
      // Backoff with jitter: 0s, ~2s, ~5s, ~10s, then every ~20s.
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) => {
          const base = [0, 2000, 5000, 10000][context.previousRetryCount] ?? 20000;
          return base + Math.floor(Math.random() * 1000);
        },
      })
      .configureLogging(LogLevel.Warning)
      .build();

    this.bind(connection);

    connection.onreconnected(async () => {
      handlers.onConnectionChanged?.(true);
      await this.rejoin(stationId, locationId);
    });

    connection.onreconnecting(() => handlers.onConnectionChanged?.(false));
    connection.onclose(() => handlers.onConnectionChanged?.(false));

    await connection.start();
    this.connection = connection;

    await this.rejoin(stationId, locationId);
    handlers.onConnectionChanged?.(true);
  }

  private bind(connection: HubConnection): void {
    connection.on('CartUpdated', (cart: Cart, revision: number) => this.handlers.onCartUpdated?.(cart, revision));
    connection.on('CartLinesAdded', (lines: CartLine[], revision: number) =>
      this.handlers.onCartLinesAdded?.(lines, revision),
    );
    connection.on('CartLineRejected', (payload) => this.handlers.onCartLineRejected?.(payload));
    connection.on('TotalsChanged', (totals: CartTotals, revision: number) =>
      this.handlers.onTotalsChanged?.(totals, revision),
    );
    connection.on('TagStreamStatus', (payload) => this.handlers.onTagStreamStatus?.(payload));
    connection.on('PeripheralStatus', (status: PeripheralStatus) => this.handlers.onPeripheralStatus?.(status));
    connection.on('DrawerStateChanged', (drawer: DrawerTotals) => this.handlers.onDrawerStateChanged?.(drawer));
    connection.on('PosMessage', (payload) => this.handlers.onPosMessage?.(payload));
    connection.on('WeightReported', (payload) => this.handlers.onWeightReported?.(payload));
    connection.on('CartResyncRequired', (payload) => this.handlers.onResyncRequired?.(payload));
  }

  private async rejoin(stationId: number, locationId: number): Promise<void> {
    if (this.connection?.state !== HubConnectionState.Connected) return;

    await this.connection.invoke('JoinStation', stationId);
    await this.connection.invoke('JoinLocation', locationId);

    if (this.joinedCartId) {
      await this.connection.invoke('JoinCart', this.joinedCartId);
    }
  }

  async joinCart(cartId: number): Promise<void> {
    this.joinedCartId = cartId;
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('JoinCart', cartId);
    }
  }

  async leaveCart(cartId: number): Promise<void> {
    if (this.joinedCartId === cartId) this.joinedCartId = null;
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('LeaveCart', cartId);
    }
  }

  /** Asks the server whether we are behind. Called whenever a revision gap is spotted. */
  async requestResync(cartId: number, knownRevision: number): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('RequestCartResync', cartId, knownRevision);
    }
  }

  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    this.joinedCartId = null;
    await connection?.stop();
  }
}

export const posHub = new PosHub();
