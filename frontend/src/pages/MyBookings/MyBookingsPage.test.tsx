import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { renderWithRouter } from '../../test/helpers';
import { MyBookingsPage } from './MyBookingsPage';
import type { Booking } from '../../types/booking';

vi.mock('../../services/bookingService', () => ({
  bookingService: {
    getMyBookings: vi.fn(),
    cancel: vi.fn(),
    confirm: vi.fn(),
  },
}));

import { bookingService } from '../../services/bookingService';

const base: Booking = {
  id: 'b1',
  locationId: 'loc',
  locationName: 'Goeppingen',
  parkingSlotId: 's1',
  parkingSlotNumber: 'P001',
  date: '2026-06-10',
  timeSlot: 'Morning',
  status: 'Won',
  confirmedAt: null,
  createdAt: '2026-06-09T10:00:00Z',
  confirmationDeadline: null,
  fallbackReason: null,
};

function mockList(bookings: Booking[]) {
  vi.mocked(bookingService.getMyBookings).mockResolvedValue({
    bookings,
    totalCount: bookings.length,
    page: 1,
    pageSize: 20,
  });
}

/** Phase 20 WP4 2.8 / 2.7: the Confirm button honours the stored deadline; waitlisted rows show their position. */
describe('MyBookingsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows the Confirm button while the deadline is in the future', async () => {
    const inTwoHours = new Date(Date.now() + 2 * 3_600_000).toISOString();
    mockList([{ ...base, confirmationDeadline: inTwoHours }]);

    renderWithRouter(<MyBookingsPage />);

    expect(await screen.findByRole('button', { name: 'Confirm usage' })).toBeInTheDocument();
    expect(screen.queryByText('Deadline passed')).not.toBeInTheDocument();
  });

  it('hides the Confirm button once the deadline has passed and shows the pill instead', async () => {
    const tenMinutesAgo = new Date(Date.now() - 10 * 60_000).toISOString();
    mockList([{ ...base, confirmationDeadline: tenMinutesAgo }]);

    renderWithRouter(<MyBookingsPage />);

    expect(await screen.findByText('Deadline passed')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Confirm usage' })).not.toBeInTheDocument();
    // Cancelling a Won booking is still allowed until the job expires it.
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
  });

  it('shows the waitlist position and a Cancel button for waitlisted bookings', async () => {
    mockList([{ ...base, id: 'w1', status: 'Waitlisted', parkingSlotId: null, parkingSlotNumber: null, waitlistPosition: 3 }]);

    renderWithRouter(<MyBookingsPage />);

    expect(await screen.findByText('Position 3')).toBeInTheDocument();
    expect(screen.getAllByText('Waitlisted').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Confirm usage' })).not.toBeInTheDocument();
  });

  it('offers no actions on a Lost booking', async () => {
    mockList([{ ...base, id: 'l1', status: 'Lost', parkingSlotId: null, parkingSlotNumber: null }]);

    renderWithRouter(<MyBookingsPage />);

    await waitFor(() => expect(bookingService.getMyBookings).toHaveBeenCalled());
    expect(await screen.findByText('No slot', { selector: 'span' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
  });
});
