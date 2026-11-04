import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

import api from './api';
import { adminService } from './adminService';
import type { AdminLocation, AdminSlot, CapacityChangeResult } from '../types/admin';
import type { GridConfiguration } from '../types/grid';

const ok = <T,>(data: T) => Promise.resolve({ data });

const location: AdminLocation = { id: 'l1', name: 'Nord', isActive: true, defaultAlgorithm: 'PureRandom', totalSlots: 3, activeSlots: 2 };
const slot: AdminSlot = { id: 's1', locationId: 'l1', slotNumber: 'P001', label: null, isActive: true };
const impact: CapacityChangeResult = { affected: 2, reassigned: 1, waitlisted: 0, cancelled: 1 };

describe('adminService', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('locations', () => {
    it('getLocations() GETs /admin/locations and unwraps the data', async () => {
      vi.mocked(api.get).mockReturnValueOnce(ok([location]));

      await expect(adminService.getLocations()).resolves.toEqual([location]);
      expect(api.get).toHaveBeenCalledWith('/admin/locations');
    });

    it('createLocation() POSTs the name', async () => {
      vi.mocked(api.post).mockReturnValueOnce(ok(location));

      await expect(adminService.createLocation({ name: 'Nord' })).resolves.toBe(location);
      expect(api.post).toHaveBeenCalledWith('/admin/locations', { name: 'Nord' });
    });

    it('updateLocation() PUTs name and active flag to /admin/locations/{id}', async () => {
      vi.mocked(api.put).mockReturnValueOnce(ok({ ...location, name: 'Süd' }));

      await expect(adminService.updateLocation('l1', { name: 'Süd', isActive: false })).resolves.toMatchObject({ name: 'Süd' });
      expect(api.put).toHaveBeenCalledWith('/admin/locations/l1', { name: 'Süd', isActive: false });
    });

    it('deactivateLocation() DELETEs the location and resolves with the impact', async () => {
      vi.mocked(api.delete).mockReturnValueOnce(ok(impact));

      await expect(adminService.deactivateLocation('l1')).resolves.toEqual(impact);
      expect(api.delete).toHaveBeenCalledWith('/admin/locations/l1');
    });

    it('setAlgorithm() PUTs the algorithm to /admin/locations/{id}/algorithm', async () => {
      vi.mocked(api.put).mockReturnValueOnce(ok(undefined));

      await adminService.setAlgorithm('l1', 'RoundRobin');
      expect(api.put).toHaveBeenCalledWith('/admin/locations/l1/algorithm', { algorithm: 'RoundRobin' });
    });
  });

  describe('slots', () => {
    it('getSlots() GETs /admin/slots filtered by locationId', async () => {
      vi.mocked(api.get).mockReturnValueOnce(ok([slot]));

      await expect(adminService.getSlots('l1')).resolves.toEqual([slot]);
      expect(api.get).toHaveBeenCalledWith('/admin/slots', { params: { locationId: 'l1' } });
    });

    it('createSlot() POSTs the request body unchanged', async () => {
      vi.mocked(api.post).mockReturnValueOnce(ok(slot));
      const body = { locationId: 'l1', slotNumber: 'P001', label: 'Near entrance' };

      await expect(adminService.createSlot(body)).resolves.toBe(slot);
      expect(api.post).toHaveBeenCalledWith('/admin/slots', body);
    });

    it('updateSlot() PUTs to /admin/slots/{id}', async () => {
      vi.mocked(api.put).mockReturnValueOnce(ok(slot));
      const body = { slotNumber: 'P002', isActive: false };

      await adminService.updateSlot('s1', body);
      expect(api.put).toHaveBeenCalledWith('/admin/slots/s1', body);
    });

    it('deactivateSlot() DELETEs the slot and resolves with the impact', async () => {
      vi.mocked(api.delete).mockReturnValueOnce(ok(impact));

      await expect(adminService.deactivateSlot('s1')).resolves.toEqual(impact);
      expect(api.delete).toHaveBeenCalledWith('/admin/slots/s1');
    });
  });

  describe('blocked days', () => {
    it('getBlockedDays() passes locationId and the optional range as params', async () => {
      vi.mocked(api.get).mockReturnValue(ok([]));

      await adminService.getBlockedDays('l1');
      await adminService.getBlockedDays('l1', '2026-11-01', '2026-11-30');
      expect(api.get).toHaveBeenNthCalledWith(1, '/admin/blocked-days', { params: { locationId: 'l1', from: undefined, to: undefined } });
      expect(api.get).toHaveBeenNthCalledWith(2, '/admin/blocked-days', { params: { locationId: 'l1', from: '2026-11-01', to: '2026-11-30' } });
    });

    it('createBlockedDay() POSTs the body and unwraps block plus impact', async () => {
      const created = { blockedDay: { id: 'b1' }, impact };
      vi.mocked(api.post).mockReturnValueOnce(ok(created));

      await expect(adminService.createBlockedDay({ locationId: 'l1', date: '2026-11-20', reason: 'Works' })).resolves.toBe(created);
      expect(api.post).toHaveBeenCalledWith('/admin/blocked-days', { locationId: 'l1', date: '2026-11-20', reason: 'Works' });
    });

    it('removeBlockedDay() DELETEs /admin/blocked-days/{id}', async () => {
      vi.mocked(api.delete).mockReturnValueOnce(ok(undefined));

      await adminService.removeBlockedDay('b1');
      expect(api.delete).toHaveBeenCalledWith('/admin/blocked-days/b1');
    });
  });

  describe('bookings', () => {
    it('getBookings() GETs /admin/bookings with the query as params', async () => {
      const res = { bookings: [], totalCount: 0, page: 2, pageSize: 20 };
      vi.mocked(api.get).mockReturnValueOnce(ok(res));
      const query = { locationId: 'l1', status: 'Won,Confirmed', from: '2026-11-01', to: '2026-11-30', page: 2, pageSize: 20 };

      await expect(adminService.getBookings(query)).resolves.toBe(res);
      expect(api.get).toHaveBeenCalledWith('/admin/bookings', { params: query });
    });

    it('countBookings() asks for a single row and resolves with totalCount only', async () => {
      vi.mocked(api.get).mockReturnValueOnce(ok({ bookings: [], totalCount: 7, page: 1, pageSize: 1 }));

      await expect(adminService.countBookings({ parkingSlotId: 's1', from: '2026-11-03', status: 'Won,Confirmed' })).resolves.toBe(7);
      expect(api.get).toHaveBeenCalledWith('/admin/bookings', {
        params: { parkingSlotId: 's1', from: '2026-11-03', status: 'Won,Confirmed', page: 1, pageSize: 1 },
      });
    });

    it('countBookings() resolves with 0 when nothing matches', async () => {
      vi.mocked(api.get).mockReturnValueOnce(ok({ bookings: [], totalCount: 0, page: 1, pageSize: 1 }));

      await expect(adminService.countBookings({ locationId: 'l1' })).resolves.toBe(0);
    });
  });

  describe('lottery runs', () => {
    it('getLotteryRuns() GETs /admin/lottery-runs with params', async () => {
      const res = { lotteryRuns: [], totalCount: 0, page: 1, pageSize: 10 };
      vi.mocked(api.get).mockReturnValueOnce(ok(res));

      await expect(adminService.getLotteryRuns({ locationId: 'l1', page: 1, pageSize: 10 })).resolves.toBe(res);
      expect(api.get).toHaveBeenCalledWith('/admin/lottery-runs', { params: { locationId: 'l1', page: 1, pageSize: 10 } });
    });
  });

  describe('grid layout', () => {
    const config: GridConfiguration = { gridRows: 2, gridColumns: 3, slots: [], cells: [] };

    it('getGridConfiguration() GETs /admin/locations/{id}/grid', async () => {
      vi.mocked(api.get).mockReturnValueOnce(ok(config));

      await expect(adminService.getGridConfiguration('l1')).resolves.toBe(config);
      expect(api.get).toHaveBeenCalledWith('/admin/locations/l1/grid');
    });

    it('saveGridConfiguration() PUTs the layout to /admin/locations/{id}/grid', async () => {
      vi.mocked(api.put).mockReturnValueOnce(ok(config));
      const body = {
        gridRows: 2,
        gridColumns: 3,
        slotPositions: [{ slotId: 's1', row: 0, column: 1 }],
        cells: [{ row: 1, column: 1, cellType: 'Road' as const }],
      };

      await expect(adminService.saveGridConfiguration('l1', body)).resolves.toBe(config);
      expect(api.put).toHaveBeenCalledWith('/admin/locations/l1/grid', body);
    });
  });

  it('propagates request errors from every verb', async () => {
    const err = { response: { status: 409, data: { error: 'Conflict' } } };
    vi.mocked(api.get).mockReturnValueOnce(Promise.reject(err));
    vi.mocked(api.post).mockReturnValueOnce(Promise.reject(err));
    vi.mocked(api.put).mockReturnValueOnce(Promise.reject(err));
    vi.mocked(api.delete).mockReturnValueOnce(Promise.reject(err));

    await expect(adminService.getLocations()).rejects.toBe(err);
    await expect(adminService.createLocation({ name: 'Dup' })).rejects.toBe(err);
    await expect(adminService.updateSlot('s1', { slotNumber: 'P1', isActive: true })).rejects.toBe(err);
    await expect(adminService.deactivateLocation('l1')).rejects.toBe(err);
  });
});
