import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

import api from './api';
import { adminUsersService } from './adminUsersService';

const ok = <T,>(data: T) => Promise.resolve({ data });

describe('adminUsersService', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('pending() GETs /admin/users/pending and unwraps the data', async () => {
    vi.mocked(api.get).mockReturnValueOnce(ok({ users: [], totalCount: 0 }));

    await expect(adminUsersService.pending()).resolves.toEqual({ users: [], totalCount: 0 });
    expect(api.get).toHaveBeenCalledWith('/admin/users/pending');
  });

  it('decide() POSTs the approve flag to /admin/users/{id}/approval', async () => {
    vi.mocked(api.post).mockReturnValueOnce(ok({ id: 'u1' }));

    await expect(adminUsersService.decide('u1', false)).resolves.toEqual({ id: 'u1' });
    expect(api.post).toHaveBeenCalledWith('/admin/users/u1/approval', { approve: false });
  });

  it('list() GETs /admin/users with the filters as query params', async () => {
    const res = { users: [], totalCount: 0, page: 2, pageSize: 20 };
    vi.mocked(api.get).mockReturnValueOnce(ok(res));

    await expect(adminUsersService.list({ search: 'anna', role: 'Admin', page: 2, pageSize: 20 })).resolves.toBe(res);
    expect(api.get).toHaveBeenCalledWith('/admin/users', {
      params: { search: 'anna', role: 'Admin', page: 2, pageSize: 20 },
    });
  });

  it('updateRole() PUTs the role', async () => {
    vi.mocked(api.put).mockReturnValueOnce(ok({ id: 'u1', role: 'Admin' }));

    await adminUsersService.updateRole('u1', 'Admin');
    expect(api.put).toHaveBeenCalledWith('/admin/users/u1/role', { role: 'Admin' });
  });

  it('disable() and enable() PUT to their endpoints without a body', async () => {
    vi.mocked(api.put).mockReturnValue(ok({}));

    await adminUsersService.disable('u1');
    await adminUsersService.enable('u2');
    expect(api.put).toHaveBeenNthCalledWith(1, '/admin/users/u1/disable');
    expect(api.put).toHaveBeenNthCalledWith(2, '/admin/users/u2/enable');
  });

  // B27
  it('updateCompany() PUTs /admin/users/{id}/company with the company or null', async () => {
    vi.mocked(api.put).mockReturnValue(ok({}));

    await adminUsersService.updateCompany('u1', 'Extern AG');
    await adminUsersService.updateCompany('u1', null);
    expect(api.put).toHaveBeenNthCalledWith(1, '/admin/users/u1/company', { company: 'Extern AG' });
    expect(api.put).toHaveBeenNthCalledWith(2, '/admin/users/u1/company', { company: null });
  });

  it('remove() DELETEs the user', async () => {
    vi.mocked(api.delete).mockReturnValueOnce(ok(undefined));

    await adminUsersService.remove('u9');
    expect(api.delete).toHaveBeenCalledWith('/admin/users/u9');
  });

  it('propagates request errors', async () => {
    const err = { response: { status: 403, data: { error: 'Forbidden' } } };
    vi.mocked(api.put).mockReturnValueOnce(Promise.reject(err));
    vi.mocked(api.get).mockReturnValueOnce(Promise.reject(err));

    await expect(adminUsersService.updateCompany('u1', 'X')).rejects.toBe(err);
    await expect(adminUsersService.pending()).rejects.toBe(err);
  });
});
