import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { UsersPage } from './UsersPage';
import { adminUsersService, type AdminUser } from '../../services/adminUsersService';
import { createMockAuth, mockAdmin } from '../../test/helpers';
import type { MockAuthValue } from '../../test/helpers';

let mockAuth: MockAuthValue;

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => mockAuth,
}));

vi.mock('../../services/adminUsersService', () => ({
  adminUsersService: {
    pending: vi.fn(),
    decide: vi.fn(),
    list: vi.fn(),
    updateRole: vi.fn(),
    disable: vi.fn(),
    enable: vi.fn(),
    remove: vi.fn(),
  },
}));

const service = vi.mocked(adminUsersService);

function adminUser(overrides: Partial<AdminUser> = {}): AdminUser {
  return {
    id: 'u-1', email: 'max@schuler.de', displayName: 'Max Mustermann', role: 'User',
    isDisabled: false, hasAzureAd: false, createdAt: '2026-10-01T08:00:00Z', ...overrides,
  };
}

function listOf(users: AdminUser[], totalCount = users.length) {
  return { users, totalCount, page: 1, pageSize: 20 };
}

async function rowOf(email: string) {
  const cell = await screen.findByText(email);
  const row = cell.closest('tr');
  if (!row) throw new Error(`no row for ${email}`);
  return within(row);
}

