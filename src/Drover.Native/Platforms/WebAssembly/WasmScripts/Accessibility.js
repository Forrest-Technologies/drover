// Uno 7.0.65 can activate accessibility before the app's visual tree is attached, leaving an empty semantic root.
// Its managed exports are the same entry points the platform's own accessibility button uses. Rebuild once when
// the shell is loaded. The published-browser check requires real headings and invokes the export through its peer.
(() => {
    // Uno 7.0.65 mirrors its links as anchors without href. Give those anchors an explicit role while keeping
    // Uno's existing activation handlers and leaving ordinary HTML links alone. The runtime makes every semantic
    // element pointer-transparent so the canvas receives the input, which leaves only the anchor's keydown handler
    // able to invoke a link: a pointer click can never reach it. Give the anchors pointer input back, so a pointer
    // click and a keypress take the same invoke path, and the app still decides which addresses may be opened.
    const link = 'a:not([href]):not([role])';
    const semanticLink = '#uno-semantics-root a[role="link"]:not([href])';
    // The text-selection checks load this script with a document that has no createElement. The style is only a
    // browser concern, so a stub without it simply runs the rest of the adapter as before.
    if (typeof document.createElement === 'function') {
        const styles = document.createElement('style');
        styles.textContent = `${semanticLink} { pointer-events: auto !important; }`;
        document.head.appendChild(styles);
    }

    // Uno also sends pointer presses caught at the body to the canvas. Route primary presses on a semantic
    // link through its DOM click handler so the canvas cannot activate the same link a second time.
    const linkPointers = new Set();
    document.addEventListener('pointerdown', event => {
        if (event.button === 0 && event.target instanceof Element && event.target.closest(semanticLink)) {
            linkPointers.add(event.pointerId);
            event.stopPropagation();
        }
    }, true);
    document.addEventListener('pointerup', event => {
        if (linkPointers.delete(event.pointerId)) {
            event.stopPropagation();
        }
    }, true);
    document.addEventListener('pointercancel', event => linkPointers.delete(event.pointerId), true);

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

    // Uno 7.0.65 keeps one pending selection restore for its shared hidden input. Its document handler also sees
    // semantic inputs, so a restore left by one field can move the caret in the next field. Keep that shared restore
    // out of semantic fields, then forward only a selection event raised by the currently focused semantic field.
    const managedSelections = new WeakMap();
    let semanticUpdatesWrapped = false;
    let selectionWrapped = false;
    function isSemanticTextInput(input) {
        const semantic = input instanceof HTMLInputElement || input instanceof HTMLTextAreaElement;
        return semantic
            && /^uno-semantics-\d+$/.test(input.id)
            && Number.isInteger(input.selectionStart)
            && Number.isInteger(input.selectionEnd);
    }

    function readSelection(input) {
        return {
            start: input.selectionStart,
            end: input.selectionEnd,
            direction: input.selectionDirection,
        };
    }

    function selectionsMatch(left, right) {
        return left.start === right.start
            && left.end === right.end
            && left.direction === right.direction;
    }

    function wrapSemanticUpdates() {
        if (semanticUpdatesWrapped) {
            return;
        }

        const semanticElements = globalThis.Uno?.UI?.Runtime?.SemanticElements;
        const original = semanticElements?.updateTextBoxValue;
        if (typeof original !== 'function') {
            return;
        }

        semanticElements.updateTextBoxValue = function (handle, value, start, end, backward) {
            const result = original.call(this, handle, value, start, end, backward);
            const input = document.getElementById(`uno-semantics-${handle}`);
            if (start < 0 || end < 0) {
                if (input) {
                    managedSelections.delete(input);
                }
            } else if (isSemanticTextInput(input)) {
                managedSelections.set(input, readSelection(input));
            }
            return result;
        };
        semanticUpdatesWrapped = true;
    }

    function wrapSemanticSelection() {
        if (selectionWrapped) {
            return;
        }

        const bridge = globalThis.Uno?.UI?.Runtime?.BrowserInvisibleTextBoxViewExtension;
        const original = document.onselectionchange;
        if (!bridge?._exports || typeof original !== 'function') {
            return;
        }

        document.onselectionchange = function (event) {
            const input = document.activeElement;
            if (!isSemanticTextInput(input)) {
                return original.call(this, event);
            }

            bridge.waitingAsyncOnSelectionChange = false;
        };
        document.addEventListener('select', event => {
            const input = event.target;
            if (input !== document.activeElement || !isSemanticTextInput(input)) {
                return;
            }

            const selection = readSelection(input);
            const managed = managedSelections.get(input);
            if (managed && selectionsMatch(selection, managed)) {
                return;
            }

            bridge.isInSelectionChange = true;
            try {
                if (input.selectionDirection === 'backward') {
                    bridge._exports.OnSelectionChanged(
                        input.selectionEnd,
                        input.selectionStart - input.selectionEnd);
                } else {
                    bridge._exports.OnSelectionChanged(
                        input.selectionStart,
                        input.selectionEnd - input.selectionStart);
                }
            } finally {
                bridge.isInSelectionChange = false;
            }
        }, true);
        selectionWrapped = true;
    }

    function prepareSemanticSelection() {
        wrapSemanticUpdates();
        wrapSemanticSelection();
    }

    wrapSemanticUpdates();
    document.addEventListener('focusin', prepareSemanticSelection);

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
