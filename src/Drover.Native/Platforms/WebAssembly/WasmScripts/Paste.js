// Ordinary browser APIs, loaded as a script under the same CSP as the app. No eval or inline script.
// A picture or a file copied to the clipboard reaches the page with its paste event; the platform's own clipboard
// read carries text only. When the event carries files and no plain text, the page was meant to take the files.
//
// The app is asked, while the event runs and before anything is read, whether a message field takes the paste; that
// field is then the paste's destination, wherever the cursor goes while the files are read. Only the first files of
// one paste are read (the most the app takes at once), and only while the bytes held for all pastes still being read
// stay within a budget: the rest are counted, not read. The batch is reported as JSON: each file's name, size, whether
// the browser made it for the paste (a picture with no name of its own), and an id to collect its bytes by, or none
// when it is larger than the app reads. Bytes the app does not collect are let go once the batch is reported, and all
// of a paste's bytes are let go when one of its files cannot be read; the app is then told once that the paste failed.
globalThis.droverPaste = (() => {
    const held = new Map();
    let heldBytes = 0;
    let started = false;
    let lastFile = 0;
    let lastPaste = 0;

    // The name a browser gives a picture it made for a paste. A file of that name made at the moment of the paste is
    // taken to be that picture; one copied from storage carries the time it was last changed.
    const madeName = /^image\.(png|jpe?g|gif|bmp|webp|tiff?)$/i;
    const madeWithin = 5000;

    function release(paste) {
        for (const [id, file] of held) {
            if (file.paste === paste) {
                heldBytes -= file.size;
                held.delete(id);
            }
        }
    }

    async function read(paste, files, at, largest, budget) {
        const batch = { files: [], beyond: 0 };
        let full = false;
        for (const file of files) {
            const described = {
                name: file.name,
                size: file.size,
                made: file.name === '' || (madeName.test(file.name) && Math.abs(file.lastModified - at) <= madeWithin),
                id: 0,
            };
            if (full || (file.size <= largest && heldBytes + file.size > budget)) {
                full = true;
                batch.beyond++;
                continue;
            }
            if (file.size <= largest) {
                const id = ++lastFile;
                held.set(id, { paste, size: file.size, bytes: null });
                heldBytes += file.size;
                held.get(id).bytes = new Uint8Array(await file.arrayBuffer());
                described.id = id;
            }
            batch.files.push(described);
        }
        return batch;
    }

    return {
        listen(largest, budget, most, claim, pasted, failed) {
            if (started) {
                return;
            }

            started = true;
            document.addEventListener('paste', event => {
                const data = event.clipboardData;
                if (!data || data.files.length === 0 || data.types.includes('text/plain')) {
                    return;
                }

                const paste = ++lastPaste;
                if (!claim(paste)) {
                    return;
                }

                // The list is only readable while the event runs, so the files are taken out of it first.
                event.preventDefault();
                const at = Date.now();
                const total = data.files.length;
                const files = [...data.files].slice(0, most);
                (async () => {
                    let batch;
                    try {
                        batch = await read(paste, files, at, largest, budget);
                    } catch {
                        release(paste);
                        failed(paste);
                        return;
                    }

                    batch.beyond += total - files.length;
                    try {
                        pasted(paste, JSON.stringify(batch));
                    } finally {
                        release(paste);
                    }
                })();
            }, true);
        },

        // The bytes of a file the app was told of, handed over once.
        collect(id) {
            const file = held.get(id);
            if (!file) {
                return new Uint8Array(0);
            }

            held.delete(id);
            heldBytes -= file.size;
            return file.bytes;
        },

        // How many bytes are held now for pastes being read or not yet collected. The browser check reads it.
        held() {
            return heldBytes;
        },
    };
})();
