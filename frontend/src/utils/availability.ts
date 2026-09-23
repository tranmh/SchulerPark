import type { Availability, DayAvailability, SlotDemand } from '../types/booking';

export type AvailabilityTone = 'emerald' | 'amber' | 'rose';

export const EMPTY_DEMAND: SlotDemand = { available: 0, total: 0, pending: 0, waitlist: 0, lotteryRan: false };

export function toDemand(a: Availability): SlotDemand {
  return {
    available: a.availableSlots,
    total: a.totalSlots,
    pending: a.pendingCount,
    waitlist: a.waitlistCount,
    lotteryRan: a.lotteryRan,
  };
}

/**
 * Phase 20 WP3 3.2: before the lottery a day's dot reflects demand (requests vs slots),
 * after it the free slots. Pre-lottery slots dominate: as long as one half of the day is
 * still undecided, the day is about demand. Null when the day has no bookable slot.
 */
export function dayTone(day: DayAvailability): AvailabilityTone | null {
  const slots = [day.morning, day.afternoon].filter((s) => s.total > 0);
  if (slots.length === 0) return null;

  const pending = slots.filter((s) => !s.lotteryRan);
  if (pending.length > 0) {
    return pending.some((s) => s.pending >= s.total) ? 'amber' : 'emerald';
  }

  const free = slots.reduce((sum, s) => sum + s.available, 0);
  return free > 10 ? 'emerald' : free > 0 ? 'amber' : 'rose';
}

/** A slot that cannot be booked: blocked (no slots), full after the lottery, or already over today. */
export function slotUnavailable(d: SlotDemand, closed: boolean): boolean {
  return closed || d.total <= 0 || (d.lotteryRan && d.available <= 0);
}
