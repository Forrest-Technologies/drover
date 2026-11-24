// Runs before the app loads. Applies the stored theme so the first paint is already right, and registers
// the service worker that keeps the shell available and moves the page to a new build when one arrives.
(() => {
    // "system" sets nothing, and boot.css follows the device. Anything else is said outright.
    const mode = storedMode();
    if (mode !== "system") {
        document.documentElement.dataset.theme = mode;
    }

    if ("serviceWorker" in navigator) {
        keepCurrent();
    }

    // A new build's worker takes over on its own. This page was started by the old build, so it loads again to
    // run the new one: at once, unless the person is in the middle of writing, and then when they next look away.
    function keepCurrent() {
        const workers = navigator.serviceWorker;
        const startedByAWorker = workers.controller !== null;

        workers.addEventListener("controllerchange", () => {
            // The first worker a browser ever gets replaces nothing, so there is nothing old to leave.
            if (!startedByAWorker) {
                return;
            }

            if (!isWriting() || document.visibilityState === "hidden") {
                location.reload();
                return;
            }

            document.addEventListener("visibilitychange", () => {
                if (document.visibilityState === "hidden") {
                    location.reload();
                }
            });
        });

        // The worker's list of files is asked of the server each time too, never taken from the browser's cache.
        workers.register("service-worker.js", { updateViaCache: "none" }).then(registration => {
            // A tab left open is asked again each time the person comes back to it.
            document.addEventListener("visibilitychange", () => {
                if (document.visibilityState === "visible") {
                    registration.update().catch(() => { });
                }
            });
        }).catch(() => { });
    }

    // The choice is kept as JSON with a "mode", and before that as the mode alone, one word. A value that is
    // neither, no value, or storage that is turned off is a device that has not chosen: dark.
    function storedMode() {
        try {
            const stored = (localStorage.getItem("drover.theme") ?? "").trim();
            const mode = stored.startsWith("{") ? JSON.parse(stored).mode : stored;
            const word = typeof mode === "string" ? mode.toLowerCase() : "";
            return ["light", "dark", "black", "system"].includes(word) ? word : "dark";
        } catch {
            return "dark";
        }
    }

    function isWriting() {
        const field = document.activeElement;
        return field !== null
            && (field.tagName === "TEXTAREA" || field.tagName === "INPUT")
            && field.value !== "";
    }
})();
