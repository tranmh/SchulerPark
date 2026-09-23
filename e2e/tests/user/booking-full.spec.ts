import { test, expect } from '@playwright/test';
import { loginAsFinn, USER_FINN, apiLogin } from '../../helpers/auth';
import { AdminApi } from '../../helpers/api';
import { inDays, tomorrow } from '../../helpers/data';

test.describe('User → Booking flow (full)', () => {
  test('user can complete a booking through the 4-step wizard', async ({ page, request }) => {
    // Bug #24: ensure a clean slate so the confirm deterministically navigates to My Bookings.
    // A pre-existing booking for this date/slot would make the wizard fail — and the old
    // assertion (URL is /my-bookings OR still /booking) passed anyway.
    const seedToken = await apiLogin(request, USER_FINN.email, USER_FINN.password);
    const seedLocs = await request.get('/api/locations', { headers: { Authorization: `Bearer ${seedToken}` } });
    const seedLocations: Array<{ id: string; name: string }> = await seedLocs.json();
    const gp = seedLocations.find((l) => l.name === 'Goeppingen') ?? seedLocations[0];
    const seedMy = await request.get('/api/bookings/my?pageSize=100', { headers: { Authorization: `Bearer ${seedToken}` } });
    if (seedMy.ok()) {
      const my: { bookings: Array<{ id: string; date: string; locationId: string; timeSlot: string }> } = await seedMy.json();
      for (const b of my.bookings) {
        if (b.date === tomorrow() && b.locationId === gp.id && b.timeSlot === 'Morning') {
          await request.delete(`/api/bookings/${b.id}`, { headers: { Authorization: `Bearer ${seedToken}` } });
        }
      }
    }

    await loginAsFinn(page);
    await page.goto('/booking');

    // Step 1: pick location
    await expect(page.getByRole('heading', { name: 'Select a location' })).toBeVisible();
    await page.getByRole('button', { name: /^G Goeppingen/ }).click();

    // Step 2: pick a date — click the day-of-month for tomorrow
    const day = String(new Date(tomorrow() + 'T00:00:00').getDate());
    await expect(page.getByRole('heading', { name: /select a date at goeppingen/i })).toBeVisible();
    await page.getByRole('button', { name: day, exact: true }).first().click();

    // Step 3: time slot
    await expect(page.getByRole('heading', { name: /select a time slot/i })).toBeVisible();
    await page.getByRole('button', { name: /Morning/i }).first().click();

    // Step 4: review + confirm
    await expect(page.getByText('Review your booking')).toBeVisible();
    await page.getByRole('button', { name: /Confirm Booking/i }).click();

    // Bug #24: a successful confirm must navigate to My Bookings. The old assertion also
    // accepted staying on /booking — i.e. it passed even when the confirm did nothing.
    await page.waitForLoadState('networkidle');
    await expect(page).toHaveURL(/\/my-bookings$/);
  });

  test('user can cancel a Pending booking from My Bookings', async ({ page, request }) => {
    // Seed a booking via API so the test does not depend on the wizard succeeding.
    const token = await apiLogin(request, USER_FINN.email, USER_FINN.password);
    const locsRes = await request.get('/api/locations', {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(locsRes.ok()).toBeTruthy();
    const locations: Array<{ id: string; name: string }> = await locsRes.json();
    const loc = locations.find((l) => l.name === 'Erfurt') ?? locations[0];

    // Book for ~2 weeks out to keep the date safe even after first test ran above.
    const date = new Date();
    date.setDate(date.getDate() + 14);
    const dateStr = date.toISOString().split('T')[0];

    // Bug #24: 400 was originally allowed because a prior run may have left a booking for this
    // date/slot (duplicate → 400); pre-clean it instead so 400 now signals a real failure.
    const existing = await request.get('/api/bookings/my?pageSize=100', {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (existing.ok()) {
      const my: { bookings: Array<{ id: string; date: string; locationId: string; timeSlot: string }> } =
        await existing.json();
      for (const b of my.bookings) {
        if (b.date === dateStr && b.locationId === loc.id && b.timeSlot === 'Afternoon') {
          await request.delete(`/api/bookings/${b.id}`, { headers: { Authorization: `Bearer ${token}` } });
        }
      }
    }

    const create = await request.post('/api/bookings', {
      headers: { Authorization: `Bearer ${token}` },
      data: { locationId: loc.id, date: dateStr, timeSlot: 'Afternoon' },
    });
    expect([200, 201]).toContain(create.status());

    await loginAsFinn(page);
    await page.goto('/my-bookings');
    await page.waitForLoadState('networkidle');

    // Find the row for our specific location
    const row = page.locator('div.flex.flex-wrap.items-center.gap-4').filter({ hasText: loc.name }).first();
    await row.getByRole('button', { name: 'Cancel', exact: true }).click();

    // Register the listener BEFORE the click that fires the DELETE. Registering it
    // after (as before) races the backend: when the response arrives quickly, it is
    // gone by the time waitForResponse starts listening and the test times out even
    // though the cancel succeeded (CI run 35562379079 captured the row as Cancelled).
    const deleted = page.waitForResponse(
      (r) => r.url().includes('/api/bookings/') && r.request().method() === 'DELETE' && r.ok(),
    );
    await page.getByRole('button', { name: /cancel booking/i }).click();
    await deleted;
  });

  test('user confirms a Won booking from My Bookings (Phase 20 WP4)', async ({ page, request, baseURL }) => {
    // Seed: a Pending booking ~3 weeks out at a location with free slots, then run that
    // slot's lottery via the admin API so the booking becomes Won with a stored deadline
    // (07:00 Berlin on the booking day → far in the future → Confirm button visible).
    const token = await apiLogin(request, USER_FINN.email, USER_FINN.password);
    const auth = { Authorization: `Bearer ${token}` };
    const locsRes = await request.get('/api/locations', { headers: auth });
    const locations: Array<{ id: string; name: string }> = await locsRes.json();
    const loc = locations.find((l) => l.name === 'Hessdorf') ?? locations[0];
    const dateStr = inDays(21);

    const existing = await request.get('/api/bookings/my?pageSize=100', { headers: auth });
    if (existing.ok()) {
      const my: { bookings: Array<{ id: string; date: string; timeSlot: string; status: string }> } = await existing.json();
      for (const b of my.bookings) {
        if (b.date === dateStr && b.timeSlot === 'Afternoon' && !['Cancelled', 'Expired', 'Lost'].includes(b.status)) {
          await request.delete(`/api/bookings/${b.id}`, { headers: auth });
        }
      }
    }

    const create = await request.post('/api/bookings', {
      headers: auth,
      data: { locationId: loc.id, date: dateStr, timeSlot: 'Afternoon' },
    });
    expect([200, 201]).toContain(create.status());
    const created: { id: string; status: string } = await create.json();

    // A previous run may already have drawn this slot; then the booking was assigned directly.
    if (created.status === 'Pending') {
      const admin = await AdminApi.create(baseURL!);
      try {
        const run = await admin.runForLocation(loc.id, dateStr, 'Afternoon');
        expect(run.ok()).toBeTruthy();
      } finally {
        await admin.dispose();
      }
    }

    const afterLottery = await request.get('/api/bookings/my?status=Won&pageSize=100', { headers: auth });
    const won: { bookings: Array<{ id: string; confirmationDeadline: string | null }> } = await afterLottery.json();
    const mine = won.bookings.find((b) => b.id === created.id);
    test.skip(!mine, 'slot was assigned directly (lottery had already run) — nothing to confirm');
    expect(mine!.confirmationDeadline).not.toBeNull();
    expect(new Date(mine!.confirmationDeadline!).getTime()).toBeGreaterThan(Date.now());

    await loginAsFinn(page);
    await page.goto('/my-bookings');
    await page.getByRole('button', { name: 'Won', exact: true }).click();

    const row = page.locator('div.flex.flex-wrap.items-center').filter({ hasText: loc.name }).filter({ hasText: /Confirm usage/ }).first();
    const confirmed = page.waitForResponse(
      (r) => r.url().includes(`/api/bookings/${created.id}/confirm`) && r.request().method() === 'POST' && r.ok(),
    );
    await row.getByRole('button', { name: 'Confirm usage' }).click();
    await confirmed;

    const check = await request.get('/api/bookings/my?status=Confirmed&pageSize=100', { headers: auth });
    const confirmedList: { bookings: Array<{ id: string }> } = await check.json();
    expect(confirmedList.bookings.some((b) => b.id === created.id)).toBeTruthy();

    // Clean up so the slot is free for the next run.
    await request.delete(`/api/bookings/${created.id}`, { headers: auth });
  });

  test('shows error when API rejects booking with 400 (mocked)', async ({ page }) => {
    await page.route('**/api/bookings', (route) => {
      if (route.request().method() === 'POST') {
        route.fulfill({
          status: 400,
          contentType: 'application/problem+json',
          body: JSON.stringify({ title: 'Bad Request', detail: 'Mocked rejection', status: 400 }),
        });
      } else {
        route.continue();
      }
    });

    await loginAsFinn(page);
    await page.goto('/booking');
    await page.getByRole('button', { name: /^G Goeppingen/ }).click();
    const day = String(new Date(tomorrow() + 'T00:00:00').getDate());
    await page.getByRole('button', { name: day, exact: true }).first().click();
    await page.getByRole('button', { name: /Morning/i }).first().click();
    await page.getByRole('button', { name: /Confirm Booking/i }).click();

    await expect(page.getByText('Mocked rejection')).toBeVisible({ timeout: 10_000 });
  });
});
