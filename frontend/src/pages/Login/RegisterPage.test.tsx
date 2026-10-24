import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RegisterPage } from './RegisterPage';
import { renderWithRouter, createMockAuth, mockUser } from '../../test/helpers';
import type { MockAuthValue } from '../../test/helpers';

let mockAuth: MockAuthValue;

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => mockAuth,
}));

const STRONG = 'Parken2026!';

async function fillForm(
  user: ReturnType<typeof userEvent.setup>,
  { email = 'neu@schuler.de', name = 'Neue Person', password = STRONG, confirm = STRONG } = {},
) {
  if (email) await user.type(screen.getByLabelText('Email'), email);
  if (name) await user.type(screen.getByLabelText('Display name'), name);
  if (password) await user.type(screen.getByLabelText('Password'), password);
  if (confirm) await user.type(screen.getByLabelText('Confirm password'), confirm);
}

/** Submits the form directly, bypassing native constraint validation (required/minLength). */
function submitForm(container: HTMLElement) {
  const form = container.querySelector('form');
  if (!form) throw new Error('form not rendered');
  fireEvent.submit(form);
}

describe('RegisterPage', () => {
  beforeEach(() => {
    mockAuth = createMockAuth();
  });

  it('renders the registration fields and the sign-in link', () => {
    renderWithRouter(<RegisterPage />);

    expect(screen.getByLabelText('Email')).toBeInTheDocument();
    expect(screen.getByLabelText('Display name')).toBeInTheDocument();
    expect(screen.getByLabelText('Password')).toBeInTheDocument();
    expect(screen.getByLabelText('Confirm password')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create account' })).toBeEnabled();
    expect(screen.getByRole('link', { name: /sign in/i })).toHaveAttribute('href', '/login');
  });

  it('registers with email, display name and password and shows the verify-email screen', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockResolvedValueOnce(undefined);
    renderWithRouter(<RegisterPage />);

    await fillForm(user);
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    expect(mockAuth.register).toHaveBeenCalledWith('neu@schuler.de', 'Neue Person', STRONG);
    expect(await screen.findByText('Check your email')).toBeInTheDocument();
    expect(screen.getByText(/verification link to neu@schuler\.de/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to/i })).toHaveAttribute('href', '/login');
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
  });

  it('rejects a password shorter than 8 characters without calling register', async () => {
    const user = userEvent.setup();
    const { container } = renderWithRouter(<RegisterPage />);

    await fillForm(user, { password: 'Ab1!xyz', confirm: 'Ab1!xyz' });
    submitForm(container);

    expect(await screen.findByText('Password must be at least 8 characters.')).toBeInTheDocument();
    expect(mockAuth.register).not.toHaveBeenCalled();
  });

  it('accepts a password of exactly 8 characters (boundary)', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockResolvedValueOnce(undefined);
    renderWithRouter(<RegisterPage />);

    await fillForm(user, { password: 'Ab1!wxyz', confirm: 'Ab1!wxyz' });
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    expect(mockAuth.register).toHaveBeenCalledWith('neu@schuler.de', 'Neue Person', 'Ab1!wxyz');
  });

  it('rejects a password with fewer than 3 character classes', async () => {
    const user = userEvent.setup();
    renderWithRouter(<RegisterPage />);

    await fillForm(user, { password: 'abcdefgh1', confirm: 'abcdefgh1' });
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    expect(await screen.findByText(/password is too weak/i)).toBeInTheDocument();
    expect(mockAuth.register).not.toHaveBeenCalled();
  });

  it('rejects mismatching passwords', async () => {
    const user = userEvent.setup();
    renderWithRouter(<RegisterPage />);

    await fillForm(user, { confirm: 'Parken2026?' });
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    expect(await screen.findByText('Passwords do not match.')).toBeInTheDocument();
    expect(mockAuth.register).not.toHaveBeenCalled();
  });

  it('checks the password policy before the confirmation match', async () => {
    const user = userEvent.setup();
    const { container } = renderWithRouter(<RegisterPage />);

    await fillForm(user, { password: 'short', confirm: 'different' });
    submitForm(container);

    expect(await screen.findByText('Password must be at least 8 characters.')).toBeInTheDocument();
    expect(screen.queryByText('Passwords do not match.')).not.toBeInTheDocument();
  });

  it('steers SSO-domain addresses to the sign-in page (case-insensitive) and blocks submission', async () => {
    const user = userEvent.setup();
    mockAuth.authConfig = { azureAdEnabled: true, azureAdClientId: 'id', azureAdTenantId: 'tid', ssoDomains: ['andritz.com'] };
    const { container } = renderWithRouter(<RegisterPage />);

    await fillForm(user, { email: 'max.mustermann@ANDRITZ.com' });

    expect(screen.getByText(/signs in with Microsoft \(SSO\)/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create account' })).toBeDisabled();

    submitForm(container);
    expect(mockAuth.register).not.toHaveBeenCalled();
  });

  it('does not treat a domain that merely contains the SSO domain as SSO', async () => {
    const user = userEvent.setup();
    mockAuth.authConfig = { azureAdEnabled: true, azureAdClientId: 'id', azureAdTenantId: 'tid', ssoDomains: ['andritz.com'] };
    renderWithRouter(<RegisterPage />);

    await user.type(screen.getByLabelText('Email'), 'someone@mail.andritz.com');

    expect(screen.queryByText(/signs in with Microsoft \(SSO\)/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create account' })).toBeEnabled();
  });

  it('maps the server code sso_only_domain to the SSO note', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockRejectedValueOnce({ response: { status: 400, data: { code: 'sso_only_domain', error: 'raw' } } });
    renderWithRouter(<RegisterPage />);

    await fillForm(user);
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    expect(await screen.findByText(/signs in with Microsoft \(SSO\)/)).toBeInTheDocument();
    expect(screen.queryByText('raw')).not.toBeInTheDocument();
    expect(screen.queryByText('Check your email')).not.toBeInTheDocument();
  });

  it('shows the server error text when registration fails', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockRejectedValueOnce({ response: { status: 400, data: { detail: 'Password is a common password.' } } });
    renderWithRouter(<RegisterPage />);

    await fillForm(user);
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    expect(await screen.findByText('Password is a common password.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create account' })).toBeEnabled();
  });

  it('falls back to a generic message when the error has no payload', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockRejectedValueOnce(new Error('Network Error'));
    renderWithRouter(<RegisterPage />);

    await fillForm(user);
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    expect(await screen.findByText('Registration failed. Please try again.')).toBeInTheDocument();
  });

  it('clears a previous error on resubmit', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockResolvedValueOnce(undefined);
    renderWithRouter(<RegisterPage />);

    await fillForm(user, { confirm: 'Parken2026?' });
    await user.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByText('Passwords do not match.')).toBeInTheDocument();

    const confirm = screen.getByLabelText('Confirm password');
    await user.clear(confirm);
    await user.type(confirm, STRONG);
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    await waitFor(() => expect(screen.queryByText('Passwords do not match.')).not.toBeInTheDocument());
    expect(mockAuth.register).toHaveBeenCalledTimes(1);
  });

  it('disables the button while the registration is pending', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockReturnValue(new Promise(() => {}));
    renderWithRouter(<RegisterPage />);

    await fillForm(user);
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    expect(screen.getByRole('button', { name: 'Creating account…' })).toBeDisabled();
  });

  it('renders nothing when already authenticated', () => {
    mockAuth = createMockAuth({ isAuthenticated: true, user: mockUser });
    renderWithRouter(<RegisterPage />, { initialEntries: ['/register'] });

    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
    expect(mockAuth.register).not.toHaveBeenCalled();
  });
});
