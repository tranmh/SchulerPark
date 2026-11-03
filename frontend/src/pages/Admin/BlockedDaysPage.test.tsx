import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BlockedDaysPage } from './BlockedDaysPage';
import type { AdminBlockedDay, AdminLocation } from '../../types/admin';

vi.mock('../../utils/berlinTime', () => ({ todayInBerlin: () => '2026-11-03' }));

vi.mock('../../services/adminService', () => ({
  adminService: {
    getLocations: vi.fn(),
    getBlockedDays: vi.fn(),
    removeBlockedDay: vi.fn(),
    countBookings: vi.fn(),
    createBlockedDay: vi.fn(),
  },
}));

import { adminService } from '../../services/adminService';

const loc = (id: string, name: string, isActive = true): AdminLocation => ({
  id, name, isActive, defaultAlgorithm: 'Random', totalSlots: 10, activeSlots: 10,
});

const block = (id: string, date: string, extra: Partial<AdminBlockedDay> = {}): AdminBlockedDay => ({
  id, locationId: 'l1', locationName: 'Nord', parkingSlotId: null, slotNumber: null, date, reason: null, createdAt: '2026-11-01T00:00:00Z', ...extra,
});

const day = (n: number) => screen.getByRole('button', { name: String(n) });

describe('BlockedDaysPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(adminService.getLocations).mockResolvedValue([loc('l0', 'Old', false), loc('l1', 'Nord'), loc('l2', 'Süd')]);
    vi.mocked(adminService.getBlockedDays).mockResolvedValue([
      block('b1', '2026-11-20', { reason: 'Holiday' }),
      block('b2', '2026-11-21', { parkingSlotId: 's1', slotNumber: 'P1' }),
    ]);
    vi.mocked(adminService.countBookings).mockResolvedValue(0);
  });

  it('offers only active locations and loads the first one', async () => {
    render(<BlockedDaysPage />);

    const select = await screen.findByLabelText('Location');
    expect(within(select).queryByRole('option', { name: 'Old' })).not.toBeInTheDocument();
    expect(select).toHaveValue('l1');
    await waitFor(() => expect(adminService.getBlockedDays).toHaveBeenCalledWith('l1'));
  });

  it('lists only location-wide blocks with their reason', async () => {
    render(<BlockedDaysPage />);

    const rows = await screen.findAllByTestId('blocked-day-row');
    expect(rows).toHaveLength(1);
    expect(within(rows[0]).getByText('2026-11-20')).toBeInTheDocument();
    expect(within(rows[0]).getByText('Holiday')).toBeInTheDocument();
    expect(day(20)).toBeDisabled();
  });

  it('shows the empty list for a location without blocks', async () => {
    vi.mocked(adminService.getBlockedDays).mockResolvedValue([]);
    render(<BlockedDaysPage />);

    expect(await screen.findByText('No blocked days for this location.')).toBeInTheDocument();
  });

  it('blocks a day with a reason after previewing the affected bookings', async () => {
    const user = userEvent.setup();
    vi.mocked(adminService.countBookings).mockResolvedValue(2);
    vi.mocked(adminService.createBlockedDay).mockResolvedValue({
      blockedDay: block('b3', '2026-11-25'),
      impact: { affected: 2, reassigned: 0, waitlisted: 0, cancelled: 2 },
    });
    render(<BlockedDaysPage />);

    await screen.findAllByTestId('blocked-day-row');
    await user.click(day(25));

    expect(adminService.countBookings).toHaveBeenCalledWith({
      locationId: 'l1', from: '2026-11-25', to: '2026-11-25', status: 'Pending,Won,Confirmed,Waitlisted',
    });
    expect(await screen.findByTestId('block-impact-preview')).toHaveTextContent('2 bookings will be cancelled');

    await user.type(screen.getByLabelText('Reason (optional)'), 'Works');
    await user.click(screen.getByRole('button', { name: 'Block day' }));

    expect(adminService.createBlockedDay).toHaveBeenCalledWith({ locationId: 'l1', date: '2026-11-25', reason: 'Works' });
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Block day' })).not.toBeInTheDocument());
    expect(adminService.getBlockedDays).toHaveBeenCalledTimes(2);
  });

  it('sends no reason when it is left empty, and cancel closes the dialog', async () => {
    const user = userEvent.setup();
    vi.mocked(adminService.createBlockedDay).mockResolvedValue({
      blockedDay: block('b3', '2026-11-26'),
      impact: { affected: 0, reassigned: 0, waitlisted: 0, cancelled: 0 },
    });
    render(<BlockedDaysPage />);

    await screen.findAllByTestId('blocked-day-row');
    await user.click(day(26));
    expect(await screen.findByText('No existing bookings are affected.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('button', { name: 'Block day' })).not.toBeInTheDocument();

    await user.click(day(26));
    await user.click(screen.getByRole('button', { name: 'Block day' }));
    expect(adminService.createBlockedDay).toHaveBeenCalledWith({ locationId: 'l1', date: '2026-11-26', reason: undefined });
  });

  it('shows an error when blocking fails', async () => {
    const user = userEvent.setup();
    vi.mocked(adminService.createBlockedDay).mockRejectedValue({ response: { status: 409 } });
    render(<BlockedDaysPage />);

    await screen.findAllByTestId('blocked-day-row');
    await user.click(day(25));
    await user.click(screen.getByRole('button', { name: 'Block day' }));

    expect(await screen.findByText('Failed to block day.')).toBeInTheDocument();
  });

  it('removes a block from the list', async () => {
    const user = userEvent.setup();
    vi.mocked(adminService.removeBlockedDay).mockResolvedValue(undefined as never);
    render(<BlockedDaysPage />);

    const [row] = await screen.findAllByTestId('blocked-day-row');
    vi.mocked(adminService.getBlockedDays).mockResolvedValue([]);
    await user.click(within(row).getByRole('button', { name: 'Remove' }));

    expect(adminService.removeBlockedDay).toHaveBeenCalledWith('b1');
    expect(await screen.findByText('No blocked days for this location.')).toBeInTheDocument();
  });

  it('aqaa: open bug, see report — clicking a blocked date on the calendar unblocks it', async () => {
    // aqaa: suspected bug: the page says "Click a date on the calendar to block or unblock it" and
    // handleDateClick removes an existing location-wide block, but CalendarPicker disables blocked
    // dates, so the unblock path is unreachable from the calendar.
    const user = userEvent.setup();
    vi.mocked(adminService.removeBlockedDay).mockResolvedValue(undefined as never);
    render(<BlockedDaysPage />);

    await screen.findAllByTestId('blocked-day-row');
    await user.click(day(20));

    expect(adminService.removeBlockedDay).toHaveBeenCalledWith('b1');
  });

  it.skip('aqaa: open bug, see report — a failing Remove in the list shows an error', async () => {
    // aqaa: suspected bug: the list's Remove handler has no try/catch (unlike the calendar path,
    // which shows "Failed to remove block."), so a failed delete is an unhandled rejection and
    // the admin gets no feedback.
    const user = userEvent.setup();
    vi.mocked(adminService.removeBlockedDay).mockRejectedValue(new Error('500'));
    render(<BlockedDaysPage />);

    const [row] = await screen.findAllByTestId('blocked-day-row');
    await user.click(within(row).getByRole('button', { name: 'Remove' }));

    expect(await screen.findByText('Failed to remove block.')).toBeInTheDocument();
  });

  it('reloads the blocks when another location is chosen', async () => {
    const user = userEvent.setup();
    render(<BlockedDaysPage />);

    await user.selectOptions(await screen.findByLabelText('Location'), 'l2');

    await waitFor(() => expect(adminService.getBlockedDays).toHaveBeenCalledWith('l2'));
  });

  it('shows an error when locations cannot be loaded', async () => {
    vi.mocked(adminService.getLocations).mockRejectedValue(new Error('500'));
    render(<BlockedDaysPage />);

    expect(await screen.findByText('Failed to load locations.')).toBeInTheDocument();
  });
});
