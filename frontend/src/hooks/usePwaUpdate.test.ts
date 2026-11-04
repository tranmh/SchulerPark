import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { usePwaUpdate, UPDATE_CHECK_INTERVAL_MS } from './usePwaUpdate';

const mocks = vi.hoisted(() => ({
  registration: { update: vi.fn(async () => {}) },
}));

vi.mock('virtual:pwa-register/react', async () => {
  const { useState } = await import('react');
  return {
    useRegisterSW: (options: { onRegisteredSW?: (url: string, r: unknown) => void }) => {
      const needRefresh = useState(false);
      options.onRegisteredSW?.('/sw.js', mocks.registration);
      return { needRefresh, offlineReady: useState(false), updateServiceWorker: async () => {} };
    },
  };
});

function setVisibility(state: DocumentVisibilityState) {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
}

describe('usePwaUpdate', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    mocks.registration.update.mockClear();
    Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => true });
  });
  afterEach(() => {
    vi.useRealTimers();
    setVisibility('visible');
  });

  it('polls for a new service worker on an interval', () => {
    renderHook(() => usePwaUpdate());
    expect(mocks.registration.update).not.toHaveBeenCalled();
    act(() => vi.advanceTimersByTime(UPDATE_CHECK_INTERVAL_MS));
    expect(mocks.registration.update).toHaveBeenCalledTimes(1);
  });

  it('checks again when the tab returns to the foreground, but not while offline', () => {
    renderHook(() => usePwaUpdate());
    setVisibility('visible');
    act(() => document.dispatchEvent(new Event('visibilitychange')));
    expect(mocks.registration.update).toHaveBeenCalledTimes(1);

    Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => false });
    act(() => document.dispatchEvent(new Event('visibilitychange')));
    expect(mocks.registration.update).toHaveBeenCalledTimes(1);
  });

  it('stops polling after unmount', () => {
    const { unmount } = renderHook(() => usePwaUpdate());
    unmount();
    act(() => vi.advanceTimersByTime(UPDATE_CHECK_INTERVAL_MS * 2));
    expect(mocks.registration.update).not.toHaveBeenCalled();
  });
});
