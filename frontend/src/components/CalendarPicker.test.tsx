import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CalendarPicker } from './CalendarPicker';

// Pin "today" (a Tuesday) so the month view and past/future logic are deterministic.
vi.mock('../utils/berlinTime', () => ({ todayInBerlin: () => '2026-11-03' }));

type Props = Parameters<typeof CalendarPicker>[0];

function setup(props: Partial<Props> = {}) {
  const onSelect = vi.fn<(d: string) => void>();
  render(<CalendarPicker selectedDate={null} onSelect={onSelect} blockedDates={new Set()} {...props} />);
  return onSelect;
}

const day = (n: number) => screen.getByRole('button', { name: String(n) });

describe('CalendarPicker', () => {
  it('opens on the current Berlin month and marks today', () => {
    setup();

    expect(screen.getByText('November 2026')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Today 3' })).toBeEnabled();
    expect(day(30)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '31' })).not.toBeInTheDocument();
  });

  it('disables days before today and selects a future day', async () => {
    const user = userEvent.setup();
    const onSelect = setup();

    expect(day(2)).toBeDisabled();
    await user.click(day(10));

    expect(onSelect).toHaveBeenCalledWith('2026-11-10');
  });

  it('honours explicit minDate and maxDate boundaries (inclusive)', () => {
    setup({ minDate: '2026-11-05', maxDate: '2026-11-20' });

    expect(day(4)).toBeDisabled();
    expect(day(5)).toBeEnabled();
    expect(day(20)).toBeEnabled();
    expect(day(21)).toBeDisabled();
  });

  it('disables blocked days', async () => {
    const user = userEvent.setup();
    const onSelect = setup({ blockedDates: new Set(['2026-11-12']) });

    expect(day(12)).toBeDisabled();
    await user.click(day(12));
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('keeps blocked days clickable when blockedSelectable is set, disabled otherwise', async () => {
    const user = userEvent.setup();
    const onSelect = setup({ blockedDates: new Set(['2026-11-12']), blockedSelectable: true });

    expect(day(12)).toBeEnabled();
    expect(day(12)).toHaveClass('bg-rose-50', 'text-rose-700');
    expect(day(12)).not.toHaveClass('line-through');
    await user.click(day(12));
    expect(onSelect).toHaveBeenCalledWith('2026-11-12');
  });

  it('with blockedSelectable false (default) a blocked day is disabled and not selectable', async () => {
    const user = userEvent.setup();
    const onSelect = setup({ blockedDates: new Set(['2026-11-12']), blockedSelectable: false });

    expect(day(12)).toBeDisabled();
    expect(day(12)).toHaveClass('line-through');
    await user.click(day(12));
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('navigates months across the year boundary in both directions', async () => {
    const user = userEvent.setup();
    setup();

    await user.click(screen.getByRole('button', { name: 'Next month' }));
    await user.click(screen.getByRole('button', { name: 'Next month' }));
    expect(screen.getByText('January 2027')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Previous month' }));
    expect(screen.getByText('December 2026')).toBeInTheDocument();
    expect(day(31)).toBeEnabled();
  });

  it('in week mode disables weekends and selects the Monday of the clicked week', async () => {
    const user = userEvent.setup();
    const onSelect = setup({ weekMode: true });

    expect(day(14)).toBeDisabled(); // Saturday
    expect(day(15)).toBeDisabled(); // Sunday
    await user.click(day(12)); // Thursday

    expect(onSelect).toHaveBeenCalledWith('2026-11-09');
  });
});
