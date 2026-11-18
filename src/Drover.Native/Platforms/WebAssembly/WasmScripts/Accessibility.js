// Uno 7.0.65 can activate accessibility before the app's visual tree is attached, leaving an empty semantic root.
// Its managed exports are the same entry points the platform's own accessibility button uses. Rebuild once when
// the shell is loaded. The published-browser check requires real headings and invokes the export through its peer.
(() => {
    // Uno 7.0.65 mirrors its links as anchors without href. Give those anchors an explicit role while keeping
    // Uno's existing activation handlers and leaving ordinary HTML links alone.
    const link = 'a:not([href]):not([role])';
    function exposeLinks(node) {
        if (node.matches(link)) {
            node.setAttribute('role', 'link');
        }
        for (const anchor of node.querySelectorAll(link)) {
            anchor.setAttribute('role', 'link');
        }
    }

    const links = new MutationObserver(changes => {
        for (const change of changes) {
            for (const node of change.addedNodes) {
                if (node instanceof Element) {
                    exposeLinks(node);
                }
            }
        }
    });

    globalThis.droverAccessibility = {
        refresh() {
            const accessibility = globalThis.Uno.UI.Runtime.Accessibility;
            accessibility.managedDisableAccessibility();
            accessibility.managedEnableAccessibility();

            links.disconnect();
            const root = document.getElementById('uno-semantics-root');
            if (root) {
                exposeLinks(root);
                links.observe(root, { childList: true, subtree: true });
            }
        }
    };
})();
