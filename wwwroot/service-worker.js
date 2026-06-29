// Service worker desactivado a propósito (causaba que la web se viera "vieja"
// hasta borrar datos del sitio). Este SW se auto-elimina, borra toda la caché
// y recarga las pestañas una vez, para que cualquier cliente quede limpio.
self.addEventListener("install", () => self.skipWaiting());

self.addEventListener("activate", event => {
  event.waitUntil((async () => {
    try {
      const keys = await caches.keys();
      await Promise.all(keys.map(k => caches.delete(k)));
      await self.registration.unregister();
      const clients = await self.clients.matchAll({ type: "window" });
      for (const client of clients) {
        try { client.navigate(client.url); } catch (_) {}
      }
    } catch (_) {}
  })());
});

// Siempre red directa; nunca servir desde caché.
self.addEventListener("fetch", () => {});
