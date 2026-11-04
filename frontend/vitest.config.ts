import { configDefaults, defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  resolve: {
    // vite-plugin-pwa is not loaded here, so its virtual register module needs a stub.
    alias: { 'virtual:pwa-register/react': new URL('./src/test/pwaRegisterStub.ts', import.meta.url).pathname },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/test/setup.ts',
    css: false,
    // Playwright end-to-end specs live in e2e/ and are run by `playwright test`, not vitest.
    exclude: [...configDefaults.exclude, 'e2e/**'],
    // Pin a positive-offset zone so timezone-sensitive tests (e.g. the Bug #21
    // booking-window date math) are deterministic on CI (which otherwise runs UTC,
    // where the toISOString roll-back cannot reproduce). Matches the app's Europe/Berlin.
    env: { TZ: 'Europe/Berlin' },
  },
});
