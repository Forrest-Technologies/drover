// Caches the app shell so Drover's files load without waiting on the network and it still opens offline.
// The worker answers only GET requests for Drover's own files and pages. Everything else goes to the network as if
// there were no worker: the server's API and sign-in callbacks, anything that is not a GET, and every request to
// another host. A request
// the worker answered itself would be held to the worker's own content security policy, so the worker never answers
// for another host.
self.importScripts("./service-worker-assets.js", "./push-worker.js");

// Every shell this origin stored before is this worker's to remove once its own is stored: an older build's, and the
// one the client stored while it was served under /native/ (until 2026-11-20).
const cacheNamePrefix = "drover-shell-";
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const shellFiles = [/\.dll$/, /\.pdb$/, /\.wasm$/, /\.html$/, /\.js$/, /\.json$/, /\.css$/, /\.woff2$/, /\.ttf$/, /\.manifest$/, /\.png$/, /\.svg$/, /\.webmanifest$/, /\.dat$/, /\.blat$/];
const neverCached = [/^service-worker\.js$/];
// Addresses the server answers itself. /native/ is where the client was served until 2026-11-20; the server sends
// it to the root.
const serverPaths = ["/api/", "/health/", "/openapi/", "/oauth/", "/native/"];

// A new build takes over as soon as its shell is stored, without waiting for every open tab to close: a person
// who leaves Drover open would otherwise keep running the old build for as long as any tab lives. The page
// reloads itself when the worker changes (boot.js), so it never runs old code against the new shell.
self.addEventListener("install", event => event.waitUntil(cacheShell().then(() => self.skipWaiting())));
self.addEventListener("activate", event => event.waitUntil(dropOldShells().then(() => self.clients.claim())));
self.addEventListener("fetch", event => {
    if (answersFor(event.request)) {
        event.respondWith(answer(event.request));
    }
});

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

// Only Drover's own files and pages. Not answering leaves the request to the browser, exactly as without a worker.
function answersFor(request) {
    const url = new URL(request.url);
    return request.method === "GET"
        && url.origin === self.location.origin
        && !serverPaths.some(path => url.pathname.startsWith(path));
}

// Never rejects: a request the worker cannot answer gets a network error response, which the page sees as a failed
// request, instead of a rejection nothing handles.
async function answer(request) {
    try {
        const cache = await caches.open(cacheName);
        return isPage(request) ? await page(request, cache) : await cache.match(request) ?? await fetch(request);
    } catch {
        return Response.error();
    }
}

// A page load: a navigation, or a GET for HTML at an address with no extension. Drover's own files all have an
// extension; a page address has none. The second kind was seen on a live server: a GET for a page address that was
// not a navigation failed in the worker with a cache miss, where the stored shell should have answered.
function isPage(request) {
    const wantsHtml = request.headers.get("accept")?.includes("text/html") === true;
    return request.mode === "navigate" || (wantsHtml && !new URL(request.url).pathname.split("/").pop().includes("."));
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
