import { describe, it, expect, vi, beforeEach } from 'vitest';
import api from './api';
import { adminService } from './adminService';
import {
  buildLocation, buildImpact, buildBlockedDay, LOCATION_ID, SLOT_ID, type ApiFake,
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
});
