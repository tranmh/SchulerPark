import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BookingsPage } from './BookingsPage';
import type { AdminBooking, AdminLocation } from '../../types/admin';

vi.mock('../../services/adminService', () => ({
  adminService: {
    getLocations: vi.fn(),
    getBookings: vi.fn(),
  },
}));

import { adminService } from '../../services/adminService';

const loc = (id: string, name: string): AdminLocation => ({
  id, name, isActive: true, defaultAlgorithm: 'PureRandom', totalSlots: 2, activeSlots: 2,
});

const booking = (id: string, extra: Partial<AdminBooking> = {}): AdminBooking => ({
  id,
  userId: 'u1',
  userEmail: 'anna@extern.de',
  userDisplayName: 'Anna Meier',
  locationId: 'l1',
  locationName: 'Nord',
  parkingSlotId: 's1',
  parkingSlotNumber: 'P001',
  date: '2026-11-10',
  timeSlot: 'Morning',
  status: 'Won',
  confirmedAt: null,
  createdAt: '2026-11-01T08:00:00Z',
  cancelledAt: null,
  cancelReason: null,
  cancelledByUserId: null,
  autoConfirmReason: null,
  ...extra,
});

function mockBookings(bookings: AdminBooking[], totalCount = bookings.length) {
  vi.mocked(adminService.getBookings).mockResolvedValue({ bookings, totalCount, page: 1, pageSize: 20 });
}

const defaultQuery = { locationId: undefined, status: undefined, from: undefined, to: undefined, page: 1, pageSize: 20 };

/** The "<n> booking(s) match your filters." line directly under the heading (not the empty-state row). */
const counter = () => screen.getByRole('heading', { name: 'All bookings' }).nextElementSibling as HTMLElement;

