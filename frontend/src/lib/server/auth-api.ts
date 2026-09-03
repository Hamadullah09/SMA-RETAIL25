import 'server-only';
import type { Session, SessionUser } from './session';

/**
 * Signing in, against the API's own token endpoint.
 *
 * This replaces the OpenIddict redirect dance — authorize, code, PKCE verifier, back-channel
 * exchange — with one POST. That flow is the right shape when third parties sign in to your API;
 * here there is exactly one client, this file, and the redirect bought a login page the design
 * system could not reach.
 *
 * The token never leaves this process. It goes into the encrypted, httpOnly session cookie and is
 * attached by the proxy server-side, exactly as the reference token was. Nothing here is reachable
 * from the browser, which is the property the end-to-end suite asserts and the reason a JWT in
 * localStorage was never on the table: this is a till, and one cross-site script would be enough.
 */
const API = process.env.API_URL ?? 'http://localhost:5000';

interface TokenResponse {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
  refreshExpiresAt: string;
}

interface MeResponse {
  subject?: string;
  name?: string;
  staffId?: string;
  locationId?: string;
  accessLevel?: string;
  roles?: string[];
  permissions?: string[];
}

export interface SignInFailure {
  ok: false;
  /** Already written for the person reading it — the API decides the wording. */
  message: string;
}

export type SignInResult = { ok: true; session: Session } | SignInFailure;

/** Exchanges a username and password for a session. */
export async function signIn(username: string, password: string): Promise<SignInResult> {
  let response: Response;

  try {
    response = await fetch(`${API}/auth/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
      cache: 'no-store',
    });
  } catch {
    // The API being unreachable is not a wrong password, and saying so saves somebody retyping a
    // password they got right.
    return { ok: false, message: 'Cannot reach the server. Check the connection and try again.' };
  }

  if (!response.ok) {
    const body = await response.json().catch(() => null);

    return {
      ok: false,
      message: (body as { message?: string } | null)?.message ?? 'That username or password is not right.',
    };
  }

  const tokens = (await response.json()) as TokenResponse;
  const user = await loadUser(tokens.accessToken);

  return { ok: true, session: toSession(tokens, user) };
}

/**
 * Trades a refresh token for a fresh pair.
 *
 * Returns null rather than throwing, because every caller's answer to a failed refresh is the same:
 * the session is over, sign in again.
 */
export async function refreshSession(session: Session): Promise<Session | null> {
  if (!session.refreshToken) return null;

  let response: Response;

  try {
    response = await fetch(`${API}/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: session.refreshToken }),
      cache: 'no-store',
    });
  } catch {
    return null;
  }

  if (!response.ok) return null;

  const tokens = (await response.json()) as TokenResponse;

  // The identity is re-read rather than carried over, so a permission taken away five minutes ago
  // does not survive the refresh that was meant to pick it up.
  const user = await loadUser(tokens.accessToken);

  return toSession(tokens, user ?? session.user);
}

async function loadUser(accessToken: string): Promise<SessionUser | null> {
  try {
    const response = await fetch(`${API}/auth/me`, {
      headers: { Authorization: `Bearer ${accessToken}` },
      cache: 'no-store',
    });

    if (!response.ok) return null;

    const me = (await response.json()) as MeResponse;

    return {
      sub: me.subject ?? '',
      name: me.name ?? '',
      staffId: numberOrUndefined(me.staffId),
      locationId: numberOrUndefined(me.locationId),
      accessLevel: numberOrUndefined(me.accessLevel),
      roles: me.roles ?? [],
      permissions: me.permissions ?? [],
    };
  } catch {
    return null;
  }
}

function toSession(tokens: TokenResponse, user: SessionUser | null): Session {
  return {
    accessToken: tokens.accessToken,
    refreshToken: tokens.refreshToken,
    // Sixty seconds early, so a request is not sent with a token that expires while it is in flight.
    expiresAt: new Date(tokens.expiresAt).getTime() - 60_000,
    user: user ?? { sub: '', name: '', roles: [], permissions: [] },
  };
}

/**
 * What actually goes in the cookie.
 *
 * The permissions are stripped, because they are already inside the access token sitting next to
 * them — and carrying them twice pushed the sealed cookie past 4KB, which browsers discard without
 * a word. Sign-in returned 200, the cookie vanished, and every request after it was anonymous.
 *
 * They are put back by readSession, decoded from the token, so nothing downstream notices.
 */
export function forStorage(session: Session): Session {
  return {
    ...session,
    user: { ...session.user, permissions: [], roles: [] },
  };
}

/**
 * Puts them back.
 *
 * The token is read, not verified: it arrived from the API over a channel this process opened, it
 * is sealed in a cookie only this process can open, and the API verifies the signature again on
 * every call it is presented to. Verifying here would mean shipping the signing key to the front
 * end, which is a real cost for no gain.
 */
export function withClaims(session: Session): Session {
  const payload = decodePayload(session.accessToken);

  if (!payload) return session;

  const packed = typeof payload.perms === 'string' ? payload.perms.split(' ').filter(Boolean) : [];
  const individual = Array.isArray(payload.permission)
    ? (payload.permission as string[])
    : typeof payload.permission === 'string'
      ? [payload.permission]
      : [];

  const roles = Array.isArray(payload.role)
    ? (payload.role as string[])
    : typeof payload.role === 'string'
      ? [payload.role]
      : [];

  return {
    ...session,
    user: {
      ...session.user,
      permissions: [...new Set([...packed, ...individual])],
      roles,
    },
  };
}

function decodePayload(token: string): Record<string, unknown> | null {
  try {
    const segment = token.split('.')[1];

    if (!segment) return null;

    return JSON.parse(Buffer.from(segment, 'base64url').toString('utf8')) as Record<string, unknown>;
  } catch {
    return null;
  }
}

function numberOrUndefined(value: string | undefined): number | undefined {
  if (!value) return undefined;

  const parsed = Number(value);

  return Number.isFinite(parsed) ? parsed : undefined;
}
