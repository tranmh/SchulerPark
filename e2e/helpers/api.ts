import { request as playwrightRequest, type APIRequestContext } from '@playwright/test';
import { adminToken } from './auth';

/** Thin typed wrapper for /api/admin/* — set up + teardown for tests. */
export class AdminApi {
  constructor(public readonly request: APIRequestContext, private readonly token: string) {}

  /**
   * Build an AdminApi with a freshly-created APIRequestContext so it can be
   * used inside beforeAll/afterAll (Playwright forbids reusing the per-test
   * `request` fixture from beforeAll).
   */
  static async create(baseURL: string): Promise<AdminApi> {
    const ctx = await playwrightRequest.newContext({ baseURL });
    const token = await adminToken(ctx);
    return new AdminApi(ctx, token);
  }

  async dispose() {
    await this.request.dispose();
  }

  private headers() {
    return { Authorization: `Bearer ${this.token}` };
  }

  // Locations
  listLocations() {
    return this.request.get('/api/admin/locations', { headers: this.headers() }).then(r => r.json());
  }
  createLocation(name: string, address: string) {
    return this.request
      .post('/api/admin/locations', { headers: this.headers(), data: { name, address } })
      .then(r => r.json());
  }
  updateLocation(id: string, body: { name?: string; address?: string; isActive?: boolean }) {
    return this.request.put(`/api/admin/locations/${id}`, { headers: this.headers(), data: body });
  }
  deactivateLocation(id: string) {
    return this.request.delete(`/api/admin/locations/${id}`, { headers: this.headers() });
  }

  // Slots
  listSlots(locationId: string) {
    return this.request
      .get(`/api/admin/slots?locationId=${locationId}`, { headers: this.headers() })
      .then(r => r.json());
  }
  createSlot(locationId: string, slotNumber: string, label?: string) {
    return this.request
      .post('/api/admin/slots', { headers: this.headers(), data: { locationId, slotNumber, label } })
      .then(r => r.json());
  }
  deactivateSlot(id: string) {
    return this.request.delete(`/api/admin/slots/${id}`, { headers: this.headers() });
  }

  // Blocked days
  listBlockedDays(locationId: string) {
    return this.request
      .get(`/api/admin/blocked-days?locationId=${locationId}`, { headers: this.headers() })
      .then(r => r.json());
  }
  createBlockedDay(body: { locationId: string; date: string; reason?: string; parkingSlotId?: string }) {
    return this.request
      .post('/api/admin/blocked-days', { headers: this.headers(), data: body })
      .then(r => r.json());
  }
  removeBlockedDay(id: string) {
    return this.request.delete(`/api/admin/blocked-days/${id}`, { headers: this.headers() });
  }

  // Lottery
  runAll(date: string) {
    return this.request.post(`/api/lottery/run?date=${date}`, { headers: this.headers() });
  }
  runForLocation(locationId: string, date: string, timeSlot: 'Morning' | 'Afternoon') {
    return this.request.post(
      `/api/lottery/run/${locationId}?date=${date}&timeSlot=${timeSlot}`,
      { headers: this.headers() }
    );
  }

  // Lottery runs (read)
  getLotteryRuns(params: { locationId?: string; page?: number; pageSize?: number } = {}) {
    const qs = new URLSearchParams();
    for (const [k, v] of Object.entries(params)) if (v != null) qs.set(k, String(v));
    return this.request
      .get(`/api/admin/lottery-runs?${qs}`, { headers: this.headers() })
      .then(r => r.json());
  }

  // Bookings
  getBookings(params: {
    locationId?: string; status?: string; from?: string; to?: string;
    userId?: string; page?: number; pageSize?: number;
  } = {}) {
    const qs = new URLSearchParams();
    for (const [k, v] of Object.entries(params)) if (v != null) qs.set(k, String(v));
    return this.request
      .get(`/api/admin/bookings?${qs}`, { headers: this.headers() })
      .then(r => r.json());
  }
}
