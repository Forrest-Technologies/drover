// Ordinary browser APIs, loaded as a script under the same CSP as the app. No eval or inline script.
// A picture or a file copied to the clipboard reaches the page with its paste event; the platform's own clipboard
// read carries text only. When the event carries files and no plain text, the page was meant to take the files. All
// the files of one paste are read, up to the most the app takes plus one (so the app can say it took only the first
// of them), and reported together as one batch: a JSON list of each file's name and size, with an id to collect its
// bytes by, or none when it is larger than the app reads. The bytes are kept until the app collects them.
globalThis.droverPaste = (() => {
    const read = new Map();
    let started = false;
    let last = 0;

    return {
        listen(largest, most, pasted) {
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
                const files = [...data.files].slice(0, most);
                event.preventDefault();
                (async () => {
                    const batch = [];
                    for (const file of files) {
                        if (file.size > largest) {
                            batch.push({ name: file.name, size: file.size, id: 0 });
                        } else {
                            const id = ++last;
                            read.set(id, new Uint8Array(await file.arrayBuffer()));
                            batch.push({ name: file.name, size: file.size, id });
                        }
                    }
                    pasted(JSON.stringify(batch));
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
