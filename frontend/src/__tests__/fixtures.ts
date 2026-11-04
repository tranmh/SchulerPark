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
import { vi, type MockedFunction } from 'vitest';
import type { AxiosResponse } from 'axios';
import type {
  AdminBlockedDay, AdminBlockedDayCreated, AdminBooking, AdminLocation, AdminSlot, CapacityChangeResult, LotteryRun,
} from '../types/admin';
import type { GridCell, GridConfiguration, GridSlot, SaveGridConfigurationRequest } from '../types/grid';
import type { adminService } from '../services/adminService';
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

export function buildAdminSlot(overrides: Partial<AdminSlot> = {}): AdminSlot {
  return { id: nextId('slot'), locationId: LOCATION_ID, slotNumber: 'P001', label: null, isActive: true, ...overrides };
}

export function buildAdminBooking(overrides: Partial<AdminBooking> = {}): AdminBooking {
  return {
    id: nextId('booking'),
    userId: 'user-erika',
    userEmail: 'erika.mustermann@schuler.de',
    userDisplayName: 'Erika Mustermann',
    locationId: LOCATION_ID,
    locationName: 'Goeppingen',
    parkingSlotId: null,
    parkingSlotNumber: null,
    date: FIXED_TODAY,
    timeSlot: 'Morning',
    status: 'Pending',
    confirmedAt: null,
    createdAt: '2026-06-09T10:00:00.000Z',
    cancelledAt: null,
    cancelReason: null,
    cancelledByUserId: null,
    autoConfirmReason: null,
    ...overrides,
  };
}

export function buildAdminBookingPage(bookings: AdminBooking[] = [buildAdminBooking()]) {
  return { bookings, totalCount: bookings.length, page: 1, pageSize: 20 };
}

export function buildLotteryRun(overrides: Partial<LotteryRun> = {}): LotteryRun {
  return {
    id: nextId('lottery'),
    locationId: LOCATION_ID,
    locationName: 'Goeppingen',
    date: FIXED_TODAY,
    timeSlot: 'Morning',
    algorithm: 'Random',
    ranAt: '2026-06-09T20:00:00.000Z',
    totalBookings: 3,
    availableSlots: 10,
    ...overrides,
  };
}

export function buildGridSlot(overrides: Partial<GridSlot> = {}): GridSlot {
  return { id: nextId('slot'), slotNumber: 'P001', label: null, isActive: true, row: 0, column: 0, ...overrides };
}

export function buildGridCell(overrides: Partial<GridCell> = {}): GridCell {
  return { id: nextId('cell'), row: 0, column: 1, cellType: 'Road', label: null, ...overrides };
}

/** A 2x3 grid with one placed slot and one road cell. */
export function buildGridConfiguration(overrides: Partial<GridConfiguration> = {}): GridConfiguration {
  return { gridRows: 2, gridColumns: 3, slots: [buildGridSlot()], cells: [buildGridCell()], ...overrides };
}

export function buildSaveGridRequest(overrides: Partial<SaveGridConfigurationRequest> = {}): SaveGridConfigurationRequest {
  return {
    gridRows: 2,
    gridColumns: 3,
    slotPositions: [{ slotId: 'slot-1', row: 0, column: 0 }],
    cells: [{ row: 0, column: 1, cellType: 'Road' }],
    ...overrides,
  };
}

/**
 * A typed test double for `adminService` with every method a `vi.fn` that resolves to a sane
 * default. Use with `vi.mock('../../services/adminService', () => ({ adminService: mockAdminService() }))`
 * (inside the factory, so hoisting is safe) and override per test with `vi.mocked(adminService.x)`.
 */
export type MockAdminService = { [K in keyof typeof adminService]: MockedFunction<(typeof adminService)[K]> };

export function mockAdminService(overrides: Partial<MockAdminService> = {}): MockAdminService {
  const ok = { status: 200, data: undefined, statusText: 'OK', headers: {}, config: {} } as unknown as AxiosResponse;
  const svc: MockAdminService = {
    getLocations: vi.fn<typeof adminService.getLocations>().mockResolvedValue([buildAdminLocation()]),
    createLocation: vi.fn<typeof adminService.createLocation>().mockImplementation(async (d) => buildAdminLocation({ id: nextId('loc'), ...d })),
    updateLocation: vi.fn<typeof adminService.updateLocation>().mockImplementation(async (id, d) => buildAdminLocation({ id, ...d })),
    deactivateLocation: vi.fn<typeof adminService.deactivateLocation>().mockResolvedValue(buildCapacityChange()),
    setAlgorithm: vi.fn<typeof adminService.setAlgorithm>().mockResolvedValue(ok),
    getSlots: vi.fn<typeof adminService.getSlots>().mockResolvedValue([]),
    createSlot: vi.fn<typeof adminService.createSlot>().mockImplementation(async (d) => buildAdminSlot({ ...d, label: d.label ?? null })),
    updateSlot: vi.fn<typeof adminService.updateSlot>().mockImplementation(async (id, d) => buildAdminSlot({ id, ...d, label: d.label ?? null })),
    deactivateSlot: vi.fn<typeof adminService.deactivateSlot>().mockResolvedValue(buildCapacityChange()),
    getBlockedDays: vi.fn<typeof adminService.getBlockedDays>().mockResolvedValue([]),
    createBlockedDay: vi.fn<typeof adminService.createBlockedDay>().mockImplementation(async (d) =>
      buildAdminBlockedDayCreated(buildAdminBlockedDay({ ...d, parkingSlotId: d.parkingSlotId ?? null, reason: d.reason ?? null }))),
    removeBlockedDay: vi.fn<typeof adminService.removeBlockedDay>().mockResolvedValue(ok),
    getBookings: vi.fn<typeof adminService.getBookings>().mockResolvedValue(buildAdminBookingPage([])),
    countBookings: vi.fn<typeof adminService.countBookings>().mockResolvedValue(0),
    getLotteryRuns: vi.fn<typeof adminService.getLotteryRuns>().mockResolvedValue({ lotteryRuns: [], totalCount: 0, page: 1, pageSize: 20 }),
    getGridConfiguration: vi.fn<typeof adminService.getGridConfiguration>().mockResolvedValue(buildGridConfiguration()),
    saveGridConfiguration: vi.fn<typeof adminService.saveGridConfiguration>().mockImplementation(async (_id, d) =>
      buildGridConfiguration({ gridRows: d.gridRows, gridColumns: d.gridColumns })),
  };
  return { ...svc, ...overrides };
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
