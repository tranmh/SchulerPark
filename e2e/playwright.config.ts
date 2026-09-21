import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  timeout: 30_000,
  expect: { timeout: 5_000 },
  fullyParallel: false,
  retries: 0,
  workers: 1,
  // 'list' for readable console output plus 'html' so failed CI runs retain a
  // report artifact (ci.yml uploads e2e/playwright-report/ on failure; the 'list'
  // reporter alone never writes that directory).
  reporter: [['list'], ['html', { outputFolder: 'playwright-report', open: 'never' }]],
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
    // Desktop suite: everything except tests/mobile/. The sidebar is static at 1280px.
    { name: 'chromium', use: { browserName: 'chromium' }, testIgnore: /tests\/mobile\// },
    // Phone suite: Pixel 7 emulation (Chromium-based, so the same browser install).
    // Only tests/mobile/ runs here; the top bar + drawer replace the sidebar.
    {
      name: 'mobile-chrome',
      use: { ...devices['Pixel 7'], video: { mode: 'on', size: { width: 412, height: 915 } } },
      testMatch: /tests\/mobile\//,
    },
  ],
});
