import { test, expect } from '@playwright/test';
import { loginAsAdmin } from '../../helpers/auth';
import { AdminApi } from '../../helpers/api';
import { uniqueName } from '../../helpers/data';

test.describe('Admin → Slots CRUD', () => {
  let api: AdminApi;
  let testLocationId: string;
  const createdSlotIds: string[] = [];

  test.beforeAll(async () => {
    api = await AdminApi.create(process.env.BASE_URL || 'http://localhost:5173');
    // All slot tests scope to a fresh isolated location.
    const loc = await api.createLocation(uniqueName('Slots-Loc'), 'Slots Test Strasse');
    testLocationId = loc.id;
  });

  test.afterAll(async () => {
    for (const id of createdSlotIds) await api.deactivateSlot(id).catch(() => {});
    await api.deactivateLocation(testLocationId).catch(() => {});
    await api.dispose();
  });

  test('parking slots page lists slots for the selected location', async ({ page }) => {
    // seed one slot via API so the table has a row to verify visibility
    const slotNumber = `P-${Date.now().toString(36).slice(-6)}`;
    const slot = await api.createSlot(testLocationId, slotNumber, 'Seeded label');
    createdSlotIds.push(slot.id);

    await loginAsAdmin(page);
    await page.goto('/admin/slots');

    // Switch to the test location
    await page.getByLabel('Location').selectOption(testLocationId);

    await expect(page.getByText(slotNumber)).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText('Seeded label')).toBeVisible();
  });

  test('create a slot via the UI', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/slots');
    await page.getByLabel('Location').selectOption(testLocationId);

    const slotNumber = `UI-${Date.now().toString(36).slice(-6)}`;
    await page.getByRole('button', { name: 'New Slot' }).click();
    await page.getByLabel('Slot Number').fill(slotNumber);
    await page.getByLabel('Label (optional)').fill('UI created');
    await page.getByRole('button', { name: 'Save' }).click();

    const row = page.getByRole('row').filter({ hasText: slotNumber });
    await expect(row).toBeVisible({ timeout: 10_000 });
    await expect(row.getByText('UI created')).toBeVisible();

    // Track for cleanup
    const slots = await api.listSlots(testLocationId);
    const fresh = slots.find((s: { slotNumber: string }) => s.slotNumber === slotNumber);
    expect(fresh).toBeDefined();
    createdSlotIds.push(fresh.id);
  });

  test('edit a slot label', async ({ page }) => {
    const slotNumber = `Edit-${Date.now().toString(36).slice(-6)}`;
    const slot = await api.createSlot(testLocationId, slotNumber, 'Old label');
    createdSlotIds.push(slot.id);

    await loginAsAdmin(page);
    await page.goto('/admin/slots');
    await page.getByLabel('Location').selectOption(testLocationId);

    const row = page.getByRole('row').filter({ hasText: slotNumber });
    await row.getByRole('button', { name: 'Edit' }).click();
    await page.getByLabel('Label (optional)').fill('New label');
    await page.getByRole('button', { name: 'Save' }).click();

    const updatedRow = page.getByRole('row').filter({ hasText: slotNumber });
    await expect(updatedRow.getByText('New label')).toBeVisible({ timeout: 10_000 });
  });

  test('deactivate a slot', async ({ page }) => {
    const slotNumber = `Deact-${Date.now().toString(36).slice(-6)}`;
    const slot = await api.createSlot(testLocationId, slotNumber);
    createdSlotIds.push(slot.id);

    await loginAsAdmin(page);
    await page.goto('/admin/slots');
    await page.getByLabel('Location').selectOption(testLocationId);

    const row = page.getByRole('row').filter({ hasText: slotNumber });
    await row.getByRole('button', { name: 'Deactivate' }).click();

    const updatedRow = page.getByRole('row').filter({ hasText: slotNumber });
    await expect(updatedRow.getByText('Inactive')).toBeVisible({ timeout: 10_000 });
  });

  test('save button is disabled when slot number is empty', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/slots');
    await page.getByLabel('Location').selectOption(testLocationId);

    await page.getByRole('button', { name: 'New Slot' }).click();
    await expect(page.getByRole('button', { name: 'Save' })).toBeDisabled();

    await page.getByLabel('Slot Number').fill('SAVEABLE');
    await expect(page.getByRole('button', { name: 'Save' })).toBeEnabled();

    await page.getByRole('button', { name: 'Cancel' }).click();
  });
});
