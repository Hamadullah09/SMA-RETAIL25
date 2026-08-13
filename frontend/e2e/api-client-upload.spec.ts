import { test, expect } from '@playwright/test';
import type { InternalAxiosRequestConfig } from 'axios';
import { apiClient } from '../src/lib/api-client';

/**
 * What the client says a request body is.
 *
 * Every image upload in the application answered 415. The cause was one line of shared
 * configuration: the axios instance declared `Content-Type: application/json` for all requests, so
 * a multipart body went out described as JSON and without the boundary the browser had generated.
 * ASP.NET Core cannot bind an `IFormFile` from that, and answers "unsupported media type".
 *
 * The upload components were already doing the right thing — none of them set the header, with a
 * comment explaining why. It was the default underneath them that overrode it, which is why fixing
 * the pages one at a time would never have worked.
 *
 * Driven through axios itself with a stub adapter rather than by calling the interceptor directly,
 * because the bug lived in the interaction between defaults and interceptors — the layer a unit
 * test of either half on its own would step over.
 */

/** Captures the headers axios finally settled on, without a network call. */
async function headersFor(send: () => Promise<unknown>): Promise<Record<string, string>> {
  let captured: Record<string, string> = {};

  const original = apiClient.defaults.adapter;

  apiClient.defaults.adapter = async (config: InternalAxiosRequestConfig) => {
    captured = Object.fromEntries(
      Object.entries(config.headers ?? {})
        .filter(([, value]) => typeof value === 'string' || typeof value === 'number')
        .map(([key, value]) => [key.toLowerCase(), String(value)]),
    );

    return { data: null, status: 200, statusText: 'OK', headers: {}, config };
  };

  try {
    await send();
  } finally {
    apiClient.defaults.adapter = original;
  }

  return captured;
}

test('a file upload is left for the browser to describe', async () => {
  const body = new FormData();
  body.append('file', new Blob([new Uint8Array([0xff, 0xd8, 0xff])], { type: 'image/jpeg' }), 'photo.jpg');

  const headers = await headersFor(() => apiClient.put('/products/1/image', body));

  // The invariant, and the whole of the bug: the client must not call a file upload JSON.
  //
  // Deliberately not asserting that it *is* multipart. This suite runs in Node, where axios
  // serialises a FormData through its own transform and labels it `x-www-form-urlencoded`; in a
  // browser the header is absent at this point and XHR writes `multipart/form-data; boundary=…`
  // itself. Asserting the browser's outcome here would pin Node's behaviour and pass for the wrong
  // reason — the browser end is verified against the deployed site instead.
  expect(
    headers['content-type'],
    "a file upload must not be labelled application/json — that is what made every image return 415",
  ).not.toBe('application/json');
});

test('an ordinary body is still sent as JSON', async () => {
  const headers = await headersFor(() => apiClient.post('/customers', { firstName: 'Ayesha' }));

  expect(headers['content-type']).toContain('application/json');
});

test('a GET carries no body type at all', async () => {
  const headers = await headersFor(() => apiClient.get('/customers'));

  expect(headers['content-type']).toBeUndefined();
});
