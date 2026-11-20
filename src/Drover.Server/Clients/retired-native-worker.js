// Takes the place of the service worker of the browser client as it was served under /native/, before it moved to
// the root on 2026-11-20. The browser asks for a worker's script again when a page in its folder opens, and at least
// once a day, so every browser that kept the old worker gets this one. It removes what the old worker stored,
// unregisters itself, and moves the pages it controlled to the same address at the root. It answers no request.
//
// Unregistering ends the old worker's push subscription. When there was one, it first leaves a note (a cache named
// drover-push-moved, which no worker of the client removes), and the client at the root subscribes in its place the
// next time the person is signed in, without asking when the browser still allows notifications, and otherwise says
// they are off and where to turn them on (MovedNotifications).
//
// The start page of an app installed from /native/ is the server's moving page (retired-native-page.html), which an
// installed app must stay on for a moment so the browser can read its new manifest. That page moves itself to the
// root, so this worker leaves it alone.
const movedNote = "drover-push-moved";

self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", event => event.waitUntil(retire()));

async function retire() {
    await handOverNotifications();
    const names = await caches.keys();
    await Promise.all(names.filter(name => name.startsWith("drover-shell-native-")).map(name => caches.delete(name)));
    await self.registration.unregister();
    const pages = await self.clients.matchAll({ type: "window" });
    await Promise.all(pages
        .filter(page => !isMovingPage(new URL(page.url)))
        .map(page => page.navigate(atRoot(page.url)).catch(() => null)));
}

// A push service that cannot be reached leaves the subscription in place. The server forgets it when the push
// service says it is gone.
async function handOverNotifications() {
    try {
        const subscription = await self.registration.pushManager.getSubscription();

        if (subscription === null) {
            return;
        }

        await (await caches.open(movedNote)).put("moved", new Response(""));
        await subscription.unsubscribe();
    } catch {
        // Nothing more can be done from here. Unregistering ends the subscription in any case.
    }
}

function isMovingPage(url) {
    return url.pathname === "/native/" && url.search === "";
}

function atRoot(address) {
    const url = new URL(address);
    url.pathname = url.pathname.replace(/^\/native(\/|$)/, "/");
    return url.href;
}
