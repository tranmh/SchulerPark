import api from './api';
import type { AuthConfig, AuthResponse, LoginRequest, RegisterRequest, User } from '../types/auth';

export const authService = {
  login: (data: LoginRequest) =>
    api.post<AuthResponse>('/auth/login', data).then(r => r.data),

  register: (data: RegisterRequest) =>
    api.post<AuthResponse>('/auth/register', data).then(r => r.data),

  loginWithAzureAd: (idToken: string) =>
    api.post<AuthResponse>('/auth/azure-callback', { idToken }).then(r => r.data),

  refresh: () =>
    api.post<AuthResponse>('/auth/refresh').then(r => r.data),

  getMe: () =>
    api.get<User>('/auth/me').then(r => r.data),

  logout: () =>
    api.post('/auth/logout'),

  getAuthConfig: () =>
    api.get<AuthConfig>('/auth/config').then(r => r.data),
};
