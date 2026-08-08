'use client';

/**
 * The sound a till makes.
 *
 * RFID removes the one piece of feedback a barcode scanner always gave: the beep. A cashier who
 * passes an item over a scanner and hears nothing knows immediately to try again. Waving a basket
 * at an antenna gives them nothing at all — and the screen is not a substitute, because at the
 * moment the item is scanned the cashier is looking at the item, not at the till.
 *
 * So the scan feed is audible, and the tones carry meaning rather than merely existing:
 *
 *   accepted    one short high blip     an item joined the sale
 *   rejected    two lower blips         something was refused — already sold, held by another till
 *   unknown     one mid tone, longer    a tag nothing recognises; usually a customer's own coat
 *
 * Three distinguishable sounds rather than one, because "did it work" is exactly the question the
 * cashier is asking, and a single beep for both outcomes answers it wrongly half the time.
 *
 * Synthesised rather than played from files. Three oscillator envelopes are a few hundred bytes of
 * code against a few kilobytes of audio per tone, they need no network at all — a till on a dropped
 * connection still has to sound right — and the pitch can be tuned without a round trip to whoever
 * owns the asset.
 */

type ScanOutcome = 'accepted' | 'rejected' | 'unknown';

interface Tone {
  /** Hz. Spaced by more than a third so they are told apart in a noisy shop, not just in a quiet office. */
  frequency: number;
  durationMs: number;
  /** Repeats, spaced by `durationMs`. Two is a "no"; one is a "yes". */
  count: number;
  /** Peak gain. Well under 1: this plays a hundred times an hour next to someone's head. */
  gain: number;
}

const TONES: Record<ScanOutcome, Tone> = {
  accepted: { frequency: 1_180, durationMs: 55, count: 1, gain: 0.16 },
  rejected: { frequency: 420, durationMs: 90, count: 2, gain: 0.2 },
  unknown: { frequency: 700, durationMs: 150, count: 1, gain: 0.16 },
};

let context: AudioContext | null = null;

/**
 * One context for the page, created on first use.
 *
 * Browsers suspend an AudioContext created outside a user gesture, and a suspended context plays
 * nothing without throwing — which is the worst failure mode available, because it looks like the
 * feature simply does not work. `resume()` is called on every play for that reason: by the time a
 * tag is scanned the cashier has certainly clicked something, and resuming an already-running
 * context is free.
 */
function audio(): AudioContext | null {
  if (typeof window === 'undefined') return null;

  const Constructor =
    window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;

  if (!Constructor) return null;

  context ??= new Constructor();

  return context;
}

/**
 * Plays the tone for an outcome. Never throws: a till whose audio device has been unplugged must go
 * on selling, and an exception here would take the SignalR handler that called it down with it.
 */
export function playScanTone(outcome: ScanOutcome): void {
  const ctx = audio();

  if (!ctx) return;

  try {
    if (ctx.state === 'suspended') {
      void ctx.resume();
    }

    const tone = TONES[outcome];

    for (let i = 0; i < tone.count; i++) {
      const startsAt = ctx.currentTime + (i * tone.durationMs * 2) / 1000;
      const endsAt = startsAt + tone.durationMs / 1000;

      const oscillator = ctx.createOscillator();
      const envelope = ctx.createGain();

      // A square wave carries through shop noise better than a sine at the same volume; the
      // envelope is what stops it sounding like an alarm.
      oscillator.type = 'square';
      oscillator.frequency.value = tone.frequency;

      // Ramped rather than switched. A gain that steps from 0 to full produces a click at the
      // discontinuity, which is audible as a second, nastier sound on top of the intended one.
      envelope.gain.setValueAtTime(0.0001, startsAt);
      envelope.gain.exponentialRampToValueAtTime(tone.gain, startsAt + 0.008);
      envelope.gain.exponentialRampToValueAtTime(0.0001, endsAt);

      oscillator.connect(envelope);
      envelope.connect(ctx.destination);

      oscillator.start(startsAt);
      oscillator.stop(endsAt + 0.01);
    }
  } catch {
    // Deliberately silent. There is no useful recovery and nothing a cashier could do about it.
  }
}
