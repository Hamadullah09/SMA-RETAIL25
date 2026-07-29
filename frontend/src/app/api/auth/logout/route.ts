import { NextRequest, NextResponse } from 'next/server';
import { buildLogoutUrl } from '@/lib/server/oidc';
import { clearSession, readSession } from '@/lib/server/session';

export const dynamic = 'force-dynamic';

/**
 * Signs out both halves.
 *
 * The local cookie goes first, so the browser is signed out even if the identity provider is
 * unreachable — a logout that silently fails because a server was down is worse than one that
 * happens twice.
 */
export async function POST(request: NextRequest) {
  const session = await readSession();
  clearSession();

  return NextResponse.json(
    { signedOut: true, endSessionUrl: buildLogoutUrl(session?.idToken) },
    { status: 200, headers: { 'Cache-Control': 'no-store' } },
  );
}

/** Also allowed as a link target, for the "sign out" menu item. */
export async function GET(request: NextRequest) {
  const session = await readSession();
  clearSession();

  return NextResponse.redirect(buildLogoutUrl(session?.idToken));
}
