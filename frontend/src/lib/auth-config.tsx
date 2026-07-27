'use client';

import { AuthProvider as OidcAuthProvider } from 'react-oidc-context';

const authority = process.env.NEXT_PUBLIC_AUTHORITY ?? 'http://localhost:5000';
const clientId = process.env.NEXT_PUBLIC_CLIENT_ID ?? 'retail25-web';
const redirectUri = typeof window !== 'undefined' ? `${window.location.origin}/callback` : '/callback';

const oidcConfig = {
  authority,
  client_id: clientId,
  redirect_uri: redirectUri,
  scope: 'openid profile email api offline_access',
  post_logout_redirect_uri: typeof window !== 'undefined' ? window.location.origin : '/',
  automaticSilentRenew: true,
  includeIdTokenInSilentRenew: true,
};

export function AuthProvider({ children }: { children: React.ReactNode }) {
  return <OidcAuthProvider {...oidcConfig}>{children}</OidcAuthProvider>;
}
