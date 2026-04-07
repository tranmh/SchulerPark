import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { PublicClientApplication } from '@azure/msal-browser';
import { MsalProvider } from '@azure/msal-react';
import { setAccessToken } from '../services/api';
import { authService } from '../services/authService';
import { createMsalConfig, loginRequest } from '../config/msalConfig';
import type { AuthConfig, User } from '../types/auth';

interface AuthContextType {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  isAdmin: boolean;
  authConfig: AuthConfig | null;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, displayName: string, password: string) => Promise<void>;
  loginWithAzureAd: () => Promise<void>;
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
  const [authConfig, setAuthConfig] = useState<AuthConfig | null>(null);
  const [msalInstance, setMsalInstance] = useState<PublicClientApplication | null>(null);

  // Initialize: fetch config + try silent refresh
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

  const register = useCallback(async (email: string, displayName: string, password: string) => {
    const response = await authService.register({ email, displayName, password });
    setAccessToken(response.accessToken);
    setUser(response.user);
  }, []);

  const loginWithAzureAd = useCallback(async () => {
    if (!msalInstance) throw new Error('Azure AD is not configured');

    const result = await msalInstance.acquireTokenPopup(loginRequest);
    if (!result.idToken) throw new Error('No ID token received');

    const response = await authService.loginWithAzureAd(result.idToken);
    setAccessToken(response.accessToken);
    setUser(response.user);
  }, [msalInstance]);

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
      isAdmin: user?.role === 'Admin',
      authConfig,
      login,
      register,
      loginWithAzureAd,
      logout,
    }),
    [user, isLoading, authConfig, login, register, loginWithAzureAd, logout]
  );

  const content = <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;

  if (msalInstance) {
    return <MsalProvider instance={msalInstance}>{content}</MsalProvider>;
  }

  return content;
}
