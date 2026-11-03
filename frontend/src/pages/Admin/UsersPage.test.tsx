import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { UsersPage } from './UsersPage';
import { createMockAuth, mockAdmin } from '../../test/helpers';
import type { MockAuthValue } from '../../test/helpers';
import type { AdminUser } from '../../services/adminUsersService';

let mockAuth: MockAuthValue;

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => mockAuth,
}));

vi.mock('../../services/adminUsersService', () => ({
  adminUsersService: {
    list: vi.fn(),
    updateRole: vi.fn(),
    disable: vi.fn(),
    enable: vi.fn(),
    remove: vi.fn(),
    updateCompany: vi.fn(),
  },
}));

import { adminUsersService } from '../../services/adminUsersService';

const base: AdminUser = {
  id: 'u1',
  email: 'anna@extern.de',
  displayName: 'Anna Meier',
  firstName: 'Anna',
  lastName: 'Meier',
  company: 'Extern AG',
  role: 'User',
  approvalStatus: 'Approved',
  isDisabled: false,
  hasAzureAd: false,
  createdAt: '2026-10-01T08:00:00Z',
};

const sso: AdminUser = { ...base, id: 'u2', email: 'max@andritz.com', displayName: 'Max Muster', company: null, hasAzureAd: true };
const legacy: AdminUser = { ...base, id: 'u3', email: 'old@local.de', displayName: 'Old Local', company: null };
const pending: AdminUser = { ...base, id: 'u4', email: 'pen@extern.de', displayName: 'Pen Ding', approvalStatus: 'Pending' };
const rejected: AdminUser = { ...base, id: 'u5', email: 'rej@extern.de', displayName: 'Rej Ected', approvalStatus: 'Rejected' };
const self: AdminUser = { ...base, id: mockAdmin.id, email: mockAdmin.email, displayName: 'Admin', role: 'Admin' };

function mockList(users: AdminUser[], totalCount = users.length) {
  vi.mocked(adminUsersService.list).mockResolvedValue({ users, totalCount, page: 1, pageSize: 20 });
}

const rowOf = (email: string) => within(screen.getByText(email).closest('tr') as HTMLElement);

