import api from './api';
import type { User } from '../types/auth';
import type { UpdateProfileRequest, DataExport } from '../types/profile';

export const profileService = {
  getProfile: () =>
    api.get<User>('/profile').then(r => r.data),

  updateProfile: (data: UpdateProfileRequest) =>
    api.put<User>('/profile', data).then(r => r.data),

  exportData: () =>
    api.get<DataExport>('/profile/data-export').then(r => r.data),

  requestDeletion: () =>
    api.delete('/profile/data').then(r => r.data),
};
