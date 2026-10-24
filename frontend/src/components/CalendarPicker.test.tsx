import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CalendarPicker } from './CalendarPicker';
import type { DayAvailability, SlotDemand } from '../types/booking';

// Wednesday 2026-10-21, 10:00 Berlin (CEST). October 2026 starts on a Thursday.
const NOW = new Date('2026-10-21T08:00:00Z');

function day(n: number) {
  return screen.getByRole('button', { name: String(n) });
}

function demand(overrides: Partial<SlotDemand> = {}): SlotDemand {
  return { available: 0, total: 10, pending: 0, waitlist: 0, lotteryRan: false, ...overrides };
}

describe('CalendarPicker', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('opens on the current Berlin month and marks today', () => {
    render(<CalendarPicker selectedDate={null} onSelect={vi.fn()} blockedDates={new Set()} />);

    expect(screen.getByText('October 2026')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Today 21' })).toBeEnabled();
  });

  it('disables days before today but not today itself when no minDate is given', () => {
    render(<CalendarPicker selectedDate={null} onSelect={vi.fn()} blockedDates={new Set()} />);

    expect(day(1)).toBeDisabled();
    expect(day(20)).toBeDisabled();
    expect(day(22)).toBeEnabled();
    expect(day(31)).toBeEnabled();
  });

  it('honours minDate and maxDate inclusively', () => {
    render(
      <CalendarPicker
        selectedDate={null}
        onSelect={vi.fn()}
        blockedDates={new Set()}
        minDate="2026-10-23"
        maxDate="2026-10-28"
      />,
    );

    expect(screen.getByRole('button', { name: 'Today 21' })).toBeDisabled();
    expect(day(22)).toBeDisabled();
    expect(day(23)).toBeEnabled();
    expect(day(28)).toBeEnabled();
    expect(day(29)).toBeDisabled();
  });

  it('disables blocked dates and styles them as blocked', () => {
    render(
      <CalendarPicker selectedDate={null} onSelect={vi.fn()} blockedDates={new Set(['2026-10-27'])} />,
    );

    expect(day(27)).toBeDisabled();
    expect(day(27)).toHaveClass('line-through');
    expect(day(26)).toBeEnabled();
  });

  it('selects the clicked date', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<CalendarPicker selectedDate={null} onSelect={onSelect} blockedDates={new Set()} />);

    await user.click(day(22));

    expect(onSelect).toHaveBeenCalledWith('2026-10-22');
  });

  it('does not select disabled dates', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<CalendarPicker selectedDate={null} onSelect={onSelect} blockedDates={new Set(['2026-10-27'])} />);

    await user.click(day(20));
    await user.click(day(27));

    expect(onSelect).not.toHaveBeenCalled();
  });

  it('allows weekends in day mode but not in week mode', () => {
    const { rerender } = render(<CalendarPicker selectedDate={null} onSelect={vi.fn()} blockedDates={new Set()} />);
    expect(day(24)).toBeEnabled();
    expect(day(25)).toBeEnabled();

    rerender(<CalendarPicker selectedDate={null} onSelect={vi.fn()} blockedDates={new Set()} weekMode />);
    expect(day(24)).toBeDisabled();
    expect(day(25)).toBeDisabled();
  });

  it('in week mode reports the Monday of the clicked week', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<CalendarPicker selectedDate={null} onSelect={onSelect} blockedDates={new Set()} weekMode />);

    await user.click(day(29));

    expect(onSelect).toHaveBeenCalledWith('2026-10-26');
  });

  it('in week mode highlights Monday to Friday of the selected week', () => {
    render(<CalendarPicker selectedDate="2026-10-26" onSelect={vi.fn()} blockedDates={new Set()} weekMode />);

    for (const n of [26, 27, 28, 29, 30]) expect(day(n)).toHaveClass('bg-brand-500');
    expect(day(23)).not.toHaveClass('bg-brand-500');
  });

  it('highlights only the selected date in day mode', () => {
    render(<CalendarPicker selectedDate="2026-10-27" onSelect={vi.fn()} blockedDates={new Set()} />);

    expect(day(27)).toHaveClass('bg-brand-500');
    expect(day(28)).not.toHaveClass('bg-brand-500');
  });

  it('navigates months and rolls over the year in both directions', async () => {
    const user = userEvent.setup();
    render(<CalendarPicker selectedDate={null} onSelect={vi.fn()} blockedDates={new Set()} />);
    const next = screen.getByRole('button', { name: 'Next month' });
    const prev = screen.getByRole('button', { name: 'Previous month' });

    await user.click(next);
    await user.click(next);
    await user.click(next);
    expect(screen.getByText('January 2027')).toBeInTheDocument();
    expect(day(4)).toBeEnabled();

    await user.click(prev);
    expect(screen.getByText('December 2026')).toBeInTheDocument();
  });

  it('greys out a past month entirely', async () => {
    const user = userEvent.setup();
    render(<CalendarPicker selectedDate={null} onSelect={vi.fn()} blockedDates={new Set()} />);

    await user.click(screen.getByRole('button', { name: 'Previous month' }));

    expect(screen.getByText('September 2026')).toBeInTheDocument();
    expect(day(1)).toBeDisabled();
    expect(day(30)).toBeDisabled();
  });

  it('shows an availability dot for bookable days only', () => {
    const busy: DayAvailability = { morning: demand({ pending: 10 }), afternoon: demand() };
    const availability = new Map<string, DayAvailability>([
      ['2026-10-22', busy],
      ['2026-10-27', busy],
      ['2026-10-20', busy],
    ]);
    render(
      <CalendarPicker
        selectedDate={null}
        onSelect={vi.fn()}
        blockedDates={new Set(['2026-10-27'])}
        availability={availability}
      />,
    );

    expect(day(22).querySelector('.bg-amber-500')).not.toBeNull();
    expect(day(27).querySelector('span')).toBeNull();
    expect(day(20).querySelector('span')).toBeNull();
    expect(day(23).querySelector('span')).toBeNull();
  });
});
