import axios from 'axios';

/**
 * Every API call goes through the same-origin BFF proxy (doc 07 §Topology).
 *
 * There is no token handling here, and that is the point: the browser has no credential to attach.
 * The session cookie is httpOnly and travels automatically; the proxy reads it server-side and adds
 * the bearer. The previous version of this file read an access token out of localStorage — which is
 * exactly what the brief forbids, because any script on the page could read it too.
 */
export const apiClient = axios.create({
  baseURL: '/api/proxy',
  // Declared for JSON bodies only. Setting it here for every request is what made every image
  // upload in the application answer 415: a multipart body carries a boundary parameter the browser
  // generates, `Content-Type: application/json` replaced it wholesale, and ASP.NET Core then had a
  // request it could not bind to IFormFile. The upload components were already careful not to set
  // the header themselves — this default overrode that care.
  headers: { post: { 'Content-Type': 'application/json' }, put: { 'Content-Type': 'application/json' }, patch: { 'Content-Type': 'application/json' } },
  timeout: 30_000,
  // Same-origin, so the httpOnly session cookie is sent without the browser being able to read it.
  withCredentials: true,
});

/**
 * Hands multipart bodies back to the browser to describe.
 *
 * Axios only omits the header when none is configured, and the per-method defaults above are still
 * configuration. Deleting it here — for FormData specifically — is what lets the browser write
 * `multipart/form-data; boundary=…`, which is the only form of that header a server can parse.
 */
apiClient.interceptors.request.use((config) => {
  if (typeof FormData !== 'undefined' && config.data instanceof FormData) {
    delete config.headers['Content-Type'];
    delete config.headers['content-type'];
  }

  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    // The proxy already tried to refresh once. A 401 here means the session is genuinely over, so
    // send the user to sign in and bring them back to where they were.
    if (error.response?.status === 401 && typeof window !== 'undefined') {
      const returnTo = encodeURIComponent(window.location.pathname + window.location.search);
      window.location.href = `/api/auth/login?returnTo=${returnTo}`;
    }

    return Promise.reject(error);
  },
);

export default apiClient;
