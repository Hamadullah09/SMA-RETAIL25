/**
 * The three anonymous account calls, against the BFF's own origin.
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
