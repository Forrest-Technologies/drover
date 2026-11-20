// The page's side of notifications. Permission is asked only in subscribe, which runs when the person turns
// notifications on. What a notification says is never sent through the push service: the worker asks this server.

// What the browser allows. An iPhone or iPad gives web apps the Push API only once added to the Home Screen.
export async function check() {
    const supported = "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;

    if (!supported) {
        return { support: isAppleTouchDevice() && !isInstalled() ? "needs-home-screen" : "not-supported", endpoint: null };
    }

    if (Notification.permission === "denied") {
        return { support: "blocked", endpoint: null };
    }

    const registration = await navigator.serviceWorker.ready;
    const subscription = await registration.pushManager.getSubscription();
    return { support: "available", endpoint: subscription?.endpoint ?? null };
}

// Asks for permission and subscribes with the server's key. A subscription made with another key is replaced:
// the push service would refuse this server's signature on it.
export async function subscribe(publicKey) {
    if (await Notification.requestPermission() !== "granted") {
        return null;
    }

    const registration = await navigator.serviceWorker.ready;
    const key = fromBase64Url(publicKey);
    let subscription = await registration.pushManager.getSubscription();

    if (subscription !== null && !sameKey(subscription.options.applicationServerKey, key)) {
        await subscription.unsubscribe();
        subscription = null;
    }

    subscription ??= await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });
    const json = subscription.toJSON();
    return { endpoint: json.endpoint, publicKey: json.keys.p256dh, authSecret: json.keys.auth };
}

// The note the retired worker of the client's former address, /native/, leaves when it ends a subscription
// (retired-native-worker.js in the server). The client at the root takes it up again (MovedNotifications).
const movedNote = "drover-push-moved";

// "none" without the note; otherwise whether the browser still allows notifications, so the client at the root can
// subscribe without asking.
export async function moved() {
    if (!("caches" in self) || !await caches.has(movedNote)) {
        return "none";
    }

    return "Notification" in window && Notification.permission === "granted" ? "permission-kept" : "permission-lost";
}

export async function forgetMove() {
    if ("caches" in self) {
        await caches.delete(movedNote);
    }
}

export async function unsubscribe() {
    const registration = await navigator.serviceWorker.ready;
    const subscription = await registration.pushManager.getSubscription();

    if (subscription === null) {
        return null;
    }

    await subscription.unsubscribe();
    return subscription.endpoint;
}

function isAppleTouchDevice() {
    return /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}

function isInstalled() {
    return window.matchMedia("(display-mode: standalone)").matches || navigator.standalone === true;
}

function fromBase64Url(text) {
    const base64 = text.replace(/-/g, "+").replace(/_/g, "/");
    return Uint8Array.from(atob(base64 + "=".repeat((4 - base64.length % 4) % 4)), c => c.charCodeAt(0));
}

function sameKey(current, wanted) {
    if (current === null) {
        return false;
    }

    const bytes = new Uint8Array(current);
    return bytes.length === wanted.length && bytes.every((value, index) => value === wanted[index]);
}
