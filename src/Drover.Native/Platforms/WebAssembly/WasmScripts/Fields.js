// Ordinary browser APIs, loaded as a script under the same CSP as the app. No eval or inline script.
// The app draws its fields. A password manager finds them as the inputs the platform keeps in the accessibility
// tree, one per field, named by the field's label. The platform makes and replaces those inputs as screens change,
// so the names are put back whenever an input appears, and removed only when the last registration for a label unloads.
globalThis.droverFields = (() => {
    const named = new Map();

    const inputsLabelled = label =>
        [...document.querySelectorAll("input[aria-label]")].filter(input => input.getAttribute("aria-label") === label);

    const nameFieldsOnScreen = () => {
        for (const [label, entry] of named) {
            for (const input of inputsLabelled(label)) {
                if (input.getAttribute("autocomplete") !== entry.autofill) {
                    input.setAttribute("autocomplete", entry.autofill);
                }
            }
        }
    };

    new MutationObserver(nameFieldsOnScreen).observe(document.documentElement, {
        subtree: true,
        childList: true,
        attributeFilter: ["aria-label"],
    });

    return {
        name(label, autofill) {
            if (autofill) {
                const current = named.get(label);
                named.set(label, {
                    autofill,
                    count: current && current.autofill === autofill ? current.count + 1 : 1,
                });
                nameFieldsOnScreen();
            } else {
                const current = named.get(label);
                if (!current) {
                    return;
                }

                if (current.count > 1) {
                    named.set(label, { autofill: current.autofill, count: current.count - 1 });
                    return;
                }

                named.delete(label);
                for (const input of inputsLabelled(label)) {
                    input.removeAttribute("autocomplete");
                }
            }
        }
    };
})();
