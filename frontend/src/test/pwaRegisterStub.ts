// Stand-in for vite-plugin-pwa's `virtual:pwa-register/react` under vitest
// (see vitest.config.ts alias). Tests that need to drive `needRefresh` mock
// this module with vi.mock('virtual:pwa-register/react', …).
import { useState } from 'react';

export function useRegisterSW() {
  const needRefresh = useState(false);
  const offlineReady = useState(false);
  return { needRefresh, offlineReady, updateServiceWorker: async () => {} };
}
