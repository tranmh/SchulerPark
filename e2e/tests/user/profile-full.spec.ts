import { test, expect } from '@playwright/test';
import { loginAsAnna, USER_ANNA, apiLogin } from '../../helpers/auth';

test.describe('User → Profile (full)', () => {
  test('profile page renders with user info', async ({ page }) => {
    await loginAsAnna(page);
    await page.goto('/profile');
    await expect(page.getByRole('heading', { name: 'Profile' })).toBeVisible();
    await expect(page.getByLabel('Email')).toHaveValue(USER_ANNA.email);
  });

  test('saving display name persists', async ({ page }) => {
    await loginAsAnna(page);
    await page.goto('/profile');

    const newName = `Anna E2E ${Date.now().toString(36).slice(-4)}`;
    await page.getByLabel('Display Name').fill(newName);
    await page.getByRole('button', { name: 'Save Changes' }).click();
    await expect(page.getByText(/profile updated successfully/i)).toBeVisible({ timeout: 10_000 });

    await page.reload();
    await page.waitForLoadState('networkidle');
    await expect(page.getByLabel('Display Name')).toHaveValue(newName);

    // Restore for follow-up test isolation
    await page.getByLabel('Display Name').fill('Anna Mueller');
    await page.getByRole('button', { name: 'Save Changes' }).click();
    await expect(page.getByText(/profile updated successfully/i)).toBeVisible({ timeout: 10_000 });
  });

  test('preferred location dropdown selection is persisted', async ({ page }) => {
    await loginAsAnna(page);
    await page.goto('/profile');

    // Pick the first concrete location option
    const select = page.getByLabel('Preferred Parking Location');
    const optionValue = await select.locator('option').nth(1).getAttribute('value');
    test.skip(!optionValue, 'No locations available.');
    await select.selectOption(optionValue!);
    await page.getByRole('button', { name: 'Save Changes' }).click();
    await expect(page.getByText(/profile updated successfully/i)).toBeVisible({ timeout: 10_000 });

    await page.reload();
    await page.waitForLoadState('networkidle');
    await expect(page.getByLabel('Preferred Parking Location')).toHaveValue(optionValue!);
  });

  test('data export endpoint returns user JSON', async ({ request }) => {
    const token = await apiLogin(request, USER_ANNA.email, USER_ANNA.password);
    const res = await request.get('/api/profile/data-export', {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.profile?.email).toBe(USER_ANNA.email);
    expect(Array.isArray(body.bookings)).toBe(true);
    expect(Array.isArray(body.lotteryHistory)).toBe(true);
  });

  test('account deletion confirm-dialog shows on click (mocked DELETE)', async ({ page }) => {
    // Mock the DELETE so we don't actually delete the user.
    await page.route('**/api/profile/data', (route) => {
      if (route.request().method() === 'DELETE') {
        route.fulfill({ status: 204, body: '' });
      } else {
        route.continue();
      }
    });

    await loginAsAnna(page);
    await page.goto('/profile');

    await page.getByRole('button', { name: 'Delete My Account' }).click();
    // The dialog uses heading "Delete Account" exactly (no DSGVO suffix).
    await expect(page.getByRole('heading', { name: 'Delete Account', exact: true })).toBeVisible();

    // Confirm via the dialog button (exact name to disambiguate from card button)
    await page.getByRole('button', { name: 'Delete Account', exact: true }).click();

    // After deletion the auth context logs the user out — they land on /login.
    await expect(page).toHaveURL('/login', { timeout: 10_000 });
  });
});
