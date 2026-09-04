import { NextResponse, type NextRequest } from 'next/server';
import { authorityUrl } from '@/lib/server/oidc';

/**
 * The anonymous account flows: sign up, ask for a reset link, redeem one.
 *
 * These do not go through `/api/proxy` because that route attaches the session's bearer token and
 * refuses without one — and nobody creating an account or recovering a password has a session yet.
 *
 * They are proxied rather than called from the browser for two reasons. The API's origin is not in
 * the app's connect-src, so a direct call would be blocked by the content-security policy; and
 * keeping the API off the public origin means the browser never learns where it lives.
 */

/** Only these three. An open proxy onto the identity provider is not what this is. */
const ALLOWED = new Set(['register', 'forgot-password', 'reset-password']);

/**
 * Readable without a session, and only this one.
 *
 * Kept as a separate list from ALLOWED rather than a flag on it, because the two answer different
 * questions: ALLOWED is what may be *done* anonymously, this is what may be *asked*. Sharing one
 * set would mean a future addition to either silently joined the other.
 */
const ALLOWED_GET = new Set(['registration']);

/**
 * Whether this deployment accepts self sign-up.
 *
 * The sign-in page asks before offering to create an account. Self-registration is off by default
 * on the API (`Auth:SelfRegistration:Enabled`), so a link shown unconditionally leads to a 403 —
 * which is a dead end a new employee has no way to interpret, and is exactly what the API's own
 * `registration` endpoint was added to prevent.
 *
 * Anonymous by necessity: the people who need the answer are the ones who cannot sign in. It
 * discloses nothing a single POST to `register` would not.
 *
 * A deployment that cannot be reached answers "off". The alternative is showing the link on a
 * network blip and sending somebody to a page that will refuse them.
 */
export async function GET(request: NextRequest, context: { params: Promise<{ action: string }> }) {
  const { action } = await context.params;

  if (!ALLOWED_GET.has(action)) {
    return NextResponse.json({ title: 'not_found' }, { status: 404 });
  }

  try {
    const upstream = await fetch(authorityUrl(`/api/v1/account/${action}`), { cache: 'no-store' });

    if (!upstream.ok) return NextResponse.json({ enabled: false }, { status: 200 });

    const body = (await upstream.json()) as { enabled?: unknown };

    return NextResponse.json({ enabled: body.enabled === true }, { status: 200 });
  } catch {
    return NextResponse.json({ enabled: false }, { status: 200 });
  }
}

export async function POST(request: NextRequest, context: { params: Promise<{ action: string }> }) {
  const { action } = await context.params;

  if (!ALLOWED.has(action)) {
    return NextResponse.json({ title: 'not_found' }, { status: 404 });
  }

  let body: string;

  try {
    body = JSON.stringify(await request.json());
  } catch {
    return NextResponse.json({ title: 'invalid_body' }, { status: 400 });
  }

  let upstream: Response;

  try {
    upstream = await fetch(authorityUrl(`/api/v1/account/${action}`), {
      method: 'POST',
      headers: {
        'content-type': 'application/json',

        // The API rate-limits these endpoints, and without this every request would look like it came
        // from this server — one bucket for the whole shop, which one script could exhaust.
        'x-forwarded-for': request.headers.get('x-forwarded-for') ?? '',
      },
      body,
      cache: 'no-store',
    });
  } catch {
    return NextResponse.json(
      { title: 'upstream_unreachable', detail: 'The server is not responding. Try again shortly.' },
      { status: 502 },
    );
  }

  const text = await upstream.text();

  // Passed through verbatim. The API is careful about what these responses reveal — a sign-up for an
  // address that already exists answers exactly as one for a new address does — and rewriting them
  // here would be a good way to undo that by accident.
  return new NextResponse(text.length > 0 ? text : null, {
    status: upstream.status,
    headers: { 'content-type': upstream.headers.get('content-type') ?? 'application/json' },
  });
}
