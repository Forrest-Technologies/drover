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

    // The choice is kept as JSON with a theme and a "mode", and before that as the mode alone, one word. A value
    // that is neither, no value, or storage that is turned off is a device that has not chosen: dark. The app reads
    // the same value the same way (ThemeChoice.Parse), so the first paint is the mode the app then draws.
    function storedMode() {
        try {
            const stored = (localStorage.getItem("drover.theme") ?? "").trim();
            const mode = stored.startsWith("{") ? modeOf(JSON.parse(stored)) : stored;
            const word = typeof mode === "string" ? mode.toLowerCase() : "";
            return ["light", "dark", "black", "system"].includes(word) ? word : "dark";
        } catch {
            return "dark";
        }
    }

    // An object names a mode only when it names a theme, and its accent, when it has one, is a word.
    function modeOf(choice) {
        const named = typeof choice.themeId === "string" && choice.themeId !== "";
        const accent = choice.accent === undefined || choice.accent === null || typeof choice.accent === "string";
        return named && accent ? choice.mode : "";
    }

    function isWriting() {
        const field = document.activeElement;
        return field !== null
            && (field.tagName === "TEXTAREA" || field.tagName === "INPUT")
            && field.value !== "";
    }
})();