describe('UsersPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAuth = createMockAuth({ isAuthenticated: true, isAdmin: true, user: mockAdmin });
    service.list.mockResolvedValue(listOf([adminUser()]));
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('loads the first page and renders each user', async () => {
    service.list.mockResolvedValue(listOf([
      adminUser(),
      adminUser({ id: 'u-2', email: 'aad@andritz.com', displayName: 'Azure Nutzer', role: 'Admin', hasAzureAd: true, isDisabled: true }),
    ]));
    render(<UsersPage />);

    const max = await rowOf('max@schuler.de');
    expect(max.getByText('Active')).toBeInTheDocument();
    expect(max.getByText('Local')).toBeInTheDocument();
    expect(max.getByText('MM')).toBeInTheDocument();

    const aad = await rowOf('aad@andritz.com');
    expect(aad.getByText('Disabled')).toBeInTheDocument();
    expect(aad.getByText('Azure AD')).toBeInTheDocument();
    expect(aad.getByRole('button', { name: 'Enable' })).toBeEnabled();

    expect(service.list).toHaveBeenCalledWith({ search: undefined, role: undefined, page: 1, pageSize: 20 });
    expect(screen.getByText(/users · role changes/)).toBeInTheDocument();
  });

  it('shows the empty state and a load error', async () => {
    service.list.mockResolvedValueOnce(listOf([]));
    const { unmount } = render(<UsersPage />);
    expect(await screen.findByText('No users found.')).toBeInTheDocument();
    unmount();

    service.list.mockRejectedValueOnce(new Error('500'));
    render(<UsersPage />);
    expect(await screen.findByText('Failed to load users.')).toBeInTheDocument();
  });

  it('locks the actions on the current admin\'s own row', async () => {
    service.list.mockResolvedValue(listOf([adminUser({ id: mockAdmin.id, email: mockAdmin.email, role: 'Admin' })]));
    render(<UsersPage />);

    const self = await rowOf(mockAdmin.email);
    expect(self.getByText('You')).toBeInTheDocument();
    expect(self.getByRole('combobox')).toBeDisabled();
    expect(self.getByRole('button', { name: 'Disable' })).toBeDisabled();
    expect(self.getByRole('button', { name: 'Delete' })).toBeDisabled();
  });

  it('changes the role and reloads', async () => {
    const user = userEvent.setup();
    service.updateRole.mockResolvedValueOnce(adminUser({ role: 'Admin' }));
    render(<UsersPage />);

    const row = await rowOf('max@schuler.de');
    await user.selectOptions(row.getByRole('combobox'), 'Admin');

    expect(service.updateRole).toHaveBeenCalledWith('u-1', 'Admin');
    await waitFor(() => expect(service.list).toHaveBeenCalledTimes(2));
  });

  it('shows the server detail when the role change fails', async () => {
    const user = userEvent.setup();
    service.updateRole.mockRejectedValueOnce({ response: { status: 400, data: { detail: 'Cannot demote the last SuperAdmin.' } } });
    render(<UsersPage />);

    const row = await rowOf('max@schuler.de');
    await user.selectOptions(row.getByRole('combobox'), 'SuperAdmin');

    expect(await screen.findByText('Cannot demote the last SuperAdmin.')).toBeInTheDocument();
  });

  it('falls back to a generic role-change error', async () => {
    const user = userEvent.setup();
    service.updateRole.mockRejectedValueOnce(new Error('Network Error'));
    render(<UsersPage />);

    const row = await rowOf('max@schuler.de');
    await user.selectOptions(row.getByRole('combobox'), 'Admin');

    expect(await screen.findByText('Failed to change role.')).toBeInTheDocument();
  });

  it('disables an active user and enables a disabled one', async () => {
    const user = userEvent.setup();
    service.list.mockResolvedValue(listOf([
      adminUser(),
      adminUser({ id: 'u-2', email: 'aus@schuler.de', isDisabled: true }),
    ]));
    service.disable.mockResolvedValueOnce(adminUser({ isDisabled: true }));
    service.enable.mockResolvedValueOnce(adminUser({ id: 'u-2' }));
    render(<UsersPage />);

    await user.click((await rowOf('max@schuler.de')).getByRole('button', { name: 'Disable' }));
    expect(service.disable).toHaveBeenCalledWith('u-1');

    await user.click((await rowOf('aus@schuler.de')).getByRole('button', { name: 'Enable' }));
    expect(service.enable).toHaveBeenCalledWith('u-2');
    expect(service.disable).toHaveBeenCalledTimes(1);
  });

  it('uses the problem title when disabling fails without detail', async () => {
    const user = userEvent.setup();
    service.disable.mockRejectedValueOnce({ response: { status: 409, data: { title: 'Conflict' } } });
    render(<UsersPage />);

    await user.click((await rowOf('max@schuler.de')).getByRole('button', { name: 'Disable' }));

    expect(await screen.findByText('Conflict')).toBeInTheDocument();
  });

  it('deletes only after confirmation', async () => {
    const user = userEvent.setup();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    service.remove.mockResolvedValueOnce({ data: undefined } as never);
    render(<UsersPage />);

    const row = await rowOf('max@schuler.de');
    await user.click(row.getByRole('button', { name: 'Delete' }));
    expect(service.remove).not.toHaveBeenCalled();

    await user.click(row.getByRole('button', { name: 'Delete' }));
    expect(confirm).toHaveBeenLastCalledWith(expect.stringContaining('max@schuler.de'));
    expect(service.remove).toHaveBeenCalledWith('u-1');
  });

  it('shows a generic error when deleting fails', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    service.remove.mockRejectedValueOnce(new Error('boom'));
    render(<UsersPage />);

    await user.click((await rowOf('max@schuler.de')).getByRole('button', { name: 'Delete' }));

    expect(await screen.findByText('Failed to delete user.')).toBeInTheDocument();
  });

  it('passes search and role filters and resets to page 1', async () => {
    const user = userEvent.setup();
    service.list.mockResolvedValue(listOf([adminUser()], 45));
    render(<UsersPage />);

    expect(await screen.findByText(/Page/)).toHaveTextContent('Page 1 of 3');
    await user.click(screen.getByRole('button', { name: /next/i }));
    await waitFor(() => expect(service.list).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 })));

    await user.selectOptions(screen.getByDisplayValue('All roles'), 'SuperAdmin');
    await waitFor(() =>
      expect(service.list).toHaveBeenLastCalledWith({ search: undefined, role: 'SuperAdmin', page: 1, pageSize: 20 }),
    );

    await user.type(screen.getByPlaceholderText('Search email or name…'), 'm');
    await waitFor(() =>
      expect(service.list).toHaveBeenLastCalledWith({ search: 'm', role: 'SuperAdmin', page: 1, pageSize: 20 }),
    );
  });

  it('bounds pagination at the first and last page', async () => {
    const user = userEvent.setup();
    service.list.mockResolvedValue(listOf([adminUser()], 21));
    render(<UsersPage />);

    expect(await screen.findByText(/Page/)).toHaveTextContent('Page 1 of 2');
    expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled();

    await user.click(screen.getByRole('button', { name: /next/i }));
    await waitFor(() => expect(screen.getByText(/Page/)).toHaveTextContent('Page 2 of 2'));
    expect(screen.getByRole('button', { name: /next/i })).toBeDisabled();
  });

  it('hides pagination for exactly one page of users', async () => {
    service.list.mockResolvedValue(listOf([adminUser()], 20));
    render(<UsersPage />);

    await screen.findByText('max@schuler.de');
    expect(screen.queryByRole('button', { name: /next/i })).not.toBeInTheDocument();
  });
});
