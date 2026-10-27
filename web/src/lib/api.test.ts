import { describe, expect, it, vi } from 'vitest';
import { ApiError, CSRF_HEADER, CSRF_VALUE, FORBIDDEN_MESSAGE, createApi, queryString, safeNext, signInUrl } from './api';

type Call = { url: string; init: RequestInit };

/** A fake `fetch` that answers every request with `status` and `body`, and remembers the calls. */
function fakeFetch(status: number, body?: unknown, contentType = 'application/json') {
  const calls: Call[] = [];
  const fn = vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ url: String(url), init: init ?? {} });
    const text = body === undefined ? '' : typeof body === 'string' ? body : JSON.stringify(body);
    return new Response(status === 204 ? null : text, { status, headers: { 'Content-Type': contentType } });
  });
  return { fetch: fn as unknown as typeof fetch, calls };
}

function headersOf(call: Call): Record<string, string> {
  return call.init.headers as Record<string, string>;
}

describe('the API client', () => {
  it('calls the API under the base, with the cookie and the query', async () => {
    const { fetch, calls } = fakeFetch(200, { items: [], total: 0, page: 1, pageSize: 50 });
    const api = createApi({ base: 'http://localhost:5080', fetch });
    await api.sales({ storeId: 's1', from: '2026-10-01', to: '2026-10-28', search: '', station: undefined });
    expect(calls[0].url).toBe('http://localhost:5080/api/sales?storeId=s1&from=2026-10-01&to=2026-10-28');
    expect(calls[0].init.credentials).toBe('include');
    expect(calls[0].init.method).toBe('GET');
  });

  it('does not send the CSRF header on reads', async () => {
    const { fetch, calls } = fakeFetch(200, []);
    await createApi({ base: '', fetch }).stores();
    expect(headersOf(calls[0])[CSRF_HEADER]).toBeUndefined();
  });

  it.each([
    ['POST', (api: ReturnType<typeof createApi>) => api.addStore({ code: 'B', name: 'Branch', timeZoneId: null })],
    ['PUT', (api: ReturnType<typeof createApi>) => api.saveStore('s1', { code: 'B', name: 'Branch', timeZoneId: null })],
    ['DELETE', (api: ReturnType<typeof createApi>) => api.deleteItem('i1')],
  ])('sends X-Requested-With on %s', async (method, call) => {
    const { fetch, calls } = fakeFetch(method === 'DELETE' ? 204 : 200, method === 'DELETE' ? undefined : { id: 's1' });
    await call(createApi({ base: '', fetch }));
    expect(calls[0].init.method).toBe(method);
    expect(headersOf(calls[0])[CSRF_HEADER]).toBe(CSRF_VALUE);
    expect(calls[0].init.credentials).toBe('include');
  });

  it('sends JSON bodies as JSON', async () => {
    const { fetch, calls } = fakeFetch(200, { token: 't', expiresAtUtc: '2026-10-28T10:00:00Z', user: {} });
    await createApi({ base: '', fetch }).signIn({ userName: 'admin', password: 'secret' });
    expect(headersOf(calls[0])['Content-Type']).toBe('application/json');
    expect(JSON.parse(String(calls[0].init.body))).toEqual({ userName: 'admin', password: 'secret' });
  });

  it('returns undefined for 204', async () => {
    const { fetch } = fakeFetch(204);
    await expect(createApi({ base: '', fetch }).changePassword({ currentPassword: 'a', newPassword: 'bbbbbbbb' })).resolves.toBeUndefined();
  });

  it("throws an ApiError carrying the server's error message", async () => {
    const { fetch } = fakeFetch(400, { error: 'A store code has 1 to 10 characters.' });
    const failure = createApi({ base: '', fetch }).addStore({ code: '', name: 'x', timeZoneId: null });
    await expect(failure).rejects.toBeInstanceOf(ApiError);
    await expect(failure).rejects.toMatchObject({ status: 400, message: 'A store code has 1 to 10 characters.' });
  });

  it('maps a 409 to a conflict', async () => {
    const { fetch } = fakeFetch(409, { error: 'Someone else changed this item.' });
    const error = await createApi({ base: '', fetch })
      .saveItem('i1', {} as never)
      .catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).isConflict).toBe(true);
    expect((error as ApiError).message).toBe('Someone else changed this item.');
  });

  it('says "Your role cannot open this page." for a 403 without a reason', async () => {
    const { fetch } = fakeFetch(403, '', 'text/plain');
    const error = (await createApi({ base: '', fetch }).audit({}).catch((e: unknown) => e)) as ApiError;
    expect(error.status).toBe(403);
    expect(error.isForbidden).toBe(true);
    expect(error.message).toBe(FORBIDDEN_MESSAGE);
  });

  it('keeps the reason of a 403 when the server gives one', async () => {
    const { fetch } = fakeFetch(403, { error: 'The package belongs to a till of another store.' });
    const error = (await createApi({ base: '', fetch }).batches({}).catch((e: unknown) => e)) as ApiError;
    expect(error.message).toBe('The package belongs to a till of another store.');
  });

  it('falls back to a plain message when the body is not JSON', async () => {
    const { fetch } = fakeFetch(500, '<html>oops</html>', 'text/html');
    const error = (await createApi({ base: '', fetch }).stores().catch((e: unknown) => e)) as ApiError;
    expect(error.status).toBe(500);
    expect(error.message).toMatch(/server had a problem/i);
  });

  it('turns a network failure into status 0', async () => {
    const fetch = vi.fn(async () => {
      throw new TypeError('Failed to fetch');
    }) as unknown as typeof globalThis.fetch;
    const error = (await createApi({ base: '', fetch }).stores().catch((e: unknown) => e)) as ApiError;
    expect(error.status).toBe(0);
    expect(error.message).toMatch(/cannot be reached/i);
  });

  it('sends a 401 to the sign-in page with the way back', async () => {
    const { fetch } = fakeFetch(401, { error: 'Sign in again.' });
    const go = vi.fn();
    const api = createApi({ base: '', fetch, onUnauthorized: go, currentPath: () => '/sales/view/?id=42' });
    await expect(api.sale('42')).rejects.toMatchObject({ status: 401 });
    expect(go).toHaveBeenCalledWith('/sign-in/?next=%2Fsales%2Fview%2F%3Fid%3D42');
  });

  it('redirects once when several requests fail with 401 together', async () => {
    const { fetch } = fakeFetch(401, { error: 'Sign in again.' });
    const go = vi.fn();
    const api = createApi({ base: '', fetch, onUnauthorized: go, currentPath: () => '/' });
    await Promise.allSettled([api.me(), api.stores(), api.tills({})]);
    expect(go).toHaveBeenCalledTimes(1);
  });

  it('does not leave the sign-in page when the password is wrong', async () => {
    const { fetch } = fakeFetch(401, { error: 'Wrong user name or password, or the account is locked for a few minutes.' });
    const go = vi.fn();
    const api = createApi({ base: '', fetch, onUnauthorized: go });
    await expect(api.signIn({ userName: 'a', password: 'b' })).rejects.toMatchObject({
      status: 401,
      message: 'Wrong user name or password, or the account is locked for a few minutes.',
    });
    expect(go).not.toHaveBeenCalled();
  });
});

describe('query strings and sign-in links', () => {
  it('leaves out empty values', () => {
    expect(queryString({ a: 1, b: '', c: null, d: undefined, e: false, f: 'x y' })).toBe('?a=1&e=false&f=x+y');
    expect(queryString({})).toBe('');
  });

  it('builds the sign-in link', () => {
    expect(signInUrl('/tills/')).toBe('/sign-in/?next=%2Ftills%2F');
    expect(signInUrl('/sign-in/?next=%2F')).toBe('/sign-in/');
    expect(signInUrl()).toBe('/sign-in/');
  });

  it('only follows a sign-in to a path on this site', () => {
    expect(safeNext('/sales/view/?id=1')).toBe('/sales/view/?id=1');
    expect(safeNext('https://evil.example/')).toBeNull();
    expect(safeNext('//evil.example/')).toBeNull();
    expect(safeNext('/\\evil.example')).toBeNull();
    expect(safeNext('/sign-in/')).toBeNull();
    expect(safeNext(null)).toBeNull();
  });
});
