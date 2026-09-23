import { describe, it, expect } from 'vitest';
import { dayTone, slotUnavailable, EMPTY_DEMAND } from './availability';
import type { SlotDemand } from '../types/booking';

const slot = (o: Partial<SlotDemand>): SlotDemand => ({ ...EMPTY_DEMAND, total: 10, available: 10, ...o });

describe('dayTone (Phase 20 WP3 3.2)', () => {
  it('shows demand before the lottery: 12 requests for 10 slots → amber, 4 for 10 → emerald', () => {
    expect(dayTone({ morning: slot({ pending: 12 }), afternoon: slot({ pending: 4 }) })).toBe('amber');
    expect(dayTone({ morning: slot({ pending: 4 }), afternoon: slot({ pending: 0 }) })).toBe('emerald');
  });

  it('treats "at capacity" as high demand', () => {
    expect(dayTone({ morning: slot({ pending: 10 }), afternoon: slot({ pending: 0 }) })).toBe('amber');
  });

  it('shows free slots after the lottery', () => {
    const ran = (available: number) => slot({ lotteryRan: true, available });
    expect(dayTone({ morning: ran(8), afternoon: ran(8) })).toBe('emerald');
    expect(dayTone({ morning: ran(3), afternoon: ran(2) })).toBe('amber');
    expect(dayTone({ morning: ran(0), afternoon: ran(0) })).toBe('rose');
  });

  it('lets the undecided half dominate a mixed day', () => {
    expect(dayTone({ morning: slot({ lotteryRan: true, available: 0 }), afternoon: slot({ pending: 1 }) })).toBe('emerald');
  });

  it('returns null for a day without bookable slots (blocked)', () => {
    expect(dayTone({ morning: EMPTY_DEMAND, afternoon: EMPTY_DEMAND })).toBeNull();
  });
});

describe('slotUnavailable', () => {
  it('pre-lottery demand never blocks, post-lottery full does, closed always does', () => {
    expect(slotUnavailable(slot({ pending: 99 }), false)).toBe(false);
    expect(slotUnavailable(slot({ lotteryRan: true, available: 0 }), false)).toBe(true);
    expect(slotUnavailable(slot({ lotteryRan: true, available: 1 }), false)).toBe(false);
    expect(slotUnavailable(slot({}), true)).toBe(true);
    expect(slotUnavailable(EMPTY_DEMAND, false)).toBe(true);
  });
});
