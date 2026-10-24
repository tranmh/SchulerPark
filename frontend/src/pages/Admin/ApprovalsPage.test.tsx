import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApprovalsPage } from './ApprovalsPage';
import { adminUsersService, type PendingUser } from '../../services/adminUsersService';

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

function pendingUser(overrides: Partial<PendingUser> = {}): PendingUser {
  return {
    id: 'u-1', email: 'neu@schuler.de', displayName: 'Neue Person',
    emailVerified: true, createdAt: '2026-10-20T08:00:00Z', ...overrides,
  };
}

describe('ApprovalsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('lists pending registrations with their email verification state', async () => {
    service.pending.mockResolvedValueOnce({
      users: [pendingUser(), pendingUser({ id: 'u-2', email: 'offen@schuler.de', displayName: 'Offen', emailVerified: false })],
      totalCount: 2,
    });
    render(<ApprovalsPage />);

    expect(await screen.findByText('neu@schuler.de')).toBeInTheDocument();
    expect(screen.getByText('offen@schuler.de')).toBeInTheDocument();
    expect(screen.getByText('Verified')).toBeInTheDocument();
    expect(screen.getByText('Unverified')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Approve' })).toHaveLength(2);
  });

  it('shows the empty state when nobody is waiting', async () => {
    service.pending.mockResolvedValueOnce({ users: [], totalCount: 0 });
    render(<ApprovalsPage />);

    expect(await screen.findByText('No registrations waiting for approval.')).toBeInTheDocument();
  });

  it('shows a load error when the pending list fails', async () => {
    service.pending.mockRejectedValueOnce(new Error('500'));
    render(<ApprovalsPage />);

    expect(await screen.findByText('Failed to load pending users.')).toBeInTheDocument();
  });

  it('approves without confirmation and reloads the list (B9)', async () => {
    const user = userEvent.setup();
    const confirm = vi.spyOn(window, 'confirm');
    service.pending
      .mockResolvedValueOnce({ users: [pendingUser()], totalCount: 1 })
      .mockResolvedValueOnce({ users: [], totalCount: 0 });
    service.decide.mockResolvedValueOnce(pendingUser());
    render(<ApprovalsPage />);

    await user.click(await screen.findByRole('button', { name: 'Approve' }));

    expect(service.decide).toHaveBeenCalledWith('u-1', true);
    expect(confirm).not.toHaveBeenCalled();
    expect(await screen.findByText('No registrations waiting for approval.')).toBeInTheDocument();
    expect(service.pending).toHaveBeenCalledTimes(2);
  });

  it('rejects after the admin confirms (B9)', async () => {
    const user = userEvent.setup();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    service.pending
      .mockResolvedValueOnce({ users: [pendingUser()], totalCount: 1 })
      .mockResolvedValueOnce({ users: [], totalCount: 0 });
    service.decide.mockResolvedValueOnce(pendingUser());
    render(<ApprovalsPage />);

    await user.click(await screen.findByRole('button', { name: 'Reject' }));

    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('neu@schuler.de'));
    expect(service.decide).toHaveBeenCalledWith('u-1', false);
  });

  it('does nothing when the reject confirmation is cancelled', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    service.pending.mockResolvedValueOnce({ users: [pendingUser()], totalCount: 1 });
    render(<ApprovalsPage />);

    await user.click(await screen.findByRole('button', { name: 'Reject' }));

    expect(service.decide).not.toHaveBeenCalled();
    expect(service.pending).toHaveBeenCalledTimes(1);
  });

  it('shows an error and keeps the row when the decision fails (e.g. double approval)', async () => {
    const user = userEvent.setup();
    service.pending.mockResolvedValueOnce({ users: [pendingUser()], totalCount: 1 });
    service.decide.mockRejectedValueOnce({ response: { status: 409, data: { detail: 'Already decided.' } } });
    render(<ApprovalsPage />);

    await user.click(await screen.findByRole('button', { name: 'Approve' }));

    expect(await screen.findByText('Failed to save the decision.')).toBeInTheDocument();
    expect(screen.getByText('neu@schuler.de')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled());
  });

  it('disables the row buttons while a decision is in flight', async () => {
    const user = userEvent.setup();
    service.pending.mockResolvedValueOnce({
      users: [pendingUser(), pendingUser({ id: 'u-2', email: 'zwei@schuler.de' })],
      totalCount: 2,
    });
    service.decide.mockReturnValue(new Promise(() => {}));
    render(<ApprovalsPage />);

    const approveButtons = await screen.findAllByRole('button', { name: 'Approve' });
    await user.click(approveButtons[0]);

    const rejectButtons = screen.getAllByRole('button', { name: 'Reject' });
    expect(approveButtons[0]).toBeDisabled();
    expect(rejectButtons[0]).toBeDisabled();
    expect(approveButtons[1]).toBeEnabled();
  });
});
