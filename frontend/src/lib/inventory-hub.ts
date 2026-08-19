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

  /**
   * Everyone currently listening, rather than whoever subscribed most recently.
   *
   * This was a single slot that each acquire overwrote, and a reference count beside it. The two
   * disagree the moment anything subscribes twice, which React does on every mount in development:
   * effects run mount, cleanup, mount. The cleanup marked the first subscriber cancelled, the slot
   * still pointed at it, and so the connection came up and reported itself to a listener that had
   * already stopped listening. The socket was open and working; every grid on the page sat on
   * "Connecting…" indefinitely, and nothing anywhere said why.
   *
   * A set removes the disagreement — membership *is* the reference count — and makes the reconnect
   * callbacks fan out, which the single slot could never do for more than one grid at a time.
   */
  private readonly subscribers = new Set<LiveGridHandlers<unknown>>();

  private locationId: number | null = null;

  /**
   * The connection attempt still in flight, and which location it is for.
   *
   * Opening a socket is not instant and cannot be cancelled part-way, so the window between asking
   * and arriving is real and things happen inside it. Two grids mounting together both ask; a grid
   * unmounting mid-connect asks for it to stop before there is anything to stop. Holding the attempt
   * here is what lets the second case adopt the first's socket, and what stops the third leaving one
   * running that nothing owns.
   */
  private pending: { locationId: number; promise: Promise<HubConnection> } | null = null;

  private announce(connected: boolean): void {
    for (const handlers of this.subscribers) handlers.onConnectionChanged?.(connected);
  }

  async acquire(locationId: number, handlers: LiveGridHandlers<unknown>): Promise<void> {
    this.subscribers.add(handlers);

    if (this.connection && this.locationId === locationId) {
      handlers.onConnectionChanged?.(this.connection.state === HubConnectionState.Connected);
      return;
    }

    // Join an attempt already under way rather than tearing it down and starting another.
    if (this.pending?.locationId === locationId) {
      const connection = await this.pending.promise;
      handlers.onConnectionChanged?.(connection.state === HubConnectionState.Connected);
      return;
    }

    await this.stop();

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

    // Every listener, not the newest one. Two grids on a page share this socket, and with a single
    // slot only one of them ever saw a row patch.
    connection.on('RowChanged', (event: RowChanged<unknown>) => {
      for (const handlers of this.subscribers) handlers.onRowChanged?.(event);
    });

    connection.on('RowRemoved', (event: { entity: string; id: number }) => {
      for (const handlers of this.subscribers) handlers.onRowRemoved?.(event);
    });

    connection.on('SettingsChanged', (event: { section: string }) => {
      for (const handlers of this.subscribers) handlers.onSettingsChanged?.(event);
    });

    // Rejoin before announcing, not after. Announcing first says "live" during the window where the
    // connection is up but this client is in no group, so nothing would reach it — the badge would
    // be telling the truth about the socket and a lie about the screen.
    connection.onreconnected(async () => {
      await connection.invoke('JoinLocation', locationId);
      this.announce(true);
    });

    connection.onreconnecting(() => this.announce(false));
    connection.onclose(() => this.announce(false));

    const promise = (async () => {
      await connection.start();
      await connection.invoke('JoinLocation', locationId);

      return connection;
    })();

    const pending = { locationId, promise };
    this.pending = pending;

    let opened: HubConnection;

    try {
      opened = await promise;
    } catch (error) {
      if (this.pending === pending) this.pending = null;
      throw error;
    }

    // Adopt it only if it is still the attempt anyone is waiting on. Between asking and arriving the
    // last grid may have unmounted, or a different location been asked for; publishing this one then
    // would put a socket nobody wants into `connection`, where the early return above reports its
    // state to every future subscriber. That is a grid stuck on "Connecting…" over a live socket,
    // which is precisely what this looked like from the outside.
    if (this.pending !== pending) {
      await opened.stop();
      return;
    }

    this.pending = null;
    this.connection = opened;
    this.locationId = locationId;

    this.announce(true);
  }

  release(handlers: LiveGridHandlers<unknown>): void {
    this.subscribers.delete(handlers);

    if (this.subscribers.size === 0) {
      void this.stop();
    }
  }

  private async stop(): Promise<void> {
    const pending = this.pending;
    const connection = this.connection;

    this.pending = null;
    this.connection = null;
    this.locationId = null;

    // An attempt in flight is waited for and then closed. It cannot be cancelled, and abandoning it
    // leaves a socket that goes on receiving with nothing reading it — the failure that hides,
    // because everything on screen looks merely idle.
    if (pending) await pending.promise.then((c) => c.stop()).catch(() => undefined);

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
): { connected: boolean; hasEverConnected: boolean; changed: ReadonlySet<number> } {
  const [connected, setConnected] = useState(false);

  // Whether this grid has ever been live. Without it, "still opening" and "dropped out" are the
  // same `connected === false`, and every screen reported the first as a fault — a red badge on a
  // cold start, before anything has had a chance to go wrong.
  const [hasEverConnected, setHasEverConnected] = useState(false);
  const [changed, setChanged] = useState<Set<number>>(new Set());

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

    // Held in a variable so the same object is handed back on release. The hub keys its subscribers
    // by identity, and an object literal written out twice is two objects: releasing one of them
    // would leave the other listening for the life of the page.
    const handlers: LiveGridHandlers<unknown> = {
      onConnectionChanged: (isConnected) => {
        if (!cancelled) {
          setConnected(isConnected);
          if (isConnected) setHasEverConnected(true);
        }
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
    };

    // Keep trying to make the first connection.
    //
    // withAutomaticReconnect covers a socket that drops, not one that never opened — so a hub ticket
    // that failed while the page was still settling used to end it there: no retry, no error, a grid
    // reading "Connecting…" for as long as anyone left it open, quietly serving whatever it had
    // loaded once. The state that most needs a retry was the only one that had none.
    //
    // Backoff, capped, and unbounded in time: the reason to stop trying is the user leaving the
    // screen, and that is what the cleanup below is. Giving up after N attempts only means the grid
    // is stale in a different way.
    let attempt = 0;
    let timer: number | undefined;

    const connect = (): void => {
      void hub.acquire(locationId, handlers).catch(() => {
        if (cancelled) return;

        // A grid that cannot reach the hub still works; it just stops updating on its own.
        setConnected(false);

        const delay = Math.min(30_000, 1_000 * 2 ** attempt) + Math.floor(Math.random() * 500);
        attempt += 1;
        timer = window.setTimeout(connect, delay);
      });
    };

    connect();

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
      hub.release(handlers);
    };
  }, [entity, locationId, flash]);

  return { connected, hasEverConnected, changed };
}
