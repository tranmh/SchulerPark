import { describe, it, expect, vi } from 'vitest';
import { axiosOk, buildAdminLocation, createApiMock } from './fixtures';

vi.mock('../../services/api', async () => {
  const { createApiMock: make } = await import('./fixtures');
  return { default: make() };
});

import api from '../../services/api';
import { adminService } from '../../services/adminService';

describe('shared fixtures', () => {
  it('serve adminService through the api mock', async () => {
    const location = buildAdminLocation({ name: 'Erfurt' });
    vi.mocked(api.get).mockResolvedValue(axiosOk([location]));

    await expect(adminService.getLocations()).resolves.toEqual([location]);
    expect(api.get).toHaveBeenCalledWith('/admin/locations');
    expect(typeof createApiMock().post).toBe('function');
  });
});
