import { test, expect, type Page } from '@playwright/test';

async function loginAs(page: Page, email: string, password: string) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL('/', { timeout: 10000 });
}

async function loginAsTestUser(page: Page) {
  // Finn has no preferred location — booking step 1 (location picker) renders
  // for him. Users with a preferred location auto-skip step 1, which this
  // suite's step-1 assertions rely on.
  await loginAs(page, 'finn.werner@schuler.de', 'Test1234!');
}

test.describe('Booking Flow', () => {
  test('navigate to booking page via sidebar', async ({ page }) => {
    await loginAsTestUser(page);

    await page.getByRole('link', { name: /book a spot/i }).click();
    await expect(page).toHaveURL('/booking');
    // Assert the step-1 heading (unique) rather than /book a parking spot/i, which now
    // matches both the page subtitle and a "Book a parking spot →" link (strict-mode violation).
    await expect(page.getByRole('heading', { name: 'Select a location' })).toBeVisible();
  });

  test('booking page shows step indicators', async ({ page }) => {
    await loginAsTestUser(page);
    await page.goto('/booking');

    // Step indicators in the booking wizard
    await expect(page.getByRole('button', { name: /Location/i })).toBeVisible();
    await expect(page.getByRole('button', { name: /Date/i })).toBeVisible();
    await expect(page.getByRole('button', { name: /time slot/i })).toBeVisible();
    await expect(page.getByRole('button', { name: /Confirm/i })).toBeVisible();
  });

  test('booking page shows locations to select', async ({ page }) => {
    await loginAsTestUser(page);
    await page.goto('/booking');

    // Step 1: should see location options
    await expect(page.getByRole('heading', { name: 'Select a location' })).toBeVisible();
    await expect(page.getByText('Goeppingen').first()).toBeVisible({ timeout: 10000 });
    await expect(page.getByText('Erfurt').first()).toBeVisible();
  });

  test('can progress through booking steps', async ({ page }) => {
    await loginAsTestUser(page);
    await page.goto('/booking');

    // Step 1: Select location — use exact name match because admin tests can
    // leave behind locations whose addresses contain "Goeppingen".
    await page.getByRole('button', { name: /^G Goeppingen/ }).click();

    // Step 2: Date picker should appear
    await expect(page.getByText(/select a date at goeppingen/i)).toBeVisible({ timeout: 5000 });
  });

  test('my bookings page shows bookings or empty state', async ({ page }) => {
    await loginAsTestUser(page);

    await page.getByRole('link', { name: /my bookings/i }).click();
    await expect(page).toHaveURL('/my-bookings');
    await expect(page.getByRole('heading', { name: /my bookings/i })).toBeVisible();

    // Should show either booking cards with Pending badges, or the empty state message
    const hasPending = await page.getByText('Pending').first().isVisible().catch(() => false);
    const hasEmpty = await page.getByText(/no bookings found/i).isVisible().catch(() => false);
    expect(hasPending || hasEmpty).toBeTruthy();
  });

  test('my bookings page has status filter tabs', async ({ page }) => {
    await loginAsTestUser(page);
    await page.goto('/my-bookings');

    await expect(page.getByRole('button', { name: 'All' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Pending' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Won' })).toBeVisible();
  });
});
