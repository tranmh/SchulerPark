// Shared builders and an axios-like api fake for admin pages/services tests.
import { vi } from 'vitest';
import type {
  AdminLocation, AdminSlot, AdminBlockedDay, AdminBooking, CapacityChangeResult, LotteryRun,
} from '../../types/admin';

export const LOCATION_ID = '00000000-0000-0000-0000-00000000a001';
export const SLOT_ID = '00000000-0000-0000-0000-00000000b001';
export const USER_ID = '00000000-0000-0000-0000-00000000c001';

export function buildLocation(overrides: Partial<AdminLocation> = {}): AdminLocation {
  return {
    id: LOCATION_ID, name: 'Göppingen Werk 1', isActive: true,
    defaultAlgorithm: 'Random', totalSlots: 10, activeSlots: 10, ...overrides,
  };
}

export function buildSlot(overrides: Partial<AdminSlot> = {}): AdminSlot {
  return { id: SLOT_ID, locationId: LOCATION_ID, slotNumber: 'P1', label: null, isActive: true, ...overrides };
}

export function buildBlockedDay(overrides: Partial<AdminBlockedDay> = {}): AdminBlockedDay {
  return {
    id: '00000000-0000-0000-0000-00000000d001', locationId: LOCATION_ID, locationName: 'Göppingen Werk 1',
    parkingSlotId: null, slotNumber: null, date: '2026-11-02', reason: 'Betriebsversammlung',
    createdAt: '2026-10-20T08:00:00Z', ...overrides,
  };
}

export function buildImpact(overrides: Partial<CapacityChangeResult> = {}): CapacityChangeResult {
  return { affected: 0, reassigned: 0, waitlisted: 0, cancelled: 0, ...overrides };
}

export function buildBooking(overrides: Partial<AdminBooking> = {}): AdminBooking {
  return {
    id: '00000000-0000-0000-0000-00000000e001', userId: USER_ID, userEmail: 'test@schuler.de',
    userDisplayName: 'Test User', locationId: LOCATION_ID, locationName: 'Göppingen Werk 1',
    parkingSlotId: SLOT_ID, parkingSlotNumber: 'P1', date: '2026-11-03', timeSlot: 'Morning',
    status: 'Won', confirmedAt: null, createdAt: '2026-10-20T08:00:00Z', cancelledAt: null,
    cancelReason: null, cancelledByUserId: null, autoConfirmReason: null, ...overrides,
  };
}

export function buildLotteryRun(overrides: Partial<LotteryRun> = {}): LotteryRun {
  return {
    id: '00000000-0000-0000-0000-00000000f001', locationId: LOCATION_ID, locationName: 'Göppingen Werk 1',
    date: '2026-11-03', timeSlot: 'Morning', algorithm: 'Random', ranAt: '2026-11-02T16:00:00Z',
    totalBookings: 12, availableSlots: 10, ...overrides,
  };
}

/** Axios-shaped fake for `services/api`: each verb resolves `{ data }` with the queued or default payload. */
export function createApiFake() {
  const respond = (data: unknown = undefined) => Promise.resolve({ data });
  return {
    get: vi.fn((_url: string, _config?: unknown) => respond()),
    post: vi.fn((_url: string, _body?: unknown, _config?: unknown) => respond()),
    put: vi.fn((_url: string, _body?: unknown, _config?: unknown) => respond()),
    patch: vi.fn((_url: string, _body?: unknown, _config?: unknown) => respond()),
    delete: vi.fn((_url: string, _config?: unknown) => respond()),
  };
}

export type ApiFake = ReturnType<typeof createApiFake>;

/** Builds the axios error shape the pages read (`response.data.message` / `.errors`). */
export function apiError(status: number, data: Record<string, unknown> = {}) {
  return Object.assign(new Error(`Request failed with status code ${status}`), {
    isAxiosError: true, response: { status, data },
  });
}
