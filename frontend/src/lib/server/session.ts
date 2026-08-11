import 'server-only';

import { cookies } from 'next/headers';
import { EncryptJWT, jwtDecrypt } from 'jose';
import { createHash } from 'node:crypto';

/**
 * The BFF session (doc 07 §Topology).
 *
 * The browser holds one httpOnly, Secure, SameSite=Lax cookie and nothing else. The tokens live
 * inside it, encrypted, and are only ever read on the server. **No token reaches JavaScript**, which
 * removes XSS token theft as a class rather than mitigating it — and is why the brief forbids JWTs
 * in localStorage.
 *
 * The `__Host-` prefix is not decoration: it makes the browser refuse the cookie unless it is Secure,
 * has no Domain attribute and is path `/`, so a subdomain that gets compromised cannot set one. That
 * cuts both ways, though: since `secure()` is deliberately false in development (plain HTTP), a
 * `__Host-`-prefixed name in that same environment is a cookie the browser will silently refuse to
 * store at all — the session would look like it saved and then vanish on the very next request. So
 * the prefix itself, not just the Secure flag, has to follow environment.
 */

const SESSION_COOKIE = process.env.NODE_ENV === 'production' ? '__Host-r25.session' : 'r25.session';
const FLOW_COOKIE = process.env.NODE_ENV === 'production' ? '__Host-r25.flow' : 'r25.flow';

/** The BFF is same-origin with the app, so Lax is enough and Strict would break the OAuth return. */
const COOKIE_BASE = {
  httpOnly: true,
  sameSite: 'lax',
  path: '/',
} as const;

export interface Session {
  accessToken: string;
  refreshToken?: string;
  /** Epoch milliseconds. Used to refresh slightly early rather than waiting for a 401. */
  expiresAt: number;
  idToken?: string;
  user: SessionUser;
}

export interface SessionUser {
  sub: string;
  name: string;
  email?: string;
  staffId?: number;
  locationId?: number;
  accessLevel?: number;
  roles: string[];
  permissions: string[];
}

/** The short-lived state carried across the authorization redirect. */
export interface AuthFlowState {
  codeVerifier: string;
  state: string;
  nonce: string;
  returnTo: string;
}

function secure(): boolean {
  // Secure cookies cannot be set over plain HTTP, and local development is plain HTTP. Production
  // is always HTTPS, so this is a development affordance rather than a configurable weakening.
  return process.env.NODE_ENV === 'production';
}

/**
 * The encryption key, derived from a configured secret.
 *
 * There is no fallback. A default key would mean every deployment that forgot to set one shares a
 * secret with every other, and a session cookie forged against one would be valid everywhere.
 */
function encryptionKey(): Uint8Array {
  const secret = process.env.SESSION_SECRET;

  if (!secret || secret.length < 32) {
    throw new Error('SESSION_SECRET must be set to at least 32 characters before the app can hold a session.');
  }

  // A256GCM needs exactly 32 bytes; SHA-256 gives that from a secret of any length.
  return new Uint8Array(createHash('sha256').update(secret).digest());
}

async function seal(payload: object, maxAgeSeconds: number): Promise<string> {
  return new EncryptJWT({ ...payload })
    .setProtectedHeader({ alg: 'dir', enc: 'A256GCM' })
    .setIssuedAt()
    .setExpirationTime(`${maxAgeSeconds}s`)
    .encrypt(encryptionKey());
}

async function open<T>(value: string): Promise<T | null> {
  try {
    const { payload } = await jwtDecrypt(value, encryptionKey());
    return payload as T;
  } catch {
    // A cookie that will not decrypt is a rotated key, a tampered value or a different deployment.
    // In every case the answer is the same: there is no session.
    return null;
  }
}

export async function readSession(): Promise<Session | null> {
  const cookie = cookies().get(SESSION_COOKIE)?.value;
  if (!cookie) return null;

  const session = await open<Session>(cookie);
  return session?.accessToken ? session : null;
}

export async function writeSession(session: Session): Promise<void> {
  // The cookie outlives the access token by design — it carries the refresh token, which is what
  // lets a shift run without a re-login.
  const maxAge = 8 * 60 * 60;

  cookies().set(SESSION_COOKIE, await seal(session, maxAge), {
    ...COOKIE_BASE,
    secure: secure(),
    maxAge,
  });
}

/**
 * Expires a cookie on the same terms it was issued on.
 *
 * `cookies().delete(name)` is not enough here, and the way it fails is silent. A deletion is a
 * Set-Cookie like any other, so it has to satisfy every rule the browser applied when it stored the
 * original — and in production these names carry the `__Host-` prefix, whose contract is Secure, no
 * Domain, and Path=/. The bare delete emits `Path=/` and no `Secure`, so the browser discarded it
 * and kept the cookie.
 *
 * That is what made signing out do nothing. The BFF believed it had cleared the session, the
 * identity provider genuinely cleared its own cookie, and the redirect home then found this cookie
 * still sitting there and treated the user as signed in — so logging out silently signed you back
 * in. Spelling the options out means the delete and the set cannot drift apart.
 */
function clearCookie(name: string): void {
  cookies().set(name, '', { ...COOKIE_BASE, secure: secure(), maxAge: 0 });
}

export function clearSession(): void {
  clearCookie(SESSION_COOKIE);
  clearCookie(FLOW_COOKIE);
}

export async function writeFlowState(flow: AuthFlowState): Promise<void> {
  // Ten minutes: long enough to sign in, short enough that an abandoned attempt cannot be resumed
  // later from a shared machine.
  cookies().set(FLOW_COOKIE, await seal(flow, 600), {
    ...COOKIE_BASE,
    secure: secure(),
    maxAge: 600,
  });
}

export async function readFlowState(): Promise<AuthFlowState | null> {
  const cookie = cookies().get(FLOW_COOKIE)?.value;
  if (!cookie) return null;

  return open<AuthFlowState>(cookie);
}

export function clearFlowState(): void {
  clearCookie(FLOW_COOKIE);
}

/**
 * What the browser is allowed to know about the session: who the user is and what they may do, so
 * the UI can hide affordances. Never the tokens.
 */
export function toPublicSession(session: Session): SessionUser {
  return session.user;
}
