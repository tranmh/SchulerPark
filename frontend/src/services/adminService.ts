import api from './api';
import type {
  AdminLocation, CreateLocationRequest, UpdateLocationRequest,
  AdminSlot, CreateSlotRequest, UpdateSlotRequest,
  AdminBlockedDay, AdminBlockedDayCreated, CreateBlockedDayRequest,
  AdminBooking, CapacityChangeResult, LotteryRun,
} from '../types/admin';
import type { GridConfiguration, SaveGridConfigurationRequest } from '../types/grid';

export interface AdminBookingQuery {
  locationId?: string;
  /** One status or a comma-separated list, e.g. "Won,Confirmed". */
  status?: string;
  from?: string;
  to?: string;
  userId?: string;
  parkingSlotId?: string;
  page?: number;
  pageSize?: number;
}

export const adminService = {
  // Locations
  getLocations: () =>
    api.get<AdminLocation[]>('/admin/locations').then(r => r.data),
  createLocation: (data: CreateLocationRequest) =>
    api.post<AdminLocation>('/admin/locations', data).then(r => r.data),
  updateLocation: (id: string, data: UpdateLocationRequest) =>
    api.put<AdminLocation>(`/admin/locations/${id}`, data).then(r => r.data),
  /** Deactivates the location; resolves with what happened to its future bookings. */
  deactivateLocation: (id: string) =>
    api.delete<CapacityChangeResult>(`/admin/locations/${id}`).then(r => r.data),
  setAlgorithm: (id: string, algorithm: string) =>
    api.put(`/admin/locations/${id}/algorithm`, { algorithm }),

  // Slots
  getSlots: (locationId: string) =>
    api.get<AdminSlot[]>('/admin/slots', { params: { locationId } }).then(r => r.data),
  createSlot: (data: CreateSlotRequest) =>
    api.post<AdminSlot>('/admin/slots', data).then(r => r.data),
  updateSlot: (id: string, data: UpdateSlotRequest) =>
    api.put<AdminSlot>(`/admin/slots/${id}`, data).then(r => r.data),
  /** Deactivates the slot; resolves with what happened to the bookings holding it. */
  deactivateSlot: (id: string) =>
    api.delete<CapacityChangeResult>(`/admin/slots/${id}`).then(r => r.data),

  // Blocked Days
  getBlockedDays: (locationId: string, from?: string, to?: string) =>
    api.get<AdminBlockedDay[]>('/admin/blocked-days', { params: { locationId, from, to } }).then(r => r.data),
  /** Creates the block; resolves with the block plus its impact on existing bookings. */
  createBlockedDay: (data: CreateBlockedDayRequest) =>
    api.post<AdminBlockedDayCreated>('/admin/blocked-days', data).then(r => r.data),
  removeBlockedDay: (id: string) =>
    api.delete(`/admin/blocked-days/${id}`),

  // Bookings
  getBookings: (params: AdminBookingQuery) =>
    api.get<{ bookings: AdminBooking[]; totalCount: number; page: number; pageSize: number }>(
      '/admin/bookings', { params }).then(r => r.data),
  /** Number of bookings matching the filters (used for the capacity-impact preview). */
  countBookings: (params: Omit<AdminBookingQuery, 'page' | 'pageSize'>) =>
    api.get<{ totalCount: number }>('/admin/bookings', { params: { ...params, page: 1, pageSize: 1 } })
      .then(r => r.data.totalCount),

  // Lottery Runs
  getLotteryRuns: (params: {
    locationId?: string; from?: string; to?: string; page?: number; pageSize?: number;
  }) =>
    api.get<{ lotteryRuns: LotteryRun[]; totalCount: number; page: number; pageSize: number }>(
      '/admin/lottery-runs', { params }).then(r => r.data),

  // Grid Layout
  getGridConfiguration: (locationId: string) =>
    api.get<GridConfiguration>(`/admin/locations/${locationId}/grid`).then(r => r.data),
  saveGridConfiguration: (locationId: string, data: SaveGridConfigurationRequest) =>
    api.put<GridConfiguration>(`/admin/locations/${locationId}/grid`, data).then(r => r.data),
};
