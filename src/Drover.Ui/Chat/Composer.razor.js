// Enter sends and Shift+Enter breaks the line, as on a desktop. On a touch keyboard Enter breaks the line
// and the Send button sends. The field grows with its text where the browser cannot do that itself. Files pasted
// into the field or dropped on the composer go to the hidden file input, as if they had been chosen with Attach.
export function attach(field, form, input) {
    if (input) {
        takeFiles(field, form, input);
    }

    const grows = CSS.supports("field-sizing", "content");
    const touch = matchMedia("(pointer: coarse)");

    field.addEventListener("keydown", event => {
        if (event.key !== "Enter" || event.shiftKey || event.isComposing || touch.matches) {
            return;
        }

        event.preventDefault();
        field.form.requestSubmit();
    });

    if (!grows) {
        const fit = () => {
            field.style.height = "auto";
            field.style.height = `${field.scrollHeight}px`;
        };
        field.addEventListener("input", fit);
        field.form.addEventListener("submit", () => requestAnimationFrame(fit));
    }
}

// Opens the device's file chooser for the hidden input.
export function pick(input) {
    input.click();
}

function takeFiles(field, form, input) {
    const hand = files => {
        const transfer = new DataTransfer();
        for (const file of files) {
            transfer.items.add(file);
        }
        input.files = transfer.files;
        input.dispatchEvent(new Event("change", { bubbles: true }));
    };

    // A copy from an office program carries its text and a picture of it: then the text is what was meant.
    field.addEventListener("paste", event => {
        const files = event.clipboardData?.files;
        const hasText = [...(event.clipboardData?.types ?? [])].includes("text/plain");
        if (files && files.length > 0 && !hasText) {
            event.preventDefault();
            hand(files);
        }
    });

    const carriesFiles = event => event.dataTransfer && [...event.dataTransfer.types].includes("Files");

    form.addEventListener("dragover", event => {
        if (carriesFiles(event)) {
            event.preventDefault();
            form.classList.add("dv-composer-drop");
        }
    });
    form.addEventListener("dragleave", event => {
        if (!form.contains(event.relatedTarget)) {
            form.classList.remove("dv-composer-drop");
        }
    });
    form.addEventListener("drop", event => {
        form.classList.remove("dv-composer-drop");
        if (carriesFiles(event) && event.dataTransfer.files.length > 0) {
            event.preventDefault();
            hand(event.dataTransfer.files);
        }
    });
}

// Moves focus to the first element inside the root that matches.
export function focusFirst(root, selector) {
    root.querySelector(selector)?.focus();
}
