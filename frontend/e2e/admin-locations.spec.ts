import { expect, request as playwrightRequest, test, type APIRequestContext, type Page } from '@playwright/test';

// Dev seed credentials (CLAUDE.md "Default Credentials").
const ADMIN = { email: 'admin@schulerpark.local', password: 'Admin123!' };

interface LocationRow {
  id: string;
  name: string;
  isActive: boolean;
}

async function loginAsAdmin(page: Page) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(ADMIN.email);
  await page.getByLabel('Password', { exact: true }).fill(ADMIN.password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.waitForURL('/', { timeout: 15_000 });
  // Let the refresh-token cookie and dashboard settle before a hard navigation,
  // otherwise the auth init can race and bounce back to /login.
  await page.waitForLoadState('networkidle');
}

/** Admin API access for cleanup (independent of the browser session). */
async function adminApi(baseURL: string): Promise<{ ctx: APIRequestContext; headers: Record<string, string> }> {
  const ctx = await playwrightRequest.newContext({ baseURL });
  const res = await ctx.post('/api/auth/login', { data: ADMIN });
  if (!res.ok()) throw new Error(`admin API login failed (${res.status()}): ${await res.text()}`);
  const { accessToken } = (await res.json()) as { accessToken: string };
  return { ctx, headers: { Authorization: `Bearer ${accessToken}` } };
}

test.describe('Admin → Locations (t18)', () => {
  // Location names are unique across the whole table (DB index + location_name_taken),
  // and the database outlives a test run, so the name carries a per-run suffix. The
  // suffix only makes the name unique; nothing in the test depends on its value.
  const locationName = `AQAA Loc ${Date.now().toString(36)}`;

  test.afterEach(async ({ baseURL }) => {
    // Deactivate whatever this test created, even if it failed halfway through.
    const { ctx, headers } = await adminApi(baseURL!);
    try {
      const locations = (await (await ctx.get('/api/admin/locations', { headers })).json()) as LocationRow[];
      for (const loc of locations.filter((l) => l.name === locationName && l.isActive)) {
        await ctx.delete(`/api/admin/locations/${loc.id}`, { headers });
      }
    } finally {
      await ctx.dispose();
    }
  });

  test('admin creates a location, is refused a duplicate name, then deactivates it with impact feedback', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/locations');
    await expect(page.getByRole('heading', { name: 'Locations' })).toBeVisible();

    // ── create ────────────────────────────────────────────────────────────
    await page.getByRole('button', { name: /new location/i }).click();
    await expect(page.getByRole('button', { name: 'Save' })).toBeDisabled();
    await page.locator('#location-name').fill(locationName);
    await page.getByRole('button', { name: 'Save' }).click();

    const row = page.getByRole('row').filter({ hasText: locationName });
    await expect(row).toHaveCount(1);
    await expect(row.getByText('Active')).toBeVisible();
    await expect(row.getByText('0', { exact: true })).toBeVisible();
    // New locations start with the backend default algorithm.
    await expect(row.locator('select')).toHaveValue('WeightedHistory');
    await expect(row.getByRole('button', { name: 'Deactivate' })).toBeVisible();

    // ── duplicate ─────────────────────────────────────────────────────────
    await page.getByRole('button', { name: /new location/i }).click();
    await page.locator('#location-name').fill(locationName);
    const duplicateResponse = page.waitForResponse(
      (r) => r.url().includes('/api/admin/locations') && r.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Save' }).click();
    expect((await duplicateResponse).status()).toBe(400);

    // The page reports the unique violation and keeps the form open for a correction.
    await expect(page.getByText('Failed to save location.')).toBeVisible();
    await expect(page.locator('#location-name')).toHaveValue(locationName);
    await page.getByRole('button', { name: 'Cancel' }).click();
    await expect(page.locator('#location-name')).toHaveCount(0);
    await expect(page.getByRole('row').filter({ hasText: locationName })).toHaveCount(1);

    // ── deactivate: cancel first ──────────────────────────────────────────
    await row.getByRole('button', { name: 'Deactivate' }).click();
    const dialog = page.getByRole('alertdialog');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('heading', { name: 'Deactivate location' })).toBeVisible();
    await expect(dialog).toContainText(`Location ${locationName} will be deactivated. No further bookings can be made.`);
    // A fresh location holds no bookings: the preview resolves to "none affected".
    await expect(dialog).toContainText('No existing bookings are affected.');

    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toHaveCount(0);
    await expect(row.getByText('Active')).toBeVisible();

    // ── deactivate: Escape also cancels (B49) ─────────────────────────────
    await row.getByRole('button', { name: 'Deactivate' }).click();
    await expect(dialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await expect(row.getByText('Active')).toBeVisible();

    // ── deactivate: confirm ───────────────────────────────────────────────
    await row.getByRole('button', { name: 'Deactivate' }).click();
    await expect(dialog).toBeVisible();
    const deactivateResponse = page.waitForResponse(
      (r) => /\/api\/admin\/locations\/[0-9a-f-]+$/i.test(r.url()) && r.request().method() === 'DELETE',
    );
    await dialog.getByRole('button', { name: 'Deactivate' }).click();
    expect((await deactivateResponse).ok()).toBe(true);

    await expect(dialog).toHaveCount(0);
    await expect(row.getByText('Inactive')).toBeVisible();
    await expect(row.getByRole('button', { name: 'Deactivate' })).toHaveCount(0);
    await expect(row.getByRole('button', { name: 'Edit' })).toBeVisible();

    // The impact banner reports what happened to the (zero) bookings and can be dismissed.
    const banner = page.getByRole('status');
    await expect(banner).toContainText('No existing bookings are affected.');
    await banner.getByRole('button', { name: 'Close' }).click();
    await expect(banner).toHaveCount(0);

    // The state survives a reload (persisted server-side, not just local state).
    await page.reload();
    const reloadedRow = page.getByRole('row').filter({ hasText: locationName });
    await expect(reloadedRow.getByText('Inactive')).toBeVisible();
  });
});
