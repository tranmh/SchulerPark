import { useState, useEffect, useCallback } from 'react';
import { subscribeToPush, unsubscribeFromPush, sendTestPush, type PushTestResult } from '../services/pushService';

// Bug #11: a discriminated result so callers can tell a real failure ('error')
// from the user declining ('denied') or an unsupported browser ('unsupported').
export type PushEnableResult =
  | { ok: true }
  | { ok: false; reason: 'unsupported' | 'denied' | 'error' };

export function usePushNotifications() {
  const [permission, setPermission] = useState<NotificationPermission>(
    'Notification' in window ? Notification.permission : 'denied'
  );
  const [isSubscribed, setIsSubscribed] = useState(false);
  const isSupported = 'serviceWorker' in navigator && 'PushManager' in window;

  useEffect(() => {
    if (!isSupported || permission !== 'granted') return;

    navigator.serviceWorker.ready.then((reg) => {
      reg.pushManager.getSubscription().then((sub) => {
        setIsSubscribed(sub !== null);
      });
    });
  }, [isSupported, permission]);

  const requestPermission = useCallback(async (): Promise<PushEnableResult> => {
    if (!isSupported) return { ok: false, reason: 'unsupported' };

    // requestPermission() can reject on some browsers — guard it so the click
    // handler never sees an unhandled rejection.
    let result: NotificationPermission;
    try {
      result = await Notification.requestPermission();
    } catch {
      return { ok: false, reason: 'error' };
    }
    setPermission(result);

    if (result !== 'granted') return { ok: false, reason: 'denied' };

    try {
      await subscribeToPush();
      setIsSubscribed(true);
      return { ok: true };
    } catch {
      // A subscribe failure (e.g. backend 500) is a real error, not a denial.
      return { ok: false, reason: 'error' };
    }
  }, [isSupported]);

  const disable = useCallback(async () => {
    const success = await unsubscribeFromPush();
    if (success) setIsSubscribed(false);
    return success;
  }, []);

  // Manual verification: ask the backend to push a test message to this account's
  // devices. If the server no longer knows this device (404) the local state is
  // stale, so drop the browser-side subscription too and show the Enable button.
  const sendTest = useCallback(async (): Promise<PushTestResult> => {
    const result = await sendTestPush();
    if (!result.ok && result.reason === 'no-subscription') {
      setIsSubscribed(false);
    }
    return result;
  }, []);

  return { isSupported, permission, isSubscribed, requestPermission, disable, sendTest };
}
