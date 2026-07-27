import api from './api';

export const pushService = {
  getVapidPublicKey: () =>
    api.get<{ publicKey: string }>('/push/vapid-public-key').then(r => r.data.publicKey),

  subscribe: (subscription: PushSubscriptionJSON) =>
    api.post('/push/subscribe', {
      endpoint: subscription.endpoint,
      p256dh: subscription.keys?.p256dh ?? '',
      auth: subscription.keys?.auth ?? '',
    }),

  // Body, not query string: the endpoint URL is a bearer capability and must
  // not end up in access logs.
  unsubscribe: (endpoint: string) =>
    api.delete('/push/subscribe', { data: { endpoint } }),
};

function urlBase64ToUint8Array(base64String: string): Uint8Array {
  const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
  const rawData = window.atob(base64);
  return Uint8Array.from(rawData, (char) => char.charCodeAt(0));
}

// Bug #11: let errors propagate so callers can distinguish a real failure (e.g. a 500
// from /push/subscribe) from the user declining. Previously every error was swallowed
// as `false`, making a backend error indistinguishable from a denial.
export async function subscribeToPush(): Promise<void> {
  if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
    throw new Error('Push notifications are not supported in this browser.');
  }

  const vapidKey = await pushService.getVapidPublicKey();
  const registration = await navigator.serviceWorker.ready;

  const subscription = await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: urlBase64ToUint8Array(vapidKey),
  });

  await pushService.subscribe(subscription.toJSON());
}

export async function unsubscribeFromPush(): Promise<boolean> {
  try {
    const registration = await navigator.serviceWorker.ready;
    const subscription = await registration.pushManager.getSubscription();
    if (subscription) {
      await pushService.unsubscribe(subscription.endpoint);
      await subscription.unsubscribe();
    }
    return true;
  } catch {
    return false;
  }
}
