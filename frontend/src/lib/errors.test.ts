import { describe, expect, it } from 'vitest';
import { describeError, isWorthRetrying } from './errors';
import { PosApiError } from './pos-api';

/**
 * What a person is told when something fails.
 *
 * The sentence this replaced — "Something went wrong." — was copy-pasted into twenty-one files and
 * was the most common error message in the application. It names nothing, blames nothing and
 * suggests nothing, and it was shown to somebody standing at a till with a customer waiting.
 *
 * The tests below are mostly about what must *not* reach that person. A message written for a log
 * is worse than a vague one, because it reads as a crash.
 */
function problem(status: number, detail = '', code = 'x.y', title = 'Failed') {
  return new PosApiError({ status, code, detail, title } as never);
}

describe('a status gets the sentence that fits it', () => {
  it.each([
    [403, /permission/i],
    [404, /no longer there/i],
    [409, /somebody else changed/i],
    [413, /too large/i],
    [415, /not accepted/i],
    [503, /not available/i],
  ])('%i', (status, expected) => {
    expect(describeError(problem(status))).toMatch(expected);
  });

  /**
   * The distinction that matters most. A 403 is not a transient fault, and offering "try again" for
   * one invites somebody to press it until they conclude the software is broken.
   */
  it('does not invite a retry on a refusal', () => {
    expect(isWorthRetrying(problem(403))).toBe(false);
    expect(isWorthRetrying(problem(404))).toBe(false);
    expect(isWorthRetrying(problem(500))).toBe(true);
    expect(isWorthRetrying(problem(429))).toBe(true);
  });
});

describe('the API text is shown only when it reads like prose', () => {
  it('shows a sentence written for a person', () => {
    const message = 'This item is on a stock count that has not been posted.';

    expect(describeError(problem(422, message))).toBe(message);
  });

  it.each([
    ['System.NullReferenceException: Object reference not set', 'a .NET exception'],
    ['at Retail25.Application.Sales.Handle(cmd)', 'a stack frame'],
    ['{"errors":{"price":["required"]}}', 'raw JSON'],
    ['value cannot be null', 'a null complaint'],
    ['Microsoft.Data.SqlClient.SqlException', 'a provider name'],
  ])('hides %s', (detail) => {
    const shown = describeError(problem(422, detail));

    expect(shown).not.toContain(detail);
    expect(shown).toMatch(/cannot be done|did not work/i);
  });

  it('hides an essay, however well written', () => {
    const shown = describeError(problem(500, 'x'.repeat(400)));

    expect(shown).toMatch(/server had a problem/i);
  });
});

describe('errors that never reached the API', () => {
  /**
   * The one case where the advice genuinely differs: check the connection, not the input. A failed
   * fetch arrives as a TypeError with no status at all.
   */
  it('tells somebody to check the connection', () => {
    expect(describeError(new TypeError('Failed to fetch'))).toMatch(/cannot reach the server/i);
    expect(isWorthRetrying(new TypeError('Failed to fetch'))).toBe(true);
  });

  it('never shows a bare object', () => {
    expect(describeError({})).toMatch(/did not work/i);
    expect(describeError(null)).toMatch(/did not work/i);
    expect(describeError('a string thrown from somewhere')).toMatch(/did not work/i);
  });
});

describe('the axios shape, for the calls that bypass the typed client', () => {
  /**
   * Branding, product images and tag import call axios directly. Their failures arrive wrapped in a
   * response object, and their own helpers read `detail` straight out of it — which is how "Request
   * failed with status code 413" came to be shown to somebody uploading a photograph.
   */
  it('reads a wrapped problem', () => {
    const error = { response: { status: 413, data: { detail: 'x', title: 'Too large' } } };

    expect(describeError(error)).toMatch(/too large/i);
  });

  it('prefers the status sentence over an unhelpful detail', () => {
    const error = { response: { status: 415, data: { detail: 'Unsupported Media Type' } } };

    expect(describeError(error)).toMatch(/file type is not accepted/i);
  });

  it('retries a wrapped server fault but not a wrapped refusal', () => {
    expect(isWorthRetrying({ response: { status: 502 } })).toBe(true);
    expect(isWorthRetrying({ response: { status: 403 } })).toBe(false);
  });
});
