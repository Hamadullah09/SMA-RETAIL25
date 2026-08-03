import { NextRequest, NextResponse } from 'next/server';
import { AUTHORITY } from '@/lib/server/oidc';
import { refreshSession } from '@/lib/server/oidc';
import { clearSession, readSession, writeSession, type Session } from '@/lib/server/session';

export const dynamic = 'force-dynamic';

/**
 * Every API call the browser makes goes through here (doc 07 §Topology, steps 7–8).
 *
 * The page calls a same-origin path with no credentials of its own. This handler reads the encrypted
 * cookie, attaches the bearer token and forwards. **The token never crosses back**, which is what
 * makes "no JWT in localStorage" a structural property rather than a convention someone has to
 * remember.
 *
 * It refreshes proactively when the token is nearly expired, and once reactively on a 401 — because
 * a token can be revoked server-side between one request and the next, and a single retry turns that
 * into an invisible hiccup rather than a logout mid-sale.
 */

const HOP_BY_HOP = new Set([
  'connection',
  'keep-alive',
  'transfer-encoding',
  'upgrade',
  'host',
  'content-length',
  // fetch() transparently decompresses the response body, so forwarding the API's original
  // Content-Encoding here would tell the browser to decode already-decoded bytes a second time —
  // it works for small (uncompressed) error bodies and silently breaks on anything large enough
  // to cross ASP.NET's compression threshold.
  'content-encoding',
]);

async function handle(request: NextRequest, path: string[]): Promise<NextResponse> {
  let session = await readSession();

  if (!session) {
    return unauthenticated();
  }

  // Refresh slightly early rather than waiting for a failure: a sale being saved should not have to
  // survive a round trip that was always going to 401.
  if (session.expiresAt <= Date.now()) {
    const refreshed = await tryRefresh(session);

    if (!refreshed) {
      clearSession();
      return unauthenticated();
    }

    session = refreshed;
  }

  let response = await forward(request, path, session.accessToken);

  // Revoked between requests. One retry, then give up — retrying a refresh that just failed only
  // delays telling the cashier they need to sign in again.
  if (response.status === 401) {
    const refreshed = await tryRefresh(session);

    if (!refreshed) {
      clearSession();
      return unauthenticated();
    }

    session = refreshed;
    response = await forward(request, path, session.accessToken);
  }

  return toNextResponse(response);
}

async function tryRefresh(session: Session): Promise<Session | null> {
  if (!session.refreshToken) return null;

  const refreshed = await refreshSession(session.refreshToken);

  if (refreshed) {
    await writeSession(refreshed);
  }

  return refreshed;
}

async function forward(request: NextRequest, path: string[], accessToken: string): Promise<Response> {
  const target = new URL(`/api/v1/${path.join('/')}`, AUTHORITY);
  target.search = request.nextUrl.search;

  const headers = new Headers();

  for (const [key, value] of request.headers) {
    if (!HOP_BY_HOP.has(key.toLowerCase()) && key.toLowerCase() !== 'cookie') {
      headers.set(key, value);
    }
  }

  headers.set('Authorization', `Bearer ${accessToken}`);

  // The session cookie is deliberately not forwarded: the API authenticates on the bearer token, and
  // passing the cookie on would widen what a compromised API could do with it.
  const body = request.method === 'GET' || request.method === 'HEAD'
    ? undefined
    : await request.arrayBuffer();

  return fetch(target, {
    method: request.method,
    headers,
    body,
    cache: 'no-store',
    redirect: 'manual',
  });
}

async function toNextResponse(response: Response): Promise<NextResponse> {
  const headers = new Headers();

  for (const [key, value] of response.headers) {
    // Set-Cookie from the API must not reach the browser: the only cookie this origin sets is the
    // session, and it is set here.
    if (!HOP_BY_HOP.has(key.toLowerCase()) && key.toLowerCase() !== 'set-cookie') {
      headers.set(key, value);
    }
  }

  // API responses are not cached by default — most of them are somebody's balance, price or sale, and
  // a shared cache holding those is a data leak waiting for a shared machine. The exception is an
  // upstream that explicitly said `private`: only the product-image endpoint does, and a till redrawing
  // forty tiles per category change should not re-fetch forty JPEGs each time. `private` keeps them out
  // of any shared cache, and the ETag means a replaced picture still appears at once.
  const upstream = response.headers.get('cache-control');
  headers.set(
    'Cache-Control',
    upstream?.toLowerCase().startsWith('private') ? upstream : 'no-store',
  );

  return new NextResponse(response.body, { status: response.status, headers });
}

function unauthenticated(): NextResponse {
  return NextResponse.json(
    {
      status: 401,
      title: 'Not signed in',
      detail: 'This session has expired. Sign in again to continue.',
      code: 'auth.session_expired',
    },
    { status: 401, headers: { 'Cache-Control': 'no-store' } },
  );
}

export async function GET(request: NextRequest, context: { params: { path: string[] } }) {
  return handle(request, context.params.path);
}

export async function POST(request: NextRequest, context: { params: { path: string[] } }) {
  return handle(request, context.params.path);
}

export async function PUT(request: NextRequest, context: { params: { path: string[] } }) {
  return handle(request, context.params.path);
}

export async function PATCH(request: NextRequest, context: { params: { path: string[] } }) {
  return handle(request, context.params.path);
}

export async function DELETE(request: NextRequest, context: { params: { path: string[] } }) {
  return handle(request, context.params.path);
}
