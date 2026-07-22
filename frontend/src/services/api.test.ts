import { describe, it, expect, beforeEach } from 'vitest';
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios';
import api, { setAccessToken } from './api';
import { authService } from './authService';

// Bug #23: the 401 auto-refresh interceptor must delegate to authService's single-flight
// refresh() instead of calling `api.post('/auth/refresh')` directly. Otherwise a direct
// refresh (e.g. a proactive call) racing an interceptor-triggered refresh rotates the token
// TWICE — and the server's refresh-token reuse detection then revokes everything, logging the
// user out. This test races both paths and asserts exactly ONE network refresh.

function ok(config: InternalAxiosRequestConfig, data: unknown): AxiosResponse {
  return { data, status: 200, statusText: 'OK', headers: {}, config } as AxiosResponse;
}

describe('api refresh interceptor (Bug #23)', () => {
  beforeEach(() => setAccessToken('old-token'));

  it('shares one rotation when a direct refresh races an interceptor refresh', async () => {
    let refreshCount = 0;
    let releaseRefresh!: () => void;
    const refreshGate = new Promise<void>((r) => { releaseRefresh = r; });

    api.defaults.adapter = async (config) => {
      const url = config.url ?? '';
      if (url === '/auth/refresh') {
        refreshCount++;
        await refreshGate;                 // stay in-flight until both callers have attached
        return ok(config, { accessToken: 'new-token' });
      }
      const retried = (config as { _retry?: boolean })._retry === true;
      if (url === '/protected' && !retried) {
        return Promise.reject({
          config,
          response: { status: 401, data: {}, statusText: 'Unauthorized', headers: {}, config },
          isAxiosError: true,
        });
      }
      return ok(config, { ok: true });
    };

    // Path A: a direct single-flight refresh. Path B: a request that 401s → interceptor refresh.
    const direct = authService.refresh();
    const viaInterceptor = api.get('/protected');

    // Let microtasks run so the interceptor reaches authService.refresh() while the first
    // refresh is still gated (in-flight), then release.
    await new Promise((r) => setTimeout(r, 20));
    releaseRefresh();

    const [, protectedRes] = await Promise.all([direct, viaInterceptor]);

    expect(protectedRes.status).toBe(200);
    // GREEN (delegates to single-flight): both paths share ONE refresh network call.
    // RED (interceptor calls api.post directly): two rotations.
    expect(refreshCount).toBe(1);
  });
});
