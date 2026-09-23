import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ResetPasswordPage } from './ResetPasswordPage';
import { renderWithRouter } from '../../test/helpers';
import { authService } from '../../services/authService';

vi.mock('../../services/authService', () => ({
  authService: {
    resetPassword: vi.fn(),
  },
}));

const resetPassword = vi.mocked(authService.resetPassword);

const withToken = { initialEntries: ['/reset-password?token=abc123'] };

describe('ResetPasswordPage', () => {
  beforeEach(() => {
    resetPassword.mockReset();
  });

  it('shows the invalid-link card when the token is missing', () => {
    renderWithRouter(<ResetPasswordPage />, { initialEntries: ['/reset-password'] });

    expect(screen.getByRole('heading', { name: /link invalid/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /request a new link/i })).toHaveAttribute('href', '/forgot-password');
    expect(screen.queryByLabelText(/^new password$/i)).not.toBeInTheDocument();
  });

  it('validates the password client-side before calling the API', async () => {
    const user = userEvent.setup();
    renderWithRouter(<ResetPasswordPage />, withToken);

    await user.type(screen.getByLabelText(/^new password$/i), 'abcdefgh');
    await user.type(screen.getByLabelText(/confirm new password/i), 'abcdefgh');
    await user.click(screen.getByRole('button', { name: /save password/i }));

    expect(screen.getByText(/too weak/i)).toBeInTheDocument();
    expect(resetPassword).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText(/^new password$/i));
    await user.type(screen.getByLabelText(/^new password$/i), 'Test1234!');
    await user.click(screen.getByRole('button', { name: /save password/i }));

    expect(screen.getByText(/passwords do not match/i)).toBeInTheDocument();
    expect(resetPassword).not.toHaveBeenCalled();
  });

  it('resets the password with the token and shows the success card', async () => {
    const user = userEvent.setup();
    resetPassword.mockResolvedValueOnce(undefined);
    renderWithRouter(<ResetPasswordPage />, withToken);

    await user.type(screen.getByLabelText(/^new password$/i), 'Test1234!');
    await user.type(screen.getByLabelText(/confirm new password/i), 'Test1234!');
    await user.click(screen.getByRole('button', { name: /save password/i }));

    expect(resetPassword).toHaveBeenCalledWith('abc123', 'Test1234!');
    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /password changed/i })).toBeInTheDocument();
    });
    expect(screen.getByRole('link', { name: /back to sign in/i })).toHaveAttribute('href', '/login');
  });

  it('switches to the invalid-link card when the server rejects the token', async () => {
    const user = userEvent.setup();
    resetPassword.mockRejectedValueOnce({
      response: { status: 400, data: { code: 'reset_token_invalid', detail: 'Reset link is invalid or has expired.' } },
    });
    renderWithRouter(<ResetPasswordPage />, withToken);

    await user.type(screen.getByLabelText(/^new password$/i), 'Test1234!');
    await user.type(screen.getByLabelText(/confirm new password/i), 'Test1234!');
    await user.click(screen.getByRole('button', { name: /save password/i }));

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /link invalid/i })).toBeInTheDocument();
    });
  });

  it('shows the localized API error for other failures and stays on the form', async () => {
    const user = userEvent.setup();
    resetPassword.mockRejectedValueOnce({
      response: { status: 400, data: { code: 'password_too_weak', detail: 'weak' } },
    });
    renderWithRouter(<ResetPasswordPage />, withToken);

    // Passes the client rule (3 classes) but the server may still reject (e.g. site term).
    await user.type(screen.getByLabelText(/^new password$/i), 'Schuler2026!');
    await user.type(screen.getByLabelText(/confirm new password/i), 'Schuler2026!');
    await user.click(screen.getByRole('button', { name: /save password/i }));

    await waitFor(() => {
      expect(screen.getByText(/too weak/i)).toBeInTheDocument();
    });
    expect(screen.getByLabelText(/^new password$/i)).toBeInTheDocument();
  });
});
