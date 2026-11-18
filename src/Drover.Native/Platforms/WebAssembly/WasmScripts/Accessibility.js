// Uno 7.0.65 can activate accessibility before the app's visual tree is attached, leaving an empty semantic root.
// Its managed exports are the same entry points the platform's own accessibility button uses. Rebuild once when
// the shell is loaded. The published-browser check requires real headings and invokes the export through its peer.
globalThis.droverAccessibility = {
    refresh() {
        const accessibility = globalThis.Uno.UI.Runtime.Accessibility;
        accessibility.managedDisableAccessibility();
        accessibility.managedEnableAccessibility();
    }
};
