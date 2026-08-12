/**
 * One vocabulary for "is this screen still being told things".
 *
 * There were three, for one underlying state. The point of sale said "Disconnected from the server
 * — reconnecting", inventory and purchasing said "live updates offline", and every browse screen
 * said "Not updating — reconnecting". A cashier moving between them could not tell whether they
 * were looking at three problems or one, and none of the three said whether what was on screen
 * could still be trusted — which is the only thing they actually needed to know.
 *
 * The states are deliberately few. Anything a user cannot act on differently does not deserve its
 * own word.
 */
export type ConnectionState =
  /** Established. Data on screen is current. */
  | 'connected'
  /** Opening for the first time. Not a fault, and must never be reported as one. */
  | 'connecting'
  /** Was connected, dropped, trying again by itself. Data may be stale. */
  | 'reconnecting'
  /** Given up, or never got there. Nothing on screen is being updated. */
  | 'disconnected';

export interface ConnectionCopy {
  /** Two or three words, for a badge beside a heading. */
  readonly label: string;
  /** A sentence, for a banner — says what it means for what is on screen. */
  readonly detail: string;
  /** How loudly to say it. */
  readonly tone: 'live' | 'muted' | 'warning';
}

/**
 * The words, in one place.
 *
 * `connecting` is muted and says nothing alarming on purpose. A hub takes a moment to open on a
 * cold start, and the old point-of-sale screen spent that moment showing a red "Server offline" to
 * a cashier with a customer waiting — a fault message for the normal case, which teaches people to
 * ignore the one time it is real.
 */
const COPY: Record<ConnectionState, ConnectionCopy> = {
  connected: {
    label: 'Live',
    detail: 'Up to date.',
    tone: 'live',
  },
  connecting: {
    label: 'Connecting…',
    detail: 'Connecting to the server.',
    tone: 'muted',
  },
  reconnecting: {
    label: 'Reconnecting…',
    detail: 'Reconnecting. What is on screen may be a few moments out of date.',
    tone: 'warning',
  },
  disconnected: {
    label: 'Not updating',
    detail: 'Not connected to the server. This screen has stopped updating.',
    tone: 'warning',
  },
};

export function connectionCopy(state: ConnectionState): ConnectionCopy {
  return COPY[state];
}

/**
 * Works the state out from what the hub clients actually report, which is a boolean and whether
 * they have ever succeeded.
 *
 * The distinction that matters is the second one: not yet connected and no longer connected look
 * identical to `connected === false` and mean completely different things to the person reading
 * the screen. Everywhere except the point of sale used to conflate them.
 */
export function connectionStateFrom(connected: boolean, hasEverConnected: boolean): ConnectionState {
  if (connected) return 'connected';
  return hasEverConnected ? 'reconnecting' : 'connecting';
}
