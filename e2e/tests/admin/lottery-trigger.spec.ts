import { test, expect } from '@playwright/test';
import { loginAsAdmin, apiLogin } from '../../helpers/auth';
import { AdminApi } from '../../helpers/api';
import { uniqueName, inDays } from '../../helpers/data';

test.describe('Admin → Lottery Trigger (API + UI verification)', () => {
  let api: AdminApi;
  let testLocationId: string;
  let baseURL: string;

  test.beforeAll(async () => {
    baseURL = process.env.BASE_URL || 'http://localhost:5173';
    api = await AdminApi.create(baseURL);
    const loc = await api.createLocation(uniqueName('Lottery-Loc'), 'Lottery Test');
    testLocationId = loc.id;
    // Add slots so the lottery has something to assign.
    for (let i = 1; i <= 3; i++) {
      await api.createSlot(testLocationId, `LT-${i}`);
    }
  });

  test.afterAll(async () => {
    await api.deactivateLocation(testLocationId).catch(() => {});
    await api.dispose();
  });

  test('admin can trigger lottery for a future date (run all locations)', async () => {
    const date = inDays(2);
    const res = await api.runAll(date);
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.message).toMatch(/Lottery completed/i);
  });

  test('admin can trigger lottery for a specific location + time slot', async () => {
    const date = inDays(3);
    const res = await api.runForLocation(testLocationId, date, 'Morning');
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.message).toMatch(/Morning/);

    // Verify a LotteryRun was recorded.
    const runs = await api.getLotteryRuns({ locationId: testLocationId });
    const match = runs.lotteryRuns.find(
      (r: { date: string; timeSlot: string }) => r.date === date && r.timeSlot === 'Morning'
    );
    expect(match).toBeDefined();
  });

  test('invalid time slot returns 400 with ProblemDetails', async () => {
    const date = inDays(4);
    const res = await api.runForLocation(testLocationId, date, 'Evening' as 'Morning');
    expect(res.status()).toBe(400);
    const body = await res.json();
    expect(body.detail).toMatch(/time slot/i);
  });

  test('non-admin user cannot trigger lottery (403)', async ({ request }) => {
    const userToken = await apiLogin(request, 'anna.mueller@schuler.de', 'Test1234!');
    const date = inDays(5);
    const res = await request.post(`/api/lottery/run?date=${date}`, {
      headers: { Authorization: `Bearer ${userToken}` },
    });
    expect(res.status()).toBe(403);
  });

  test('triggered run shows up in the lottery history UI', async ({ page }) => {
    const date = inDays(6);
    const res = await api.runForLocation(testLocationId, date, 'Afternoon');
    expect(res.status()).toBe(200);

    await loginAsAdmin(page);
    await page.goto('/admin/lottery-history');
    await page.waitForLoadState('networkidle');

    // Filter to the test location to keep results small
    await page.locator('select').first().selectOption(testLocationId);
    await page.waitForResponse((r) => r.url().includes('/admin/lottery-runs') && r.status() === 200);

    await expect(page.getByText(date).first()).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText('Afternoon').first()).toBeVisible();
  });
});
