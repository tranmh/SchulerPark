import { test, expect } from '@playwright/test';
import { loginAsAdmin } from '../../helpers/auth';
import { AdminApi } from '../../helpers/api';
import { uniqueName } from '../../helpers/data';

test.describe('Admin → Locations CRUD', () => {
  let api: AdminApi;
  const created: string[] = [];

  test.beforeAll(async () => {
    api = await AdminApi.create(process.env.BASE_URL || 'http://localhost:5173');
  });

  test.afterAll(async () => {
    for (const id of created) {
      await api.deactivateLocation(id).catch(() => {});
    }
    await api.dispose();
  });

  test('admin lands on locations page and seeded locations are visible', async ({ page }) => {
    await loginAsAdmin(page);
    await page.getByRole('link', { name: 'Locations' }).click();
    await expect(page).toHaveURL('/admin/locations');
    await expect(page.getByRole('heading', { name: 'Locations' })).toBeVisible();
    await expect(page.getByText('Goeppingen').first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'New Location' })).toBeVisible();
  });

  test('create a new location via the UI', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/locations');

    const name = uniqueName('Loc');
    const address = `${name} Strasse 1`;

    await page.getByRole('button', { name: 'New Location' }).click();
    await page.getByLabel('Name').fill(name);
    await page.getByLabel('Address').fill(address);
    await page.getByRole('button', { name: 'Save' }).click();

    const row = page.getByRole('row').filter({ hasText: name });
    await expect(row).toBeVisible({ timeout: 10_000 });
    await expect(row.getByText(address)).toBeVisible();

    const locs = await api.listLocations();
    const fresh = locs.find((l: { name: string }) => l.name === name);
    expect(fresh).toBeDefined();
    created.push(fresh.id);
  });

  test('edit a location name and address', async ({ page }) => {
    const original = uniqueName('Loc-Edit');
    const loc = await api.createLocation(original, 'Original Address');
    created.push(loc.id);

    await loginAsAdmin(page);
    await page.goto('/admin/locations');
    await expect(page.getByText(original)).toBeVisible({ timeout: 10_000 });

    const row = page.getByRole('row').filter({ hasText: original });
    await row.getByRole('button', { name: 'Edit' }).click();

    const renamed = `${original}-renamed`;
    const newAddress = `${renamed} address`;
    await page.getByLabel('Name').fill(renamed);
    await page.getByLabel('Address').fill(newAddress);
    await page.getByRole('button', { name: 'Save' }).click();

    const renamedRow = page.getByRole('row').filter({ hasText: renamed });
    await expect(renamedRow).toBeVisible({ timeout: 10_000 });
    await expect(renamedRow.getByText(newAddress)).toBeVisible();
  });

  test('change algorithm via the row dropdown', async ({ page }) => {
    const name = uniqueName('Loc-Algo');
    const loc = await api.createLocation(name, 'Algo Strasse 1');
    created.push(loc.id);

    await loginAsAdmin(page);
    await page.goto('/admin/locations');
    await expect(page.getByText(name)).toBeVisible({ timeout: 10_000 });

    const row = page.getByRole('row').filter({ hasText: name });
    await row.locator('select').selectOption('RoundRobin');

    await page.reload();
    await page.waitForLoadState('networkidle');
    const reloadedRow = page.getByRole('row').filter({ hasText: name });
    await expect(reloadedRow.locator('select')).toHaveValue('RoundRobin', { timeout: 10_000 });
  });

  test('deactivate a location', async ({ page }) => {
    const name = uniqueName('Loc-Deact');
    const loc = await api.createLocation(name, 'Deactivate Strasse 1');
    created.push(loc.id);

    await loginAsAdmin(page);
    await page.goto('/admin/locations');
    await expect(page.getByText(name)).toBeVisible({ timeout: 10_000 });

    const row = page.getByRole('row').filter({ hasText: name });
    await row.getByRole('button', { name: 'Deactivate' }).click();

    const updatedRow = page.getByRole('row').filter({ hasText: name });
    await expect(updatedRow.getByText('Inactive')).toBeVisible({ timeout: 10_000 });
    await expect(updatedRow.getByRole('button', { name: 'Deactivate' })).toHaveCount(0);
  });

  test('save button is disabled when name or address is empty', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/locations');

    await page.getByRole('button', { name: 'New Location' }).click();
    await expect(page.getByRole('button', { name: 'Save' })).toBeDisabled();

    await page.getByLabel('Name').fill('Only Name');
    await expect(page.getByRole('button', { name: 'Save' })).toBeDisabled();

    await page.getByLabel('Address').fill('Some Address');
    await expect(page.getByRole('button', { name: 'Save' })).toBeEnabled();

    await page.getByRole('button', { name: 'Cancel' }).click();
  });
});
