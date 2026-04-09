import { test, expect, type Page } from '@playwright/test';

async function loginAsAdmin(page: Page) {
  await page.goto('/login');
  await page.getByLabel('Email').fill('admin@schulerpark.local');
  await page.getByLabel('Password').fill('Admin123!');
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL('/', { timeout: 10000 });
}

test.describe('Admin Features', () => {
  test('admin sees admin sidebar items', async ({ page }) => {
    await loginAsAdmin(page);

    await expect(page.getByRole('link', { name: 'Locations' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Parking Slots' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Blocked Days' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'All Bookings' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Lottery History' })).toBeVisible();
  });

  test('regular user does not see admin sidebar items', async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('Email').fill('anna.mueller@schuler.de');
    await page.getByLabel('Password').fill('Test1234!');
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page).toHaveURL('/', { timeout: 10000 });

    await expect(page.getByRole('link', { name: 'Locations' })).not.toBeVisible();
    await expect(page.getByRole('link', { name: 'All Bookings' })).not.toBeVisible();
  });

  test('admin locations page loads', async ({ page }) => {
    await loginAsAdmin(page);

    await page.getByRole('link', { name: 'Locations' }).click();
    await expect(page).toHaveURL('/admin/locations');

    // Should see seeded locations (use first() to avoid strict mode on name+address cells)
    await expect(page.getByText('Goeppingen').first()).toBeVisible({ timeout: 10000 });
    await expect(page.getByText('Erfurt').first()).toBeVisible();
    await expect(page.getByText('Hessdorf').first()).toBeVisible();
    await expect(page.getByText('Gemmingen').first()).toBeVisible();
  });

  test('admin parking slots page loads', async ({ page }) => {
    await loginAsAdmin(page);

    await page.getByRole('link', { name: 'Parking Slots' }).click();
    await expect(page).toHaveURL('/admin/slots');
  });

  test('admin blocked days page loads', async ({ page }) => {
    await loginAsAdmin(page);

    await page.getByRole('link', { name: 'Blocked Days' }).click();
    await expect(page).toHaveURL('/admin/blocked-days');
  });

  test('admin all bookings page loads', async ({ page }) => {
    await loginAsAdmin(page);

    await page.getByRole('link', { name: 'All Bookings' }).click();
    await expect(page).toHaveURL('/admin/bookings');
  });

  test('admin lottery history page loads', async ({ page }) => {
    await loginAsAdmin(page);

    await page.getByRole('link', { name: 'Lottery History' }).click();
    await expect(page).toHaveURL('/admin/lottery-history');
  });

  test('regular user cannot access admin routes directly', async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('Email').fill('anna.mueller@schuler.de');
    await page.getByLabel('Password').fill('Test1234!');
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page).toHaveURL('/', { timeout: 10000 });

    // Try to navigate to admin page directly
    await page.goto('/admin/locations');

    // Should redirect back to dashboard (ProtectedRoute with requireAdmin)
    await expect(page).toHaveURL('/');
  });
});
