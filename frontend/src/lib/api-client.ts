import axios from 'axios';

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';

export const apiClient = axios.create({
  baseURL: `${API_BASE}/api/v1`,
  headers: { 'Content-Type': 'application/json' },
  timeout: 30_000,
});

apiClient.interceptors.request.use((config) => {
  if (typeof window !== 'undefined') {
    const oidcStorage = localStorage.getItem('oidc.user');
    if (oidcStorage) {
      try {
        const user = JSON.parse(oidcStorage);
        if (user?.access_token) {
          config.headers.Authorization = `Bearer ${user.access_token}`;
        }
      } catch {
        // ignore parse errors
      }
    }
  }
  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      if (typeof window !== 'undefined') {
        localStorage.removeItem('oidc.user');
        window.location.href = '/';
      }
    }
    return Promise.reject(error);
  }
);

export default apiClient;
