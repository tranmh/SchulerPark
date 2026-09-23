// Phase 20 WP3: the server decides everything in Europe/Berlin. A browser in another
// zone (or a laptop with a wrong clock zone) must still show the same "today" as the
// backend, so calendar dates are derived from the Berlin wall clock via Intl — not from
// the device's local date.

const BERLIN = 'Europe/Berlin';

const partsFormatter = new Intl.DateTimeFormat('en-GB', {
  timeZone: BERLIN,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
});

export interface BerlinParts {
  year: number;
  month: number; // 1–12
  day: number;
  hour: number;
  minute: number;
  second: number;
}

/** The Berlin wall-clock components for an instant (defaults to now). */
export function berlinParts(at: Date = new Date()): BerlinParts {
  const parts: Partial<Record<Intl.DateTimeFormatPartTypes, string>> = {};
  for (const p of partsFormatter.formatToParts(at)) parts[p.type] = p.value;
  return {
    year: Number(parts.year),
    month: Number(parts.month),
    day: Number(parts.day),
    hour: Number(parts.hour),
    minute: Number(parts.minute),
    second: Number(parts.second),
  };
}

function pad(n: number): string {
  return String(n).padStart(2, '0');
}

/** Today's calendar date in Europe/Berlin as `YYYY-MM-DD`. */
export function todayInBerlin(at: Date = new Date()): string {
  const { year, month, day } = berlinParts(at);
  return `${year}-${pad(month)}-${pad(day)}`;
}

/** Adds calendar days to a `YYYY-MM-DD` string (DST-safe: local Y/M/D arithmetic). */
export function addDays(dateStr: string, days: number): string {
  const [y, m, d] = dateStr.split('-').map(Number);
  const next = new Date(y, m - 1, d + days);
  return `${next.getFullYear()}-${pad(next.getMonth() + 1)}-${pad(next.getDate())}`;
}
