const cachePrefix = 'restaurantes-kds-';
const cacheName = `${cachePrefix}v17`;
const appShell = [
  './',
  './index.html',
  './css/kds.css',
  './js/app.js',
  './js/api-client.js',
  './js/realtime-client.js',
  './js/order-renderer.js',
  './js/kitchen-clock.js',
  './manifest.webmanifest',
  './icons/icon-192.png',
  './icons/icon-512.png'
];

function cacheResponse(event, request, response) {
  event.waitUntil(
    caches.open(cacheName)
      .then(cache => cache.put(request, response))
      .catch(error => console.error('No se pudo guardar la respuesta en caché.', error))
  );
}

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(cacheName)
      .then(cache => cache.addAll(appShell))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(
        keys
          .filter(key => key.startsWith(cachePrefix) && key !== cacheName)
          .map(key => caches.delete(key))
      ))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', event => {
  const request = event.request;
  if (request.method !== 'GET') return;

  const requestUrl = new URL(request.url);
  const scopeUrl = new URL(self.registration.scope);
  if (requestUrl.origin !== scopeUrl.origin || !requestUrl.pathname.startsWith(scopeUrl.pathname)) return;

  if (request.mode === 'navigate') {
    event.respondWith(
      fetch(request)
        .then(response => {
          cacheResponse(event, './index.html', response.clone());
          return response;
        })
        .catch(() => caches.match('./index.html'))
    );
    return;
  }

  if (requestUrl.pathname.endsWith('.js')) {
    event.respondWith(
      fetch(request)
        .then(response => {
          if (response.ok) {
            cacheResponse(event, request, response.clone());
          }
          return response;
        })
        .catch(() => caches.match(request))
    );
    return;
  }

  event.respondWith(
    caches.match(request).then(cached => cached || fetch(request).then(response => {
      if (response.ok) {
        cacheResponse(event, request, response.clone());
      }
      return response;
    }))
  );
});
