'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';

/**
 * Asks the BFF for a single-use hub ticket.
 *
 * Same reasoning as the till's connection: a WebSocket handshake cannot carry an Authorization
 * header, and handing the browser a real access token would undo the BFF. A ticket opens one
 * connection, expires in a minute, and cannot call an API endpoint.
 */
async function fetchHubTicket(): Promise<string> {
  const response = await fetch('/api/auth/hub-ticket', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({}),
    cache: 'no-store',
  });

  if (!response.ok) {
    throw new Error('Could not obtain a hub ticket.');
  }

  const payload = (await response.json()) as { ticket: string };
  return payload.ticket;
}

export interface RowChanged<TRow> {
  entity: string;
  id: number;
  row: TRow;
}

export interface LiveGridHandlers<TRow> {
  onRowChanged?: (event: RowChanged<TRow>) => void;
  onRowRemoved?: (event: { entity: string; id: number }) => void;
  onSettingsChanged?: (event: { section: string }) => void;
  onConnectionChanged?: (connected: boolean) => void;
}

/**
 * The back-office realtime channel (doc 05 §SignalR).
 *
 * This is the direct answer to the legacy complaint that browse windows go stale over a network
 * (guide p.100–101). A second workstation editing an item patches the row here instead of the user
 * refreshing and losing their scroll position and selection.
 */
class InventoryHub {
  private connection: HubConnection | null = null;

  private handlers: LiveGridHandlers<unknown> = {};

  private locationId: number | null = null;

  /** Reference counted, because several grids on one page share one socket. */
  private subscribers = 0;

  async acquire(locationId: number, handlers: LiveGridHandlers<unknown>): Promise<void> {
    this.handlers = handlers;
    this.subscribers += 1;

    if (this.connection && this.locationId === locationId) {
      handlers.onConnectionChanged?.(this.connection.state === HubConnectionState.Connected);
      return;
    }

    await this.stop();
    this.locationId = locationId;

    const connection = new HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/inventory`, { accessTokenFactory: () => fetchHubTicket() })
      // Backoff with jitter, so a switch flapping does not produce a thundering herd of clients.
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) => {
          const base = [0, 2000, 5000, 10000][context.previousRetryCount] ?? 20000;
          return base + Math.floor(Math.random() * 1000);
        },
      })
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('RowChanged', (event: RowChanged<unknown>) => this.handlers.onRowChanged?.(event));
    connection.on('RowRemoved', (event: { entity: string; id: number }) => this.handlers.onRowRemoved?.(event));
    connection.on('SettingsChanged', (event: { section: string }) => this.handlers.onSettingsChanged?.(event));

    connection.onreconnected(async () => {
      this.handlers.onConnectionChanged?.(true);
      await connection.invoke('JoinLocation', locationId);
    });

    connection.onreconnecting(() => this.handlers.onConnectionChanged?.(false));
    connection.onclose(() => this.handlers.onConnectionChanged?.(false));

    await connection.start();
    await connection.invoke('JoinLocation', locationId);

    this.connection = connection;
    this.handlers.onConnectionChanged?.(true);
  }

  release(): void {
    this.subscribers = Math.max(0, this.subscribers - 1);

    if (this.subscribers === 0) {
      void this.stop();
    }
  }

  private async stop(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    this.locationId = null;
    await connection?.stop();
  }
}

const hub = new InventoryHub();

/**
 * Keeps a list of rows in step with the server.
 *
 * Patching rather than refetching is the whole point: a refetch would reset the scroll position and
 * the selection of whoever happens to be reading the grid when someone else saves. The briefly-held
 * `changed` set drives a one-shot highlight so a live edit is visible rather than silent.
 */
export function useLiveGrid<TRow extends { id: number }>(
  entity: string,
  locationId: number | undefined,
  setRows: (updater: (current: TRow[]) => TRow[]) => void,
  options: { onSettingsChanged?: (section: string) => void } = {},
): { connected: boolean; changed: ReadonlySet<string> } {
  const [connected, setConnected] = useState(false);
  const [changed, setChanged] = useState<Set<string>>(new Set());

  // Held in a ref so the effect below depends on the location alone. Re-subscribing on every render
  // of a changing row list would tear the socket down and back up continuously.
  const setRowsRef = useRef(setRows);
  setRowsRef.current = setRows;

  const settingsRef = useRef(options.onSettingsChanged);
  settingsRef.current = options.onSettingsChanged;

  const flash = useCallback((id: number) => {
    setChanged((current) => new Set(current).add(id));

    window.setTimeout(() => {
      setChanged((current) => {
        const next = new Set(current);
        next.delete(id);
        return next;
      });
    }, 1200);
  }, []);

  useEffect(() => {
    if (!locationId) return undefined;

    let cancelled = false;

    void hub
      .acquire(locationId, {
        onConnectionChanged: (isConnected) => {
          if (!cancelled) setConnected(isConnected);
        },
        onRowChanged: (event) => {
          if (cancelled || event.entity !== entity) return;

          const row = event.row as TRow;

          setRowsRef.current((current) => {
            const index = current.findIndex((r) => r.id === event.id);

            // A row that is not on this page is not appended: it may not match the active filter, and
            // inserting it would put it in the wrong sort position with no way to know the right one.
            if (index < 0) return current;

            const next = [...current];
            next[index] = row;
            return next;
          });

          flash(event.id);
        },
        onRowRemoved: (event) => {
          if (cancelled || event.entity !== entity) return;
          setRowsRef.current((current) => current.filter((r) => r.id !== event.id));
        },
        onSettingsChanged: (event) => {
          if (!cancelled) settingsRef.current?.(event.section);
        },
      })
      .catch(() => {
        // A grid that cannot reach the hub still works; it just stops updating on its own.
        if (!cancelled) setConnected(false);
      });

    return () => {
      cancelled = true;
      hub.release();
    };
  }, [entity, locationId, flash]);

  return { connected, changed };
}
