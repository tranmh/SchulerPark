/**
 * Shared test data builders for the main domain types. Every builder returns a complete,
 * valid object; pass overrides for the fields a test cares about. Ids come from a
 * deterministic counter — call `resetFixtureIds()` in `beforeEach` when a test asserts ids.
 */
import type { User, AuthResponse, AuthConfig } from '../types/auth';
import type {
  Availability,
  BlockedDay,
  Booking,
  BookingWindowResponse,
  Location,
  MyBookingsResponse,
  ParkingSlot,
} from '../types/booking';
import type { AdminBlockedDay, AdminBlockedDayCreated, AdminLocation, CapacityChangeResult } from '../types/admin';
import type {
  AdminUser,
  AdminUserListResponse,
  PendingUser,
  PendingUserListResponse,
} from '../services/adminUsersService';
import type { ApiErrorData } from '../utils/apiError';

let seq = 0;

/** Next deterministic id with the given prefix: `user-1`, `booking-2`, ... */
export function nextId(prefix: string): string {
  seq += 1;
  return `${prefix}-${seq}`;
}

export function resetFixtureIds(): void {
  seq = 0;
}

/** Fixed reference instants so no fixture depends on the wall clock. */
export const FIXED_TODAY = '2026-06-10';
export const FIXED_NOW_ISO = '2026-06-10T08:00:00.000Z';

export const LOCATION_ID = 'loc-goeppingen';

export function buildUser(overrides: Partial<User> = {}): User {
  const firstName = overrides.firstName === undefined ? 'Erika' : overrides.firstName;
  const lastName = overrides.lastName === undefined ? 'Mustermann' : overrides.lastName;
  return {
    id: nextId('user'),
    email: 'erika.mustermann@schuler.de',
    displayName: [firstName, lastName].filter(Boolean).join(' ') || 'Erika Mustermann',
    firstName,
    lastName,
    company: 'Schuler AG',
    carLicensePlate: 'GP-EM 1234',
    role: 'User',
    hasAzureAd: false,
    preferredLocationId: null,
    preferredSlotId: null,
    preferredLanguage: 'en',
    hasPassword: true,
    ...overrides,
  };
}

export function buildAuthResponse(overrides: Partial<AuthResponse> = {}): AuthResponse {
  return {
    accessToken: 'test-access-token',
    expiresAt: '2026-06-10T09:00:00.000Z',
    user: buildUser(),
    ...overrides,
  };
}

export function buildAuthConfig(overrides: Partial<AuthConfig> = {}): AuthConfig {
  return {
    azureAdEnabled: false,
    azureAdClientId: null,
    azureAdTenantId: null,
    ssoDomains: [],
    ...overrides,
  };
}

export function buildAdminUser(overrides: Partial<AdminUser> = {}): AdminUser {
  return {
    id: nextId('user'),
    email: 'max.extern@partner.example',
    displayName: 'Max Extern',
    firstName: 'Max',
    lastName: 'Extern',
    company: 'Partner GmbH',
    role: 'User',
    approvalStatus: 'Approved',
    isDisabled: false,
    hasAzureAd: false,
    createdAt: '2026-05-01T10:00:00.000Z',
    ...overrides,
  };
}

export function buildAdminUserList(
  users: AdminUser[] = [buildAdminUser()],
  overrides: Partial<Omit<AdminUserListResponse, 'users'>> = {},
): AdminUserListResponse {
  return { users, totalCount: users.length, page: 1, pageSize: 20, ...overrides };
}

export function buildPendingUser(overrides: Partial<PendingUser> = {}): PendingUser {
  return {
    id: nextId('pending'),
    email: 'new.extern@partner.example',
    displayName: 'New Extern',
    firstName: 'New',
    lastName: 'Extern',
    company: 'Partner GmbH',
    emailVerified: true,
    createdAt: '2026-06-09T12:00:00.000Z',
    ...overrides,
  };
}

