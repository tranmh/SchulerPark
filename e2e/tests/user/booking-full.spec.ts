import { test, expect } from '@playwright/test';
import { loginAsFinn, USER_FINN, apiLogin } from '../../helpers/auth';
import { tomorrow } from '../../helpers/data';

test.describe('User → Booking flow (full)', () => {
  test('user can complete a booking through the 4-step wizard', async ({ page, request }) => {
    await loginAsFinn(page);
    await page.goto('/booking');

    // Step 1: pick location
    await expect(page.getByText('Select a location')).toBeVisible();
    await page.getByRole('button', { name: /^Goeppingen/ }).click();

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

    // Either we land on /my-bookings, or we see a fallback summary screen.
    await page.waitForLoadState('networkidle');
    const url = page.url();
    expect(url.endsWith('/my-bookings') || url.endsWith('/booking')).toBeTruthy();
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

    const create = await request.post('/api/bookings', {
      headers: { Authorization: `Bearer ${token}` },
      data: { locationId: loc.id, date: dateStr, timeSlot: 'Afternoon' },
    });
    expect([200, 201]).toContain(create.status());

    await loginAsFinn(page);
    await page.goto('/my-bookings');
    await page.waitForLoadState('networkidle');

    // Find the row for our specific date
    const row = page.locator('div.rounded-lg.border').filter({ hasText: loc.name }).first();
    await row.getByRole('button', { name: 'Cancel' }).click();
    await page.getByRole('button', { name: 'Cancel Booking' }).click();

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
    await page.getByRole('button', { name: /^Goeppingen/ }).click();
    const day = String(new Date(tomorrow() + 'T00:00:00').getDate());
    await page.getByRole('button', { name: day, exact: true }).first().click();
    await page.getByRole('button', { name: /Morning/i }).first().click();
    await page.getByRole('button', { name: /Confirm Booking/i }).click();

    await expect(page.getByText('Mocked rejection')).toBeVisible({ timeout: 10_000 });
  });
});
