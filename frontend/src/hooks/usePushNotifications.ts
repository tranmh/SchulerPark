import { useState, useEffect, useCallback } from 'react';
import { subscribeToPush, unsubscribeFromPush } from '../services/pushService';

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

  const requestPermission = useCallback(async () => {
    if (!isSupported) return false;

    const result = await Notification.requestPermission();
    setPermission(result);

    if (result === 'granted') {
      const success = await subscribeToPush();
      setIsSubscribed(success);
      return success;
    }
    return false;
  }, [isSupported]);

  const disable = useCallback(async () => {
    const success = await unsubscribeFromPush();
    if (success) setIsSubscribed(false);
    return success;
  }, []);

  return { isSupported, permission, isSubscribed, requestPermission, disable };
}
