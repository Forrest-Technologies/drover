// Development: no caching, so a rebuild always shows. The published worker is service-worker.published.js.
// Notifications work the same in both.
self.importScripts("./push-worker.js");
self.addEventListener("fetch", () => { });
