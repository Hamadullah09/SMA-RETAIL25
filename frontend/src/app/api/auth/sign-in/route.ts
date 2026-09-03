import { NextRequest, NextResponse } from 'next/server';
import { signIn } from '@/lib/server/auth-api';
import { writeSession, toPublicSession } from '@/lib/server/session';

export const dynamic = 'force-dynamic';

/**
 * The sign-in, server-side.
 *
 * The password reaches this route and goes no further into the browser's world: it is posted to the
 * API from the server, and what comes back is written straight into the encrypted session cookie.
 * The page that collected it never sees a token, which is what keeps the "no token is reachable
 * from JavaScript" guarantee true after moving off the redirect flow.
 */
export async function POST(request: NextRequest) {
  const body = (await request.json().catch(() => null)) as
    | { username?: string; password?: string }
    | null;

  const username = body?.username?.trim() ?? '';
  const password = body?.password ?? '';

  if (!username || !password) {
    return NextResponse.json({ message: 'Enter your username and password.' }, { status: 400 });
  }

  const result = await signIn(username, password);

  if (!result.ok) {
    // The API's wording, passed through. It knows the difference between a wrong password, a
    // disabled account and a lockout with a time on it, and flattening those into one sentence
    // sends people to an administrator for a problem that clears itself in ten minutes.
    return NextResponse.json({ message: result.message }, { status: 401 });
  }

  await writeSession(result.session);

  return NextResponse.json({ user: toPublicSession(result.session) });
}
