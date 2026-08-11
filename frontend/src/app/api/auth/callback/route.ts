import { NextRequest, NextResponse } from 'next/server';
import { appUrl, exchangeCode } from '@/lib/server/oidc';
import { clearFlowState, readFlowState, writeSession } from '@/lib/server/session';

export const dynamic = 'force-dynamic';

/**
 * Completes the sign-in (doc 07 §Flow detail, steps 4–6).
 *
 * The code is exchanged here, server-side, and the tokens go straight into the encrypted cookie.
 * Nothing token-shaped is ever put in the response, so there is nothing for a script on the page to
 * find.
 */
export async function GET(request: NextRequest) {
  const params = request.nextUrl.searchParams;
  const error = params.get('error');
  const code = params.get('code');
  const state = params.get('state');

  const flow = await readFlowState();
  clearFlowState();

  if (error) {
    return fail(error === 'access_denied' ? 'access_denied' : 'authorization_failed');
  }

  if (!code || !state || !flow) {
    return fail('invalid_callback');
  }

  // The state check is what makes the callback belong to the sign-in this browser started. Without
  // it, an attacker can hand someone a callback URL and log them into an account they control.
  if (state !== flow.state) {
    return fail('state_mismatch');
  }

  const session = await exchangeCode(code, flow.codeVerifier);

  if (!session) {
    return fail('token_exchange_failed');
  }

  await writeSession(session);

  return NextResponse.redirect(appUrl(flow.returnTo));
}

function fail(reason: string): NextResponse {
  const url = appUrl('/');
  url.searchParams.set('authError', reason);
  return NextResponse.redirect(url);
}
