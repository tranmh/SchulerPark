// Bug #21: the bookable date window must be computed from LOCAL calendar components, not via
// `toISOString()` (which converts to UTC and rolls the date back a day after ~22:00–23:00 in
// Berlin) and not via `+ 30 * 24 * 60 * 60 * 1000` ms arithmetic (which ignores the 23/25-hour
// DST days and drifts `maxDate` by one across the switch). Formatting from local Y/M/D and
// advancing days on the Date object (as `getWeekFriday` already does) avoids both problems.

/** Format a Date as a local `YYYY-MM-DD` string (no UTC conversion). */
export function formatLocalDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export interface BookingWindow {
  /** Earliest bookable day: tomorrow (local). */
  minDate: string;
  /** Latest bookable day: 30 days after minDate (local). */
  maxDate: string;
}

/**
 * The bookable window relative to `from` (defaults to now): from tomorrow to 30 days later,
 * using local calendar days so it is stable across the midnight and DST boundaries.
 */
export function getBookingWindow(from: Date = new Date()): BookingWindow {
  const min = new Date(from.getFullYear(), from.getMonth(), from.getDate() + 1);
  const max = new Date(from.getFullYear(), from.getMonth(), from.getDate() + 31);
  return { minDate: formatLocalDate(min), maxDate: formatLocalDate(max) };
}
