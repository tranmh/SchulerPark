import api from './api';
import type { AuthConfig, AuthResponse, LoginRequest, RegisterRequest, User } from '../types/auth';

// Concurrent refresh calls all share the same in-flight request. The server
// rotates refresh tokens and treats reuse of a revoked token as a breach by
// revoking *all* tokens for the user — so two parallel refreshes (e.g. React
// StrictMode double-effect, multiple tabs) would log the user out.
let inFlightRefresh: Promise<AuthResponse> | null = null;

export const authService = {
  login: (data: LoginRequest) =>
    api.post<AuthResponse>('/auth/login', data).then(r => r.data),

  // Registration no longer returns tokens: the account must verify its email
  // address before it can sign in.
  register: (data: RegisterRequest) =>
    api.post<{ message: string }>('/auth/register', data).then(r => r.data),

  verifyEmail: (token: string) =>
    api.post<{ message: string }>('/auth/verify-email', { token }).then(r => r.data),

  resendVerification: (email: string) =>
    api.post<{ message: string }>('/auth/resend-verification', { email }).then(r => r.data),

  loginWithAzureAd: (idToken: string) =>
    api.post<AuthResponse>('/auth/azure-callback', { idToken }).then(r => r.data),

  refresh: () => {
    if (!inFlightRefresh) {
      inFlightRefresh = api.post<AuthResponse>('/auth/refresh')
        .then(r => r.data)
        .finally(() => { inFlightRefresh = null; });
    }
    return inFlightRefresh;
  },

  getMe: () =>
    api.get<User>('/auth/me').then(r => r.data),

  logout: () =>
    api.post('/auth/logout'),

  getAuthConfig: () =>
    api.get<AuthConfig>('/auth/config').then(r => r.data),
};
