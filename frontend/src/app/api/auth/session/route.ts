import { NextResponse } from 'next/server';
import { readSession, toPublicSession } from '@/lib/server/session';

export const dynamic = 'force-dynamic';

/**
 * Who is signed in, for the app shell.
 *
 * It returns identity and permissions and nothing else. Permissions are here so the UI can hide
 * affordances the user cannot use — a convenience, never a control: the server checks every command
 * regardless, and a client that lied about its permissions would simply receive a 403.
 */
export async function GET() {
  const session = await readSession();

  if (!session) {
    return NextResponse.json({ authenticated: false }, { status: 200, headers: noStore });
  }

  return NextResponse.json(
    { authenticated: true, user: toPublicSession(session) },
    { status: 200, headers: noStore },
  );
}

/** A session response must never sit in a shared cache on a shop network. */
const noStore = { 'Cache-Control': 'no-store' };
