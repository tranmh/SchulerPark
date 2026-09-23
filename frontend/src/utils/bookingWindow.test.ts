import { describe, it, expect } from 'vitest';
import { getBookingWindow, formatLocalDate } from './bookingWindow';

describe('getBookingWindow (Bug #21 / Phase 20 WP3)', () => {
  it('minDate is tomorrow in BERLIN time in the small hours (no UTC roll-back)', () => {
    // 00:30 Berlin on 2026-03-10 (23:30Z the day before). toISOString() would report the
    // previous UTC day and so minDate as *today*; Berlin-calendar math yields 2026-03-11.
    const smallHours = new Date('2026-03-09T23:30:00Z');
    const { today, minDate } = getBookingWindow(smallHours);
    expect(today).toBe('2026-03-10');
    expect(minDate).toBe('2026-03-11');
  });

  it('window is exactly maxDaysAhead calendar days across a DST switch (no ms drift)', () => {
    // Europe/Berlin springs forward on 2026-03-29.
    const beforeDst = new Date('2026-03-20T11:00:00Z');
    const { today, maxDate } = getBookingWindow(beforeDst);
    expect(today).toBe('2026-03-20');
    expect(maxDate).toBe('2026-04-20'); // 2026-03-20 + 31 calendar days

    const days = Math.round(
      (new Date(maxDate + 'T00:00:00').getTime() - new Date(today + 'T00:00:00').getTime())
      / 86_400_000,
    );
    expect(days).toBe(31);
  });

  it('uses today + maxDaysAhead, not a calendar month (month-length case)', () => {
    const { maxDate } = getBookingWindow(new Date('2026-01-31T12:00:00Z'));
    expect(maxDate).toBe('2026-03-03'); // AddMonths(1) would have been 2026-02-28
  });

  it('honours a custom horizon', () => {
    const { maxDate } = getBookingWindow(new Date('2026-06-10T12:00:00Z'), 14);
    expect(maxDate).toBe('2026-06-24');
  });

  it('formatLocalDate zero-pads month and day', () => {
    expect(formatLocalDate(new Date(2026, 0, 5))).toBe('2026-01-05');
  });
});
