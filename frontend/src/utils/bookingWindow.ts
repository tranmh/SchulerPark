// Bug #21: the bookable date window must be computed from LOCAL calendar components, not via
// `toISOString()` (which converts to UTC and rolls the date back a day after ~22:00–23:00 in
// Berlin) and not via `+ 30 * 24 * 60 * 60 * 1000` ms arithmetic (which ignores the 23/25-hour
// DST days and drifts `maxDate` by one across the switch). Formatting from local Y/M/D and
// advancing days on the Date object (as `getWeekFriday` already does) avoids both problems.
//
// Phase 20 WP3: the window now starts from the BERLIN calendar date and its length comes from
// the server (`GET /api/bookings/window`, `Booking:MaxDaysAhead`); this module is the offline
// fallback and the DST-safe arithmetic the page uses either way.

import { addDays, todayInBerlin } from './berlinTime';

/** Format a Date as a local `YYYY-MM-DD` string (no UTC conversion). */
export function formatLocalDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Default horizon when the server window has not been fetched (matches `Booking:MaxDaysAhead`). */
export const DEFAULT_MAX_DAYS_AHEAD = 31;

/** Schedule defaults used until the server window arrives (match `Booking:*` in appsettings). */
export const DEFAULT_SCHEDULE = {
  lotteryTime: '21:00',
  morningDeadline: '07:00',
  afternoonDeadline: '13:00',
} as const;

export interface BookingWindow {
  /** Berlin calendar date right now. */
  today: string;
  /** Earliest bookable day. */
  minDate: string;
  /** Latest bookable day: today + maxDaysAhead. */
  maxDate: string;
  maxDaysAhead: number;
  /** Whether today's Morning slot can still be booked (before 12:00 Berlin). */
  morningOpenToday: boolean;
  /** Whether today's Afternoon slot can still be booked (before 18:00 Berlin). */
  afternoonOpenToday: boolean;
  /** Berlin "HH:mm" of the nightly lottery (WP4). */
  lotteryTime: string;
  /** Default confirmation deadlines, Berlin "HH:mm" (WP4). */
  morningDeadline: string;
  afternoonDeadline: string;
}

/**
 * Offline fallback for the server window: Berlin today through today + `maxDaysAhead`,
 * with tomorrow as the first bookable day (the same-day cutoffs are the server's call, so
 * without its answer the fallback stays conservative).
 */
export function getBookingWindow(from: Date = new Date(), maxDaysAhead = DEFAULT_MAX_DAYS_AHEAD): BookingWindow {
  const today = todayInBerlin(from);
  return {
    today,
    minDate: addDays(today, 1),
    maxDate: addDays(today, maxDaysAhead),
    maxDaysAhead,
    morningOpenToday: false,
    afternoonOpenToday: false,
    ...DEFAULT_SCHEDULE,
  };
}
