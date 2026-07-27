import { describe, it, expect } from 'vitest';
import { getBookingWindow, formatLocalDate } from './bookingWindow';

describe('getBookingWindow (Bug #21)', () => {
  it('minDate is tomorrow in LOCAL time in the small hours (no UTC roll-back)', () => {
    // 00:30 local on 2026-03-10 in a positive-offset tz (Europe/Berlin = UTC+1 here; the
    // suite pins TZ=Europe/Berlin). The old toISOString() code converts to the PREVIOUS day
    // in UTC (2026-03-09T23:30Z) and so reports minDate as *today*. Local-component math must
    // yield tomorrow's local date: 2026-03-11.
    const smallHours = new Date(2026, 2, 10, 0, 30, 0);
    const { minDate } = getBookingWindow(smallHours);
    expect(minDate).toBe('2026-03-11');
  });

  it('window spans exactly 30 calendar days across a DST switch (no ms drift)', () => {
    // Europe/Berlin springs forward on 2026-03-29. A window starting near it must still be
    // exactly 30 days wide when measured in calendar days, not 29/31 from ms arithmetic.
    const beforeDst = new Date(2026, 2, 20, 12, 0, 0);
    const { minDate, maxDate } = getBookingWindow(beforeDst);
    expect(minDate).toBe('2026-03-21');
    expect(maxDate).toBe('2026-04-20'); // 2026-03-21 + 30 calendar days

    const days = Math.round(
      (new Date(maxDate + 'T00:00:00').getTime() - new Date(minDate + 'T00:00:00').getTime())
      / 86_400_000,
    );
    expect(days).toBe(30);
  });

  it('formatLocalDate zero-pads month and day', () => {
    expect(formatLocalDate(new Date(2026, 0, 5))).toBe('2026-01-05');
  });
});
