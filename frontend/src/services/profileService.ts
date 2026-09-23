import api, { setAccessToken } from './api';
import type { AuthResponse, User } from '../types/auth';
import type { UpdateProfileRequest, DataExport } from '../types/profile';

export const profileService = {
  getProfile: () =>
    api.get<User>('/profile').then(r => r.data),

  updateProfile: (data: UpdateProfileRequest) =>
    api.put<User>('/profile', data).then(r => r.data),

  /** Stores the notification language ('de' | 'en'); returns the updated user. */
  updateLanguage: (language: string) =>
    api.put<User>('/profile/language', { language }).then(r => r.data),

  /**
   * Phase 20 WP2: changes the local password. The server revokes every other
   * session and returns a fresh token pair; the new access token is installed
   * here so the current session simply continues.
   */
  changePassword: (currentPassword: string, newPassword: string) =>
    api.post<AuthResponse>('/profile/change-password', { currentPassword, newPassword }).then(r => {
      setAccessToken(r.data.accessToken);
      return r.data;
    }),

  exportData: () =>
    api.get<DataExport>('/profile/data-export').then(r => r.data),

  requestDeletion: () =>
    api.delete('/profile/data').then(r => r.data),
};
