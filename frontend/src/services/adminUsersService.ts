import api from './api';

export type UserRole = 'User' | 'Admin' | 'SuperAdmin';

export type ApprovalStatus = 'Approved' | 'Pending' | 'Rejected';

export interface AdminUser {
  id: string;
  email: string;
  displayName: string;
  firstName: string | null;
  lastName: string | null;
  /** Phase 21: null for SSO and legacy accounts; editable by SuperAdmin only. */
  company: string | null;
  role: UserRole;
  approvalStatus: ApprovalStatus;
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
  firstName: string | null;
  lastName: string | null;
  company: string | null;
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

  /** Phase 21: set or clear (null / blank) a user's company — SuperAdmin only. */
  updateCompany: (id: string, company: string | null) =>
    api.put<AdminUser>(`/admin/users/${id}/company`, { company }).then((r) => r.data),

  remove: (id: string) => api.delete(`/admin/users/${id}`),
};