describe('BookingsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(adminService.getLocations).mockResolvedValue([loc('l1', 'Nord'), loc('l2', 'Süd')]);
    mockBookings([
      booking('b1'),
      booking('b2', { userDisplayName: 'Max Muster', userEmail: 'max@andritz.com', parkingSlotNumber: null, status: 'Pending', date: '2026-11-11', timeSlot: 'Afternoon', locationName: 'Süd' }),
    ]);
  });

  it('loads bookings unfiltered on the first page and lists them', async () => {
    render(<BookingsPage />);

    expect(screen.getByRole('heading', { name: 'All bookings' })).toBeInTheDocument();
    expect(screen.getByText('Loading…')).toBeInTheDocument();

    const anna = within((await screen.findByText('Anna Meier')).closest('tr') as HTMLElement);
    expect(adminService.getBookings).toHaveBeenCalledWith(defaultQuery);
    expect(anna.getByText('2026-11-10')).toBeInTheDocument();
    expect(anna.getByText('Morning')).toBeInTheDocument();
    expect(anna.getByText('Nord')).toBeInTheDocument();
    expect(anna.getByText('anna@extern.de')).toBeInTheDocument();
    expect(anna.getByText('P001')).toBeInTheDocument();
    // The badge carries the localized label (e.g. "Won — Confirm"); only the status itself matters here.
    expect(anna.getByText(/^Won/)).toBeInTheDocument();

    const max = within(screen.getByText('Max Muster').closest('tr') as HTMLElement);
    expect(max.getByText('—')).toBeInTheDocument();
    expect(max.getByText(/^Pending/)).toBeInTheDocument();
    expect(counter()).toHaveTextContent('2 bookings match your filters.');
    expect(screen.queryByText('Loading…')).not.toBeInTheDocument();
  });

  it('uses the singular when exactly one booking matches', async () => {
    mockBookings([booking('b1')]);
    render(<BookingsPage />);

    await screen.findByText('Anna Meier');
    expect(counter()).toHaveTextContent('1 booking match your filters.');
  });

  it('shows the empty state when nothing matches', async () => {
    mockBookings([]);
    render(<BookingsPage />);

    expect(await screen.findByText('No bookings match your filters.')).toBeInTheDocument();
    expect(counter()).toHaveTextContent('0 bookings match your filters.');
  });

  it('shows an error when bookings cannot be loaded', async () => {
    vi.mocked(adminService.getBookings).mockRejectedValue(new Error('500'));
    render(<BookingsPage />);

    expect(await screen.findByText('Failed to load bookings.')).toBeInTheDocument();
    expect(screen.queryByText('Loading…')).not.toBeInTheDocument();
  });

  it('still renders the bookings when the location list fails to load', async () => {
    vi.mocked(adminService.getLocations).mockRejectedValue(new Error('500'));
    render(<BookingsPage />);

    expect(await screen.findByText('Anna Meier')).toBeInTheDocument();
    expect(within(screen.getByLabelText('Location')).getAllByRole('option')).toHaveLength(1);
  });

  it('offers all locations and every status in the filters', async () => {
    render(<BookingsPage />);

    await screen.findByText('Anna Meier');
    const locations = within(screen.getByLabelText('Location')).getAllByRole('option').map((o) => o.textContent);
    expect(locations).toEqual(['All locations', 'Nord', 'Süd']);
    const statuses = within(screen.getByLabelText('Status')).getAllByRole('option').map((o) => o.textContent);
    expect(statuses).toEqual(['All', 'Pending', 'Won', 'Waitlisted', 'Lost', 'Confirmed', 'Cancelled', 'Expired']);
  });

  it('reloads with the chosen location, status and date range', async () => {
    const user = userEvent.setup();
    render(<BookingsPage />);
    await screen.findByText('Anna Meier');

    await user.selectOptions(screen.getByLabelText('Location'), 'l2');
    await waitFor(() => expect(adminService.getBookings).toHaveBeenLastCalledWith({ ...defaultQuery, locationId: 'l2' }));

    await user.selectOptions(screen.getByLabelText('Status'), 'Won');
    await waitFor(() => expect(adminService.getBookings).toHaveBeenLastCalledWith({ ...defaultQuery, locationId: 'l2', status: 'Won' }));

    await user.type(screen.getByLabelText('From'), '2026-11-01');
    await user.type(screen.getByLabelText('To'), '2026-11-30');
    await waitFor(() => expect(adminService.getBookings).toHaveBeenLastCalledWith({
      ...defaultQuery, locationId: 'l2', status: 'Won', from: '2026-11-01', to: '2026-11-30',
    }));

    // Back to "All" sends no status and clearing the location sends none either.
    await user.selectOptions(screen.getByLabelText('Status'), 'All');
    await user.selectOptions(screen.getByLabelText('Location'), '');
    await waitFor(() => expect(adminService.getBookings).toHaveBeenLastCalledWith({
      ...defaultQuery, from: '2026-11-01', to: '2026-11-30',
    }));
  });

  describe('pagination', () => {
    it('is hidden when everything fits on one page', async () => {
      mockBookings([booking('b1')], 20);
      render(<BookingsPage />);

      await screen.findByText('Anna Meier');
      expect(screen.queryByRole('button', { name: /Next/ })).not.toBeInTheDocument();
      expect(screen.queryByText(/Page/)).not.toBeInTheDocument();
    });

    it('pages forward and back within the bounds', async () => {
      const user = userEvent.setup();
      mockBookings([booking('b1')], 45);
      render(<BookingsPage />);

      await screen.findByText('Anna Meier');
      expect(screen.getByText(/Page/)).toHaveTextContent('Page 1 of 3');
      expect(screen.getByRole('button', { name: /Previous/ })).toBeDisabled();

      await user.click(screen.getByRole('button', { name: /Next/ }));
      await waitFor(() => expect(adminService.getBookings).toHaveBeenLastCalledWith({ ...defaultQuery, page: 2 }));
      expect(screen.getByText(/Page/)).toHaveTextContent('Page 2 of 3');
      expect(screen.getByRole('button', { name: /Previous/ })).toBeEnabled();

      await user.click(screen.getByRole('button', { name: /Next/ }));
      await waitFor(() => expect(screen.getByText(/Page/)).toHaveTextContent('Page 3 of 3'));
      expect(screen.getByRole('button', { name: /Next/ })).toBeDisabled();

      await user.click(screen.getByRole('button', { name: /Previous/ }));
      await waitFor(() => expect(adminService.getBookings).toHaveBeenLastCalledWith({ ...defaultQuery, page: 2 }));
    });

    it('resets to the first page when a filter changes', async () => {
      const user = userEvent.setup();
      mockBookings([booking('b1')], 45);
      render(<BookingsPage />);

      await screen.findByText('Anna Meier');
      await user.click(screen.getByRole('button', { name: /Next/ }));
      await waitFor(() => expect(screen.getByText(/Page/)).toHaveTextContent('Page 2 of 3'));

      await user.selectOptions(screen.getByLabelText('Status'), 'Confirmed');

      await waitFor(() => expect(adminService.getBookings).toHaveBeenLastCalledWith({ ...defaultQuery, status: 'Confirmed', page: 1 }));
      expect(screen.getByText(/Page/)).toHaveTextContent('Page 1 of 3');
    });
  });
});
