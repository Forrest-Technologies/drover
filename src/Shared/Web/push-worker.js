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

// The folder of the client this worker serves. A notification opens that client.
const pushHome = new URL(self.registration.scope).pathname;

const look = { lang: "en", icon: `${pushHome}icon-192.png` };

// A push must always show something, or the browser shows a notice of its own. When the server does not say what
// (the person signed out, or the thread is gone) the notification says only that Drover has something.
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

async function fetchText(wakeUp) {
    const query = new URLSearchParams({ kind: String(wakeUp.kind), thread: String(wakeUp.thread) });

    try {
        const answer = await fetch(`/api/push/wake?${query}`, { credentials: "same-origin", headers: { Accept: "application/json" } });
        return answer.ok ? await answer.json() : null;
    } catch {
        return null;
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

// A window of this client: the same origin, inside this worker's folder.
function isThisClient(url) {
    return url.origin === self.location.origin && url.pathname.startsWith(pushHome);
}
