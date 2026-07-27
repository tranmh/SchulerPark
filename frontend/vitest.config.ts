import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/test/setup.ts',
    css: false,
    // Pin a positive-offset zone so timezone-sensitive tests (e.g. the Bug #21
    // booking-window date math) are deterministic on CI (which otherwise runs UTC,
    // where the toISOString roll-back cannot reproduce). Matches the app's Europe/Berlin.
    env: { TZ: 'Europe/Berlin' },
  },
});
