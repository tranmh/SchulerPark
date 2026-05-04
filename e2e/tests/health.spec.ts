import { test, expect } from '@playwright/test';

test.describe('Health & Infrastructure', () => {
  test('health endpoint returns healthy', async ({ request }) => {
    const response = await request.get('/api/health');
    expect(response.ok()).toBeTruthy();

    const body = await response.json();
    expect(body.status).toBe('healthy');
  });

  test('swagger is accessible in development', async ({ page, request }) => {
    // Hit the API host directly (not the SPA / Vite proxy), since /swagger lives
    // on the backend, not the frontend.
    const apiBase = process.env.API_BASE_URL || 'http://localhost:5000';
    const response = await request.get(`${apiBase}/swagger`).catch(() => null);
    if (!response) {
      test.skip(true, 'API host not reachable from this environment.');
      return;
    }
    if (!response.ok()) {
      test.skip(true, `Swagger not enabled (status ${response.status()}).`);
      return;
    }
    const body = await response.text();
    expect(body).toMatch(/swagger|SchulerPark API/i);
    void page;
  });
});
