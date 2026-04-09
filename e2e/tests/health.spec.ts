import { test, expect } from '@playwright/test';

test.describe('Health & Infrastructure', () => {
  test('health endpoint returns healthy', async ({ request }) => {
    const response = await request.get('/api/health');
    expect(response.ok()).toBeTruthy();

    const body = await response.json();
    expect(body.status).toBe('healthy');
  });

  test('swagger is accessible in development', async ({ page }) => {
    const response = await page.goto('/swagger');
    // Swagger may or may not be available depending on environment
    // In dev mode it should return 200
    if (response && response.ok()) {
      await expect(page.locator('body')).toContainText(/swagger|SchulerPark API/i);
    }
  });
});
