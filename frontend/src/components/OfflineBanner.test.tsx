import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import { OfflineBanner } from './OfflineBanner';

function setNavigatorOnline(value: boolean) {
  Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => value });
}

describe('OfflineBanner', () => {
  afterEach(() => setNavigatorOnline(true));

  it('renders nothing while online', () => {
    setNavigatorOnline(true);
    render(<OfflineBanner />);
    expect(screen.queryByTestId('offline-banner')).toBeNull();
  });

  it('appears when the browser goes offline and disappears when it returns', () => {
    setNavigatorOnline(true);
    render(<OfflineBanner />);

    act(() => window.dispatchEvent(new Event('offline')));
    expect(screen.getByRole('status')).toBeInTheDocument();

    act(() => window.dispatchEvent(new Event('online')));
    expect(screen.queryByTestId('offline-banner')).toBeNull();
  });
});
