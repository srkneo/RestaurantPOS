// Service worker: lets the POS open without internet. The page, its scripts and styles, and the fonts they use are saved
// in the browser as soon as the POS is opened online, so nothing is missing when the connection drops.
// API calls are never cached here (the POS keeps its own copy of the menu, and orders wait in the offline queue).
// Keep this cache name: the files of each release are swapped inside it, so a saved copy is never thrown away early.
const CACHE = 'pos-shell-v2';

// Shown only when this till has no saved copy of the POS yet and the server cannot be reached. Tries again every 15 seconds.
function offlinePage() {
  const html = '<!doctype html><meta charset="utf-8"><meta http-equiv="refresh" content="15"><meta name="viewport" content="width=device-width, initial-scale=1">'
    + '<title>No connection</title><body style="font-family:sans-serif;padding:32px;max-width:520px;margin:auto">'
    + '<h1>No connection</h1><p>The POS cannot reach the server, and this till has not saved the POS yet.</p>'
    + '<p>Check the internet connection. This page tries again by itself.</p><p><a href="">Try now</a></p></body>';
  return new Response(html, { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8' } });
}

// Saves every built file the page needs: the scripts and styles named in the page, the scripts those load later,
// and the fonts (woff2) named in the styles. Files already saved are skipped; files of older versions are removed.
async function saveAll(html) {
  const cache = await caches.open(CACHE);
  const wanted = new Set();
  const queue = [...html.matchAll(/(?:src|href)="(\/assets\/[^"]+)"/g)].map((m) => m[1]);
  while (queue.length > 0 && wanted.size < 300) {
    const file = queue.pop();
    if (wanted.has(file)) continue;
    wanted.add(file);
    let response = await cache.match(file);
    if (!response) {
      response = await fetch(file);
      if (!response.ok) continue;
      await cache.put(file, response.clone());
    }
    if (file.endsWith('.css')) {
      const css = await response.clone().text();
      for (const m of css.matchAll(/url\(\s*["']?([^"')]+\.woff2)["']?\s*\)/g)) queue.push(new URL(m[1], self.location.origin + file).pathname);
    } else if (file.endsWith('.js')) {
      const js = await response.clone().text();
      for (const m of js.matchAll(/["'`](?:\.\/|\/?assets\/)([\w.-]+\.(?:js|css))["'`]/g)) queue.push(`/assets/${m[1]}`);
    }
  }
  for (const request of await cache.keys()) {
    const path = new URL(request.url).pathname;
    if (path.startsWith('/assets/') && !wanted.has(path) && !path.endsWith('.woff')) await cache.delete(request);
  }
}

self.addEventListener('install', (event) => {
  // If the page cannot be saved right now (server restarting, no connection), this install fails on purpose:
  // the helper that is already running keeps its saved files, and the browser tries again on a later visit.
  event.waitUntil((async () => {
    const page = await fetch('/', { cache: 'no-cache' });
    if (!page.ok) throw new Error('The page is not available right now');
    const cache = await caches.open(CACHE);
    await cache.put('/', page.clone());
    await saveAll(await page.text()).catch(() => {}); // files that fail here are saved when the page first uses them
    await self.skipWaiting();
  })());
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    // Remove the caches of earlier helpers only once this one really holds the page.
    const cache = await caches.open(CACHE);
    if (await cache.match('/')) {
      const names = await caches.keys();
      await Promise.all(names.filter((name) => name !== CACHE).map((name) => caches.delete(name)));
    }
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET') return;
  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/orderHub') || url.pathname.startsWith('/health')) return;

  // Pages: always try the server first so new versions show at once; fall back to the saved page when offline.
  // After a new version is seen, its files are saved in the background.
  if (request.mode === 'navigate') {
    event.respondWith(fetch(request)
      .then((response) => {
        if (response.ok) {
          try {
            const copy = response.clone();
            const text = response.clone();
            event.waitUntil(caches.open(CACHE).then((cache) => cache.put('/', copy)).then(() => text.text()).then(saveAll).catch(() => {}));
          } catch {
            // saving is extra; the page itself is still shown
          }
        }
        return response;
      })
      .catch(async () => (await caches.match('/')) || offlinePage()));
    return;
  }

  // Built files have a hash in their name and never change, so the saved copy is always right.
  if (url.pathname.startsWith('/assets/')) {
    event.respondWith(caches.match(request).then((hit) => hit || fetch(request).then((response) => {
      if (response.ok) { const copy = response.clone(); caches.open(CACHE).then((cache) => cache.put(request, copy)); }
      return response;
    })).catch(() => Response.error()));
  }
});
