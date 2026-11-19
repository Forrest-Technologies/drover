// Ordinary browser APIs, loaded as a script under the same CSP as the app. No eval or inline script.
// The app draws its fields; typing goes through the one input the platform keeps for the focused field
// (id "uno-input"). The platform makes a new input when a field's kind changes and reuses it otherwise, so what
// the field holds is set again whenever that input takes the focus, and whenever the app says it changed.
globalThis.droverFields = (() => {
    let wanted = null;

    const apply = () => {
        const input = document.getElementById("uno-input");
        if (input === null) {
            return;
        }

        if (wanted === null) {
            input.removeAttribute("autocomplete");
        } else {
            input.setAttribute("autocomplete", wanted);
        }
    };

    document.addEventListener("focusin", apply, true);

    return {
        describe(autofill) {
            wanted = autofill || null;
            apply();
        }
    };
})();
