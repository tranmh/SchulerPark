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

export interface PendingUser {
  id: string;
  email: string;
  displayName: string;
  emailVerified: boolean;
  createdAt: string;
}

export interface PendingUserListResponse {
  users: PendingUser[];
  totalCount: number;
}

export const adminUsersService = {
  pending: () =>
    api.get<PendingUserListResponse>('/admin/users/pending').then((r) => r.data),

  decide: (id: string, approve: boolean) =>
    api.post<PendingUser>(`/admin/users/${id}/approval`, { approve }).then((r) => r.data),

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
