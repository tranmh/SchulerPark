import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { installFakeApi, type FakeApi } from './fakeApi';
import {
  apiError,
  buildAdminUser,
  buildAdminUserList,
  buildBooking,
  buildPendingUserList,
  resetFixtureIds,
} from './fixtures';
import { adminUsersService } from '../services/adminUsersService';
import { getApiErrorCode } from '../utils/apiError';
import { setAccessToken } from '../services/api';

describe('shared test doubles', () => {
  let http: FakeApi;

  beforeEach(() => {
    resetFixtureIds();
    setAccessToken(null);
    http = installFakeApi();
  });

  afterEach(() => http.restore());

  it('builders produce deterministic ids and honour overrides', () => {
    const a = buildAdminUser();
    const b = buildAdminUser({ role: 'Admin', company: null });
    expect(a.id).toBe('user-1');
    expect(b).toMatchObject({ id: 'user-2', role: 'Admin', company: null, firstName: 'Max' });
    expect(buildBooking({ status: 'Won' }).status).toBe('Won');
  });

  it('serves routed responses and records the request', async () => {
    const list = buildAdminUserList([buildAdminUser()]);
    http.on('get', '/admin/users', { data: list });

    await expect(adminUsersService.list({ search: 'max', page: 2 })).resolves.toEqual(list);
    expect(http.requests).toEqual([
      expect.objectContaining({ method: 'get', url: '/admin/users', params: { search: 'max', page: 2 } }),
    ]);
  });

  it('parses JSON bodies and matches RegExp routes', async () => {
    http.on('post', /^\/admin\/users\/[^/]+\/approval$/, (req) => ({ data: { echoed: req.body } }));

    await expect(adminUsersService.decide('u-9', true)).resolves.toEqual({ echoed: { approve: true } });
    expect(http.requests[0].url).toBe('/admin/users/u-9/approval');
  });

  it('rejects non-2xx replies with an axios-shaped error the app helpers can read', async () => {
    http.on('get', '/admin/users/pending', { status: 403, data: { code: 'FORBIDDEN' } });

    const err = await adminUsersService.pending().catch((e: unknown) => e);
    expect(getApiErrorCode(err)).toBe('FORBIDDEN');
    expect(getApiErrorCode(apiError(409, { code: 'DUPLICATE' }))).toBe('DUPLICATE');
  });

  it('answers unrouted requests with 404', async () => {
    http.on('get', '/admin/users/pending', { data: buildPendingUserList() });
    await expect(adminUsersService.remove('x')).rejects.toMatchObject({ response: { status: 404 } });
  });
});
