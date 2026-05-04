import { test, expect } from '@playwright/test';
import { loginAsAdmin } from '../../helpers/auth';
import { AdminApi } from '../../helpers/api';

test.describe('Admin → All Bookings (read-only view)', () => {
  let api: AdminApi;
  let baseURL: string;

  test.beforeAll(async () => {
    baseURL = process.env.BASE_URL || 'http://localhost:5173';
    api = await AdminApi.create(baseURL);
  });
  test.afterAll(async () => { await api.dispose(); });

  test('renders bookings page with a total count', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/bookings');
    await expect(page.getByRole('heading', { name: 'All Bookings' })).toBeVisible();
    // Match either "X bookings total" or "0 bookings total"
    await expect(page.getByText(/booking[s]? total/i)).toBeVisible();
  });

  test('status filter narrows results', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/bookings');
    await page.waitForLoadState('networkidle');

    // Pick "Pending" — seed data ships with pending bookings
    const statusSelects = page.locator('select');
    await statusSelects.nth(1).selectOption('Pending');
    // Wait for re-fetch
    await page.waitForResponse((r) =>
      r.url().includes('/api/admin/bookings') && r.url().includes('status=Pending') && r.status() === 200
    );

    // All visible status badges (if any) should read "Pending"
    const badges = page.locator('table tbody td:last-child span');
    const count = await badges.count();
    if (count > 0) {
      for (let i = 0; i < count; i++) {
        await expect(badges.nth(i)).toHaveText(/Pending/i);
      }
    } else {
      await expect(page.getByText(/no bookings found/i)).toBeVisible();
    }
  });

  test('renders empty-state UI when API returns no bookings (mocked)', async ({ page }) => {
    await page.route('**/api/admin/bookings*', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ bookings: [], totalCount: 0, page: 1, pageSize: 20 }),
      });
    });

    await loginAsAdmin(page);
    await page.goto('/admin/bookings');

    await expect(page.getByText('0 bookings total')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText(/no bookings found/i)).toBeVisible();
  });

  test('renders error state when bookings API returns 500 (mocked)', async ({ page }) => {
    await page.route('**/api/admin/bookings*', (route) => {
      route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: 'Internal', status: 500 }),
      });
    });

    await loginAsAdmin(page);
    await page.goto('/admin/bookings');
    await expect(page.getByText(/failed to load bookings/i)).toBeVisible({ timeout: 10_000 });
  });

  test('pagination renders next/prev buttons when results exceed page size (mocked)', async ({ page }) => {
    // Generate 25 fake bookings to force pagination (page size = 20).
    const bookings = Array.from({ length: 20 }, (_, i) => ({
      id: `00000000-0000-0000-0000-${String(i).padStart(12, '0')}`,
      date: '2026-05-10',
      timeSlot: 'Morning',
      locationName: 'Mock Location',
      userDisplayName: `User ${i}`,
      userEmail: `user${i}@example.com`,
      parkingSlotNumber: `P${i}`,
      status: 'Pending',
    }));
    await page.route('**/api/admin/bookings*', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ bookings, totalCount: 25, page: 1, pageSize: 20 }),
      });
    });

    await loginAsAdmin(page);
    await page.goto('/admin/bookings');

    await expect(page.getByText('25 bookings total')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole('button', { name: 'Next' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Previous' })).toBeDisabled();
  });
});
