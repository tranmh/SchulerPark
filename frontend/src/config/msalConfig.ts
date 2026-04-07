import { type Configuration, LogLevel } from '@azure/msal-browser';

export const createMsalConfig = (clientId: string, tenantId: string): Configuration => ({
  auth: {
    clientId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
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
