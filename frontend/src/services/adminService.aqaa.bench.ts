import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios';
import { bench, describe } from 'vitest';
import api from './api';
import { adminService } from './adminService';

// A stub adapter: requests run through axios and its interceptors, never the network.
let payload: unknown = null;
const adapter: AxiosAdapter = (config: InternalAxiosRequestConfig) =>
  Promise.resolve({ data: payload, status: 200, statusText: 'OK', headers: {}, config });
api.defaults.adapter = adapter;

let sink = 0;
const sizes = [10, 1000, 10000];

function bookings(n: number) {
  return {
    bookings: Array.from({ length: n }, (_, i) => ({ id: `b${i}`, date: '2026-10-27', status: 'Won' })),
    totalCount: n,
    page: 1,
    pageSize: n,
  };
}

describe('adminService.getBookings (response size)', () => {
  for (const n of sizes) {
    const data = bookings(n);
    bench(`bookings=${n}`, async () => {
      payload = data;
      const r = await adminService.getBookings({ locationId: 'loc', status: 'Won,Confirmed', page: 1, pageSize: n });
      sink += r.bookings.length;
    });
  }
});

describe('adminService.saveGridConfiguration (payload size)', () => {
  for (const n of sizes) {
    const data = { cells: Array.from({ length: n }, (_, i) => ({ row: i % 50, column: Math.floor(i / 50), type: 'Slot' })) };
    bench(`cells=${n}`, async () => {
      payload = data;
      const r = await adminService.saveGridConfiguration('loc', data as never);
      sink += r ? 1 : 0;
    });
  }
});

describe('adminService simple calls', () => {
  bench('countBookings', async () => {
    payload = { totalCount: 42 };
    sink += await adminService.countBookings({ locationId: 'loc', status: 'Won' });
  });
  bench('getBlockedDays', async () => {
    payload = [];
    sink += (await adminService.getBlockedDays('loc', '2026-10-01', '2026-10-31')).length;
  });
});

export { sink };
