// The theme choice: "system", "dark", or "light". Kept on the device. The host page applies the stored
// choice before first paint; this module reads it and changes it.
const key = "drover.theme";

export function read() {
    try {
        return localStorage.getItem(key) ?? "system";
    } catch {
        // Storage can be turned off. The page then follows the system.
        return "system";
    }
}

export function apply(choice) {
    if (choice === "dark" || choice === "light") {
        document.documentElement.dataset.theme = choice;
    } else {
        delete document.documentElement.dataset.theme;
    }

    try {
        localStorage.setItem(key, choice);
    } catch {
        // The choice still holds for this visit.
    }

    return choice;
}
