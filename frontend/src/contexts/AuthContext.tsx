import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import {
  BrowserAuthError,
  BrowserAuthErrorCodes,
  PublicClientApplication,
} from '@azure/msal-browser';
import { MsalProvider } from '@azure/msal-react';
import { useTranslation } from 'react-i18next';
import { setAccessToken } from '../services/api';
import { authService } from '../services/authService';
import { profileService } from '../services/profileService';
import { createMsalConfig, loginRequest, shouldUseRedirectFlow } from '../config/msalConfig';
import type { AuthConfig, User } from '../types/auth';

interface AuthContextType {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  // True for Admin or SuperAdmin — kept inclusive so existing admin gates still work.
  isAdmin: boolean;
  isSuperAdmin: boolean;
  authConfig: AuthConfig | null;
  login: (email: string, password: string) => Promise<void>;
  /** Registers an account. Does NOT sign in — the email must be verified first. */
  register: (email: string, displayName: string, password: string) => Promise<void>;
  /**
   * Signs in via Microsoft. On desktop this opens a popup and resolves once
   * the backend has issued a session. On mobile / installed PWAs (or when the
   * popup is blocked) it navigates away to Microsoft; the promise then never
   * settles because the page unloads, and the result is picked up on return
   * during provider initialization.
   */
  loginWithAzureAd: () => Promise<void>;
  /** Error from an Azure AD redirect login that failed on return, if any. */
  azureLoginError: unknown;
  clearAzureLoginError: () => void;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within AuthProvider');
  return context;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const { i18n } = useTranslation();
  const uiLanguage = i18n.language.startsWith('en') ? 'en' : 'de';
  const [authConfig, setAuthConfig] = useState<AuthConfig | null>(null);
  const [msalInstance, setMsalInstance] = useState<PublicClientApplication | null>(null);
  const [azureLoginError, setAzureLoginError] = useState<unknown>(null);

  // Initialize: fetch config, finish a pending Azure AD redirect, else try silent refresh
  useEffect(() => {
    const init = async () => {
      try {
        const config = await authService.getAuthConfig();
        setAuthConfig(config);

        if (config.azureAdEnabled && config.azureAdClientId && config.azureAdTenantId) {
          const msalConfig = createMsalConfig(config.azureAdClientId, config.azureAdTenantId);
          const instance = new PublicClientApplication(msalConfig);
          await instance.initialize();
          setMsalInstance(instance);

          // Returning from a redirect login: the ID token is in the URL hash.
          // A failure here must not fall through to the generic catch below —
          // the login page needs to show *why* Microsoft sign-in failed.
          let redirectResult;
          try {
            redirectResult = await instance.handleRedirectPromise();
          } catch (err) {
            console.error('Azure AD redirect login failed', err);
            setAzureLoginError(err);
            redirectResult = null;
          }

          if (redirectResult?.idToken) {
            try {
              const response = await authService.loginWithAzureAd(redirectResult.idToken);
              setAccessToken(response.accessToken);
              setUser(response.user);
              return;
            } catch (err) {
              console.error('Azure AD token exchange failed', err);
              setAzureLoginError(err);
              setAccessToken(null);
              setUser(null);
              return;
            }
          }
        }

        // Try silent refresh
        const response = await authService.refresh();
        setAccessToken(response.accessToken);
        setUser(response.user);
      } catch {
        // Not authenticated — that's fine
        setAccessToken(null);
        setUser(null);
      } finally {
        setIsLoading(false);
      }
    };

    init();
  }, []);

  // Listen for forced logout from axios interceptor
  useEffect(() => {
    const handleLogout = () => {
      setAccessToken(null);
      setUser(null);
    };
    window.addEventListener('auth:logout', handleLogout);
    return () => window.removeEventListener('auth:logout', handleLogout);
  }, []);

  const login = useCallback(async (email: string, password: string) => {
    const response = await authService.login({ email, password });
    setAccessToken(response.accessToken);
    setUser(response.user);
  }, []);

  const register = useCallback(
    async (email: string, displayName: string, password: string) => {
      // The verification mail (and later notifications) go out in the language
      // the user is registering in.
      await authService.register({ email, displayName, password, preferredLanguage: uiLanguage });
    },
    [uiLanguage]
  );

  // Notification language follows the UI language: whenever the signed-in user's
  // current UI language differs from the stored one (language toggle, or a login
  // on a device set to the other language), persist it. Emails and push
  // notifications are rendered from the stored value on the server.
  useEffect(() => {
    if (!user || user.preferredLanguage === uiLanguage) return;
    let cancelled = false;
    profileService
      .updateLanguage(uiLanguage)
      .then((updated) => {
        if (!cancelled) setUser(updated);
      })
      .catch((err) => console.warn('Could not save the notification language', err));
    return () => {
      cancelled = true;
    };
  }, [user, uiLanguage]);

  const loginWithAzureAd = useCallback(async () => {
    if (!msalInstance) throw new Error('Azure AD is not configured');
    setAzureLoginError(null);

    if (shouldUseRedirectFlow()) {
      await msalInstance.loginRedirect(loginRequest);
      return;
    }

    let idToken: string;
    try {
      const result = await msalInstance.acquireTokenPopup(loginRequest);
      if (!result.idToken) throw new Error('No ID token received');
      idToken = result.idToken;
    } catch (err) {
      // Popup blocked or closed by the browser before it could load: fall
      // back to the redirect flow rather than failing the login.
      if (
        err instanceof BrowserAuthError &&
        (err.errorCode === BrowserAuthErrorCodes.popupWindowError ||
          err.errorCode === BrowserAuthErrorCodes.emptyWindowError)
      ) {
        await msalInstance.loginRedirect(loginRequest);
        return;
      }
      throw err;
    }

    const response = await authService.loginWithAzureAd(idToken);
    setAccessToken(response.accessToken);
    setUser(response.user);
  }, [msalInstance]);

  const clearAzureLoginError = useCallback(() => setAzureLoginError(null), []);

  const logout = useCallback(async () => {
    try {
      await authService.logout();
    } finally {
      setAccessToken(null);
      setUser(null);
    }
  }, []);

  const value = useMemo<AuthContextType>(
    () => ({
      user,
      isAuthenticated: !!user,
      isLoading,
      isAdmin: user?.role === 'Admin' || user?.role === 'SuperAdmin',
      isSuperAdmin: user?.role === 'SuperAdmin',
      authConfig,
      login,
      register,
      loginWithAzureAd,
      azureLoginError,
      clearAzureLoginError,
      logout,
    }),
    [
      user,
      isLoading,
      authConfig,
      login,
      register,
      loginWithAzureAd,
      azureLoginError,
      clearAzureLoginError,
      logout,
    ]
  );

  const content = <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;

  if (msalInstance) {
    return <MsalProvider instance={msalInstance}>{content}</MsalProvider>;
  }

  return content;
}
