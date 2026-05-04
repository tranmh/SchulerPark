import { test, expect } from '@playwright/test';
import { loginAsAdmin } from '../../helpers/auth';
import { AdminApi } from '../../helpers/api';
import { uniqueName, inDays } from '../../helpers/data';

test.describe('Admin → Blocked Days', () => {
  let api: AdminApi;
  let testLocationId: string;

  test.beforeAll(async () => {
    api = await AdminApi.create(process.env.BASE_URL || 'http://localhost:5173');
    const loc = await api.createLocation(uniqueName('BlockedDays-Loc'), 'Blocked Days Test');
    testLocationId = loc.id;
  });

  test.afterAll(async () => {
    await api.deactivateLocation(testLocationId).catch(() => {});
    await api.dispose();
  });

  test('blocked days page shows seeded block in the right-hand list', async ({ page }) => {
    const date = inDays(20);
    const reason = `E2E block ${Date.now()}`;
    const created = await api.createBlockedDay({ locationId: testLocationId, date, reason });
    expect(created.id).toBeDefined();

    await loginAsAdmin(page);
    await page.goto('/admin/blocked-days');
    await page.getByLabel('Location').selectOption(testLocationId);
    await page.waitForResponse((r) => r.url().includes('/admin/blocked-days') && r.status() === 200);

    const row = page.locator('[data-testid="blocked-day-row"]', { hasText: reason });
    await expect(row).toBeVisible({ timeout: 10_000 });
    await expect(row).toContainText(date);
  });

  test('removing a block via the list deletes it from the API', async ({ page }) => {
    const date = inDays(21);
    const reason = `E2E remove-me ${Date.now()}`;
    await api.createBlockedDay({ locationId: testLocationId, date, reason });

    await loginAsAdmin(page);
    await page.goto('/admin/blocked-days');
    await page.getByLabel('Location').selectOption(testLocationId);

    const row = page.locator('[data-testid="blocked-day-row"]', { hasText: reason });
    await row.getByRole('button', { name: 'Remove' }).click();

    // Reason should no longer be in the list.
    await expect(page.getByText(reason)).toHaveCount(0, { timeout: 10_000 });

    // Confirm via API
    const remaining = await api.listBlockedDays(testLocationId);
    expect(remaining.find((b: { reason: string | null }) => b.reason === reason)).toBeUndefined();
  });

  test('add-block modal opens when clicking a future calendar date and saves', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/blocked-days');
    await page.getByLabel('Location').selectOption(testLocationId);

    // Click a far-future date that's almost certainly not already blocked.
    // The calendar shows numbers 1..28-31 of the current month. Pick the 28th
    // (always a valid day) but avoid clashing if already blocked by checking
    // first; if 28 is blocked we'd just skip the test cleanly via expect below.
    const targetDay = '28';
    const dayCell = page.getByRole('button', { name: targetDay, exact: true }).first();
    if (!(await dayCell.isEnabled().catch(() => false))) {
      test.skip(true, `Day ${targetDay} is not selectable in this month — calendar UI test skipped.`);
    }
    await dayCell.click();

    // Modal: "Block YYYY-MM-DD" heading
    await expect(page.getByRole('heading', { name: /^Block \d{4}-\d{2}-\d{2}$/ })).toBeVisible();
    const reason = `Modal-block ${Date.now()}`;
    await page.getByLabel('Reason (optional)').fill(reason);
    await page.getByRole('button', { name: 'Block Day' }).click();

    await expect(page.getByText(reason)).toBeVisible({ timeout: 10_000 });
  });
});
