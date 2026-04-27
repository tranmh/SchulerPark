import api from './api';

export type UserRole = 'User' | 'Admin' | 'SuperAdmin';

export interface AdminUser {
  id: string;
  email: string;
  displayName: string;
  role: UserRole;
  isDisabled: boolean;
  hasAzureAd: boolean;
  createdAt: string;
}

export interface AdminUserListResponse {
  users: AdminUser[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export const adminUsersService = {
  list: (params: { search?: string; role?: UserRole | ''; page?: number; pageSize?: number }) =>
    api
      .get<AdminUserListResponse>('/admin/users', { params })
      .then((r) => r.data),

  updateRole: (id: string, role: UserRole) =>
    api.put<AdminUser>(`/admin/users/${id}/role`, { role }).then((r) => r.data),

  disable: (id: string) =>
    api.put<AdminUser>(`/admin/users/${id}/disable`).then((r) => r.data),

  enable: (id: string) =>
    api.put<AdminUser>(`/admin/users/${id}/enable`).then((r) => r.data),

  remove: (id: string) => api.delete(`/admin/users/${id}`),
};
