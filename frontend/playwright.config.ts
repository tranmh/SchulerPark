import { defineConfig } from '@playwright/test';

// The apps under test are already running (frontend dev server + backend API, proxied
// under /api by Vite). No webServer here on purpose: an app that is not up is an
// environment failure, not something the test run should paper over.
export default defineConfig({
  testDir: './e2e',
  testMatch: /.*\.spec\.ts$/,
  timeout: 30_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  retries: 0,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: process.env.AQAA_BASE_URL || process.env.AQAA_APP_FRONTEND_URL || 'http://localhost:5173',
    locale: 'en-US',
    extraHTTPHeaders: { 'Accept-Language': 'en-US,en;q=0.9' },
    headless: true,
    // The PWA service worker handles /api/* with a NetworkOnly strategy; keep it out of
    // the way so every API call is a plain browser request the test can observe.
    serviceWorkers: 'block',
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
});
