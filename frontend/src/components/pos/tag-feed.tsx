'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Volume2, VolumeX } from 'lucide-react';
import { RfidHub, type ObservedTag, type RfidReaderStatus } from '@/lib/rfid-hub';
import { playScanTone } from '@/lib/scan-feedback';
import { useUIStore } from '@/stores/ui-store';
import { cn } from '@/lib/utils';

/**
 * What the antenna can see, live (guide p.42, doc 06 §2).
 *
 * The point of this panel is not the cart — lines land in the cart on their own. It is the answer to
 * the two questions a cashier actually asks when RFID misbehaves: *is the reader working*, and *did
 * it see this item*. Without it, a tag that fails to read and a reader that has silently died look
 * exactly the same from the till, and the cashier's only recourse is to wave the item around again.
 *
 * So an unknown tag is shown rather than hidden. A tag the system cannot resolve is the most
 * interesting row on the screen: it is either stock that was never commissioned, or a customer's own
 * coat, and the difference matters.
 */

/** How many tags to keep on screen. A rail of stock can put hundreds through in a minute. */
const MAX_ROWS = 40;

/**
 * How long a tag stays listed after its last read.
 *
 * Not forever: a feed that only grows stops meaning "in the field" and starts meaning "has ever been
 * in the field", which is a different and much less useful thing to show someone holding an item.
 */
const FADE_AFTER_MS = 20_000;

interface FeedRow extends ObservedTag {
  /** Client-side accumulation across observations, so the count reflects the whole time on screen. */
  totalReads: number;
  receivedAt: number;
}

export function TagFeed({ stationId, locationId }: { stationId: number; locationId: number }) {
  const [rows, setRows] = useState<FeedRow[]>([]);
  const [status, setStatus] = useState<RfidReaderStatus | null>(null);
  const [connected, setConnected] = useState(false);

  const hubRef = useRef<RfidHub | null>(null);

  const onTagsObserved = useCallback((tags: ObservedTag[]) => {
    const now = Date.now();

    setRows((current) => {
      const byEpc = new Map(current.map((row) => [row.epc, row]));

      for (const tag of tags) {
        const existing = byEpc.get(tag.epc);

        byEpc.set(tag.epc, {
          ...tag,
          totalReads: (existing?.totalReads ?? 0) + tag.readCount,
          receivedAt: now,
        });
      }

      // Most recently seen first, and capped. A cashier reads the top of this list, never the
      // bottom, and an unbounded array on a screen that stays open all day is a leak.
      return [...byEpc.values()].sort((a, b) => b.receivedAt - a.receivedAt).slice(0, MAX_ROWS);
    });
  }, []);

  useEffect(() => {
    const hub = new RfidHub();
    hubRef.current = hub;

    void hub.connect(stationId, locationId, {
      onTagsObserved,
      onReaderStatus: setStatus,
      onConnectionChanged: setConnected,
    });

    return () => {
      void hub.disconnect();
      hubRef.current = null;
    };
  }, [stationId, locationId, onTagsObserved]);

  // Retires stale rows. A tick rather than a timeout per row, because a hundred tags would otherwise
  // mean a hundred pending timers.
  useEffect(() => {
    const timer = window.setInterval(() => {
      const cutoff = Date.now() - FADE_AFTER_MS;
      setRows((current) => {
        const kept = current.filter((row) => row.receivedAt >= cutoff);
        return kept.length === current.length ? current : kept;
      });
    }, 2_000);

    return () => window.clearInterval(timer);
  }, []);

  const unknownCount = useMemo(() => rows.filter((row) => !row.productId).length, [rows]);

  return (
    <section className="pos-panel flex min-h-0 flex-col" aria-label="Tag reader">
      <header className="pos-panel-header">
        <span>Tag reader</span>

        {/*
          Stated in words, not as a coloured dot. A reader that has silently stopped looks exactly
          like a reader with nothing in front of it, and the cashier has no way to tell which.
        */}
        <span className={cn('pos-badge normal-case', readerTone(connected, status))}>
          {readerLabel(connected, status)}
        </span>
      </header>

      <div className="flex items-baseline justify-between border-b border-subtle px-3 py-1.5 text-label text-ink-muted">
        <span>
          {rows.length === 0 ? 'Nothing in the field' : `${rows.length} tag${rows.length === 1 ? '' : 's'} in the field`}
          {unknownCount > 0 ? ` · ${unknownCount} not recognised` : ''}
        </span>

        <span className="flex items-center gap-2">
          {status ? (
            <span className="pos-amount tabular-nums" title="Raw reads per second, before debounce">
              {status.readsPerSecond}/s
            </span>
          ) : null}

          <ScanSoundToggle />
        </span>
      </div>

      <ul className="min-h-0 flex-1 overflow-y-auto">
        {rows.length === 0 ? (
          <li className="px-3 py-6 text-center text-body text-ink-muted">
            {connected
              ? 'Hold a tagged item near the antenna.'
              : 'Not connected to the reader feed.'}
          </li>
        ) : (
          rows.map((row) => <TagRow key={row.epc} row={row} />)
        )}
      </ul>
    </section>
  );
}

