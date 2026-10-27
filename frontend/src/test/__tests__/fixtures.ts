import { vi } from 'vitest';
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios';
import type {
  Availability,
  BlockedDay,
  Booking,
  BookingWindowResponse,
  Location,
  MyBookingsResponse,
} from '../../types/booking';
import type { AdminBlockedDay, AdminLocation, AdminSlot } from '../../types/admin';

// Fixed reference day (a Monday) so fixtures never depend on the wall clock.
export const FIXED_TODAY = '2026-10-26';

let seq = 0;
const nextId = (prefix: string) => `${prefix}-${++seq}`;

export function buildLocation(overrides: Partial<Location> = {}): Location {
  return { id: 'loc-goeppingen', name: 'Göppingen', totalSlots: 10, ...overrides };
}

export function buildBooking(overrides: Partial<Booking> = {}): Booking {
  return {
    id: nextId('booking'),
    locationId: 'loc-goeppingen',
    locationName: 'Göppingen',
    parkingSlotId: null,
    parkingSlotNumber: null,
    date: '2026-10-27',
    timeSlot: 'Morning',
    status: 'Pending',
    confirmedAt: null,
    createdAt: `${FIXED_TODAY}T08:00:00Z`,
    confirmationDeadline: null,
    fallbackReason: null,
    waitlistPosition: null,
    ...overrides,
  };
}

export function buildMyBookings(bookings: Booking[] = [buildBooking()]): MyBookingsResponse {
  return { bookings, totalCount: bookings.length, page: 1, pageSize: 20 };
}

export function buildBlockedDay(overrides: Partial<BlockedDay> = {}): BlockedDay {
  return { id: nextId('blocked'), date: '2026-10-28', parkingSlotId: null, reason: 'Holiday', ...overrides };
}

export function buildAvailability(overrides: Partial<Availability> = {}): Availability {
  return {
    date: '2026-10-27',
    timeSlot: 'Morning',
    availableSlots: 10,
    totalSlots: 10,
    bookingCount: 0,
    pendingCount: 0,
    waitlistCount: 0,
    lotteryRan: false,
    ...overrides,
  };
}

export function buildBookingWindow(overrides: Partial<BookingWindowResponse> = {}): BookingWindowResponse {
  return {
    today: FIXED_TODAY,
    minDate: '2026-10-27',
    maxDate: '2026-11-09',
    maxDaysAhead: 14,
    morningOpenToday: false,
    afternoonOpenToday: false,
    lotteryTime: '20:00',
    morningDeadline: '09:00',
    afternoonDeadline: '13:00',
    ...overrides,
  };
}

export function buildAdminLocation(overrides: Partial<AdminLocation> = {}): AdminLocation {
  return {
    id: 'loc-goeppingen',
    name: 'Göppingen',
    isActive: true,
    defaultAlgorithm: 'Lottery',
    totalSlots: 10,
    activeSlots: 10,
    ...overrides,
  };
}

export function buildAdminSlot(overrides: Partial<AdminSlot> = {}): AdminSlot {
  return { id: nextId('slot'), locationId: 'loc-goeppingen', slotNumber: '1', label: null, isActive: true, ...overrides };
}

export function buildAdminBlockedDay(overrides: Partial<AdminBlockedDay> = {}): AdminBlockedDay {
  return {
    id: nextId('blocked'),
    locationId: 'loc-goeppingen',
    locationName: 'Göppingen',
    parkingSlotId: null,
    slotNumber: null,
    date: '2026-10-28',
    reason: null,
    createdAt: `${FIXED_TODAY}T08:00:00Z`,
    ...overrides,
  };
}

/** Wraps data in an AxiosResponse, for `vi.mocked(api.get).mockResolvedValue(axiosOk(data))`. */
export function axiosOk<T>(data: T, status = 200): AxiosResponse<T> {
  return { data, status, statusText: 'OK', headers: {}, config: {} as InternalAxiosRequestConfig };
}

/** Builds an axios-shaped error carrying a server response (what utils/apiError reads). */
export function axiosError(status: number, data: unknown = {}) {
  return Object.assign(new Error(`Request failed with status code ${status}`), {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} },
  });
}

/**
 * Factory for `vi.mock('../services/api', () => ({ default: createApiMock() }))`:
 * a stand-in for the shared axios instance with spy-able verbs.
 */
export function createApiMock() {
  return {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
    interceptors: { request: { use: vi.fn() }, response: { use: vi.fn() } },
  };
}
