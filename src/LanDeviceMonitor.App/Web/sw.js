'use strict';
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(self.clients.claim()));
self.addEventListener('push', e => {
  let data = {};
  try { data = e.data ? e.data.json() : {}; } catch { data = {}; }
  e.waitUntil(self.registration.showNotification(typeof data.title === 'string' ? data.title : '局域网设备助手', {
    body: typeof data.body === 'string' ? data.body : '有新的设备状态，请打开查看。',
    icon: '/icon.png', badge: '/icon.png', tag: typeof data.tag === 'string' ? data.tag : 'device-update',
    data: { url: self.registration.scope }
  }));
});
self.addEventListener('notificationclick', e => {
  e.notification.close();
  e.waitUntil((async () => {
    const clients = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const client of clients) if (new URL(client.url).origin === self.location.origin) { await client.focus(); return; }
    await self.clients.openWindow(self.registration.scope);
  })());
});
