import { describe, it, expect, vi, beforeEach } from 'vitest';

const api = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
}));

vi.mock('./api', () => ({ default: api }));

import { adminService } from './adminService';

describe('adminService', () => {
  beforeEach(() => {
    for (const fn of Object.values(api)) {
      fn.mockReset();
      fn.mockResolvedValue({ data: { ok: true } });
    }
  });

  it('getLocations GETs /admin/locations and unwraps data', async () => {
    api.get.mockResolvedValueOnce({ data: [{ id: 'l1' }] });
    await expect(adminService.getLocations()).resolves.toEqual([{ id: 'l1' }]);
    expect(api.get).toHaveBeenCalledWith('/admin/locations');
  });

  it('createLocation POSTs the payload', async () => {
    const body = { name: 'HQ' } as unknown as Parameters<typeof adminService.createLocation>[0];
    await expect(adminService.createLocation(body)).resolves.toEqual({ ok: true });
    expect(api.post).toHaveBeenCalledWith('/admin/locations', body);
  });

  it('updateLocation PUTs to the location id', async () => {
    const body = { name: 'HQ' } as unknown as Parameters<typeof adminService.updateLocation>[1];
    await adminService.updateLocation('l1', body);
    expect(api.put).toHaveBeenCalledWith('/admin/locations/l1', body);
  });

  it('deactivateLocation DELETEs and resolves with the capacity result', async () => {
    api.delete.mockResolvedValueOnce({ data: { cancelled: 2 } });
    await expect(adminService.deactivateLocation('l1')).resolves.toEqual({ cancelled: 2 });
    expect(api.delete).toHaveBeenCalledWith('/admin/locations/l1');
  });

  it('setAlgorithm PUTs the algorithm', async () => {
    await adminService.setAlgorithm('l1', 'Lottery');
    expect(api.put).toHaveBeenCalledWith('/admin/locations/l1/algorithm', { algorithm: 'Lottery' });
  });

  it('getSlots passes the locationId as query param', async () => {
    await adminService.getSlots('l1');
    expect(api.get).toHaveBeenCalledWith('/admin/slots', { params: { locationId: 'l1' } });
  });

  it('createSlot, updateSlot and deactivateSlot hit the slot endpoints', async () => {
    const body = { label: 'A1' } as unknown as Parameters<typeof adminService.createSlot>[0];
    await adminService.createSlot(body);
    await adminService.updateSlot('s1', body as unknown as Parameters<typeof adminService.updateSlot>[1]);
    await adminService.deactivateSlot('s1');
    expect(api.post).toHaveBeenCalledWith('/admin/slots', body);
    expect(api.put).toHaveBeenCalledWith('/admin/slots/s1', body);
    expect(api.delete).toHaveBeenCalledWith('/admin/slots/s1');
  });

  it('getBlockedDays sends locationId and the optional range', async () => {
    await adminService.getBlockedDays('l1', '2026-11-01', '2026-11-30');
    expect(api.get).toHaveBeenCalledWith('/admin/blocked-days', {
      params: { locationId: 'l1', from: '2026-11-01', to: '2026-11-30' },
    });
  });

  it('createBlockedDay POSTs and removeBlockedDay DELETEs by id', async () => {
    const body = { date: '2026-12-24' } as unknown as Parameters<typeof adminService.createBlockedDay>[0];
    await adminService.createBlockedDay(body);
    await adminService.removeBlockedDay('b1');
    expect(api.post).toHaveBeenCalledWith('/admin/blocked-days', body);
    expect(api.delete).toHaveBeenCalledWith('/admin/blocked-days/b1');
  });

  it('getBookings forwards the query as params', async () => {
    const params = { locationId: 'l1', status: 'Won,Confirmed', page: 2, pageSize: 50 };
    await adminService.getBookings(params);
    expect(api.get).toHaveBeenCalledWith('/admin/bookings', { params });
  });

  it('countBookings asks for one row and returns the total count', async () => {
    api.get.mockResolvedValueOnce({ data: { totalCount: 17, bookings: [] } });
    await expect(adminService.countBookings({ locationId: 'l1' })).resolves.toBe(17);
    expect(api.get).toHaveBeenCalledWith('/admin/bookings', {
      params: { locationId: 'l1', page: 1, pageSize: 1 },
    });
  });

  it('getLotteryRuns forwards the params', async () => {
    await adminService.getLotteryRuns({ locationId: 'l1', page: 1 });
    expect(api.get).toHaveBeenCalledWith('/admin/lottery-runs', { params: { locationId: 'l1', page: 1 } });
  });

  it('grid configuration is read and saved per location', async () => {
    const body = { cells: [] } as unknown as Parameters<typeof adminService.saveGridConfiguration>[1];
    await adminService.getGridConfiguration('l1');
    await adminService.saveGridConfiguration('l1', body);
    expect(api.get).toHaveBeenCalledWith('/admin/locations/l1/grid');
    expect(api.put).toHaveBeenCalledWith('/admin/locations/l1/grid', body);
  });

  it('propagates API errors', async () => {
    const err = new Error('500');
    api.get.mockRejectedValueOnce(err);
    await expect(adminService.getLocations()).rejects.toBe(err);
  });
});
