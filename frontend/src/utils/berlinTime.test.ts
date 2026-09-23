import { describe, it, expect } from 'vitest';
import { addDays, berlinParts, todayInBerlin } from './berlinTime';

describe('todayInBerlin (Phase 20 WP3)', () => {
  it('rolls to the next day at Berlin midnight, not UTC midnight', () => {
    // 22:30 UTC on 10 June is 00:30 CEST on 11 June.
    expect(todayInBerlin(new Date('2026-06-10T22:30:00Z'))).toBe('2026-06-11');
    expect(todayInBerlin(new Date('2026-06-10T21:59:59Z'))).toBe('2026-06-10');
  });

  it('uses the winter offset in January', () => {
    // 23:30 UTC on 31 January is 00:30 CET on 1 February.
    expect(todayInBerlin(new Date('2026-01-31T23:30:00Z'))).toBe('2026-02-01');
    expect(todayInBerlin(new Date('2026-01-31T22:59:00Z'))).toBe('2026-01-31');
  });

  it('exposes the Berlin wall-clock hour', () => {
    expect(berlinParts(new Date('2026-06-10T08:05:00Z')).hour).toBe(10);
    expect(berlinParts(new Date('2026-12-10T08:05:00Z')).hour).toBe(9);
  });
});

describe('addDays', () => {
  it('adds calendar days across a DST switch and month end', () => {
    expect(addDays('2026-03-28', 2)).toBe('2026-03-30');
    expect(addDays('2026-01-31', 31)).toBe('2026-03-03');
    expect(addDays('2026-06-10', -1)).toBe('2026-06-09');
  });
});
