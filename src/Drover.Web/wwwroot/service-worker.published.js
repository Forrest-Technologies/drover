// Caches the app shell so Drover's files load without waiting on the network and it still opens offline.
// Data is never cached: every /api request goes to the server.
self.importScripts("./service-worker-assets.js");

const cacheNamePrefix = "drover-shell-";
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const shellFiles = [/\.dll$/, /\.pdb$/, /\.wasm$/, /\.html$/, /\.js$/, /\.json$/, /\.css$/, /\.woff2$/, /\.png$/, /\.svg$/, /\.webmanifest$/, /\.dat$/, /\.blat$/];
const neverCached = [/^service-worker\.js$/];
const serverPaths = ["/api/", "/health/", "/openapi/"];

self.addEventListener("install", event => event.waitUntil(cacheShell()));
self.addEventListener("activate", event => event.waitUntil(dropOldShells()));
self.addEventListener("fetch", event => event.respondWith(answer(event)));

async function cacheShell() {
    const requests = self.assetsManifest.assets
        .filter(asset => shellFiles.some(pattern => pattern.test(asset.url)))
        .filter(asset => !neverCached.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: "no-cache" }));
    const cache = await caches.open(cacheName);
    await cache.addAll(requests);
}

async function dropOldShells() {
    const names = await caches.keys();
    await Promise.all(names
        .filter(name => name.startsWith(cacheNamePrefix) && name !== cacheName)
        .map(name => caches.delete(name)));
}

async function answer(event) {
    const url = new URL(event.request.url);
    const forServer = url.origin !== self.location.origin || serverPaths.some(path => url.pathname.startsWith(path));

    if (event.request.method !== "GET" || forServer) {
        return fetch(event.request);
    }

    const cache = await caches.open(cacheName);
    if (event.request.mode === "navigate") {
        return page(event.request, cache);
    }

    return await cache.match(event.request) ?? fetch(event.request);
}

// A page is asked of the server first. Its response carries the security headers the browser enforces for
// the whole visit, and a stored copy would keep the headers of the day it was stored: a changed policy
// would never arrive, because a change to the server alone does not change the shell's version. The stored
// shell answers only when the server cannot be reached. Every page address is the same shell.
async function page(request, cache) {
    try {
        return await fetch(request);
    } catch {
        return await cache.match("index.html") ?? Response.error();
    }
}
