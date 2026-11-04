import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { UpdateToast } from './UpdateToast';

const mocks = vi.hoisted(() => ({
  needRefresh: false,
  setNeedRefresh: vi.fn(),
  updateServiceWorker: vi.fn(async () => {}),
}));

vi.mock('virtual:pwa-register/react', () => ({
  useRegisterSW: () => ({
    needRefresh: [mocks.needRefresh, mocks.setNeedRefresh],
    offlineReady: [false, vi.fn()],
    updateServiceWorker: mocks.updateServiceWorker,
  }),
}));

describe('UpdateToast', () => {
  beforeEach(() => {
    mocks.needRefresh = false;
    mocks.setNeedRefresh.mockClear();
    mocks.updateServiceWorker.mockClear();
  });

  it('renders nothing while no new release is waiting', () => {
    render(<UpdateToast />);
    expect(screen.queryByTestId('update-toast')).toBeNull();
  });

  it('shows the toast once a new release is waiting', () => {
    mocks.needRefresh = true;
    render(<UpdateToast />);
    expect(screen.getByRole('status')).toHaveTextContent(/neue Version|new version/i);
  });

  it('asks the waiting worker to take over when the user reloads', () => {
    mocks.needRefresh = true;
    render(<UpdateToast />);
    fireEvent.click(screen.getByRole('button', { name: /neu laden|reload/i }));
    expect(mocks.updateServiceWorker).toHaveBeenCalledWith(true);
  });

  it('hides the toast when dismissed without reloading', () => {
    mocks.needRefresh = true;
    render(<UpdateToast />);
    fireEvent.click(screen.getByRole('button', { name: /später|later/i }));
    expect(mocks.setNeedRefresh).toHaveBeenCalledWith(false);
    expect(mocks.updateServiceWorker).not.toHaveBeenCalled();
  });
});
