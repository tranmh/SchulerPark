import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { SlotProblemModal } from './SlotProblemModal';
import type { Booking } from '../../types/booking';

vi.mock('../../services/feedbackService', () => ({
  feedbackService: { submit: vi.fn() },
}));

import { feedbackService } from '../../services/feedbackService';

const booking: Booking = {
  id: 'b1',
  locationId: 'loc',
  locationName: 'Goeppingen',
  parkingSlotId: 's1',
  parkingSlotNumber: 'P097',
  date: '2026-06-10',
  timeSlot: 'Morning',
  status: 'Confirmed',
  confirmedAt: null,
  createdAt: '2026-06-09T10:00:00Z',
  confirmationDeadline: null,
  fallbackReason: null,
};

describe('SlotProblemModal', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('sends an occupied report with the composed plate and comment', async () => {
    vi.mocked(feedbackService.submit).mockResolvedValue(undefined);
    render(<SlotProblemModal booking={booking} onClose={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: 'Spot is occupied' }));
    fireEvent.click(screen.getByRole('radio', { name: /Electric/ }));
    fireEvent.change(screen.getByLabelText('District code'), { target: { value: 'gp1' } });
    fireEvent.change(screen.getByLabelText('Letters'), { target: { value: 'ab' } });
    fireEvent.change(screen.getByLabelText('Digits'), { target: { value: '12x34' } });
    fireEvent.change(screen.getByLabelText('Comment'), { target: { value: '  since 8am ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Report problem' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Thank you');
    expect(feedbackService.submit).toHaveBeenCalledWith({
      category: 'SlotOccupied',
      bookingId: 'b1',
      plateCountry: 'DE',
      plateType: 'Electric',
      plateText: 'GP-AB 1234 E',
      comment: 'since 8am',
    });
  });

  it('sends no plate for other problems and shows server errors inline', async () => {
    vi.mocked(feedbackService.submit).mockRejectedValue({
      isAxiosError: true,
      response: { data: { code: 'feedback_booking_not_today' } },
    });
    render(<SlotProblemModal booking={booking} onClose={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: 'EV charger fault' }));
    expect(screen.queryByLabelText('District code')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Report problem' }));

    expect(await screen.findByText("Problems can only be reported for today's booking.")).toBeInTheDocument();
    expect(feedbackService.submit).toHaveBeenCalledWith({ category: 'ChargerFault', bookingId: 'b1', comment: undefined });
  });
});
