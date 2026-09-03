import { NextRequest, NextResponse } from 'next/server';
import { authorityUrl } from '@/lib/server/oidc';
import { refreshSession } from '@/lib/server/auth-api';
import { clearSession, readSession, writeSession } from '@/lib/server/session';

export const dynamic = 'force-dynamic';

/**
 * Mints the single-use ticket the browser needs to open a SignalR connection (doc 07 §Topology).
 *
 * This is the one value the browser is given that looks like a credential, and it is deliberately
 * almost worthless: it authenticates one hub connection, expires in a minute, and is consumed on
 * use. The access token stays on the server, so an XSS payload gains nothing it did not already
 * have by running inside the page.
 */
export async function POST(request: NextRequest) {
  let session = await readSession();

  if (!session) {
    return NextResponse.json({ code: 'auth.session_expired' }, { status: 401, headers: noStore });
  }

  if (session.expiresAt <= Date.now() && session.refreshToken) {
    const refreshed = await refreshSession(session);

    if (!refreshed) {
      clearSession();
      return NextResponse.json({ code: 'auth.session_expired' }, { status: 401, headers: noStore });
    }

    await writeSession(refreshed);
    session = refreshed;
  }

  const body = await request.json().catch(() => ({}));

  const response = await fetch(authorityUrl('/api/v1/hub-tickets'), {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${session.accessToken}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ stationId: body?.stationId ?? null }),
    cache: 'no-store',
  });

  if (!response.ok) {
    return NextResponse.json({ code: 'hub.ticket_failed' }, { status: response.status, headers: noStore });
  }

  return NextResponse.json(await response.json(), { status: 200, headers: noStore });
}

const noStore = { 'Cache-Control': 'no-store' };
