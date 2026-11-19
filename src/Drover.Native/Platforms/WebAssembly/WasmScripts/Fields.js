// Ordinary browser APIs, loaded as a script under the same CSP as the app. No eval or inline script.
// The app draws its fields. A password manager finds them as the inputs the platform keeps in the accessibility
// tree, one per field, named by the field's label. The platform makes and replaces those inputs as screens change,
// so the names are put back whenever an input appears.
globalThis.droverFields = (() => {
    const named = new Map();

    const inputsLabelled = label =>
        [...document.querySelectorAll("input[aria-label]")].filter(input => input.getAttribute("aria-label") === label);

    const nameFieldsOnScreen = () => {
        for (const [label, autofill] of named) {
            for (const input of inputsLabelled(label)) {
                if (input.getAttribute("autocomplete") !== autofill) {
                    input.setAttribute("autocomplete", autofill);
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
                named.set(label, autofill);
                nameFieldsOnScreen();
            } else if (named.delete(label)) {
                for (const input of inputsLabelled(label)) {
                    input.removeAttribute("autocomplete");
                }
            }
        }
    };
})();
