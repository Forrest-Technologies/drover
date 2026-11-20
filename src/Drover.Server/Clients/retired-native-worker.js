// Takes the place of the service worker of the browser client as it was served under /native/, before it moved to
// the root on 2026-11-20. The browser asks for a worker's script again when a page in its folder opens, and at least
// once a day, so every browser that kept the old worker gets this one. It removes what the old worker stored and its
// push subscription, so the person is not notified twice, unregisters itself, and moves the pages it controlled to
// the same address at the root. It answers no request: the server sends every address under /native/ to the root.
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", event => event.waitUntil(retire()));

async function retire() {
    await unsubscribe();
    const names = await caches.keys();
    await Promise.all(names.filter(name => name.startsWith("drover-shell-native-")).map(name => caches.delete(name)));
    await self.registration.unregister();
    const pages = await self.clients.matchAll({ type: "window" });
    await Promise.all(pages.map(page => page.navigate(atRoot(page.url)).catch(() => null)));
}

// A push service that cannot be reached leaves the subscription in place. The server forgets it when the push
// service says it is gone.
async function unsubscribe() {
    try {
        const subscription = await self.registration.pushManager.getSubscription();
        await subscription?.unsubscribe();
    } catch {
        // Nothing more can be done from here.
    }
}

function atRoot(address) {
    const url = new URL(address);
    url.pathname = url.pathname.replace(/^\/native(\/|$)/, "/");
    return url.href;
}
