import 'server-only';

import { createHash, randomBytes } from 'node:crypto';
import type { Session, SessionUser } from '@/lib/server/session';

/**
 * The OpenID Connect half of the BFF (doc 07 §Flow detail).
 *
 * Everything here runs on the Next.js server. The code exchange, the refresh and the userinfo call
 * all happen over a back channel, so the tokens exist only in this process and in the encrypted
 * cookie — never in a page, a bundle or a network response the browser can read.
 */

export const AUTHORITY = process.env.API_URL ?? 'http://localhost:5000';
export const CLIENT_ID = process.env.OIDC_CLIENT_ID ?? 'retail25-web';
export const APP_ORIGIN = process.env.APP_ORIGIN ?? 'http://localhost:3000';

export const REDIRECT_URI = `${APP_ORIGIN.replace(/\/$/, '')}/api/auth/callback`;

/**
 * Narrows a candidate to a path on this app, falling back to the root.
 *
 * An absolute URL that reached a redirect would be an open redirect, and a sign-in is exactly where
 * one is worth the most: the bounce happens after the credentials are accepted, so it looks entirely
 * legitimate to whoever is being sent.
 */
export function localPath(candidate: string | null | undefined): string {
  return candidate && candidate.startsWith('/') && !candidate.startsWith('//') ? candidate : '/';
}

/**
 * Resolves an in-app path against the configured public origin.
 *
 * Deliberately not against the incoming request's origin. Behind IIS the front end runs under
 * HttpPlatformHandler, which proxies to Node on a private port it picks at start-up, so the request
 * Next.js sees is addressed to `localhost:<HTTP_PLATFORM_PORT>` and `request.nextUrl.origin` is that
 * port. A redirect built from it sends the browser somewhere only the server can reach.
 *
 * The failure is nastier than it sounds, because everything before the redirect works: the password
 * is accepted, the code is exchanged, the session cookie is written — and then the browser lands on
 * ERR_CONNECTION_REFUSED. Going back and resubmitting the sign-in form is the obvious thing to try,
 * and the API answers that stale form "That form had expired", which is where the hunt starts and
 * why it starts in the wrong place.
 *
 * APP_ORIGIN is the value {@link REDIRECT_URI} is already pinned to, so the address the browser is
 * sent to and the address the authorization request registered cannot drift apart.
 */
export function appUrl(path: string): URL {
  return new URL(localPath(path), APP_ORIGIN);
}

/**
 * Joins a path onto {@link AUTHORITY}, keeping any path the authority already has.
 * <p>
 * `new URL('/connect/token', 'https://shop.example/backend')` is
 * `https://shop.example/connect/token` — a leading slash means "root of the origin", so the
 * `/backend` is discarded without a word. Everything here used to be written that way, which
 * worked only because the API had an origin to itself. Mounted as a sub-application it does not,
 * and every back-channel call lands on the front end instead: sign-in redirects to an authorize
 * endpoint that isn't there, and the proxy asks Next.js for `/api/v1/...`.
 * </p>
 * <p>
 * Appending to a base that ends in a slash is the form that keeps both cases right — the API at
 * `https://api.example.com` and the API at `https://shop.example/backend`.
 * </p>
 */
