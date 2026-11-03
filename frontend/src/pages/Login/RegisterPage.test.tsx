import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RegisterPage } from './RegisterPage';
import { renderWithRouter, createMockAuth } from '../../test/helpers';
import type { MockAuthValue } from '../../test/helpers';

let mockAuth: MockAuthValue;

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => mockAuth,
}));

type User = ReturnType<typeof userEvent.setup>;

async function fillForm(
  user: User,
  {
    email = 'jane@extern.de',
    firstName = 'Jane',
    lastName = 'Doe',
    company = 'Extern AG',
    password = 'Secret123!',
    confirm = password,
  }: Partial<Record<'email' | 'firstName' | 'lastName' | 'company' | 'password' | 'confirm', string>> = {}
) {
  await user.type(screen.getByLabelText(/^email$/i), email);
  await user.type(screen.getByLabelText(/^first name$/i), firstName);
  await user.type(screen.getByLabelText(/^surname$/i), lastName);
  await user.type(screen.getByLabelText(/^company$/i), company);
  await user.type(screen.getByLabelText(/^password$/i), password);
  await user.type(screen.getByLabelText(/^confirm password$/i), confirm);
}

const submit = (user: User) => user.click(screen.getByRole('button', { name: /^create account$/i }));

describe('RegisterPage', () => {
  beforeEach(() => {
    mockAuth = createMockAuth();
  });

  // B24
  it('renders required first name, surname and company inputs with autocomplete hints and max lengths', () => {
    renderWithRouter(<RegisterPage />);

    const first = screen.getByLabelText(/^first name$/i);
    const last = screen.getByLabelText(/^surname$/i);
    const company = screen.getByLabelText(/^company$/i);

    expect(first).toBeRequired();
    expect(first).toHaveAttribute('autocomplete', 'given-name');
    expect(first).toHaveAttribute('maxlength', '100');
    expect(last).toBeRequired();
    expect(last).toHaveAttribute('autocomplete', 'family-name');
    expect(last).toHaveAttribute('maxlength', '100');
    expect(company).toBeRequired();
    expect(company).toHaveAttribute('autocomplete', 'organization');
    expect(company).toHaveAttribute('maxlength', '200');
    expect(screen.queryByLabelText(/display name/i)).not.toBeInTheDocument();
  });

  it('shows the company hint and the approval note', () => {
    renderWithRouter(<RegisterPage />);

    expect(screen.getByText(/administrators see your name and company/i)).toBeInTheDocument();
    expect(screen.getByText(/Technikum Laubholz email addresses/i)).toBeInTheDocument();
  });

  it('rejects a password shorter than 8 characters without calling register', async () => {
    const user = userEvent.setup();
    renderWithRouter(<RegisterPage />);

    await fillForm(user, { password: 'Ab1!xyz' });
    await submit(user);

    expect(await screen.findByText('Password must be at least 8 characters.')).toBeInTheDocument();
    expect(mockAuth.register).not.toHaveBeenCalled();
  });

  it('rejects a password with fewer than 3 character classes', async () => {
    const user = userEvent.setup();
    renderWithRouter(<RegisterPage />);

    await fillForm(user, { password: 'abcdefgh1' });
    await submit(user);

    expect(await screen.findByText(/password is too weak/i)).toBeInTheDocument();
    expect(mockAuth.register).not.toHaveBeenCalled();
  });

  it('accepts a password of exactly 8 characters with 3 classes', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockResolvedValueOnce(undefined);
    renderWithRouter(<RegisterPage />);

    await fillForm(user, { password: 'Abcdef12' });
    await submit(user);

    await waitFor(() => expect(mockAuth.register).toHaveBeenCalledTimes(1));
  });

  it('rejects mismatching confirmation', async () => {
    const user = userEvent.setup();
    renderWithRouter(<RegisterPage />);

    await fillForm(user, { password: 'Secret123!', confirm: 'Secret123?' });
    await submit(user);

    expect(await screen.findByText('Passwords do not match.')).toBeInTheDocument();
    expect(mockAuth.register).not.toHaveBeenCalled();
  });

  // B24 + B25
  it('submits trimmed names and company, then shows the verify-email card', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockResolvedValueOnce(undefined);
    renderWithRouter(<RegisterPage />);

    await fillForm(user, { firstName: '  Jane ', lastName: ' Doe  ', company: '  Extern AG ' });
    await submit(user);

    await waitFor(() => {
      expect(mockAuth.register).toHaveBeenCalledWith({
        email: 'jane@extern.de',
        firstName: 'Jane',
        lastName: 'Doe',
        company: 'Extern AG',
        password: 'Secret123!',
      });
    });
    expect(await screen.findByRole('heading', { name: 'Check your email' })).toBeInTheDocument();
    expect(screen.getByText(/verification link to jane@extern\.de/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to/i })).toHaveAttribute('href', '/login');
  });

  it('shows the server error when registration fails', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockRejectedValueOnce({ response: { data: { error: 'Email already taken.' } } });
    renderWithRouter(<RegisterPage />);

    await fillForm(user);
    await submit(user);

    expect(await screen.findByText('Email already taken.')).toBeInTheDocument();
    expect(screen.queryByText('Check your email')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^create account$/i })).toBeEnabled();
  });

  it('falls back to a generic message when the error has no payload', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockRejectedValueOnce(new Error('Network Error'));
    renderWithRouter(<RegisterPage />);

    await fillForm(user);
    await submit(user);

    expect(await screen.findByText('Registration failed. Please try again.')).toBeInTheDocument();
  });

  it('maps the sso_only_domain error code to the SSO note', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockRejectedValueOnce({ response: { data: { code: 'sso_only_domain', error: 'SSO only' } } });
    renderWithRouter(<RegisterPage />);

    await fillForm(user);
    await submit(user);

    expect(await screen.findByText(/signs in with Microsoft \(SSO\)/i)).toBeInTheDocument();
    expect(screen.queryByText('SSO only')).not.toBeInTheDocument();
  });

  it('steers SSO-domain addresses to sign-in and disables submit', async () => {
    const user = userEvent.setup();
    mockAuth.authConfig = { azureAdEnabled: true, azureAdClientId: 'id', azureAdTenantId: 'tid', ssoDomains: ['Andritz.com'] };
    renderWithRouter(<RegisterPage />);

    await user.type(screen.getByLabelText(/^email$/i), 'max@ANDRITZ.com');

    expect(screen.getByText(/signs in with Microsoft \(SSO\)/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^create account$/i })).toBeDisabled();
  });

  it('shows a disabled button while the registration is in flight', async () => {
    const user = userEvent.setup();
    mockAuth.register.mockReturnValue(new Promise(() => {}));
    renderWithRouter(<RegisterPage />);

    await fillForm(user);
    await submit(user);

    expect(screen.getByRole('button', { name: /creating account/i })).toBeDisabled();
  });

  it('renders nothing of the form when already authenticated', () => {
    mockAuth = createMockAuth({ isAuthenticated: true });
    renderWithRouter(<RegisterPage />, { initialEntries: ['/register'] });

    expect(screen.queryByLabelText(/^first name$/i)).not.toBeInTheDocument();
  });
});
