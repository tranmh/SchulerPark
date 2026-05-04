import { test, expect } from '@playwright/test';
import { loginAsAdmin } from '../../helpers/auth';

test.describe('Admin → Lottery History', () => {
  test('renders lottery history page with a total count', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/lottery-history');
    await expect(page.getByRole('heading', { name: 'Lottery History' })).toBeVisible();
    await expect(page.getByText(/run[s]? total/i)).toBeVisible();
  });

  test('shows empty state when API returns no runs (mocked)', async ({ page }) => {
    await page.route('**/api/admin/lottery-runs*', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ lotteryRuns: [], totalCount: 0, page: 1, pageSize: 20 }),
      });
    });

    await loginAsAdmin(page);
    await page.goto('/admin/lottery-history');

    await expect(page.getByText('0 runs total')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText(/no lottery runs found/i)).toBeVisible();
  });

  test('shows table rows when API returns runs (mocked)', async ({ page }) => {
    const runs = [
      {
        id: '11111111-1111-1111-1111-111111111111',
        ranAt: '2026-05-04T20:00:00Z',
        locationName: 'Mock Goeppingen',
        date: '2026-05-05',
        timeSlot: 'Morning',
        algorithm: 'PureRandom',
        totalBookings: 7,
        availableSlots: 5,
      },
    ];
    await page.route('**/api/admin/lottery-runs*', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ lotteryRuns: runs, totalCount: 1, page: 1, pageSize: 20 }),
      });
    });

    await loginAsAdmin(page);
    await page.goto('/admin/lottery-history');

    await expect(page.getByText('1 run total')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText('Mock Goeppingen')).toBeVisible();
    await expect(page.getByText('PureRandom')).toBeVisible();
    await expect(page.getByText('Morning')).toBeVisible();
  });

  test('location filter triggers a re-fetch with locationId in the query', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/lottery-history');
    await page.waitForLoadState('networkidle');

    // The first <select> is the location filter
    const select = page.locator('select').first();
    const options = await select.locator('option').all();
    // Find the first non-"All Locations" option
    let targetValue: string | null = null;
    for (const opt of options) {
      const value = await opt.getAttribute('value');
      if (value) { targetValue = value; break; }
    }
    test.skip(!targetValue, 'No locations available to filter on.');

    const responsePromise = page.waitForResponse((r) =>
      r.url().includes('/api/admin/lottery-runs') &&
      r.url().includes(`locationId=${targetValue!}`)
    );
    await select.selectOption(targetValue!);
    const resp = await responsePromise;
    expect(resp.status()).toBe(200);
  });
});