export function authorityUrl(path: string): URL {
  const base = AUTHORITY.endsWith('/') ? AUTHORITY : `${AUTHORITY}/`;
  return new URL(path.replace(/^\//, ''), base);
}

export const SCOPES = ['openid', 'profile', 'roles', 'offline_access', 'retail25.api'].join(' ');

function base64Url(buffer: Buffer): string {
  return buffer.toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** RFC 7636: 43–128 characters of crypto-random. 32 bytes base64url is 43. */
export function createCodeVerifier(): string {
  return base64Url(randomBytes(32));
}

export function createCodeChallenge(verifier: string): string {
  return base64Url(createHash('sha256').update(verifier).digest());
}

export function createRandomToken(): string {
  return base64Url(randomBytes(24));
}

export function buildAuthorizeUrl(params: {
  codeChallenge: string;
  state: string;
  nonce: string;
}): string {
  const url = authorityUrl('/connect/authorize');

  url.searchParams.set('client_id', CLIENT_ID);
  url.searchParams.set('response_type', 'code');
  url.searchParams.set('redirect_uri', REDIRECT_URI);
  url.searchParams.set('scope', SCOPES);
  url.searchParams.set('state', params.state);
  url.searchParams.set('nonce', params.nonce);
  url.searchParams.set('code_challenge', params.codeChallenge);
  url.searchParams.set('code_challenge_method', 'S256');

  return url.toString();
}

interface TokenResponse {
  access_token: string;
  refresh_token?: string;
  id_token?: string;
  expires_in: number;
  token_type: string;
}

async function postToken(body: URLSearchParams): Promise<TokenResponse | null> {
  const response = await fetch(authorityUrl('/connect/token'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body,
    cache: 'no-store',
  });

  if (!response.ok) {
    return null;
  }

  return (await response.json()) as TokenResponse;
}

/** Exchanges the code for tokens. PKCE is mandatory server-side, so the verifier is always sent. */
export async function exchangeCode(code: string, codeVerifier: string): Promise<Session | null> {
  const tokens = await postToken(
    new URLSearchParams({
      grant_type: 'authorization_code',
      client_id: CLIENT_ID,
      code,
      redirect_uri: REDIRECT_URI,
      code_verifier: codeVerifier,
    }),
  );

  if (!tokens) return null;

  const user = await fetchUserinfo(tokens.access_token);
  if (!user) return null;

  return toSession(tokens, user);
}

/**
 * Refreshes in flight, keyed by the token being spent.
 *
 * A refresh token is single use. Two BFF routes redeem them — the API proxy and the hub-ticket
 * minter — and a page that has been open a while opens several at once: the till alone asks for
 * three hub tickets and a cart in the same tick. Each handler reads the same cookie, finds the same
 * expired session, and posts the same refresh token.
 *
 * The server does exactly what it should with that: the first request rotates the token, and the
 * rest are replaying one that has been spent. Reuse detection is deliberately unforgiving here
 * (`SetRefreshTokenReuseLeeway(TimeSpan.Zero)`), so the losers race the winner's write and surface
 * as a 500 the caller then has to retry.
 *
 * Collapsing them costs one map entry and removes the race entirely: the first caller does the
 * round trip, every other caller awaits the same promise and gets the same rotated session. This
 * is per server instance, which is the whole scope that matters — the cookie is read and written
 * by whichever instance served the request, so concurrent redemptions can only arise within one.
 */
const refreshesInFlight = new Map<string, Promise<Session | null>>();

/**
 * Rotates the refresh token. The server issues a new one each time and revokes the family if a spent
 * one is replayed, so a stolen refresh token is worth one use and then burns the session it came from.
 */
export function refreshSession(refreshToken: string): Promise<Session | null> {
  const existing = refreshesInFlight.get(refreshToken);
  if (existing) return existing;

  const attempt = redeem(refreshToken).finally(() => {
    refreshesInFlight.delete(refreshToken);
  });

  refreshesInFlight.set(refreshToken, attempt);

  return attempt;
}

async function redeem(refreshToken: string): Promise<Session | null> {
  const tokens = await postToken(
    new URLSearchParams({
      grant_type: 'refresh_token',
      client_id: CLIENT_ID,
      refresh_token: refreshToken,
    }),
  );

  if (!tokens) return null;

  const user = await fetchUserinfo(tokens.access_token);
  if (!user) return null;

  return toSession(tokens, user);
}

/**
 * Reads the identity from the API rather than decoding the token.
 *
 * Trusting the token's own claims would mean the BFF has to validate signatures, and getting that
 * wrong is a silent authentication bypass. Asking the issuer is one round trip per sign-in and
 * cannot be forged.
 */
export async function fetchUserinfo(accessToken: string): Promise<SessionUser | null> {
  const response = await fetch(authorityUrl('/connect/userinfo'), {
    headers: { Authorization: `Bearer ${accessToken}` },
    cache: 'no-store',
  });

  if (!response.ok) return null;

  const payload = (await response.json()) as {
    sub: string;
    name?: string;
    email?: string;
    staffId?: number;
    locationId?: number;
    accessLevel?: string;
    roles?: string[];
    permissions?: string[];
  };

  return {
    sub: payload.sub,
    name: payload.name ?? '',
    email: payload.email,
    staffId: payload.staffId ?? undefined,
    locationId: payload.locationId ?? undefined,
    accessLevel: payload.accessLevel ? Number(payload.accessLevel) : undefined,
    roles: payload.roles ?? [],
    permissions: payload.permissions ?? [],
  };
}

function toSession(tokens: TokenResponse, user: SessionUser): Session {
  return {
    accessToken: tokens.access_token,
    refreshToken: tokens.refresh_token,
    idToken: tokens.id_token,
    // Thirty seconds early, so a request never leaves with a token that expires in flight.
    expiresAt: Date.now() + (tokens.expires_in - 30) * 1000,
    user,
  };
}

export function buildLogoutUrl(idToken?: string): string {
  const url = authorityUrl('/connect/logout');

  url.searchParams.set('post_logout_redirect_uri', `${APP_ORIGIN.replace(/\/$/, '')}/`);

  if (idToken) {
    url.searchParams.set('id_token_hint', idToken);
  }

  return url.toString();
}
