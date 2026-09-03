'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Eraser, Play, Radio, Square, TriangleAlert, Volume2, VolumeX } from 'lucide-react';
import { RfidHub, type ObservedTag, type RfidReaderStatus } from '@/lib/rfid-hub';
import { playScanTone } from '@/lib/scan-feedback';
import { toast } from '@/components/ui/toaster';
import { usePosStore } from '@/stores/pos-store';
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

  const reportReaderStatus = usePosStore((state) => state.reportReaderStatus);

  const hubRef = useRef<RfidHub | null>(null);

  const onTagsObserved = useCallback((tags: ObservedTag[], observedAt: number) => {
    // Only this till's reads.
    //
    // The hub hands the station along with the tags and this dropped it, so whatever arrived was
    // shown and applied here. That was invisible while one reader served one till; with an antenna
    // map it means the item a customer is holding at the next checkout lands on this sale.
    //
    // Kept even though the server now sends to the station alone: this is the end that puts a line
    // on somebody's bill, and it should not depend on a broadcast being addressed correctly.
    if (observedAt !== stationId) {
      return;
    }

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
  }, [stationId]);

  useEffect(() => {
    const hub = new RfidHub();
    hubRef.current = hub;

    void hub.connect(stationId, locationId, {
      onTagsObserved,
      // This till's reader, not the shop's. Same reasoning as the tags above, and it had a sharper
      // symptom: the Start/Stop button trusts the reported state over what was just asked for, so a
      // status from another till arriving a moment after a press flipped the button back and made a
      // working control look broken.
      onReaderStatus: (next, forStation) => {
        if (forStation === stationId) setStatus(next);
      },
      onConnectionChanged: setConnected,
    });

    return () => {
      void hub.disconnect();
      hubRef.current = null;
    };
  }, [stationId, locationId, onTagsObserved]);

  // Tells the status strip what this panel already knows.
  //
  // The strip's RFID chip is fed by the agent's heartbeat over a different hub, so it could sit on
  // "offline" while this panel listed the tags it was reading at that moment — the two disagreeing
  // in the same screenshot. The reader's own status is the better source: it comes from the reader
  // rather than from a heartbeat about it, and it is already here.
  //
  // Only ever reports connected-and-reading as online, so the chip does not brighten for a reader
  // that is attached but stopped.
  useEffect(() => {
    reportReaderStatus(!readerNotReading(connected, status), status?.readsPerSecond ?? 0);
  }, [connected, status, reportReaderStatus]);

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
    // flex-1 with a floor: it takes whatever height the sale group leaves, but never collapses to a
    // header — a reader panel with no rows visible is a reader panel nobody will trust.
    <section
      className="pos-panel flex min-h-[9rem] flex-1 flex-col overflow-hidden"
      aria-label="Tag reader"
    >
      <header className="pos-panel-header shrink-0">
        <span>Tag reader</span>

        {/*
          Stated in words, not as a coloured dot. A reader that has silently stopped looks exactly
          like a reader with nothing in front of it, and the cashier has no way to tell which.
        */}
        <span className={cn('pos-badge normal-case', readerTone(connected, status))}>
          {readerNotReading(connected, status) ? (
            <TriangleAlert className="h-4 w-4 shrink-0" aria-hidden />
          ) : (
            <Radio className="h-4 w-4 shrink-0" aria-hidden />
          )}
          {readerLabel(connected, status)}
        </span>
      </header>

      {/*
        Two rows, not one.

        The reading and the three controls used to share a line, which is why the controls were 20
        pixels tall — it was the only way they fitted beside the text in a side panel this narrow.
        Given a row of their own they can be the size a control on a touchscreen has to be, and the
        count stops being squeezed by them.
      */}
      <div className="shrink-0 border-b border-subtle bg-panel-sunken px-3 py-1.5 text-label text-ink-muted">
        <div className="flex items-center justify-between gap-2">
          <span className="truncate">
            {rows.length === 0 ? 'Nothing in the field' : `${rows.length} tag${rows.length === 1 ? '' : 's'} in the field`}
            {unknownCount > 0 ? ` · ${unknownCount} not recognised` : ''}
          </span>

          {/* An em dash rather than a zero until the reader has actually reported: "no reading yet"
              and "reading nothing" are different facts, and a fabricated 0/s is the more alarming
              of the two. */}
          <span
            className="pos-amount shrink-0 tabular-nums font-medium text-ink"
            title="Raw reads per second, before debounce"
          >
            {status ? status.readsPerSecond : '—'}/s
          </span>
        </div>

        <div className="mt-1 flex items-center gap-1.5">
          {/* Empties the list without touching the reader.
              The field does not clear itself while a tagged item is still sitting on the counter —
              a row stays until the tag stops being seen — so after a mis-scan, or a tray left in
              range, the cashier is reading a list they cannot act on. This is the "start again"
              they reach for. It clears what is displayed, not the sale. */}
          <ClearFeedButton onClear={() => setRows([])} disabled={rows.length === 0} />

          {/* Whether it is listening, not whether we can reach it — see RfidReaderStatus.mode. */}
          <ReaderRunControls statusReading={status ? status.mode !== 'Off' : null} />
          <ScanSoundToggle />
        </div>
      </div>

      <ul className="min-h-0 flex-1 overflow-y-auto">
        {rows.length === 0 ? (
          <li className="px-3 py-5 text-center text-body text-ink-muted">
            {connected ? 'Hold a tagged item near the antenna.' : 'Not connected to the reader feed.'}
          </li>
        ) : (
          rows.map((row) => <TagRow key={row.epc} row={row} />)
        )}
      </ul>
    </section>
  );
}

