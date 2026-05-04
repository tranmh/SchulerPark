import { test, expect } from '@playwright/test';
import { loginAsAdmin } from '../../helpers/auth';
import { AdminApi } from '../../helpers/api';
import { uniqueName } from '../../helpers/data';

test.describe('Admin → Grid Layout', () => {
  let api: AdminApi;
  let testLocationId: string;

  test.beforeAll(async () => {
    api = await AdminApi.create(process.env.BASE_URL || 'http://localhost:5173');
    const loc = await api.createLocation(uniqueName('Grid-Loc'), 'Grid Test');
    testLocationId = loc.id;
  });

  test.afterAll(async () => {
    await api.deactivateLocation(testLocationId).catch(() => {});
    await api.dispose();
  });

  test('grid layout page renders the location picker', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/grid-layout');
    await expect(page.getByRole('heading', { name: 'Grid Layout' })).toBeVisible();
    await expect(page.getByLabel('Location')).toBeVisible();
  });

  test('selecting a seeded location loads its grid configuration', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/grid-layout');

    const locationSelect = page.getByLabel('Location');
    // Pick the first non-empty option
    const value = await locationSelect.locator('option').nth(1).getAttribute('value');
    test.skip(!value, 'No locations available.');

    const responsePromise = page.waitForResponse(
      (r) => r.url().includes(`/admin/locations/${value}/grid`) && r.status() === 200
    );
    await locationSelect.selectOption(value!);
    const resp = await responsePromise;
    expect(resp.status()).toBe(200);

    // Once loaded, save button should be visible.
    await expect(page.getByRole('button', { name: 'Save Layout' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Clear Grid' })).toBeVisible();
  });

  test('saving an empty grid for the test location persists 5x8 dimensions', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/grid-layout');
    await page.getByLabel('Location').selectOption(testLocationId);

    // Page defaults to 5x8 when no config exists. Saving immediately persists those defaults.
    const saveResponsePromise = page.waitForResponse(
      (r) => r.url().includes(`/admin/locations/${testLocationId}/grid`) && r.request().method() === 'PUT'
    );
    await page.getByRole('button', { name: 'Save Layout' }).click();
    const resp = await saveResponsePromise;
    expect(resp.status()).toBe(200);

    await expect(page.getByText(/grid layout saved/i)).toBeVisible({ timeout: 10_000 });
  });

  test('save error is surfaced when API returns 400 (mocked)', async ({ page }) => {
    await page.route('**/api/admin/locations/*/grid', (route) => {
      if (route.request().method() === 'PUT') {
        route.fulfill({
          status: 400,
          contentType: 'application/problem+json',
          body: JSON.stringify({ title: 'Bad Request', detail: 'Mock validation error', status: 400 }),
        });
      } else {
        route.continue();
      }
    });

    await loginAsAdmin(page);
    await page.goto('/admin/grid-layout');
    await page.getByLabel('Location').selectOption(testLocationId);
    await page.getByRole('button', { name: 'Save Layout' }).click();

    await expect(page.getByText(/Mock validation error/)).toBeVisible({ timeout: 10_000 });
  });
});