/**
 * Whether the till beeps.
 *
 * Here rather than in a settings screen because it is a per-person, per-shift decision: three tills
 * within earshot is three beeps nobody can attribute, and the person who needs it off is the person
 * standing at the counter, not an administrator.
 *
 * Pressing it plays the tone it is turning on. That is the only way to answer "is it working" —
 * and it doubles as the user gesture the browser requires before it will let an AudioContext make
 * any sound at all.
 */
function ScanSoundToggle() {
  const { scanSound, toggleScanSound } = useUIStore();

  return (
    <button
      type="button"
      onClick={() => {
        toggleScanSound();
        if (!scanSound) playScanTone('accepted');
      }}
      aria-pressed={scanSound}
      title={scanSound ? 'Scan sounds are on' : 'Scan sounds are off'}
      className={cn(
        'inline-flex h-5 items-center gap-1 rounded px-1.5 text-caption transition-colors',
        scanSound ? 'text-ink-muted hover:bg-panel-hover' : 'text-ink-faint hover:bg-panel-hover',
      )}
    >
      {scanSound ? <Volume2 className="h-3.5 w-3.5" aria-hidden /> : <VolumeX className="h-3.5 w-3.5" aria-hidden />}
      <span className="sr-only">{scanSound ? 'Turn scan sounds off' : 'Turn scan sounds on'}</span>
    </button>
  );
}

function TagRow({ row }: { row: FeedRow }) {
  const known = Boolean(row.productId);

  return (
    <li className="border-b border-subtle px-3 py-1.5">
      <div className="flex items-baseline justify-between gap-2">
        <span className={cn('truncate text-body', known ? 'text-ink' : 'text-warning')}>
          {known ? row.name : 'Not recognised'}
        </span>

        <span className="shrink-0 pos-amount text-label tabular-nums text-ink-muted">
          ×{row.totalReads}
        </span>
      </div>

      <div className="flex items-baseline justify-between gap-2 text-caption text-ink-faint">
        {/*
          The EPC in full, in the mono face, because when something is wrong this is the only string
          that identifies the physical item — and a truncated one cannot be read back over a phone.
        */}
        <span className="truncate font-mono" title={row.epc}>
          {known && row.stockCode ? `${row.stockCode} · ` : ''}
          {row.epc}
        </span>

        <span className="shrink-0 tabular-nums">
          {row.antenna > 0 ? `ant ${row.antenna}` : ''}
          {row.rssi !== null ? ` · ${row.rssi} dBm` : ''}
        </span>
      </div>
    </li>
  );
}

function readerLabel(connected: boolean, status: RfidReaderStatus | null): string {
  if (!connected) return 'Feed offline';
  if (status?.detail) return status.detail;
  if (status && !status.connected) return 'Reader not responding';
  return 'Reading';
}

function readerTone(connected: boolean, status: RfidReaderStatus | null): string {
  if (!connected || (status && !status.connected)) return 'text-warning';
  return 'text-live';
}