export function buildPendingUserList(users: PendingUser[] = [buildPendingUser()]): PendingUserListResponse {
  return { users, totalCount: users.length };
}

export function buildLocation(overrides: Partial<Location> = {}): Location {
  return { id: LOCATION_ID, name: 'Goeppingen', totalSlots: 10, ...overrides };
}

export function buildAdminLocation(overrides: Partial<AdminLocation> = {}): AdminLocation {
  return {
    id: LOCATION_ID,
    name: 'Goeppingen',
    isActive: true,
    defaultAlgorithm: 'Random',
    totalSlots: 10,
    activeSlots: 10,
    ...overrides,
  };
}

export function buildSlot(overrides: Partial<ParkingSlot> = {}): ParkingSlot {
  return {
    id: nextId('slot'),
    slotNumber: 'P001',
    label: null,
    isActive: true,
    gridRow: null,
    gridColumn: null,
    ...overrides,
  };
}

export function buildBooking(overrides: Partial<Booking> = {}): Booking {
  return {
    id: nextId('booking'),
    locationId: LOCATION_ID,
    locationName: 'Goeppingen',
    parkingSlotId: null,
    parkingSlotNumber: null,
    date: FIXED_TODAY,
    timeSlot: 'Morning',
    status: 'Pending',
    confirmedAt: null,
    createdAt: '2026-06-09T10:00:00.000Z',
    confirmationDeadline: null,
    fallbackReason: null,
    ...overrides,
  };
}

export function buildMyBookings(bookings: Booking[] = [buildBooking()]): MyBookingsResponse {
  return { bookings, totalCount: bookings.length, page: 1, pageSize: 20 };
}

export function buildBlockedDay(overrides: Partial<BlockedDay> = {}): BlockedDay {
  return { id: nextId('blocked'), date: FIXED_TODAY, parkingSlotId: null, reason: null, ...overrides };
}

export function buildAdminBlockedDay(overrides: Partial<AdminBlockedDay> = {}): AdminBlockedDay {
  return {
    id: nextId('blocked'),
    locationId: LOCATION_ID,
    locationName: 'Goeppingen',
    parkingSlotId: null,
    slotNumber: null,
    date: FIXED_TODAY,
    reason: 'Company holiday',
    createdAt: '2026-06-01T09:00:00.000Z',
    ...overrides,
  };
}

export function buildCapacityChange(overrides: Partial<CapacityChangeResult> = {}): CapacityChangeResult {
  return { affected: 0, reassigned: 0, waitlisted: 0, cancelled: 0, ...overrides };
}

export function buildAdminBlockedDayCreated(
  blockedDay: AdminBlockedDay = buildAdminBlockedDay(),
  impact: CapacityChangeResult = buildCapacityChange(),
): AdminBlockedDayCreated {
  return { blockedDay, impact };
}

export function buildAvailability(overrides: Partial<Availability> = {}): Availability {
  return {
    date: FIXED_TODAY,
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
    minDate: FIXED_TODAY,
    maxDate: '2026-06-24',
    maxDaysAhead: 14,
    morningOpenToday: true,
    afternoonOpenToday: true,
    lotteryTime: '20:00',
    morningDeadline: '09:00',
    afternoonDeadline: '13:00',
    ...overrides,
  };
}

/** The shape axios rejects with, as far as the app's error helpers read it. */
export interface FakeAxiosError {
  isAxiosError: true;
  message: string;
  response: { status: number; data: ApiErrorData; statusText: string; headers: Record<string, string> };
}

/** A rejected-API-call value for `mockRejectedValue(apiError(409, { code: 'X' }))`. */
export function apiError(status: number, data: ApiErrorData = {}): FakeAxiosError {
  return {
    isAxiosError: true,
    message: `Request failed with status code ${status}`,
    response: { status, data, statusText: String(status), headers: {} },
  };
}
