import { PosApiError } from './pos-api';

/**
 * Turning something that went wrong into a sentence somebody can act on.
 *
 * This existed twenty-one times, eighteen of them identical:
 *
 *     error instanceof PosApiError ? error.problem.detail : 'Something went wrong.'
 *
 * Which made "Something went wrong." the most common error message in the application — a sentence
 * that names nothing, blames nothing and suggests nothing, shown to somebody standing at a till
 * with a customer waiting.
 *
 * The rules here, in order:
 *
 * 1. A status the API is telling us about gets the sentence that fits that status. A 403 is not a
 *    failure to be retried and a 409 is not a server fault, and telling somebody to "try again" for
 *    either sends them round a loop.
 * 2. Otherwise the API's own `detail`, but only when it reads like prose — short, sentence-shaped,
 *    not a stack frame or an identifier. A backend written for developers will otherwise put
 *    "Object reference not set to an instance of an object" in front of a cashier.
 * 3. Otherwise a plain fallback that at least says which half broke.
 */
const BY_STATUS: Record<number, string> = {
  400: 'Some of what was entered is not valid. Check the highlighted fields and try again.',
  401: 'Your session has ended. Sign in again.',
  403: 'This account does not have permission for that. Ask an administrator.',
  404: 'That is no longer there. It may have been deleted or renamed.',
  409: 'Somebody else changed this while you were working on it. Reload and try again.',
  413: 'That file is too large. Try a smaller one.',
  415: 'That file type is not accepted.',
  422: 'That cannot be done as it stands. The reason is above the form.',
  428: 'A supervisor needs to approve this.',
  429: 'Too many attempts in a row. Wait a moment and try again.',
  500: 'The server had a problem with that. It has been logged.',
  502: 'The server did not answer. Check the connection and try again.',
  503: 'The server is not available right now. Try again in a moment.',
  504: 'The server took too long to answer. Try again.',
};

/**
 * The statuses where our sentence beats the server's.
 *
 * A 500 or a 401 is about the plumbing, and whatever the server says about it was written for a log.
 * A 422 or a 409 is about the shop — "this item is on a stock count that has not been posted" is
 * something only the server knows and exactly what the person needs. So the domain statuses defer to
 * the server's own words when those words read like words, and the infrastructure statuses do not.
 */
const PREFER_OURS = new Set([401, 403, 413, 415, 429, 500, 502, 503, 504]);

const BY_CODE: Record<string, string> = {
  'auth.session_expired': 'Your session has ended. Sign in again.',
  'sale.requires_supervisor': 'A supervisor needs to approve this.',
  'network.unreachable': 'Cannot reach the server. Check the connection and try again.',
};

/**
 * Whether the API's own text is fit to show.
 *
 * A `detail` written for a person is a short sentence. One written for a log is long, or carries a
 * type name, a path, a brace or a stack frame. This is a heuristic and it is deliberately strict:
 * the cost of hiding a usable message is a slightly vaguer sentence, and the cost of showing an
 * unusable one is somebody reading "System.NullReferenceException" while a queue forms.
 */
function readsLikeProse(detail: string | undefined): detail is string {
  if (!detail) return false;

  const text = detail.trim();

  if (text.length === 0 || text.length > 200) return false;

  // Identifiers, namespaces, stack frames, JSON, SQL.
  if (/[{}<>[\]|\\]|\bat\s+\w+\.\w+|System\.|Microsoft\.|Exception\b|\bnull\b|_[a-z]+_|::/i.test(text)) {
    return false;
  }

  return true;
}

/**
 * The axios shape, for the calls that never went through the typed client.
 *
 * Branding, product images, tag import and the RFID screen call axios directly, so their failures
 * arrive as `{ response: { data: ProblemDetails } }` rather than a PosApiError — and their own local
 * helpers read `detail` straight out of it. That is how "Request failed with status code 413" came
 * to be shown to somebody uploading a photograph.
 */
function fromAxios(error: unknown): { status?: number; code?: string; detail?: string; title?: string } | null {
  const shaped = error as {
    response?: { status?: number; data?: { code?: string; detail?: string; title?: string } };
  };

  if (!shaped?.response) return null;

  return {
    status: shaped.response.status,
    code: shaped.response.data?.code,
    detail: shaped.response.data?.detail,
    title: shaped.response.data?.title,
  };
}

export function describeError(error: unknown): string {
  const axios = fromAxios(error);

  if (axios) {
    if (axios.code && BY_CODE[axios.code]) return BY_CODE[axios.code];

    const ours = axios.status ? BY_STATUS[axios.status] : undefined;

    if (ours && PREFER_OURS.has(axios.status!)) return ours;
    if (readsLikeProse(axios.detail)) return axios.detail;
    if (ours) return ours;

    // Last, not second. A ProblemDetails title is usually the status's own name — "Bad Request",
    // "Conflict" — which reads like prose and says nothing, so it must not outrank our sentence.
    if (readsLikeProse(axios.title)) return axios.title;

    return 'That did not work. Nothing has been changed.';
  }

  if (error instanceof PosApiError) {
    const byCode = BY_CODE[error.problem.code];
    if (byCode) return byCode;

    const ours = BY_STATUS[error.problem.status];

    if (ours && PREFER_OURS.has(error.problem.status)) return ours;
    if (readsLikeProse(error.problem.detail)) return error.problem.detail;
    if (ours) return ours;

    // Last, not second. See the note above: a title is usually the status's own name.
    if (readsLikeProse(error.problem.title)) return error.problem.title;

    return 'That did not work. Nothing has been changed.';
  }

  // A fetch that never reached the server throws a TypeError, and it is the one case where the
  // advice is genuinely different: check the connection rather than check the input.
  if (error instanceof TypeError && /fetch|network|load failed/i.test(error.message)) {
    return 'Cannot reach the server. Check the connection and try again.';
  }

  if (error instanceof Error && readsLikeProse(error.message)) {
    return error.message;
  }

  return 'That did not work. Nothing has been changed.';
}

/**
 * Whether trying the same thing again could plausibly work.
 *
 * Used to decide whether an error state offers a Try again button at all. Offering one for a 403 is
 * an invitation to press it repeatedly and conclude the software is broken.
 */
export function isWorthRetrying(error: unknown): boolean {
  const axios = fromAxios(error);

  if (axios?.status) {
    return axios.status >= 500 || axios.status === 408 || axios.status === 429;
  }

  if (error instanceof PosApiError) {
    const status = error.problem.status;

    return status >= 500 || status === 408 || status === 429;
  }

  return error instanceof TypeError;
}
