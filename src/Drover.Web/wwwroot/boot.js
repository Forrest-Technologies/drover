// Runs before the app loads. Applies the stored theme so the first paint is already right, and registers
// the service worker that keeps the shell available.
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
        navigator.serviceWorker.register("service-worker.js");
    }
})();
