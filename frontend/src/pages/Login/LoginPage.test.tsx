import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LoginPage } from './LoginPage';
import { renderWithRouter, createMockAuth, mockUser } from '../../test/helpers';
import type { MockAuthValue } from '../../test/helpers';

let mockAuth: MockAuthValue;

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => mockAuth,
}));

describe('LoginPage', () => {
  beforeEach(() => {
    mockAuth = createMockAuth();
  });

  it('renders login form with email and password fields', () => {
    renderWithRouter(<LoginPage />);

    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/password/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();
  });

  it('renders app title', () => {
    renderWithRouter(<LoginPage />);

    // "LouisE" appears in both the brand panel and the mobile header.
    expect(screen.getAllByText('LouisE').length).toBeGreaterThan(0);
  });

  it('shows link to register page', () => {
    renderWithRouter(<LoginPage />);

    expect(screen.getByRole('link', { name: /create an account/i })).toHaveAttribute('href', '/register');
  });

  it('calls login with email and password on submit', async () => {
    const user = userEvent.setup();
    mockAuth.login.mockResolvedValueOnce(undefined);

    renderWithRouter(<LoginPage />);

    await user.type(screen.getByLabelText(/email/i), 'test@schuler.de');
    await user.type(screen.getByLabelText(/password/i), 'Test1234!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(mockAuth.login).toHaveBeenCalledWith('test@schuler.de', 'Test1234!');
  });

  it('shows error message on login failure', async () => {
    const user = userEvent.setup();
    mockAuth.login.mockRejectedValueOnce({
      response: { data: { error: 'Invalid credentials' } },
    });

    renderWithRouter(<LoginPage />);

    await user.type(screen.getByLabelText(/email/i), 'wrong@test.de');
    await user.type(screen.getByLabelText(/password/i), 'wrongpass');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => {
      expect(screen.getByText('Invalid credentials')).toBeInTheDocument();
    });
  });

  it('shows generic error when response has no error field', async () => {
    const user = userEvent.setup();
    mockAuth.login.mockRejectedValueOnce(new Error('Network error'));

    renderWithRouter(<LoginPage />);

    await user.type(screen.getByLabelText(/email/i), 'test@test.de');
    await user.type(screen.getByLabelText(/password/i), 'pass1234');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => {
      expect(screen.getByText('Login failed. Please try again.')).toBeInTheDocument();
    });
  });

  it('disables button while loading', async () => {
    const user = userEvent.setup();
    // Never resolve so we stay in loading state
    mockAuth.login.mockReturnValue(new Promise(() => {}));

    renderWithRouter(<LoginPage />);

    await user.type(screen.getByLabelText(/email/i), 'test@test.de');
    await user.type(screen.getByLabelText(/password/i), 'pass1234');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(screen.getByRole('button', { name: /signing in/i })).toBeDisabled();
  });

  it('does not show Azure AD button when disabled', () => {
    renderWithRouter(<LoginPage />);

    // The brand panel always carries an SSO note mentioning Microsoft; the
    // assertion is about the *button*, not stray copy.
    expect(screen.queryByRole('button', { name: /continue with microsoft/i })).not.toBeInTheDocument();
  });

  it('shows Azure AD button when enabled', () => {
    mockAuth.authConfig = { azureAdEnabled: true, azureAdClientId: 'id', azureAdTenantId: 'tid', ssoDomains: [] };

    renderWithRouter(<LoginPage />);

    expect(screen.getByRole('button', { name: /continue with microsoft/i })).toBeInTheDocument();
  });

  it('shows the MSAL error code when Azure AD login fails', async () => {
    const user = userEvent.setup();
    mockAuth.authConfig = { azureAdEnabled: true, azureAdClientId: 'id', azureAdTenantId: 'tid', ssoDomains: [] };
    mockAuth.loginWithAzureAd.mockRejectedValueOnce({ errorCode: 'popup_window_error' });

    renderWithRouter(<LoginPage />);
    await user.click(screen.getByRole('button', { name: /continue with microsoft/i }));

    await waitFor(() => {
      expect(screen.getByText(/azure ad login failed.*\(popup_window_error\)/i)).toBeInTheDocument();
    });
  });

  it('shows a failed redirect-flow Azure AD login carried over from the auth context', async () => {
    mockAuth.authConfig = { azureAdEnabled: true, azureAdClientId: 'id', azureAdTenantId: 'tid', ssoDomains: [] };
    mockAuth.azureLoginError = { response: { data: { error: 'Invalid Azure AD token.' } } };

    renderWithRouter(<LoginPage />, { initialEntries: ['/login'] });

    await waitFor(() => {
      expect(screen.getByText('Invalid Azure AD token.')).toBeInTheDocument();
    });
    expect(mockAuth.clearAzureLoginError).toHaveBeenCalled();
  });

  it('redirects when already authenticated', () => {
    mockAuth = createMockAuth({ isAuthenticated: true, user: mockUser });

    renderWithRouter(<LoginPage />, { initialEntries: ['/login'] });

    // Should not render the form
    expect(screen.queryByLabelText(/email/i)).not.toBeInTheDocument();
  });
});
