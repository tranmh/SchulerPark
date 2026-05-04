import { test, expect } from '@playwright/test';
import { loginAsFinn, USER_FINN, apiLogin } from '../../helpers/auth';
import { nextMonday } from '../../helpers/data';

test.describe('User → Week booking', () => {
  test('week booking via API creates Mon-Fri bookings (or skips conflicts)', async ({ request }) => {
    const token = await apiLogin(request, USER_FINN.email, USER_FINN.password);
    const locsRes = await request.get('/api/locations', {
      headers: { Authorization: `Bearer ${token}` },
    });
    const locations: Array<{ id: string; name: string }> = await locsRes.json();
    const loc = locations.find((l) => l.name === 'Hessdorf') ?? locations[0];

    // Pick a Monday far enough in the future to avoid conflicts with bookings
    // that other tests may have created for "next Monday".
    const monday = (() => {
      const d = new Date();
      d.setDate(d.getDate() + 21);
      while (d.getDay() !== 1) d.setDate(d.getDate() + 1);
      return d.toISOString().split('T')[0];
    })();
    const res = await request.post('/api/bookings/week', {
      headers: { Authorization: `Bearer ${token}` },
      data: { locationId: loc.id, weekStartDate: monday, timeSlot: 'Morning' },
    });
    expect(res.ok(), `expected ok, got ${res.status()}: ${await res.text()}`).toBeTruthy();
    const body = await res.json();
    expect(Array.isArray(body.createdBookings)).toBe(true);
    expect(Array.isArray(body.skippedDays)).toBe(true);
    expect(body.createdBookings.length + body.skippedDays.length).toBeLessThanOrEqual(5);
  });

  test('week-mode booking through the UI shows the summary screen', async ({ page }) => {
    await loginAsFinn(page);
    await page.goto('/booking');

    await page.getByRole('button', { name: /^Goeppingen/ }).click();

    // Toggle week mode
    await page.getByRole('checkbox', { name: /book entire week/i }).check();

    // Click a Monday day-cell for next week
    const day = String(new Date(nextMonday() + 'T00:00:00').getDate());
    await page.getByRole('button', { name: day, exact: true }).first().click();

    // Time slot
    await page.getByRole('button', { name: /Morning/i }).first().click();

    // Confirm summary CTA reads "Book Entire Week"
    await expect(page.getByRole('button', { name: /Book Entire Week/i })).toBeVisible();
  });

  test('week booking with bad time slot returns 400', async ({ request }) => {
    const token = await apiLogin(request, USER_FINN.email, USER_FINN.password);
    const locsRes = await request.get('/api/locations', {
      headers: { Authorization: `Bearer ${token}` },
    });
    const locations: Array<{ id: string; name: string }> = await locsRes.json();
    const res = await request.post('/api/bookings/week', {
      headers: { Authorization: `Bearer ${token}` },
      data: { locationId: locations[0].id, weekStartDate: nextMonday(), timeSlot: 'Evening' },
    });
    expect(res.status()).toBe(400);
  });
});
