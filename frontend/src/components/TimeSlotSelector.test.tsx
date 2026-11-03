import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TimeSlotSelector } from './TimeSlotSelector';
import type { SlotDemand, TimeSlot } from '../types/booking';

type Props = Parameters<typeof TimeSlotSelector>[0];

const demand = (d: Partial<SlotDemand>): SlotDemand => ({ available: 0, total: 20, pending: 0, waitlist: 0, lotteryRan: false, ...d });

function setup(props: Partial<Props> = {}) {
  const onChange = vi.fn<(s: TimeSlot) => void>();
  render(<TimeSlotSelector value={null} onChange={onChange} {...props} />);
  return onChange;
}

const morning = () => screen.getByRole('button', { name: /morning/i });
const afternoon = () => screen.getByRole('button', { name: /afternoon/i });

describe('TimeSlotSelector', () => {
  it('selects a slot on click', async () => {
    const user = userEvent.setup();
    const onChange = setup();

    await user.click(afternoon());

    expect(onChange).toHaveBeenCalledWith('Afternoon');
    expect(morning()).toBeEnabled();
  });

  it('shows pre-lottery demand with the lottery time', () => {
    setup({ demand: { morning: demand({ pending: 3 }) }, lotteryTime: '20:30' });

    expect(screen.getByText('3 requests for 20 slots')).toBeInTheDocument();
    expect(screen.getByText('Lottery at 20:30')).toBeInTheDocument();
    expect(morning()).toBeEnabled();
  });

  it('shows free slots after the lottery', () => {
    setup({ demand: { morning: demand({ lotteryRan: true, available: 1 }), afternoon: demand({ lotteryRan: true, available: 11 }) } });

    expect(screen.getByText('1 slot available')).toBeInTheDocument();
    expect(screen.getByText('11 slots available')).toBeInTheDocument();
  });

  it('disables a slot that is full after the lottery or has no capacity', async () => {
    const user = userEvent.setup();
    const onChange = setup({
      demand: { morning: demand({ lotteryRan: true, available: 0 }), afternoon: demand({ total: 0 }) },
    });

    expect(morning()).toBeDisabled();
    expect(afternoon()).toBeDisabled();
    expect(screen.getAllByText('Full')).toHaveLength(2);
    await user.click(morning());
    expect(onChange).not.toHaveBeenCalled();
  });

  it('keeps a pre-lottery slot selectable even when oversubscribed', () => {
    setup({ demand: { morning: demand({ pending: 25 }) } });

    expect(morning()).toBeEnabled();
  });

  it('disables slots closed for today and explains why', () => {
    setup({ closedSlots: ['Morning'] });

    expect(morning()).toBeDisabled();
    expect(morning()).toHaveAttribute('title', 'This time slot has already ended today');
    expect(afternoon()).toBeEnabled();
  });
});
