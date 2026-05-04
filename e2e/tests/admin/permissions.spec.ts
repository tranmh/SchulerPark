import { test, expect } from '@playwright/test';
import { loginAsAdmin, loginAsAnna, apiLogin } from '../../helpers/auth';

test.describe('Admin → Permissions / RBAC', () => {
  test('regular user is redirected away from /admin/locations', async ({ page }) => {
    await loginAsAnna(page);
    await page.goto('/admin/locations');
    await expect(page).toHaveURL('/');
  });

  test('regular user is redirected away from /admin/slots', async ({ page }) => {
    await loginAsAnna(page);
    await page.goto('/admin/slots');
    await expect(page).toHaveURL('/');
  });

  test('regular user is redirected away from /admin/bookings', async ({ page }) => {
    await loginAsAnna(page);
    await page.goto('/admin/bookings');
    await expect(page).toHaveURL('/');
  });

  test('regular user is redirected away from /admin/lottery-history', async ({ page }) => {
    await loginAsAnna(page);
    await page.goto('/admin/lottery-history');
    await expect(page).toHaveURL('/');
  });

  test('regular user is redirected away from /admin/grid-layout', async ({ page }) => {
    await loginAsAnna(page);
    await page.goto('/admin/grid-layout');
    await expect(page).toHaveURL('/');
  });

  test('regular user is redirected away from /admin/users (super-admin only)', async ({ page }) => {
    await loginAsAnna(page);
    await page.goto('/admin/users');
    await expect(page).toHaveURL('/');
  });

  test('admin (non-superadmin) is redirected away from /admin/users', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/users');
    await expect(page).toHaveURL('/');
  });

  test('regular user does not see admin sidebar navigation', async ({ page }) => {
    await loginAsAnna(page);
    await expect(page.getByRole('link', { name: 'Locations' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Parking Slots' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'All Bookings' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Lottery History' })).toHaveCount(0);
  });

  test('regular user receives 403 from /api/admin/locations', async ({ request }) => {
    const token = await apiLogin(request, 'anna.mueller@schuler.de', 'Test1234!');
    const res = await request.get('/api/admin/locations', {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.status()).toBe(403);
  });

  test('unauthenticated request to /api/admin/locations returns 401', async ({ request }) => {
    const res = await request.get('/api/admin/locations');
    expect(res.status()).toBe(401);
  });

  test('UI handles 403 from a privileged endpoint gracefully (mocked)', async ({ page }) => {
    await page.route('**/api/admin/locations', (route) => {
      route.fulfill({
        status: 403,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: 'Forbidden', status: 403 }),
      });
    });

    await loginAsAdmin(page);
    await page.goto('/admin/locations');
    await expect(page.getByText(/failed to load locations/i)).toBeVisible({ timeout: 10_000 });
  });
});
