import { test, expect } from '@playwright/test';
import { loginAsFinn, USER_FINN, apiLogin } from '../../helpers/auth';
import { tomorrow } from '../../helpers/data';

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
    await page.getByRole('button', { name: /cancel booking/i }).click();

    await page.waitForResponse((r) =>
      r.url().includes('/api/bookings/') && r.request().method() === 'DELETE' && r.ok()
    );
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
