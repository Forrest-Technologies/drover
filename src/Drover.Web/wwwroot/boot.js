// Runs before the app loads. Applies the stored theme so the first paint is already right, and registers
// the service worker that keeps the shell available.
(() => {
    try {
        const theme = localStorage.getItem("drover.theme");
        if (theme === "dark" || theme === "light") {
            document.documentElement.dataset.theme = theme;
        }
    } catch {
        // Storage can be turned off. The page then follows the system.
    }

    if ("serviceWorker" in navigator) {
        navigator.serviceWorker.register("service-worker.js");
    }
})();
