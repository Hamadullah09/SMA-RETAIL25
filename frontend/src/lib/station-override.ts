/**
 * Which till this browser is, when an administrator has said so explicitly.
 *
 * The POS normally asks the agent installed on the machine, and falls back to the value baked in at
 * build time. That covers a shop where every till runs its own agent, and covers nothing else: a
 * back-office PC, a laptop on the shop floor, or a self-checkout counter whose reader is a handheld
 * rather than a fixed antenna all resolve to the same build-time station, and there is no way to
 * tell them apart.
 *
 * This is the escape hatch, and it is deliberately not a cache. The POS screen refuses to remember a
 * station between loads on purpose — a stale id outliving a reconfigured agent means a till quietly
 * ringing sales against the till next door, which is worse than one that asks again each morning.
 * What is stored here is different in kind: an administrator's decision, made once, shown on screen
 * whenever it is in force, and cleared from the same place it was set.
 *
 * Per browser, because that is the granularity of the problem. One front end is served to every
 * machine, so the only place "which till am I" can differ is the browser doing the asking.
 */

const KEY = 'retail25.station.override';

/** The station an administrator pinned this browser to, or null when it follows the agent. */
export function readStationOverride(): number | null {
  // Server-rendered first pass has no localStorage, and asking for it throws rather than returning
  // undefined.
  if (typeof window === 'undefined') return null;

  try {
    const raw = window.localStorage.getItem(KEY);
    if (!raw) return null;

    const id = Number(raw);

    // 0 and NaN are both rejected: a station id is a row, and a till registered as station 0 would
    // ring sales against nothing at all.
    return Number.isFinite(id) && id > 0 ? id : null;
  } catch {
    // Private browsing, or storage disabled by policy. Following the agent is the right fallback.
    return null;
  }
}

/** Pins this browser to a station, or clears the pin when given null. */
export function writeStationOverride(stationId: number | null): void {
  if (typeof window === 'undefined') return;

  try {
    if (stationId === null) {
      window.localStorage.removeItem(KEY);
      return;
    }

    window.localStorage.setItem(KEY, String(stationId));
  } catch {
    // Nothing to do and nothing to say: the caller re-reads the value and will show that it did not
    // take, rather than reporting a success that did not happen.
  }
}
