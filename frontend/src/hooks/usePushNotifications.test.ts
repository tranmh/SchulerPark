import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { usePushNotifications } from './usePushNotifications';
import { subscribeToPush } from '../services/pushService';

// Bug #11: enabling push must report a real failure distinctly from a user denial,
// instead of silently reporting "off".
vi.mock('../services/pushService', () => ({
  subscribeToPush: vi.fn(),
  unsubscribeFromPush: vi.fn(),
}));

const mockSubscribe = vi.mocked(subscribeToPush);

function stubBrowser(requestPermission: () => Promise<NotificationPermission>) {
  vi.stubGlobal('Notification', { permission: 'default', requestPermission });
  vi.stubGlobal('PushManager', function PushManager() {});
  Object.defineProperty(navigator, 'serviceWorker', {
    configurable: true,
    value: { ready: Promise.resolve({ pushManager: { getSubscription: () => Promise.resolve(null) } }) },
  });
}

describe('usePushNotifications.requestPermission', () => {
  beforeEach(() => mockSubscribe.mockReset());
  afterEach(() => vi.unstubAllGlobals());

  it('returns { ok: false, reason: "error" } when subscribe fails (e.g. backend 500)', async () => {
    stubBrowser(() => Promise.resolve('granted'));
    mockSubscribe.mockRejectedValueOnce(new Error('500'));

    const { result } = renderHook(() => usePushNotifications());

    let outcome: unknown;
    await act(async () => { outcome = await result.current.requestPermission(); });

    expect(outcome).toEqual({ ok: false, reason: 'error' });
  });

  it('returns { ok: false, reason: "denied" } when the user declines', async () => {
    stubBrowser(() => Promise.resolve('denied'));

    const { result } = renderHook(() => usePushNotifications());

    let outcome: unknown;
    await act(async () => { outcome = await result.current.requestPermission(); });

    expect(outcome).toEqual({ ok: false, reason: 'denied' });
    expect(mockSubscribe).not.toHaveBeenCalled();
  });

  it('returns { ok: true } when permission is granted and subscribe succeeds', async () => {
    stubBrowser(() => Promise.resolve('granted'));
    mockSubscribe.mockResolvedValueOnce(undefined);

    const { result } = renderHook(() => usePushNotifications());

    let outcome: unknown;
    await act(async () => { outcome = await result.current.requestPermission(); });

    expect(outcome).toEqual({ ok: true });
  });
});
