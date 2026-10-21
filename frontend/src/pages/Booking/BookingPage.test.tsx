import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithRouter, createMockAuth, mockUser, mockAdmin } from '../../test/helpers';
import type { MockAuthValue } from '../../test/helpers';
import { BookingPage } from './BookingPage';
import type { GridAvailability } from '../../types/grid';

let mockAuth: MockAuthValue;

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => mockAuth,
}));

vi.mock('../../services/bookingService', () => ({
  bookingService: {
    getWindow: vi.fn(),
    create: vi.fn(),
    createWeek: vi.fn(),
  },
}));

vi.mock('../../services/locationService', () => ({
  locationService: {
    getLocations: vi.fn(),
    getAvailability: vi.fn(),
    getGridAvailability: vi.fn(),
  },
}));

// The pickers have their own tests; here they are reduced to one button each so the
// page can be driven to the review step without depending on calendar internals.
vi.mock('../../components/CalendarPicker', () => ({
  CalendarPicker: ({ onSelect }: { onSelect: (date: string) => void }) => (
    <button type="button" onClick={() => onSelect('2026-06-10')}>pick-date</button>
  ),
}));

vi.mock('../../components/TimeSlotSelector', () => ({
  TimeSlotSelector: ({ onChange }: { onChange: (slot: 'Morning' | 'Afternoon') => void }) => (
    <button type="button" onClick={() => onChange('Morning')}>pick-slot</button>
  ),
}));

vi.mock('../../components/grid/ParkingGridView', () => ({
  ParkingGridView: () => <div data-testid="parking-grid-view" />,
}));

import { bookingService } from '../../services/bookingService';
import { locationService } from '../../services/locationService';

const grid: GridAvailability = {
  gridRows: 2,
  gridColumns: 2,
  slots: [{ id: 's1', slotNumber: 'P001', label: null, row: 0, column: 0, status: 'Free' }],
  cells: [],
};

async function goToReviewStep() {
  // `?location=` preselects the location, so the page opens on the date step.
  renderWithRouter(<BookingPage />, { initialEntries: ['/book?location=loc1'] });
  fireEvent.click(await screen.findByRole('button', { name: 'pick-date' }));
  fireEvent.click(await screen.findByRole('button', { name: 'pick-slot' }));
  // Review step is reached once the summary shows the chosen location.
  await screen.findByText('Goeppingen');
}

/** The parking-lot layout on the review step is an admin-only view. */
describe('BookingPage parking layout visibility', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(bookingService.getWindow).mockResolvedValue({
      today: '2026-06-01',
      minDate: '2026-06-01',
      maxDate: '2026-07-02',
      maxDaysAhead: 31,
      morningOpenToday: true,
      afternoonOpenToday: true,
      lotteryTime: '21:00',
      morningDeadline: '07:00',
      afternoonDeadline: '13:00',
    });
    vi.mocked(locationService.getLocations).mockResolvedValue([{ id: 'loc1', name: 'Goeppingen', totalSlots: 10 }]);
    vi.mocked(locationService.getAvailability).mockResolvedValue([]);
    vi.mocked(locationService.getGridAvailability).mockResolvedValue(grid);
  });

  it('does not fetch or render the grid for a regular user', async () => {
    mockAuth = createMockAuth({ isAuthenticated: true, user: mockUser, isAdmin: false });

    await goToReviewStep();

    expect(locationService.getGridAvailability).not.toHaveBeenCalled();
    expect(screen.queryByTestId('parking-grid-view')).not.toBeInTheDocument();
    expect(screen.queryByText(/parking layout|parkplatz-layout/i)).not.toBeInTheDocument();
  });

  it('fetches and renders the grid for an admin', async () => {
    mockAuth = createMockAuth({ isAuthenticated: true, user: mockAdmin, isAdmin: true });

    await goToReviewStep();

    await waitFor(() => expect(locationService.getGridAvailability).toHaveBeenCalledWith('loc1', '2026-06-10', 'Morning'));
    expect(await screen.findByTestId('parking-grid-view')).toBeInTheDocument();
    expect(screen.getByText(/parking layout|parkplatz-layout/i)).toBeInTheDocument();
  });
});
