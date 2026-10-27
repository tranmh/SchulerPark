import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import i18n from '../i18n';
import { CalendarPicker } from './CalendarPicker';

// Tuesday, 10 November 2026 in Berlin.
vi.mock('../utils/berlinTime', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../utils/berlinTime')>()),
  todayInBerlin: () => '2026-11-10',
}));

function day(n: number): HTMLButtonElement {
  const btn = screen
    .getAllByRole<HTMLButtonElement>('button')
    .find((b) => b.textContent?.trim() === String(n));
  if (!btn) throw new Error(`no day ${n}`);
  return btn;
}

describe('CalendarPicker', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en');
  });

  it('opens on the Berlin month and navigates across the year boundary', () => {
    render(<CalendarPicker selectedDate={null} onSelect={vi.fn()} blockedDates={new Set()} />);
    expect(screen.getByText('November 2026')).toBeInTheDocument();
    const next = screen.getByRole('button', { name: i18n.t('components.calendar.nextMonth') });
    const prev = screen.getByRole('button', { name: i18n.t('components.calendar.prevMonth') });
    fireEvent.click(next);
    fireEvent.click(next);
    expect(screen.getByText('January 2027')).toBeInTheDocument();
    fireEvent.click(prev);
    expect(screen.getByText('December 2026')).toBeInTheDocument();
  });

  it('disables days before today when no minDate is given', () => {
    render(<CalendarPicker selectedDate={null} onSelect={vi.fn()} blockedDates={new Set()} />);
    expect(day(9)).toBeDisabled();
    expect(day(10)).toBeEnabled();
    expect(day(10)).toHaveAccessibleName(`${i18n.t('components.calendar.today')} 10`);
  });

  it('honours minDate and maxDate inclusively', () => {
    render(
      <CalendarPicker selectedDate={null} onSelect={vi.fn()} blockedDates={new Set()} minDate="2026-11-12" maxDate="2026-11-20" />,
    );
    expect(day(11)).toBeDisabled();
    expect(day(12)).toBeEnabled();
    expect(day(20)).toBeEnabled();
    expect(day(21)).toBeDisabled();
  });

  it('disables blocked days', () => {
    const onSelect = vi.fn<(d: string) => void>();
    render(<CalendarPicker selectedDate={null} onSelect={onSelect} blockedDates={new Set(['2026-11-13'])} />);
    expect(day(13)).toBeDisabled();
    fireEvent.click(day(13));
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('selects a day as YYYY-MM-DD', () => {
    const onSelect = vi.fn<(d: string) => void>();
    render(<CalendarPicker selectedDate={null} onSelect={onSelect} blockedDates={new Set()} />);
    fireEvent.click(day(5 + 7));
    expect(onSelect).toHaveBeenCalledWith('2026-11-12');
  });

  it('selects days in the next month after navigating', () => {
    const onSelect = vi.fn<(d: string) => void>();
    render(<CalendarPicker selectedDate={null} onSelect={onSelect} blockedDates={new Set()} />);
    fireEvent.click(screen.getByRole('button', { name: i18n.t('components.calendar.nextMonth') }));
    fireEvent.click(day(1));
    expect(onSelect).toHaveBeenCalledWith('2026-12-01');
  });

  it('in week mode disables weekends and selects the Monday of the week', () => {
    const onSelect = vi.fn<(d: string) => void>();
    render(<CalendarPicker selectedDate={null} onSelect={onSelect} blockedDates={new Set()} weekMode />);
    expect(day(14)).toBeDisabled();
    expect(day(15)).toBeDisabled();
    fireEvent.click(day(19));
    expect(onSelect).toHaveBeenCalledWith('2026-11-16');
  });
});
