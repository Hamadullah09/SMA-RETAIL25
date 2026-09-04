/**
 * The anonymous account calls, against the BFF's own origin.
 *
 * Not part of `pos-api` because that client attaches the session and throws on anything but 2xx —
 * neither of which suits a form whose whole job is to render the failure to the person typing.
 */

export interface AccountProblem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

export type AccountResult =
  | { ok: true; status: number }
  | { ok: false; problem: AccountProblem };

export async function postAccount(
  action: 'register' | 'forgot-password' | 'reset-password',
  body: Record<string, string>,
): Promise<AccountResult> {
  let response: Response;

  try {
    response = await fetch(`/api/auth/account/${action}`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body),
    });
  } catch {
    return { ok: false, problem: { detail: 'Could not reach the server. Check the connection and try again.' } };
  }

  if (response.ok) {
    return { ok: true, status: response.status };
  }

  if (response.status === 429) {
    // Worth its own message: "something went wrong" invites the retry that caused it.
    return { ok: false, problem: { title: 'rate_limited', detail: 'Too many attempts. Wait a minute and try again.' } };
  }

  try {
    return { ok: false, problem: (await response.json()) as AccountProblem };
  } catch {
    return { ok: false, problem: { detail: 'Something went wrong. Try again.' } };
  }
}

/**
 * Whether this deployment lets somebody create their own account.
 *
 * Asked rather than assumed, because it is a per-deployment setting and it is off by default: a
 * shop that turns self-registration on must not need a rebuilt front end to get the link back.
 *
 * Never throws. Every failure — offline, 500, malformed body — answers "off", because the cost of
 * the two mistakes is not symmetric: hiding the link on a deployment that would have allowed it
 * means asking a manager for an account, which is what most shops want anyway; showing it on one
 * that refuses means a 403 with no explanation, which is what this was built to stop.
 */
export async function selfRegistrationEnabled(): Promise<boolean> {
  try {
    const response = await fetch('/api/auth/account/registration', { cache: 'no-store' });

    if (!response.ok) return false;

    const body = (await response.json()) as { enabled?: unknown };

    return body.enabled === true;
  } catch {
    return false;
  }
}
