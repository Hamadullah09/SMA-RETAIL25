import { NextRequest, NextResponse } from 'next/server';
import {
  buildAuthorizeUrl,
  createCodeChallenge,
  createCodeVerifier,
  createRandomToken,
} from '@/lib/server/oidc';
import { writeFlowState } from '@/lib/server/session';

export const dynamic = 'force-dynamic';

/**
 * Starts the sign-in (doc 07 §Flow detail, steps 1–2).
 *
 * The verifier, state and nonce are generated here and kept in a short-lived encrypted cookie. The
 * browser is given only the challenge, so intercepting the redirect yields nothing that can be
 * exchanged — which is the whole point of PKCE for a client that cannot keep a secret.
 */
export async function GET(request: NextRequest) {
  const requested = request.nextUrl.searchParams.get('returnTo') ?? '/';

  // Only ever a path on this app. An open redirect here would let an attacker bounce a freshly
  // authenticated user anywhere they liked, with the sign-in looking entirely legitimate.
  const returnTo = requested.startsWith('/') && !requested.startsWith('//') ? requested : '/';

  const codeVerifier = createCodeVerifier();
  const state = createRandomToken();
  const nonce = createRandomToken();

  await writeFlowState({ codeVerifier, state, nonce, returnTo });

  return NextResponse.redirect(
    buildAuthorizeUrl({
      codeChallenge: createCodeChallenge(codeVerifier),
      state,
      nonce,
    }),
  );
}
