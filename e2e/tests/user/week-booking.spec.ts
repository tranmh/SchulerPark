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

    // Clean up any existing Finn bookings that conflict with the target week,
    // location and time slot. Without this the API returns 400 ("all days
    // skipped") whenever a prior run already booked this user/location/week.
    const weekDates = (() => {
      const ds: string[] = [];
      const start = new Date(monday + 'T00:00:00');
      for (let i = 0; i < 5; i++) {
        const d = new Date(start);
        d.setDate(start.getDate() + i);
        ds.push(d.toISOString().split('T')[0]);
      }
      return new Set(ds);
    })();
    const myRes = await request.get('/api/bookings/my?pageSize=100', {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (myRes.ok()) {
      const my: { bookings: Array<{ id: string; date: string; locationId: string; timeSlot: string }> }
        = await myRes.json();
      for (const b of my.bookings) {
        if (weekDates.has(b.date) && b.locationId === loc.id && b.timeSlot === 'Morning') {
          await request.delete(`/api/bookings/${b.id}`, {
            headers: { Authorization: `Bearer ${token}` },
          });
        }
      }
    }

    const res = await request.post('/api/bookings/week', {
      headers: { Authorization: `Bearer ${token}` },
      data: { locationId: loc.id, weekStartDate: monday, timeSlot: 'Morning' },
    });
    expect(res.ok(), `expected ok, got ${res.status()}: ${await res.text()}`).toBeTruthy();
    const body = await res.json();
    expect(Array.isArray(body.createdBookings)).toBe(true);
    expect(Array.isArray(body.skippedDays)).toBe(true);
    // Bug #24: after the pre-clean above the week booking must actually create days. The old
    // `<= 5` passed even when zero were created. Every Mon–Fri day is either created or skipped
    // (exactly 5 accounted for), and at least one must have been created.
    expect(body.createdBookings.length).toBeGreaterThan(0);
    expect(body.createdBookings.length + body.skippedDays.length).toBe(5);
  });

  test('week-mode booking through the UI shows the summary screen', async ({ page }) => {
    await loginAsFinn(page);
    await page.goto('/booking');

    await page.getByRole('button', { name: /^G Goeppingen/ }).click();

    // Toggle week mode. The checkbox is visually hidden behind a styled
    // <span> sibling that intercepts pointer events, so click the label.
    await page.locator('label:has-text("Book entire week")').click();

    // Navigate the calendar forward until next Monday's month is visible.
    const target = new Date(nextMonday() + 'T00:00:00');
    const now = new Date();
    const monthDelta = (target.getFullYear() - now.getFullYear()) * 12
      + (target.getMonth() - now.getMonth());
    for (let i = 0; i < monthDelta; i++) {
      await page.getByRole('button', { name: /next month/i }).click();
    }

    const day = String(target.getDate());
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