/**
 * Empties the tag list on screen.
 * <para>
 * Deliberately local: it clears what this screen is showing and asks the server for nothing. A tag
 * still physically in the field will reappear on the next read, which is correct — the list
 * describes what the antenna can see, and a button that pretended otherwise would be lying about
 * the counter.
 * </para>
 */
function ClearFeedButton({ onClear, disabled }: { onClear: () => void; disabled: boolean }) {
  return (
    <button
      type="button"
      onClick={onClear}
      disabled={disabled}
      title="Clear the list. Anything still in the field will reappear as it is read again."
      className="pos-chip border-subtle text-ink-muted hover:bg-panel-hover"
    >
      <Eraser className="h-5 w-5 shrink-0" aria-hidden />
      <span>Clear</span>
    </button>
  );
}

/**
 * Start and stop the reader from the till — one button, whose label is the state.
 *
 * On the panel rather than buried in settings, because stopping the antenna is a counter-side need:
 * a customer's tagged coat drifting into the field mid-sale is resolved by stopping the reader, not
 * by an administrator. One toggle rather than a Start/Stop pair, so nobody has to know which of the
 * two applies right now — and it answers every press with a toast, because a control that works
 * silently is indistinguishable from one that is broken.
 */
function ReaderRunControls({ statusReading }: { statusReading: boolean | null }) {
  const setReaderMode = usePosStore((state) => state.setReaderMode);
  const [busy, setBusy] = useState(false);

  // The reader's own status report is the truth when it has arrived; until then the button
  // remembers what it was last asked to do, so the label flips the moment it is pressed instead of
  // waiting on the next status broadcast.
  const [asked, setAsked] = useState<boolean | null>(null);
  const reading = statusReading ?? asked ?? false;

  const flip = async () => {
    setBusy(true);

    try {
      const next = !reading;
      await setReaderMode(next ? 'Continuous' : 'Off');
      setAsked(next);
      toast({
        title: next ? 'Reader started' : 'Reader stopped',
        description: next
          ? 'Reading tags. Hold a tagged item near the antenna.'
          : 'The antenna is off. Press Start when you want tags read again.',
      });
    } finally {
      setBusy(false);
    }
  };

  return (
    <button
      type="button"
      onClick={() => void flip()}
      disabled={busy}
      title={reading ? 'Stop the reader' : 'Start reading tags'}
      className={cn(
        'pos-chip',
        reading
          ? 'border-negative/40 bg-negative-soft text-negative-text hover:bg-negative/20'
          : 'border-positive/40 bg-positive-soft text-positive-text hover:bg-positive/20',
      )}
    >
      {reading ? (
        <Square className="h-5 w-5 shrink-0" aria-hidden />
      ) : (
        <Play className="h-5 w-5 shrink-0" aria-hidden />
      )}
      {/* The button says what it is doing while it does it. It waits on a round trip to the
          reader, and a control that looks unchanged for a second reads as one that was not
          pressed - so it gets pressed again. */}
      <span>{busy ? 'Working…' : reading ? 'Stop' : 'Start'}</span>
    </button>
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
      // Names what it actually governs. A reader's buzzer is a setting held on the reader itself —
      // some refuse to have it changed over the network at all — so a cashier pressing this and
      // still hearing the antenna beep has not found a broken button, they have found two sources
      // of sound. Saying so here is cheaper than the support call.
      title={
        scanSound
          ? "The till's own scan sound is on. The reader's buzzer is separate and is set on the reader."
          : "The till's own scan sound is off. If beeping continues it is the reader's own buzzer, set on the reader."
      }
      className={cn('pos-chip border-subtle hover:bg-panel-hover', scanSound ? 'text-ink-muted' : 'text-ink-faint')}
    >
      {scanSound ? (
        <Volume2 className="h-5 w-5 shrink-0" aria-hidden />
      ) : (
        <VolumeX className="h-5 w-5 shrink-0" aria-hidden />
      )}
      {/* The state as a word, not as a crossed-out speaker. Whether a glyph shows sound *being*
          off or sound being *turned* off is a coin toss to somebody who has not seen it before,
          and this is the control people press when they cannot work out where a beep is coming
          from. */}
      <span>{scanSound ? 'Sound on' : 'Sound off'}</span>
      <span className="sr-only">. Press to turn scan sounds {scanSound ? 'off' : 'on'}.</span>
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

  // Switched off is not a fault, but it is emphatically not reading, and it used to be displayed as
  // "Reading" — so a cashier held a tag at a panel that claimed to be working and nothing happened.
  if (status?.mode === 'Off') return 'Reader stopped';

  // Nothing heard yet. Previously this said "Reading", which was a guess dressed as a fact; the
  // status arrives on the agent's next heartbeat, so this shows for a moment at most.
  if (!status) return 'Waiting for the reader';

  return 'Reading';
}

function readerTone(connected: boolean, status: RfidReaderStatus | null): string {
  if (readerNotReading(connected, status)) return 'text-warning';
  return 'text-live';
}

/**
 * Drives the glyph as well as the hue, so the badge's two states differ in shape and not only tone.
 *
 * "Not reading" rather than "offline": a stopped reader and an unreachable one are different
 * problems with different fixes, but from the cashier's side they share the only consequence that
 * matters mid-sale — no tag is going to appear — so both earn the warning glyph.
 */
function readerNotReading(connected: boolean, status: RfidReaderStatus | null): boolean {
  return !connected || !status || !status.connected || status.mode === 'Off';
}
