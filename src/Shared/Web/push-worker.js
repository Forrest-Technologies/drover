// Notifications in the service worker. A push carries the kind, the thread, and the person it was sent for, and
// nothing else, encrypted to this browser. The worker asks this server, with the person's own sign-in, what to show, so
// no push service ever sees a bot's name or a word of the conversation, and a push that was sent for one person is
// never described to another. Tapping the notification opens that conversation.

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

// What the server said about a wake: the text to show, nothing to say (the person signed out, the thread is gone,
// or it could not be asked), or a refusal because the wake is not for the person signed in on this browser.
const refused = Symbol("refused");

// A wake that is for someone else, or a browser subscription that is not the signed-in person's, shows nothing: no
// notification is made, so nothing of the last person's activity appears for the next. Every other push that cannot
// be described shows only that Drover has something, so the browser does not show a notice of its own. A browser may
// still make a notice of its own when a page shows none; it names no bot and no thread.
async function showWakeUp(wakeUp) {
    const text = wakeUp === null ? null : await fetchText(wakeUp);

    if (text === refused) {
        return undefined;
    }

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

// The worker names the subscription it holds with the request, in a header so the address is not in a URL, and the
// person the wake was sent for, which the wake itself carries. The server answers only that person, and only while
// they hold a subscription for that address. A browser left subscribed by one person, then used by another, is woken
// for the first person's runs: it is told nothing, 403 when this browser's subscription is not the signed-in person's,
// which is then ended here so the browser is woken no more for the last person's bots, and 409 when the wake was sent
// for someone else, as one already queued as the person switched is, which leaves the signed-in person's own
// subscription alone. The next person turns notifications on to subscribe afresh. Without a subscription, a push has
// nothing to be asked for and shows nothing.
async function fetchText(wakeUp) {
    const subscription = await readSubscription();

    if (subscription === null) {
        return refused;
    }

    const query = new URLSearchParams({ kind: String(wakeUp.kind), thread: String(wakeUp.thread) });

    if (typeof wakeUp.user === "string") {
        query.set("user", wakeUp.user);
    }

    try {
        const answer = await fetch(`/api/push/wake?${query}`, {
            credentials: "same-origin",
            headers: { Accept: "application/json", "Push-Endpoint": subscription.endpoint },
        });

        if (answer.status === 403) {
            await endSubscription(subscription);
            return refused;
        }

        if (answer.status === 409) {
            return refused;
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
