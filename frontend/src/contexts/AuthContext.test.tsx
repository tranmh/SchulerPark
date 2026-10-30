import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import { useEffect } from 'react';
import { AuthProvider, useAuth } from './AuthContext';

// Mirrors USER_REFRESH_THROTTLE_MS in AuthContext.tsx (kept private there for react-refresh).
const USER_REFRESH_THROTTLE_MS = 30_000;
import type { User } from '../types/auth';

vi.mock('../services/authService', () => ({
  authService: {
    getAuthConfig: vi.fn(),
    refresh: vi.fn(),
    login: vi.fn(),
    logout: vi.fn(),
  },
}));

vi.mock('@azure/msal-browser', () => ({
  PublicClientApplication: class {
    initialize = vi.fn().mockResolvedValue(undefined);
    handleRedirectPromise = vi.fn().mockResolvedValue(null);
  },
  BrowserAuthError: class extends Error {},
  BrowserAuthErrorCodes: {},
}));

vi.mock('../services/profileService', () => ({
  profileService: {
    getProfile: vi.fn(),
    updateLanguage: vi.fn(),
  },
}));

import { authService } from '../services/authService';
import { profileService } from '../services/profileService';

// Tests run with navigator.language 'en-US', so the UI language is 'en'; keep the
// stored language identical so the language-sync effect stays out of the way.
const storedUser: User = {
  id: 'u1',
  email: 'test@schuler.de',
  displayName: 'Old Name',
  carLicensePlate: 'GP-OL 1',
  role: 'User',
  hasAzureAd: false,
  preferredLocationId: null,
  preferredSlotId: null,
  preferredLanguage: 'en',
  hasPassword: true,
};

function Probe() {
  const { user, isAuthenticated, refreshUser, updateUser } = useAuth();
  return (
    <div>
      <span data-testid="name">{user?.displayName ?? '(none)'}</span>
      <span data-testid="plate">{user?.carLicensePlate ?? '(none)'}</span>
      <span data-testid="authed">{String(isAuthenticated)}</span>
      <button type="button" onClick={() => void refreshUser()}>refresh</button>
      <button type="button" onClick={() => updateUser({ ...storedUser, displayName: 'Pushed Name' })}>
        push
      </button>
    </div>
  );
}

async function renderSignedIn() {
  render(
    <AuthProvider>
      <Probe />
    </AuthProvider>
  );
  await waitFor(() => expect(screen.getByTestId('name')).toHaveTextContent('Old Name'));
}

function setVisibility(state: DocumentVisibilityState) {
  Object.defineProperty(document, 'visibilityState', { value: state, configurable: true });
}

/** Profile changes made elsewhere must reach a long-running SPA (installed PWA) without a reload. */
describe('AuthContext user refresh', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    setVisibility('visible');
    vi.mocked(authService.getAuthConfig).mockResolvedValue({
      azureAdEnabled: false,
      azureAdClientId: null,
      azureAdTenantId: null,
      ssoDomains: [],
    });
    vi.mocked(authService.refresh).mockResolvedValue({
      accessToken: 'tok',
      expiresAt: '2099-01-01T00:00:00Z',
      user: storedUser,
    });
    vi.mocked(profileService.getProfile).mockResolvedValue({
      ...storedUser,
      displayName: 'New Name',
      carLicensePlate: 'GP-NE 2',
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('refreshUser() re-reads GET /api/profile and replaces the cached user', async () => {
    await renderSignedIn();

    await act(async () => {
      screen.getByRole('button', { name: 'refresh' }).click();
    });

    expect(profileService.getProfile).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('name')).toHaveTextContent('New Name');
    expect(screen.getByTestId('plate')).toHaveTextContent('GP-NE 2');
  });

  it('keeps the old user when the refresh fails', async () => {
    vi.mocked(profileService.getProfile).mockRejectedValue(new Error('network'));
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    await renderSignedIn();

    await act(async () => {
      screen.getByRole('button', { name: 'refresh' }).click();
    });

    expect(screen.getByTestId('name')).toHaveTextContent('Old Name');
    expect(screen.getByTestId('authed')).toHaveTextContent('true');
    warn.mockRestore();
  });

  it('updateUser() replaces the cached user with a server copy', async () => {
    await renderSignedIn();

    await act(async () => {
      screen.getByRole('button', { name: 'push' }).click();
    });

    expect(screen.getByTestId('name')).toHaveTextContent('Pushed Name');
  });

  it('refreshes when the app becomes visible again, throttled to once per window', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    await renderSignedIn();

    await act(async () => {
      setVisibility('visible');
      document.dispatchEvent(new Event('visibilitychange'));
    });
    expect(profileService.getProfile).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(screen.getByTestId('name')).toHaveTextContent('New Name'));

    // A burst of focus/visibility/pageshow events inside the window is ignored.
    await act(async () => {
      window.dispatchEvent(new Event('focus'));
      document.dispatchEvent(new Event('visibilitychange'));
      window.dispatchEvent(new Event('pageshow'));
    });
    expect(profileService.getProfile).toHaveBeenCalledTimes(1);

    // Once the window has elapsed, the next resume refreshes again.
    await act(async () => {
      vi.advanceTimersByTime(USER_REFRESH_THROTTLE_MS + 1);
      window.dispatchEvent(new Event('focus'));
    });
    expect(profileService.getProfile).toHaveBeenCalledTimes(2);
  });

  it('does not refresh while the document is hidden', async () => {
    await renderSignedIn();

    await act(async () => {
      setVisibility('hidden');
      document.dispatchEvent(new Event('visibilitychange'));
      window.dispatchEvent(new Event('focus'));
    });

    expect(profileService.getProfile).not.toHaveBeenCalled();
  });

  it('does nothing while signed out', async () => {
    vi.mocked(authService.refresh).mockRejectedValue(new Error('401'));
    render(
      <AuthProvider>
        <Probe />
      </AuthProvider>
    );
    await waitFor(() => expect(screen.getByTestId('authed')).toHaveTextContent('false'));

    await act(async () => {
      window.dispatchEvent(new Event('focus'));
      screen.getByRole('button', { name: 'refresh' }).click();
    });

    expect(profileService.getProfile).not.toHaveBeenCalled();
  });
});

/**
 * With Azure AD enabled the provider used to wrap the tree in <MsalProvider> once the
 * config arrived, remounting every page. Single-use token pages (verify-email, reset)
 * then fired their request twice and reported a false failure.
 */
describe('AuthContext startup with Azure AD', () => {
  it('does not remount its children when the Azure AD config arrives', async () => {
    vi.clearAllMocks();
    vi.mocked(authService.getAuthConfig).mockResolvedValue({
      azureAdEnabled: true,
      azureAdClientId: 'client',
      azureAdTenantId: 'tenant',
      ssoDomains: [],
    });
    vi.mocked(authService.refresh).mockRejectedValue(new Error('401'));

    let mounts = 0;
    function MountCounter() {
      const { isLoading } = useAuth();
      useEffect(() => {
        mounts += 1;
      }, []);
      return <span data-testid="loading">{String(isLoading)}</span>;
    }

    render(
      <AuthProvider>
        <MountCounter />
      </AuthProvider>
    );
    await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('false'));
    expect(mounts).toBe(1);
  });
});