describe('UsersPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAuth = createMockAuth({ isAuthenticated: true, isAdmin: true, user: mockAdmin });
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('loads the first page without filters', async () => {
    mockList([base]);
    render(<UsersPage />);

    expect(await screen.findByText('Anna Meier')).toBeInTheDocument();
    expect(adminUsersService.list).toHaveBeenCalledWith({ search: undefined, role: undefined, page: 1, pageSize: 20 });
  });

  // B27
  it('shows the company, "Internal (SSO)" for SSO users without one, and a dash otherwise', async () => {
    mockList([base, sso, legacy]);
    render(<UsersPage />);

    await screen.findByText('Anna Meier');
    expect(screen.getByRole('columnheader', { name: 'Company' })).toBeInTheDocument();
    expect(rowOf('anna@extern.de').getByText('Extern AG')).toBeInTheDocument();
    expect(rowOf('max@andritz.com').getByText('Internal (SSO)')).toBeInTheDocument();
    expect(rowOf('old@local.de').getByText('—')).toBeInTheDocument();
    expect(rowOf('old@local.de').queryByText('Internal (SSO)')).not.toBeInTheDocument();
  });

  // B27
  it('shows Pending / Rejected chips for users that are not approved', async () => {
    mockList([base, pending, rejected]);
    render(<UsersPage />);

    await screen.findByText('Anna Meier');
    expect(rowOf('anna@extern.de').getByText('Active')).toBeInTheDocument();
    expect(rowOf('pen@extern.de').getByText('Pending approval')).toBeInTheDocument();
    expect(rowOf('rej@extern.de').getByText('Rejected')).toBeInTheDocument();
  });

  it('shows the empty state and the load error', async () => {
    mockList([]);
    const { unmount } = render(<UsersPage />);
    expect(await screen.findByText('No users found.')).toBeInTheDocument();
    unmount();

    vi.mocked(adminUsersService.list).mockRejectedValueOnce(new Error('boom'));
    render(<UsersPage />);
    expect(await screen.findByText('Failed to load users.')).toBeInTheDocument();
  });

  it('changes a role and reloads', async () => {
    const user = userEvent.setup();
    mockList([base]);
    vi.mocked(adminUsersService.updateRole).mockResolvedValueOnce({ ...base, role: 'Admin' });
    render(<UsersPage />);

    await screen.findByText('Anna Meier');
    await user.selectOptions(rowOf('anna@extern.de').getByRole('combobox'), 'Admin');

    expect(adminUsersService.updateRole).toHaveBeenCalledWith('u1', 'Admin');
    await waitFor(() => expect(adminUsersService.list).toHaveBeenCalledTimes(2));
  });

  it('shows the server error when the role change fails', async () => {
    const user = userEvent.setup();
    mockList([base]);
    vi.mocked(adminUsersService.updateRole).mockRejectedValueOnce({ response: { data: { error: 'Only SuperAdmins can do that.' } } });
    render(<UsersPage />);

    await screen.findByText('Anna Meier');
    await user.selectOptions(rowOf('anna@extern.de').getByRole('combobox'), 'SuperAdmin');

    expect(await screen.findByText('Only SuperAdmins can do that.')).toBeInTheDocument();
  });

  it('locks role, disable and delete on the own row', async () => {
    mockList([self]);
    render(<UsersPage />);

    await screen.findByText('admin@schulerpark.local');
    const row = rowOf('admin@schulerpark.local');
    expect(row.getByText('You')).toBeInTheDocument();
    expect(row.getByRole('combobox')).toBeDisabled();
    expect(row.getByRole('button', { name: 'Disable' })).toBeDisabled();
    expect(row.getByRole('button', { name: 'Delete' })).toBeDisabled();
  });

  it('disables an active user and enables a disabled one', async () => {
    const user = userEvent.setup();
    mockList([base, { ...legacy, isDisabled: true }]);
    vi.mocked(adminUsersService.disable).mockResolvedValueOnce(base);
    vi.mocked(adminUsersService.enable).mockResolvedValueOnce(legacy);
    render(<UsersPage />);

    await screen.findByText('Anna Meier');
    await user.click(rowOf('anna@extern.de').getByRole('button', { name: 'Disable' }));
    await waitFor(() => expect(adminUsersService.disable).toHaveBeenCalledWith('u1'));
    await user.click(rowOf('old@local.de').getByRole('button', { name: 'Enable' }));
    await waitFor(() => expect(adminUsersService.enable).toHaveBeenCalledWith('u3'));
  });

  it('deletes only after confirmation naming the email', async () => {
    const user = userEvent.setup();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    mockList([base]);
    vi.mocked(adminUsersService.remove).mockResolvedValueOnce({} as Awaited<ReturnType<typeof adminUsersService.remove>>);
    render(<UsersPage />);

    await screen.findByText('Anna Meier');
    await user.click(rowOf('anna@extern.de').getByRole('button', { name: 'Delete' }));
    expect(confirm).toHaveBeenLastCalledWith(expect.stringContaining('anna@extern.de'));
    expect(adminUsersService.remove).not.toHaveBeenCalled();

    await user.click(rowOf('anna@extern.de').getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(adminUsersService.remove).toHaveBeenCalledWith('u1'));
  });

  it('shows an error when delete fails', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    mockList([base]);
    vi.mocked(adminUsersService.remove).mockRejectedValueOnce(new Error('500'));
    render(<UsersPage />);

    await screen.findByText('Anna Meier');
    await user.click(rowOf('anna@extern.de').getByRole('button', { name: 'Delete' }));

    expect(await screen.findByText('Failed to delete user.')).toBeInTheDocument();
  });

  // B27
  it('edits the company via prompt and calls updateCompany with the trimmed value', async () => {
    const user = userEvent.setup();
    const prompt = vi.spyOn(window, 'prompt').mockReturnValue('  Neue GmbH ');
    mockList([base]);
    vi.mocked(adminUsersService.updateCompany).mockResolvedValueOnce({ ...base, company: 'Neue GmbH' });
    render(<UsersPage />);

    await screen.findByText('Anna Meier');
    await user.click(rowOf('anna@extern.de').getByRole('button', { name: 'Edit company' }));

    expect(prompt).toHaveBeenCalledWith(expect.stringContaining('Anna Meier'), 'Extern AG');
    expect(adminUsersService.updateCompany).toHaveBeenCalledWith('u1', 'Neue GmbH');
    await waitFor(() => expect(adminUsersService.list).toHaveBeenCalledTimes(2));
  });

  it('clears the company with a blank answer', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'prompt').mockReturnValue('   ');
    mockList([base]);
    vi.mocked(adminUsersService.updateCompany).mockResolvedValueOnce({ ...base, company: null });
    render(<UsersPage />);

    await screen.findByText('Anna Meier');
    await user.click(rowOf('anna@extern.de').getByRole('button', { name: 'Edit company' }));

    expect(adminUsersService.updateCompany).toHaveBeenCalledWith('u1', null);
  });

  it('does not call updateCompany when the prompt is cancelled or the value is unchanged', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'prompt').mockReturnValueOnce(null).mockReturnValueOnce('Extern AG').mockReturnValueOnce('');
    mockList([base, legacy]);
    render(<UsersPage />);

    await screen.findByText('Anna Meier');
    await user.click(rowOf('anna@extern.de').getByRole('button', { name: 'Edit company' }));
    await user.click(rowOf('anna@extern.de').getByRole('button', { name: 'Edit company' }));
    await user.click(rowOf('old@local.de').getByRole('button', { name: 'Edit company' }));

    expect(adminUsersService.updateCompany).not.toHaveBeenCalled();
  });

  it('shows an error when the company update fails', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'prompt').mockReturnValue('X');
    mockList([base]);
    vi.mocked(adminUsersService.updateCompany).mockRejectedValueOnce(new Error('500'));
    render(<UsersPage />);

    await screen.findByText('Anna Meier');
    await user.click(rowOf('anna@extern.de').getByRole('button', { name: 'Edit company' }));

    expect(await screen.findByText('Failed to update user.')).toBeInTheDocument();
  });

  it('paginates when there is more than one page', async () => {
    const user = userEvent.setup();
    mockList([base], 45);
    render(<UsersPage />);

    expect(await screen.findByText('Page 1 of 3')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: /next/i }));

    await waitFor(() => expect(adminUsersService.list).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 })));
  });
});
