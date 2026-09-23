import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ForgotPasswordPage } from './ForgotPasswordPage';
import { renderWithRouter } from '../../test/helpers';
import { authService } from '../../services/authService';

vi.mock('../../services/authService', () => ({
  authService: {
    forgotPassword: vi.fn(),
  },
}));

const forgotPassword = vi.mocked(authService.forgotPassword);

describe('ForgotPasswordPage', () => {
  beforeEach(() => {
    forgotPassword.mockReset();
  });

  it('renders the email form and a link back to login', () => {
    renderWithRouter(<ForgotPasswordPage />);

    expect(screen.getByRole('heading', { name: /forgot your password/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /send reset link/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to sign in/i })).toHaveAttribute('href', '/login');
  });

  it('submits the address and shows the generic sent card', async () => {
    const user = userEvent.setup();
    forgotPassword.mockResolvedValueOnce(undefined);

    renderWithRouter(<ForgotPasswordPage />);
    await user.type(screen.getByLabelText(/email/i), 'anna@schuler.de');
    await user.click(screen.getByRole('button', { name: /send reset link/i }));

    expect(forgotPassword).toHaveBeenCalledWith('anna@schuler.de');
    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /check your email/i })).toBeInTheDocument();
    });
    expect(screen.getByText(/anna@schuler\.de/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/email/i)).not.toBeInTheDocument();
  });

  it('shows an inline error when the request itself fails', async () => {
    const user = userEvent.setup();
    forgotPassword.mockRejectedValueOnce(new Error('Network error'));

    renderWithRouter(<ForgotPasswordPage />);
    await user.type(screen.getByLabelText(/email/i), 'anna@schuler.de');
    await user.click(screen.getByRole('button', { name: /send reset link/i }));

    await waitFor(() => {
      expect(screen.getByText(/request failed/i)).toBeInTheDocument();
    });
    // Still on the form so the user can retry.
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
  });
});
