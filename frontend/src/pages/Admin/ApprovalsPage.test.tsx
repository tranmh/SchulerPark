import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApprovalsPage } from './ApprovalsPage';
import type { PendingUser } from '../../services/adminUsersService';

vi.mock('../../services/adminUsersService', () => ({
  adminUsersService: { pending: vi.fn(), decide: vi.fn() },
}));

import { adminUsersService } from '../../services/adminUsersService';

const anna: PendingUser = {
  id: 'u1',
  email: 'anna@extern.de',
  displayName: 'Anna Meier',
  firstName: 'Anna',
  lastName: 'Meier',
  company: 'Extern AG',
  emailVerified: true,
  createdAt: '2026-10-01T08:00:00Z',
};

const bob: PendingUser = {
  ...anna,
  id: 'u2',
  email: 'bob@other.de',
  displayName: 'Bob Kurz',
  firstName: 'Bob',
  lastName: 'Kurz',
  company: null,
  emailVerified: false,
};

function mockPending(...lists: PendingUser[][]) {
  for (const users of lists) {
    vi.mocked(adminUsersService.pending).mockResolvedValueOnce({ users, totalCount: users.length });
  }
}

const rowOf = (email: string) => screen.getByText(email).closest('tr') as HTMLElement;

describe('ApprovalsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  // B26
  it('lists pending users with name, email, company (or a dash) and email status', async () => {
    mockPending([anna, bob]);
    render(<ApprovalsPage />);

    expect(await screen.findByText('Anna Meier')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Company' })).toBeInTheDocument();

    const a = within(rowOf('anna@extern.de'));
    expect(a.getByText('Anna Meier')).toBeInTheDocument();
    expect(a.getByText('Extern AG')).toBeInTheDocument();
    expect(a.getByText('Verified')).toBeInTheDocument();
    expect(a.getByRole('button', { name: 'Approve' })).toBeInTheDocument();
    expect(a.getByRole('button', { name: 'Reject' })).toBeInTheDocument();

    const b = within(rowOf('bob@other.de'));
    expect(b.getByText('—')).toBeInTheDocument();
    expect(b.getByText('Unverified')).toBeInTheDocument();
  });

  it('shows the empty state when nobody is waiting', async () => {
    mockPending([]);
    render(<ApprovalsPage />);

    expect(await screen.findByText('No registrations waiting for approval.')).toBeInTheDocument();
  });

  it('shows an error when loading fails', async () => {
    vi.mocked(adminUsersService.pending).mockRejectedValueOnce(new Error('boom'));
    render(<ApprovalsPage />);

    expect(await screen.findByText('Failed to load pending users.')).toBeInTheDocument();
  });

  it('approves without confirmation and reloads the list', async () => {
    const user = userEvent.setup();
    const confirm = vi.spyOn(window, 'confirm');
    mockPending([anna], []);
    vi.mocked(adminUsersService.decide).mockResolvedValueOnce({ ...anna });
    render(<ApprovalsPage />);

    await user.click(await screen.findByRole('button', { name: 'Approve' }));

    expect(adminUsersService.decide).toHaveBeenCalledWith('u1', true);
    expect(confirm).not.toHaveBeenCalled();
    expect(await screen.findByText('No registrations waiting for approval.')).toBeInTheDocument();
    expect(adminUsersService.pending).toHaveBeenCalledTimes(2);
  });

  // B26
  it('asks for confirmation naming name, company and email before rejecting', async () => {
    const user = userEvent.setup();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    mockPending([anna], []);
    vi.mocked(adminUsersService.decide).mockResolvedValueOnce({ ...anna });
    render(<ApprovalsPage />);

    await user.click(await screen.findByRole('button', { name: 'Reject' }));

    expect(confirm).toHaveBeenCalledTimes(1);
    const text = confirm.mock.calls[0][0] as string;
    expect(text).toContain('Anna Meier');
    expect(text).toContain('Extern AG');
    expect(text).toContain('anna@extern.de');
    expect(adminUsersService.decide).toHaveBeenCalledWith('u1', false);
  });

  it('uses a dash for the company in the reject confirmation when none is set', async () => {
    const user = userEvent.setup();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    mockPending([bob]);
    render(<ApprovalsPage />);

    await user.click(await screen.findByRole('button', { name: 'Reject' }));

    expect(confirm.mock.calls[0][0]).toContain('(—, bob@other.de)');
  });

  it('does nothing when the reject confirmation is cancelled', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    mockPending([anna]);
    render(<ApprovalsPage />);

    await user.click(await screen.findByRole('button', { name: 'Reject' }));

    expect(adminUsersService.decide).not.toHaveBeenCalled();
    expect(screen.getByText('Anna Meier')).toBeInTheDocument();
  });

  it('shows an error and keeps the row when the decision fails', async () => {
    const user = userEvent.setup();
    mockPending([anna]);
    vi.mocked(adminUsersService.decide).mockRejectedValueOnce(new Error('500'));
    render(<ApprovalsPage />);

    await user.click(await screen.findByRole('button', { name: 'Approve' }));

    expect(await screen.findByText('Failed to save the decision.')).toBeInTheDocument();
    expect(screen.getByText('Anna Meier')).toBeInTheDocument();
    await waitFor(() => expect(within(rowOf('anna@extern.de')).getByRole('button', { name: 'Approve' })).toBeEnabled());
  });
});
