/// <reference lib="webworker" />
import { clientsClaim } from 'workbox-core';
import { cleanupOutdatedCaches, createHandlerBoundToURL, precacheAndRoute } from 'workbox-precaching';
import { NavigationRoute, registerRoute } from 'workbox-routing';
import { NetworkFirst, NetworkOnly } from 'workbox-strategies';
import { ExpirationPlugin } from 'workbox-expiration';

declare let self: ServiceWorkerGlobalScope;

// registerType 'autoUpdate' (vite.config.ts) relies on the new worker taking over
// as soon as it is installed. In injectManifest mode that is our job, not the
// plugin's — without these two calls a new release waits until every tab closes.
self.skipWaiting();
clientsClaim();

// Precache static assets (injected by vite-plugin-pwa) and drop caches left by
// previous workbox versions.
precacheAndRoute(self.__WB_MANIFEST);
cleanupOutdatedCaches();

// Offline navigation fallback: any top-level navigation (typing a URL, reopening
// the installed app on /my-bookings, following a push notification's url) is
// answered with the precached app shell instead of the browser's offline error
// page. React Router then renders the route client-side. The denylist keeps
// server-rendered surfaces on the network so an outage surfaces as a real error
// there rather than as a blank SPA shell.
registerRoute(
  new NavigationRoute(createHandlerBoundToURL('index.html'), {
    denylist: [/^\/api\//, /^\/hangfire/, /^\/swagger/],
  })
);

// Runtime caching: locations API (NetworkFirst, 1h cache)
registerRoute(
  ({ url }) => url.pathname === '/api/locations',
  new NetworkFirst({
    cacheName: 'api-locations',
    plugins: [new ExpirationPlugin({ maxEntries: 10, maxAgeSeconds: 60 * 60 })],
  })
);

// All other API routes: NetworkOnly (booking data must never be stale)
registerRoute(
  ({ url }) => url.pathname.startsWith('/api/'),
  new NetworkOnly()
);

// Push notification handler
self.addEventListener('push', (event) => {
  const data = event.data?.json() ?? { title: 'LouisE', body: 'New notification' };
  event.waitUntil(
    self.registration.showNotification(data.title, {
      body: data.body,
      icon: '/pwa-192x192.png',
      // Android renders the badge as a monochrome alpha mask; use the dedicated
      // white-on-transparent glyph rather than the full-colour app icon.
      badge: '/badge-96x96.png',
      data: { url: data.url || '/' },
      // WP4: the server tags related notifications (e.g. confirmation reminder → expired)
      // so a newer one replaces the older instead of stacking up.
      ...(typeof data.tag === 'string' && data.tag ? { tag: data.tag } : {}),
    })
  );
});

// Click handler: open the app. Only same-origin targets — a notification from
// a trusted OS surface must never navigate users to an external site.
self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  let url = event.notification.data?.url || '/';
  try {
    if (new URL(url, self.location.origin).origin !== self.location.origin) {
      url = '/';
    }
  } catch {
    url = '/';
  }
  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clients) => {
      for (const client of clients) {
        if (client.url.includes(self.location.origin) && 'focus' in client) {
          client.navigate(url);
          return client.focus();
        }
      }
      return self.clients.openWindow(url);
    })
  );
});
