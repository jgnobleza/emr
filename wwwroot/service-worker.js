const CACHE_NAME = "medrec-offline-v12";
const RUNTIME_CACHE = "medrec-runtime-v12";
const CORE_ASSETS = [
  "/Account/Login",
  "/Account/Login?returnUrl=%2F",
  "/css/site.css",
  "/js/site.js",
  "/js/offline-store.js",
  "/js/pwa-install.js",
  "/uploads/labs/bmp-demo.pdf",
  "/lib/bootstrap/dist/css/bootstrap.min.css",
  "/lib/bootstrap/dist/js/bootstrap.bundle.min.js",
  "/lib/jquery/dist/jquery.min.js"
];
const OFFLINE_NAVIGATION_URLS = [
  "/",
  "/Patients",
  "/Records",
  "/Prescriptions",
  "/Reports",
  "/Sync",
  "/Settings"
];
let vaultSessionKey = null;
let offlineMode = false;

self.addEventListener("message", (event) => {
  if (event.data?.type === "SKIP_WAITING") self.skipWaiting();
  if (event.data?.type === "CACHE_CURRENT_PAGE" && event.data.url) {
    event.waitUntil(cacheCurrentPage(event.data.url));
  }
  if (event.data?.type === "WARM_OFFLINE_PAGES") {
    event.waitUntil(warmOfflinePages(event.data.urls || OFFLINE_NAVIGATION_URLS).then(() => {
      if (event.ports?.[0]) {
        event.ports[0].postMessage({ type: "WARM_OFFLINE_PAGES_DONE" });
      }
    }));
  }
  if (event.data?.type === "CACHE_URLS") {
    event.waitUntil(warmOfflinePages(event.data.urls || []).then(() => {
      if (event.ports?.[0]) {
        event.ports[0].postMessage({
          type: "CACHE_URLS_DONE",
          count: Array.isArray(event.data.urls) ? event.data.urls.length : 0
        });
      }
    }));
  }
  if (event.data?.type === "SET_VAULT_SESSION_KEY") vaultSessionKey = event.data.key || null;
  if (event.data?.type === "GET_VAULT_SESSION_KEY" && event.ports?.[0]) event.ports[0].postMessage({ key: vaultSessionKey });
  if (event.data?.type === "SET_CONNECTIVITY") offlineMode = event.data.online === false;
});

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) =>
      Promise.all(CORE_ASSETS.map((asset) => cache.add(asset).catch(() => null)))
    )
  );
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter((key) => ![CACHE_NAME, RUNTIME_CACHE].includes(key)).map((key) => caches.delete(key)))
    )
  );
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);
  if (url.origin !== self.location.origin) {
    return;
  }

  if (event.request.method !== "GET") {
    return;
  }

  if (event.request.mode === "navigate") {
    event.respondWith(
      safeNavigate(event.request, event)
    );
    return;
  }

  if (CORE_ASSETS.includes(url.pathname)) {
    event.respondWith(cacheFirst(event.request));
    return;
  }

  if (url.pathname.startsWith("/uploads/") || url.pathname.startsWith("/files/drive/")) {
    event.respondWith(cacheFirst(event.request));
    return;
  }

  if (url.pathname.startsWith("/css/")
      || url.pathname.startsWith("/js/")
      || url.pathname.startsWith("/lib/")) {
    event.respondWith(networkFirst(event.request, null, false, true, RUNTIME_CACHE));
    return;
  }

  event.respondWith(
    fetch(event.request).catch(() => caches.match(event.request))
  );
});

async function cacheFirst(request) {
  const cached = await caches.match(request);
  if (cached) return cached;
  return cacheResponse(request);
}

async function networkFirst(request, fallbackUrl, preferFallback = false, cacheSuccessful = true, cacheName = CACHE_NAME) {
  try {
    return await fetchWithTimeout(request, 4500).then(async (response) => {
      if (cacheSuccessful && response && response.ok) {
        await caches.open(cacheName).then((cache) => cache.put(request, response.clone()));
      }
      return response;
    });
  } catch {
    if (preferFallback && fallbackUrl) return caches.match(fallbackUrl);
    const cached = await caches.match(request);
    if (cached) return cached;
    if (fallbackUrl) return caches.match(fallbackUrl);
    return caches.match("/");
  }
}

