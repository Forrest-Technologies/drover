// Enter sends and Shift+Enter breaks the line, as on a desktop. On a touch keyboard Enter breaks the line
// and the Send button sends. The field grows with its text where the browser cannot do that itself.
export function attach(field) {
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
