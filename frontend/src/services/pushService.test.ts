import { describe, it, expect, afterEach } from 'vitest';
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios';
import api from './api';
import { sendTestPush } from './pushService';

function ok(config: InternalAxiosRequestConfig, data: unknown): AxiosResponse {
  return { data, status: 200, statusText: 'OK', headers: {}, config } as AxiosResponse;
}

function reject(config: InternalAxiosRequestConfig, status: number) {
  return Promise.reject({
    config,
    response: { status, data: {}, statusText: '', headers: {}, config },
    isAxiosError: true,
  });
}

describe('sendTestPush', () => {
  const originalAdapter = api.defaults.adapter;
  afterEach(() => { api.defaults.adapter = originalAdapter; });

  it('posts to /push/test and reports the delivered count', async () => {
    let calledUrl = '';
    api.defaults.adapter = async (config) => {
      calledUrl = `${config.method?.toUpperCase()} ${config.url}`;
      return ok(config, { subscriptions: 2, delivered: 2, removed: 0, failed: 0 });
    };

    const result = await sendTestPush();

    expect(calledUrl).toBe('POST /push/test');
    expect(result).toEqual({ ok: true, delivered: 2, subscriptions: 2 });
  });

  it('maps 404 to no-subscription', async () => {
    api.defaults.adapter = (config) => reject(config, 404);
    expect(await sendTestPush()).toEqual({ ok: false, reason: 'no-subscription' });
  });

  it('maps 502 to not-delivered', async () => {
    api.defaults.adapter = (config) => reject(config, 502);
    expect(await sendTestPush()).toEqual({ ok: false, reason: 'not-delivered' });
  });

  it('maps anything else (e.g. network failure) to error', async () => {
    api.defaults.adapter = () => Promise.reject(new Error('Network Error'));
    expect(await sendTestPush()).toEqual({ ok: false, reason: 'error' });
  });
});
