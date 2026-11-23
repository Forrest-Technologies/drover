// Ordinary browser APIs, loaded as a script under the same CSP as the app. No eval or inline script.
// A picture or a file copied to the clipboard reaches the page with its paste event; the platform's own clipboard
// read carries text only. When the event carries files and no plain text, the page was meant to take the files: each
// is read and kept until the app collects its bytes, or only its name and size is reported when it is larger than the
// app reads.
globalThis.droverPaste = (() => {
    const read = new Map();
    let started = false;
    let last = 0;

    return {
        listen(largest, taken, tooLarge) {
            if (started) {
                return;
            }

            started = true;
            document.addEventListener("paste", event => {
                const data = event.clipboardData;
                if (!data || data.files.length === 0 || data.types.includes("text/plain")) {
                    return;
                }

                // The list is only readable while the event runs, so the files are taken out of it first.
                const files = [...data.files];
                event.preventDefault();
                (async () => {
                    for (const file of files) {
                        if (file.size > largest) {
                            tooLarge(file.name, file.size);
                        } else {
                            const id = ++last;
                            read.set(id, new Uint8Array(await file.arrayBuffer()));
                            taken(file.name, id);
                        }
                    }
                })();
            }, true);
        },

        // The bytes of a file the app was told of, handed over once.
        collect(id) {
            const bytes = read.get(id) ?? new Uint8Array(0);
            read.delete(id);
            return bytes;
        }
    };
})();
