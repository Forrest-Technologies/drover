// The theme choice: "dark", "light", or "system". Dark until a person chooses otherwise. Kept on the device.
// The host page applies the stored choice before first paint; this module reads it and changes it.
const key = "drover.theme";

export function read() {
    try {
        return localStorage.getItem(key) ?? "dark";
    } catch {
        // Storage can be turned off. The page is then dark.
        return "dark";
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
