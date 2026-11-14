// Runs before the app loads. Applies the stored theme so the first paint is already right, and registers
// the service worker that keeps the shell available and moves the page to a new build when one arrives.
(() => {
    try {
        const theme = localStorage.getItem("drover.theme");
        if (theme !== "system") {
            // Dark unless a person chose light or asked to follow the device.
            document.documentElement.dataset.theme = theme === "light" ? "light" : "dark";
        }
    } catch {
        // Storage can be turned off. The page is then dark.
        document.documentElement.dataset.theme = "dark";
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

        workers.register("service-worker.js").then(registration => {
            // A tab left open is asked again each time the person comes back to it.
            document.addEventListener("visibilitychange", () => {
                if (document.visibilityState === "visible") {
                    registration.update().catch(() => { });
                }
            });
        }).catch(() => { });
    }

    function isWriting() {
        const field = document.activeElement;
        return field !== null
            && (field.tagName === "TEXTAREA" || field.tagName === "INPUT")
            && field.value !== "";
    }
})();
