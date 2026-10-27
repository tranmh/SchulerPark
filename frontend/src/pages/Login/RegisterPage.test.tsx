import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import i18n from '../../i18n';
import { RegisterPage } from './RegisterPage';
import { renderWithRouter, createMockAuth } from '../../test/helpers';
import type { MockAuthValue } from '../../test/helpers';

let mockAuth: MockAuthValue;

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => mockAuth,
}));

function field(id: string): HTMLInputElement {
  const el = document.getElementById(id);
  if (!(el instanceof HTMLInputElement)) throw new Error(`missing #${id}`);
  return el;
}

function fill(values: { email?: string; name?: string; password?: string; confirm?: string }) {
  fireEvent.change(field('email'), { target: { value: values.email ?? 'new@schuler.de' } });
  fireEvent.change(field('displayName'), { target: { value: values.name ?? 'New User' } });
  fireEvent.change(field('password'), { target: { value: values.password ?? 'Secret123' } });
  fireEvent.change(field('confirmPassword'), { target: { value: values.confirm ?? values.password ?? 'Secret123' } });
}

function submit() {
  const form = screen.getByRole('button', { name: i18n.t('auth.createAccountBtn') }).closest('form');
  if (!form) throw new Error('form not found');
  fireEvent.submit(form);
}

describe('RegisterPage form validation', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en');
    mockAuth = createMockAuth({
      authConfig: { azureAdEnabled: true, azureAdClientId: 'c', azureAdTenantId: 't', ssoDomains: ['Andritz.com'] },
    });
  });

  it('rejects a password shorter than 8 characters without calling register', () => {
    renderWithRouter(<RegisterPage />);
    fill({ password: 'Ab1!xyz' });
    submit();
    expect(screen.getByText(i18n.t('auth.passwordTooShort'))).toBeInTheDocument();
    expect(mockAuth.register).not.toHaveBeenCalled();
  });

  it('rejects an 8-character password with only two character classes', () => {
    renderWithRouter(<RegisterPage />);
    fill({ password: 'abcdefg1' });
    submit();
    expect(screen.getByText(i18n.t('auth.passwordTooWeak'))).toBeInTheDocument();
    expect(mockAuth.register).not.toHaveBeenCalled();
  });

  it('rejects mismatching confirmation', () => {
    renderWithRouter(<RegisterPage />);
    fill({ password: 'Secret123', confirm: 'Secret124' });
    submit();
    expect(screen.getByText(i18n.t('auth.passwordsDoNotMatch'))).toBeInTheDocument();
    expect(mockAuth.register).not.toHaveBeenCalled();
  });

  it('steers SSO domains to the login page (case-insensitive) and disables submit', () => {
    renderWithRouter(<RegisterPage />);
    fill({ email: 'someone@ANDRITZ.com ' });
    expect(screen.getByText(i18n.t('auth.ssoDomainNote'), { exact: false })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: i18n.t('auth.createAccountBtn') })).toBeDisabled();
    submit();
    expect(mockAuth.register).not.toHaveBeenCalled();
  });

  it('does not treat a subdomain-like address as SSO', () => {
    renderWithRouter(<RegisterPage />);
    fill({ email: 'someone@notandritz.com' });
    expect(screen.queryByText(i18n.t('auth.ssoDomainNote'), { exact: false })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: i18n.t('auth.createAccountBtn') })).toBeEnabled();
  });

  it('shows the approval note on the form (B24)', () => {
    renderWithRouter(<RegisterPage />);
    expect(screen.getByText(i18n.t('auth.registerApprovalNote'))).toBeInTheDocument();
  });

  it('registers and asks the user to verify the email instead of logging in (B24)', async () => {
    mockAuth.register.mockResolvedValueOnce(undefined);
    renderWithRouter(<RegisterPage />);
    fill({});
    submit();
    expect(await screen.findByText(i18n.t('auth.verifyEmailSentTitle'))).toBeInTheDocument();
    expect(mockAuth.register).toHaveBeenCalledWith('new@schuler.de', 'New User', 'Secret123');
    expect(mockAuth.login).not.toHaveBeenCalled();
    expect(screen.getByRole('link', { name: i18n.t('auth.backToLogin') })).toHaveAttribute('href', '/login');
  });

  it('maps a server sso_only_domain error to the SSO note', async () => {
    mockAuth.register.mockRejectedValueOnce({
      response: { status: 400, data: { error: 'sso only', code: 'sso_only_domain' } },
    });
    renderWithRouter(<RegisterPage />);
    fill({});
    submit();
    await waitFor(() => expect(screen.getByText(i18n.t('auth.ssoDomainNote'))).toBeInTheDocument());
    expect(screen.queryByText(i18n.t('auth.verifyEmailSentTitle'))).not.toBeInTheDocument();
  });

  it('shows the generic failure when the server gives no details and re-enables the button', async () => {
    mockAuth.register.mockRejectedValueOnce(new Error('network'));
    renderWithRouter(<RegisterPage />);
    fill({});
    submit();
    expect(await screen.findByText(i18n.t('auth.registerFailed'))).toBeInTheDocument();
    expect(screen.getByRole('button', { name: i18n.t('auth.createAccountBtn') })).toBeEnabled();
  });

  it('renders nothing for an authenticated user', () => {
    mockAuth = createMockAuth({ isAuthenticated: true });
    const { container } = renderWithRouter(<RegisterPage />);
    expect(container).toBeEmptyDOMElement();
  });
});
