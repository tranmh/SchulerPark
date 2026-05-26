import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  timeout: 30_000,
  expect: { timeout: 5_000 },
  fullyParallel: false,
  retries: 0,
  workers: 1,
  reporter: 'list',
  use: {
    baseURL: process.env.BASE_URL || 'http://localhost:8080',
    locale: 'en-US',
    extraHTTPHeaders: { 'Accept-Language': 'en-US,en;q=0.9' },
    headless: true,
    // The PWA service worker registers with a NetworkOnly strategy for /api/*.
    // page.route() does not intercept SW-mediated fetches by default, which
    // makes mocked API tests unreliable. Block SWs in test runs.
    serviceWorkers: 'block',
    screenshot: 'only-on-failure',
    trace: 'on-first-retry',
    video: {
      mode: 'on',
      size: { width: 1280, height: 720 },
    },
  },
  projects: [
    { name: 'chromium', use: { browserName: 'chromium' } },
  ],
});
