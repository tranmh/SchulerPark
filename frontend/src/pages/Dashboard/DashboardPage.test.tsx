import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { DashboardPage } from './DashboardPage';
import { renderWithRouter, createMockAuth, mockUser } from '../../test/helpers';
import type { MockAuthValue } from '../../test/helpers';
import type { Booking, BookingWindowResponse, Location } from '../../types/booking';

let mockAuth: MockAuthValue;

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => mockAuth,
}));

vi.mock('../../services/bookingService', () => ({
  bookingService: { getMyBookings: vi.fn(), getWindow: vi.fn() },
}));

vi.mock('../../services/locationService', () => ({
  locationService: { getLocations: vi.fn() },
}));

import { bookingService } from '../../services/bookingService';
import { locationService } from '../../services/locationService';

const booking: Booking = {
  id: 'b1',
  locationId: 'loc1',
  locationName: 'Goeppingen',
  parkingSlotId: 's1',
  parkingSlotNumber: 'P007',
  date: '2026-11-10',
  timeSlot: 'Afternoon',
  status: 'Confirmed',
  confirmedAt: null,
  createdAt: '2026-11-01T10:00:00Z',
  confirmationDeadline: null,
  fallbackReason: null,
};

const locations: Location[] = [
  { id: 'loc1', name: 'Goeppingen Nord', totalSlots: 12 },
  { id: 'loc2', name: 'Erfurt', totalSlots: 0 },
];

function mockData(bookings: Booking[], locs: Location[] = locations) {
  vi.mocked(bookingService.getMyBookings).mockResolvedValue({ bookings, totalCount: bookings.length, page: 1, pageSize: 5 });
  vi.mocked(locationService.getLocations).mockResolvedValue(locs);
}

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAuth = createMockAuth({ isAuthenticated: true, user: mockUser });
    vi.mocked(bookingService.getWindow).mockResolvedValue({ lotteryTime: '20:15' } as BookingWindowResponse);
  });

  it('requests the next five bookings from today and greets the user by first name', async () => {
    mockData([booking]);
    renderWithRouter(<DashboardPage />);

    expect(await screen.findByRole('heading', { level: 1, name: /, Test\.$/ })).toBeInTheDocument();
    expect(bookingService.getMyBookings).toHaveBeenCalledWith({ pageSize: 5, from: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/) });
  });

  it('lists upcoming bookings with location, slot and status', async () => {
    mockData([booking]);
    renderWithRouter(<DashboardPage />);

    expect(await screen.findByText('Goeppingen · Afternoon')).toBeInTheDocument();
    expect(screen.getByText('13:00 – 19:00', { exact: false })).toBeInTheDocument();
    expect(screen.getByText(/P007/)).toBeInTheDocument();
    expect(screen.getByText('Confirmed')).toBeInTheDocument();
    expect(screen.queryByText('No upcoming bookings')).not.toBeInTheDocument();
  });

  it('highlights Won bookings with their confirmation deadline', async () => {
    mockData([{ ...booking, status: 'Won', confirmationDeadline: '2026-11-10T08:30:00Z' }]);
    renderWithRouter(<DashboardPage />);

    expect(await screen.findByText(/booking waiting for confirmation/)).toBeInTheDocument();
    // Stat card plus the mobile and desktop row chips; 08:30Z is 09:30 in the pinned Europe/Berlin zone.
    expect(screen.getAllByText(/Confirm by/)).toHaveLength(3);
    expect(screen.getAllByText(/09:30/).length).toBeGreaterThanOrEqual(2);
    expect(screen.queryByText('Lottery cutoff')).not.toBeInTheDocument();
  });

  it('shows the empty states when nothing is booked and there are no locations', async () => {
    mockData([], []);
    renderWithRouter(<DashboardPage />);

    expect(await screen.findByText('No upcoming bookings')).toBeInTheDocument();
    expect(screen.getByText('No locations available')).toBeInTheDocument();
  });

  it('falls back to the empty state when loading fails', async () => {
    vi.mocked(bookingService.getMyBookings).mockRejectedValue(new Error('offline'));
    vi.mocked(locationService.getLocations).mockResolvedValue(locations);
    renderWithRouter(<DashboardPage />);

    expect(await screen.findByText('No upcoming bookings')).toBeInTheDocument();
    expect(bookingService.getWindow).not.toHaveBeenCalled();
  });

  it('shows the configured lottery time and quick-book links to each location', async () => {
    mockData([booking]);
    renderWithRouter(<DashboardPage />);

    expect(await screen.findByText('20:15')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Goeppingen Nord/ })).toHaveAttribute('href', '/booking?location=loc1');
  });

  it('keeps the default lottery time when the window call fails', async () => {
    vi.mocked(bookingService.getWindow).mockRejectedValue(new Error('500'));
    mockData([booking]);
    renderWithRouter(<DashboardPage />);

    expect(await screen.findByText('Lottery cutoff')).toBeInTheDocument();
    expect(screen.queryByText('20:15')).not.toBeInTheDocument();
  });
});
