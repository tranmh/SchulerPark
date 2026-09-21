import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { usePushNotifications } from './usePushNotifications';
import { subscribeToPush, sendTestPush } from '../services/pushService';

// Bug #11: enabling push must report a real failure distinctly from a user denial,
// instead of silently reporting "off".
vi.mock('../services/pushService', () => ({
  subscribeToPush: vi.fn(),
  unsubscribeFromPush: vi.fn(),
  sendTestPush: vi.fn(),
}));

const mockSubscribe = vi.mocked(subscribeToPush);
const mockSendTest = vi.mocked(sendTestPush);

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

describe('usePushNotifications.sendTest', () => {
  beforeEach(() => mockSendTest.mockReset());
  afterEach(() => vi.unstubAllGlobals());

  // Simulate a device whose browser-side subscription exists.
  function stubSubscribedBrowser() {
    vi.stubGlobal('Notification', { permission: 'granted', requestPermission: () => Promise.resolve('granted') });
    vi.stubGlobal('PushManager', function PushManager() {});
    Object.defineProperty(navigator, 'serviceWorker', {
      configurable: true,
      value: { ready: Promise.resolve({ pushManager: { getSubscription: () => Promise.resolve({ endpoint: 'https://push.example/x' }) } }) },
    });
  }

  it('passes a successful result through and keeps the subscribed state', async () => {
    stubSubscribedBrowser();
    mockSendTest.mockResolvedValueOnce({ ok: true, delivered: 1, subscriptions: 1 });

    const { result } = renderHook(() => usePushNotifications());
    await act(async () => {});
    expect(result.current.isSubscribed).toBe(true);

    let outcome: unknown;
    await act(async () => { outcome = await result.current.sendTest(); });

    expect(outcome).toEqual({ ok: true, delivered: 1, subscriptions: 1 });
    expect(result.current.isSubscribed).toBe(true);
  });

  it('drops the stale subscribed state when the server knows no subscription (404)', async () => {
    stubSubscribedBrowser();
    mockSendTest.mockResolvedValueOnce({ ok: false, reason: 'no-subscription' });

    const { result } = renderHook(() => usePushNotifications());
    await act(async () => {});
    expect(result.current.isSubscribed).toBe(true);

    await act(async () => { await result.current.sendTest(); });

    expect(result.current.isSubscribed).toBe(false);
  });

  it('keeps the subscribed state on a delivery failure (502) so the user can retry', async () => {
    stubSubscribedBrowser();
    mockSendTest.mockResolvedValueOnce({ ok: false, reason: 'not-delivered' });

    const { result } = renderHook(() => usePushNotifications());
    await act(async () => {});

    let outcome: unknown;
    await act(async () => { outcome = await result.current.sendTest(); });

    expect(outcome).toEqual({ ok: false, reason: 'not-delivered' });
    expect(result.current.isSubscribed).toBe(true);
  });
});
