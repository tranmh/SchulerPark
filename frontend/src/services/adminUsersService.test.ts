import { describe, it, expect, vi, beforeEach } from 'vitest';
import api from './api';
import { adminUsersService } from './adminUsersService';
import { USER_ID, apiError, type ApiFake } from '../__tests__/support/adminFixtures';

vi.mock('./api', async () => {
  const { createApiFake } = await import('../__tests__/support/adminFixtures');
  return { default: createApiFake() };
});

const fake = api as unknown as ApiFake;

describe('adminUsersService', () => {
  beforeEach(() => vi.clearAllMocks());

  it('pending fetches the approval queue', async () => {
    const data = { users: [], totalCount: 0 };
    fake.get.mockResolvedValueOnce({ data });
    await expect(adminUsersService.pending()).resolves.toEqual(data);
    expect(fake.get).toHaveBeenCalledWith('/admin/users/pending');
  });

  it.each([true, false])('decide posts {approve: %s} to the approval endpoint (B9)', async (approve) => {
    await adminUsersService.decide(USER_ID, approve);
    expect(fake.post).toHaveBeenCalledWith(`/admin/users/${USER_ID}/approval`, { approve });
  });

  it('decide surfaces a rejected double approval', async () => {
    const err = apiError(409, { detail: 'User is not pending approval.' });
    fake.post.mockRejectedValueOnce(err);
    await expect(adminUsersService.decide(USER_ID, true)).rejects.toBe(err);
  });

  it('list passes the filters as query params', async () => {
    const params = { search: 'max', role: 'Admin' as const, page: 2, pageSize: 20 };
    await adminUsersService.list(params);
    expect(fake.get).toHaveBeenCalledWith('/admin/users', { params });
  });

  it('updateRole, disable, enable and remove target the user endpoints', async () => {
    await adminUsersService.updateRole(USER_ID, 'SuperAdmin');
    await adminUsersService.disable(USER_ID);
    await adminUsersService.enable(USER_ID);
    await adminUsersService.remove(USER_ID);

    expect(fake.put).toHaveBeenNthCalledWith(1, `/admin/users/${USER_ID}/role`, { role: 'SuperAdmin' });
    expect(fake.put).toHaveBeenNthCalledWith(2, `/admin/users/${USER_ID}/disable`);
    expect(fake.put).toHaveBeenNthCalledWith(3, `/admin/users/${USER_ID}/enable`);
    expect(fake.delete).toHaveBeenCalledWith(`/admin/users/${USER_ID}`);
  });
});
