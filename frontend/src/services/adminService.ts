import api from './api';
import type {
  AdminLocation, CreateLocationRequest, UpdateLocationRequest,
  AdminSlot, CreateSlotRequest, UpdateSlotRequest,
  AdminBlockedDay, CreateBlockedDayRequest,
  AdminBooking, LotteryRun,
} from '../types/admin';

export const adminService = {
  // Locations
  getLocations: () =>
    api.get<AdminLocation[]>('/admin/locations').then(r => r.data),
  createLocation: (data: CreateLocationRequest) =>
    api.post<AdminLocation>('/admin/locations', data).then(r => r.data),
  updateLocation: (id: string, data: UpdateLocationRequest) =>
    api.put<AdminLocation>(`/admin/locations/${id}`, data).then(r => r.data),
  deactivateLocation: (id: string) =>
    api.delete(`/admin/locations/${id}`),
  setAlgorithm: (id: string, algorithm: string) =>
    api.put(`/admin/locations/${id}/algorithm`, { algorithm }),

  // Slots
  getSlots: (locationId: string) =>
    api.get<AdminSlot[]>('/admin/slots', { params: { locationId } }).then(r => r.data),
  createSlot: (data: CreateSlotRequest) =>
    api.post<AdminSlot>('/admin/slots', data).then(r => r.data),
  updateSlot: (id: string, data: UpdateSlotRequest) =>
    api.put<AdminSlot>(`/admin/slots/${id}`, data).then(r => r.data),
  deactivateSlot: (id: string) =>
    api.delete(`/admin/slots/${id}`),

  // Blocked Days
  getBlockedDays: (locationId: string, from?: string, to?: string) =>
    api.get<AdminBlockedDay[]>('/admin/blocked-days', { params: { locationId, from, to } }).then(r => r.data),
  createBlockedDay: (data: CreateBlockedDayRequest) =>
    api.post<AdminBlockedDay>('/admin/blocked-days', data).then(r => r.data),
  removeBlockedDay: (id: string) =>
    api.delete(`/admin/blocked-days/${id}`),

  // Bookings
  getBookings: (params: {
    locationId?: string; status?: string; from?: string; to?: string;
    userId?: string; page?: number; pageSize?: number;
  }) =>
    api.get<{ bookings: AdminBooking[]; totalCount: number; page: number; pageSize: number }>(
      '/admin/bookings', { params }).then(r => r.data),

  // Lottery Runs
  getLotteryRuns: (params: {
    locationId?: string; from?: string; to?: string; page?: number; pageSize?: number;
  }) =>
    api.get<{ lotteryRuns: LotteryRun[]; totalCount: number; page: number; pageSize: number }>(
      '/admin/lottery-runs', { params }).then(r => r.data),
};
