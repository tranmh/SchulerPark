import { useCallback, useEffect, useRef } from 'react';
import { useRegisterSW } from 'virtual:pwa-register/react';

/** How often a long-lived tab asks the server whether sw.js changed. */
export const UPDATE_CHECK_INTERVAL_MS = 60 * 60 * 1000;

/**
 * Registers the service worker (registerType 'prompt', see vite.config.ts) and
 * tells the UI when a newly deployed release is installed and waiting.
 *
 * Browsers only re-fetch sw.js on navigation, so a tab left open for days would
 * never notice a deploy. We therefore also check on a timer and whenever the
 * tab/installed app returns to the foreground. `reload` tells the waiting
 * worker to take over (sw.ts handles SKIP_WAITING); the plugin then reloads the
 * page once the new worker controls it, so the user never sees a half-updated
 * app shell.
 */
export function usePwaUpdate() {
  const registrationRef = useRef<ServiceWorkerRegistration | null>(null);

  const {
    needRefresh: [needRefresh, setNeedRefresh],
    updateServiceWorker,
  } = useRegisterSW({
    onRegisteredSW(_url, registration) {
      registrationRef.current = registration ?? null;
    },
  });

  useEffect(() => {
    const check = () => {
      if (!navigator.onLine) return;
      registrationRef.current?.update().catch(() => {
        /* offline or server unreachable — the next check will try again */
      });
    };
    const onVisible = () => {
      if (document.visibilityState === 'visible') check();
    };
    const timer = window.setInterval(check, UPDATE_CHECK_INTERVAL_MS);
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, []);

  const reload = useCallback(() => {
    void updateServiceWorker(true);
  }, [updateServiceWorker]);

  const dismiss = useCallback(() => setNeedRefresh(false), [setNeedRefresh]);

  return { needRefresh, reload, dismiss };
}