async function navigateNetworkFirst(request, event) {
  const cachedPage = await caches.match(request);
  if (cachedPage && (offlineMode || self.navigator.onLine === false)) {
    return cachedPage;
  }

  try {
    return await fetchWithTimeout(request.clone(), 4500).then(async (response) => {
      if (response && response.ok && shouldCacheNavigationResponse(request.url, response)) {
        await caches.open(RUNTIME_CACHE).then((cache) => cache.put(request, response.clone()));
      }
      return response;
    });
  } catch {
    const cachedPage = await caches.match(request);
    if (cachedPage) return cachedPage;

    const url = new URL(request.url);
    if (url.pathname === "/") {
      const cachedRoot = await caches.match("/");
      if (cachedRoot) return cachedRoot;
    }

    const cachedPath = url.search
      ? null
      : await caches.match(url.pathname);
    if (cachedPath) return cachedPath;

    const cachedHome = await caches.match("/");
    if (cachedHome) return cachedHome;

    return new Response("MedRec is not available offline yet. Open the site online once, then try again.", {
      status: 503,
      headers: { "Content-Type": "text/plain; charset=utf-8" }
    });
  }
}

async function safeNavigate(request, event) {
  try {
    const response = await navigateNetworkFirst(request, event);
    if (response) return response;
  } catch {
    // Fall through to a guaranteed HTML response below.
  }

  try {
    const cachedHome = await caches.match("/");
    if (cachedHome) return cachedHome;
  } catch {
    // Ignore cache failures and return the static fallback below.
  }

  return new Response(`<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>MedRec EMR</title>
  <style>
    body{margin:0;font-family:Arial,sans-serif;background:#f5f8fb;color:#152238}
    header{height:60px;display:flex;align-items:center;padding:0 28px;background:#fff;border-bottom:1px solid #d8e3ef;font-weight:700}
    main{max-width:860px;margin:72px auto;padding:0 24px}
    h1{font-size:42px;margin:0 0 12px}
    p{font-size:16px;line-height:1.5}
  </style>
</head>
<body>
  <header>MedRec</header>
  <main>
    <h1>MedRec is offline</h1>
    <p>The app shell is available, but this device does not have a cached MedRec page for this launch yet. Open MedRec once from its deployed link while online and wait for "Offline site ready."</p>
  </main>
</body>
</html>`, {
    status: 200,
    headers: { "Content-Type": "text/html; charset=utf-8" }
  });
}

async function cacheCurrentPage(url) {
  try {
    const request = new Request(url, { credentials: "same-origin" });
    const response = await fetch(request);
    if (response && response.ok && shouldCacheNavigationResponse(url, response)) {
      await caches.open(RUNTIME_CACHE).then((cache) => cache.put(request, response.clone()));
    }
  } catch {
    // Best-effort cache warmup.
  }
}

async function warmOfflinePages(urls) {
  const uniqueUrls = Array.from(new Set(urls || []));

  for (const url of uniqueUrls) {
    await cacheCurrentPage(url);
  }
}

async function cacheResponse(request) {
  const response = await fetch(request);
  if (response && response.ok) {
    await caches.open(CACHE_NAME).then((cache) => cache.put(request, response.clone()));
  }
  return response;
}

function shouldCacheNavigationResponse(url, response) {
  const requestUrl = new URL(url, self.location.origin);
  const responseUrl = new URL(response.url || requestUrl.href);
  const isLoginRequest = requestUrl.pathname.startsWith("/Account/Login");
  const isLoginResponse = responseUrl.pathname.startsWith("/Account/Login");

  return isLoginRequest || !isLoginResponse;
}

function fetchWithTimeout(request, timeoutMs) {
  return Promise.race([
    fetch(request),
    new Promise((_, reject) => setTimeout(() => reject(new Error("Network timeout")), timeoutMs))
  ]);
}
