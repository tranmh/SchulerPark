export type TimeSlot = 'Morning' | 'Afternoon';

/**
 * `Waitlisted` (Phase 20 WP4) is the live "no slot yet" state — promoted automatically when a
 * slot frees up; `Lost` is terminal: the day is over and no slot was ever found.
 */
export type BookingStatus = 'Pending' | 'Won' | 'Waitlisted' | 'Lost' | 'Confirmed' | 'Cancelled' | 'Expired';

export interface Location {
  id: string;
  name: string;
  address: string;
  totalSlots: number;
}

export interface ParkingSlot {
  id: string;
  slotNumber: string;
  label: string | null;
  isActive: boolean;
  gridRow: number | null;
  gridColumn: number | null;
}

export interface BlockedDay {
  id: string;
  date: string;
  parkingSlotId: string | null;
  reason: string | null;
}

export interface Booking {
  id: string;
  locationId: string;
  locationName: string;
  parkingSlotId: string | null;
  parkingSlotNumber: string | null;
  date: string;
  timeSlot: TimeSlot;
  status: BookingStatus;
  confirmedAt: string | null;
  createdAt: string;
  /** Stored when the booking became Won (WP4); null for every other status. */
  confirmationDeadline: string | null;
  fallbackReason: string | null;
  /** Approximate 1-based queue position; only present for Waitlisted bookings (WP4 2.7). */
  waitlistPosition?: number | null;
}

/**
 * Availability of one date × time slot (Phase 20 WP3 3.2). `bookingCount` is Won +
 * Confirmed (slots actually held); before the lottery the meaningful number is
 * `pendingCount` against `totalSlots`.
 */
export interface Availability {
  date: string;
  timeSlot: TimeSlot;
  availableSlots: number;
  totalSlots: number;
  bookingCount: number;
  pendingCount: number;
  waitlistCount: number;
  lotteryRan: boolean;
}

/** The per-slot figures the calendar and time-slot picker render. */
export interface SlotDemand {
  available: number;
  total: number;
  pending: number;
  waitlist: number;
  lotteryRan: boolean;
}

export interface DayAvailability {
  morning: SlotDemand;
  afternoon: SlotDemand;
}

/** Server-side bookable window in Europe/Berlin plus the lottery/confirmation schedule (`GET /api/bookings/window`). */
export interface BookingWindowResponse {
  today: string;
  minDate: string;
  maxDate: string;
  maxDaysAhead: number;
  morningOpenToday: boolean;
  afternoonOpenToday: boolean;
  /** Berlin "HH:mm" of the nightly lottery (WP4). */
  lotteryTime: string;
  /** Default confirmation deadline for Morning wins, Berlin "HH:mm". */
  morningDeadline: string;
  /** Default confirmation deadline for Afternoon wins, Berlin "HH:mm". */
  afternoonDeadline: string;
}

export interface CreateBookingRequest {
  locationId: string | null;
  date: string;
  timeSlot: TimeSlot;
}

export interface MyBookingsResponse {
  bookings: Booking[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface CreateWeekBookingRequest {
  locationId: string | null;
  weekStartDate: string;
  timeSlot: TimeSlot;
}

export interface SkippedDay {
  date: string;
  reason: string;
}

export interface WeekBookingResponse {
  createdBookings: Booking[];
  skippedDays: SkippedDay[];
}

export interface BookingFilters {
  page?: number;
  pageSize?: number;
  status?: BookingStatus;
  from?: string;
  to?: string;
}
