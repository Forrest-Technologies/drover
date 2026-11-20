// Notifications in the service worker. A push carries the kind and the thread and nothing else. The worker asks
// this server, with the person's own sign-in, what to show, so no push service ever sees a bot's name or a word of
// the conversation. Tapping the notification opens that conversation.

self.addEventListener("push", event => event.waitUntil(showWakeUp(readWakeUp(event))));
self.addEventListener("notificationclick", event => {
    event.notification.close();
    event.waitUntil(openConversation(event.notification.data));
});

function readWakeUp(event) {
    try {
        return event.data?.json() ?? null;
    } catch {
        return null;
    }
}

// The folder of the client this worker serves: "/" for the web client, "/native/" for the native client's browser
// build. A notification opens that client.
const pushHome = new URL(self.registration.scope).pathname;

const look = { lang: "en", icon: `${pushHome}icon-192.png` };

// A push must always show something, or the browser shows a notice of its own. When the server does not say what
// (the person signed out, the thread is gone, or this browser's subscription is not the signed-in person's) the
// notification says only that Drover has something.
async function showWakeUp(wakeUp) {
    const text = wakeUp === null ? null : await fetchText(wakeUp);

    if (text === null) {
        return self.registration.showNotification("Drover", { ...look, body: "Something changed. Open Drover to see it.", data: { url: pushHome } });
    }

    // One notification per conversation: a newer one replaces it, and still sounds, so a wait after a finish is heard.
    return self.registration.showNotification("Drover", {
        ...look,
        body: text.body,
        tag: text.threadId,
        renotify: true,
        data: { url: conversationAddress(text) },
    });
}

// The worker names its own subscription with the request, in a header so the address is not in a URL. A browser left
// subscribed by one person, then used by another, is woken for the first person's runs: the server answers only the
// person who holds a subscription for that address. When it says the signed-in person does not, this browser's
// subscription is not theirs, so it is ended here and the browser is woken no more for the last person's bots. They
// turn notifications on again to subscribe afresh. Without a subscription there is nothing to ask for.
async function fetchText(wakeUp) {
    const subscription = await readSubscription();

    if (subscription === null) {
        return null;
    }

    const query = new URLSearchParams({ kind: String(wakeUp.kind), thread: String(wakeUp.thread) });

    try {
        const answer = await fetch(`/api/push/wake?${query}`, {
            credentials: "same-origin",
            headers: { Accept: "application/json", "Push-Endpoint": subscription.endpoint },
        });

        if (answer.status === 403) {
            await endSubscription(subscription);
            return null;
        }

        return answer.ok ? await answer.json() : null;
    } catch {
        return null;
    }
}

async function readSubscription() {
    try {
        return await self.registration.pushManager.getSubscription();
    } catch {
        return null;
    }
}

async function endSubscription(subscription) {
    try {
        await subscription.unsubscribe();
    } catch {
        // A subscription that cannot be ended here is ended by the push service when it is next used.
    }
}

function conversationAddress(text) {
    return text.isMain ? `${pushHome}talk/${text.botId}` : `${pushHome}talk/${text.botId}/side/${text.threadId}`;
}

// An open Drover window goes to the conversation; with none open, a new one opens there.
async function openConversation(data) {
    const url = new URL(data?.url ?? pushHome, self.location.origin);

    if (url.origin !== self.location.origin) {
        return;
    }

    const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
    const open = windows.find(client => isThisClient(new URL(client.url)));

    // A window this worker does not control cannot be moved, so a new one opens instead.
    if (open !== undefined) {
        let moved;

        try {
            moved = await open.navigate(url.href) ?? open;
        } catch {
            return self.clients.openWindow(url.href);
        }

        return moved.focus();
    }

    return self.clients.openWindow(url.href);
}

// A window of this client: the same origin, inside this worker's folder, and not in the folder of the native
// client's browser build, which sits inside the web client's.
function isThisClient(url) {
    const nested = pushHome === "/" ? ["/native/"] : [];
    return url.origin === self.location.origin
        && url.pathname.startsWith(pushHome)
        && !nested.some(path => url.pathname.startsWith(path));
}
