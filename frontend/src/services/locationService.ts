import api from './api';
import type { Location, ParkingSlot, BlockedDay, Availability } from '../types/booking';

export const locationService = {
  getLocations: () =>
    api.get<Location[]>('/locations').then(r => r.data),

  getSlots: (locationId: string) =>
    api.get<ParkingSlot[]>(`/locations/${locationId}/slots`).then(r => r.data),

  getBlockedDays: (locationId: string, from?: string, to?: string) =>
    api.get<BlockedDay[]>(`/locations/${locationId}/blocked-days`, {
      params: { from, to },
    }).then(r => r.data),

  getAvailability: (locationId: string, from?: string, to?: string) =>
    api.get<Availability[]>(`/locations/${locationId}/availability`, {
      params: { from, to },
    }).then(r => r.data),
};
