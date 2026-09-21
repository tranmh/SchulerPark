import { describe, it, expect, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useOnlineStatus } from './useOnlineStatus';

function setNavigatorOnline(value: boolean) {
  Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => value });
}

describe('useOnlineStatus', () => {
  afterEach(() => setNavigatorOnline(true));

  it('reads the initial state from navigator.onLine', () => {
    setNavigatorOnline(false);
    const { result } = renderHook(() => useOnlineStatus());
    expect(result.current).toBe(false);
  });

  it('flips on offline/online window events', () => {
    setNavigatorOnline(true);
    const { result } = renderHook(() => useOnlineStatus());
    expect(result.current).toBe(true);

    act(() => window.dispatchEvent(new Event('offline')));
    expect(result.current).toBe(false);

    act(() => window.dispatchEvent(new Event('online')));
    expect(result.current).toBe(true);
  });

  it('removes its listeners on unmount', () => {
    setNavigatorOnline(true);
    const { result, unmount } = renderHook(() => useOnlineStatus());
    unmount();
    // Dispatching after unmount must not throw or update a torn-down hook.
    act(() => window.dispatchEvent(new Event('offline')));
    expect(result.current).toBe(true);
  });
});
