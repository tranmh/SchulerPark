import { type Configuration, LogLevel } from '@azure/msal-browser';

export const createMsalConfig = (clientId: string, tenantId: string): Configuration => ({
  auth: {
    clientId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
    // After a redirect login, process the response on the redirect URI (`/`)
    // instead of bouncing back to `/login` first. ProtectedRoute shows the
    // spinner while AuthContext exchanges the token, then renders home.
    navigateToLoginRequestUrl: false,
  },
  cache: {
    cacheLocation: 'sessionStorage',
    storeAuthStateInCookie: false,
  },
  system: {
    loggerOptions: {
      logLevel: LogLevel.Warning,
    },
  },
});

export const loginRequest = {
  scopes: ['openid', 'profile', 'email'],
};

/**
 * Mobile browsers (iOS Safari in particular) and installed PWAs block or
 * orphan popup windows, so `acquireTokenPopup` fails there with
 * `popup_window_error`. Use the full-page redirect flow instead.
 */
export function shouldUseRedirectFlow(): boolean {
  if (typeof window === 'undefined') return false;

  const nav = window.navigator as Navigator & { standalone?: boolean };
  const installed =
    nav.standalone === true ||
    (typeof window.matchMedia === 'function' &&
      window.matchMedia('(display-mode: standalone)').matches);
  if (installed) return true;

  return /iPhone|iPad|iPod|Android|Mobile/i.test(nav.userAgent);
}
