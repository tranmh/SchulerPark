import { describe, it, expect, vi, beforeEach } from 'vitest';
import api from './api';
import { adminService } from './adminService';
import {
  buildLocation, buildImpact, buildBlockedDay, buildSlot, buildBooking, buildLotteryRun, apiError,
  LOCATION_ID, SLOT_ID, USER_ID, type ApiFake,
} from '../__tests__/support/adminFixtures';

vi.mock('./api', async () => {
  const { createApiFake } = await import('../__tests__/support/adminFixtures');
  return { default: createApiFake() };
});

const fake = api as unknown as ApiFake;

describe('adminService', () => {
  beforeEach(() => vi.clearAllMocks());

  it('getLocations unwraps the response data', async () => {
    const locations = [buildLocation()];
    fake.get.mockResolvedValueOnce({ data: locations });
    await expect(adminService.getLocations()).resolves.toEqual(locations);
    expect(fake.get).toHaveBeenCalledWith('/admin/locations');
  });

  it('deactivateSlot deletes by id and returns the capacity impact', async () => {
    const impact = buildImpact({ affected: 2, waitlisted: 2 });
    fake.delete.mockResolvedValueOnce({ data: impact });
    await expect(adminService.deactivateSlot(SLOT_ID)).resolves.toEqual(impact);
    expect(fake.delete).toHaveBeenCalledWith(`/admin/slots/${SLOT_ID}`);
  });

  it('createBlockedDay posts the request body', async () => {
    const created = { blockedDay: buildBlockedDay(), impact: buildImpact() };
    fake.post.mockResolvedValueOnce({ data: created });
    const body = { locationId: LOCATION_ID, date: '2026-11-02', reason: 'Betriebsversammlung' };
    await expect(adminService.createBlockedDay(body)).resolves.toEqual(created);
    expect(fake.post).toHaveBeenCalledWith('/admin/blocked-days', body);
  });

  it('countBookings forces page 1 / pageSize 1 and returns totalCount', async () => {
    fake.get.mockResolvedValueOnce({ data: { totalCount: 7 } });
    await expect(adminService.countBookings({ locationId: LOCATION_ID, status: 'Won,Confirmed' })).resolves.toBe(7);
    expect(fake.get).toHaveBeenCalledWith('/admin/bookings', {
      params: { locationId: LOCATION_ID, status: 'Won,Confirmed', page: 1, pageSize: 1 },
    });
  });

  it('setAlgorithm puts the algorithm to the location endpoint', async () => {
    await adminService.setAlgorithm(LOCATION_ID, 'WeightedHistory');
    expect(fake.put).toHaveBeenCalledWith(`/admin/locations/${LOCATION_ID}/algorithm`, { algorithm: 'WeightedHistory' });
  });

  it('getGridConfiguration targets the location grid endpoint', async () => {
    fake.get.mockResolvedValueOnce({ data: { cells: [] } });
    await adminService.getGridConfiguration(LOCATION_ID);
    expect(fake.get).toHaveBeenCalledWith(`/admin/locations/${LOCATION_ID}/grid`);
  });

  it('createLocation / updateLocation post and put the body and unwrap the location', async () => {
    const location = buildLocation();
    fake.post.mockResolvedValueOnce({ data: location });
    fake.put.mockResolvedValueOnce({ data: location });
    const body = { name: 'Göppingen Werk 1' } as Parameters<typeof adminService.createLocation>[0];

    await expect(adminService.createLocation(body)).resolves.toEqual(location);
    await expect(adminService.updateLocation(LOCATION_ID, body)).resolves.toEqual(location);
    expect(fake.post).toHaveBeenCalledWith('/admin/locations', body);
    expect(fake.put).toHaveBeenCalledWith(`/admin/locations/${LOCATION_ID}`, body);
  });

  it('deactivateLocation deletes by id and returns the impact', async () => {
    const impact = buildImpact({ affected: 3, cancelled: 3 });
    fake.delete.mockResolvedValueOnce({ data: impact });
    await expect(adminService.deactivateLocation(LOCATION_ID)).resolves.toEqual(impact);
    expect(fake.delete).toHaveBeenCalledWith(`/admin/locations/${LOCATION_ID}`);
  });

  it('getSlots filters by location as a query param', async () => {
    const slots = [buildSlot()];
    fake.get.mockResolvedValueOnce({ data: slots });
    await expect(adminService.getSlots(LOCATION_ID)).resolves.toEqual(slots);
    expect(fake.get).toHaveBeenCalledWith('/admin/slots', { params: { locationId: LOCATION_ID } });
  });

  it('createSlot / updateSlot hit the slot endpoints', async () => {
    const slot = buildSlot();
    fake.post.mockResolvedValueOnce({ data: slot });
    fake.put.mockResolvedValueOnce({ data: slot });
    const create = { locationId: LOCATION_ID, slotNumber: 'P1' } as Parameters<typeof adminService.createSlot>[0];
    const update = { slotNumber: 'P2' } as Parameters<typeof adminService.updateSlot>[1];

    await expect(adminService.createSlot(create)).resolves.toEqual(slot);
    await expect(adminService.updateSlot(SLOT_ID, update)).resolves.toEqual(slot);
    expect(fake.post).toHaveBeenCalledWith('/admin/slots', create);
    expect(fake.put).toHaveBeenCalledWith(`/admin/slots/${SLOT_ID}`, update);
  });

  it('getBlockedDays passes the optional range (undefined when omitted)', async () => {
    fake.get.mockResolvedValue({ data: [] });
    await adminService.getBlockedDays(LOCATION_ID, '2026-11-01', '2026-11-30');
    await adminService.getBlockedDays(LOCATION_ID);
    expect(fake.get).toHaveBeenNthCalledWith(1, '/admin/blocked-days', {
      params: { locationId: LOCATION_ID, from: '2026-11-01', to: '2026-11-30' },
    });
    expect(fake.get).toHaveBeenNthCalledWith(2, '/admin/blocked-days', {
      params: { locationId: LOCATION_ID, from: undefined, to: undefined },
    });
  });

  it('removeBlockedDay deletes by id', async () => {
    await adminService.removeBlockedDay('bd-1');
    expect(fake.delete).toHaveBeenCalledWith('/admin/blocked-days/bd-1');
  });

  it('getBookings forwards all filters and unwraps the page', async () => {
    const page = { bookings: [buildBooking()], totalCount: 1, page: 2, pageSize: 50 };
    fake.get.mockResolvedValueOnce({ data: page });
    const params = { locationId: LOCATION_ID, status: 'Won', from: '2026-11-01', to: '2026-11-07', userId: USER_ID, page: 2, pageSize: 50 };
    await expect(adminService.getBookings(params)).resolves.toEqual(page);
    expect(fake.get).toHaveBeenCalledWith('/admin/bookings', { params });
  });

  it('countBookings returns 0 when nothing matches', async () => {
    fake.get.mockResolvedValueOnce({ data: { totalCount: 0 } });
    await expect(adminService.countBookings({})).resolves.toBe(0);
    expect(fake.get).toHaveBeenCalledWith('/admin/bookings', { params: { page: 1, pageSize: 1 } });
  });

  it('getLotteryRuns forwards the filters', async () => {
    const page = { lotteryRuns: [buildLotteryRun()], totalCount: 1, page: 1, pageSize: 20 };
    fake.get.mockResolvedValueOnce({ data: page });
    await expect(adminService.getLotteryRuns({ locationId: LOCATION_ID, page: 1 })).resolves.toEqual(page);
    expect(fake.get).toHaveBeenCalledWith('/admin/lottery-runs', { params: { locationId: LOCATION_ID, page: 1 } });
  });

  it('saveGridConfiguration puts the grid', async () => {
    const grid = { cells: [] } as unknown as Parameters<typeof adminService.saveGridConfiguration>[1];
    fake.put.mockResolvedValueOnce({ data: grid });
    await expect(adminService.saveGridConfiguration(LOCATION_ID, grid)).resolves.toEqual(grid);
    expect(fake.put).toHaveBeenCalledWith(`/admin/locations/${LOCATION_ID}/grid`, grid);
  });

  it('propagates API errors to the caller', async () => {
    const err = apiError(409, { detail: 'Slot number already exists.' });
    fake.post.mockRejectedValueOnce(err);
    await expect(adminService.createSlot({} as Parameters<typeof adminService.createSlot>[0])).rejects.toBe(err);
  });
});
