import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
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

/** The parking-lot layout is an admin tool (Admin > Grid layout); the booking flow never shows it. */
describe('BookingPage review step', () => {
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

  it.each([
    ['a regular user', () => createMockAuth({ isAuthenticated: true, user: mockUser, isAdmin: false })],
    ['an admin', () => createMockAuth({ isAuthenticated: true, user: mockAdmin, isAdmin: true })],
  ])('never fetches or renders the parking layout for %s', async (_who, auth) => {
    mockAuth = auth();

    await goToReviewStep();

    expect(locationService.getGridAvailability).not.toHaveBeenCalled();
    expect(screen.queryByTestId('parking-grid-view')).not.toBeInTheDocument();
    expect(screen.queryByText(/parking layout|parkplatz-layout/i)).not.toBeInTheDocument();
  });

  it('shows the short "what happens next" steps', async () => {
    mockAuth = createMockAuth({ isAuthenticated: true, user: mockUser, isAdmin: false });

    await goToReviewStep();

    const steps = screen.getAllByRole('listitem').map((li) => li.textContent ?? '');
    expect(steps).toHaveLength(4);
    expect(steps[1]).toContain('21:00');
    expect(steps[2]).toContain('07:00');
    expect(steps[2]).toContain('13:00');
  });
});
