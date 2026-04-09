import { test, expect, type Page } from '@playwright/test';

async function loginAsTestUser(page: Page) {
  await page.goto('/login');
  await page.getByLabel('Email').fill('max.schmidt@schuler.de');
  await page.getByLabel('Password').fill('Test1234!');
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL('/', { timeout: 10000 });
}

test.describe('Profile & DSGVO', () => {
  test('profile page shows user info', async ({ page }) => {
    await loginAsTestUser(page);

    await page.getByRole('link', { name: /profile/i }).click();
    await expect(page).toHaveURL('/profile');

    await expect(page.getByText('Display Name')).toBeVisible({ timeout: 5000 });
    await expect(page.getByText('Car License Plate')).toBeVisible();
  });

  test('profile page has data export button', async ({ page }) => {
    await loginAsTestUser(page);
    await page.goto('/profile');

    await expect(page.getByRole('button', { name: /download my data/i })).toBeVisible({ timeout: 5000 });
  });

  test('profile page has delete account button', async ({ page }) => {
    await loginAsTestUser(page);
    await page.goto('/profile');

    await expect(page.getByRole('button', { name: /delete my account/i })).toBeVisible({ timeout: 5000 });
  });

  test('privacy page is accessible without login', async ({ page }) => {
    await page.goto('/privacy');
    await expect(page.getByText(/privacy/i)).toBeVisible();
  });
});
