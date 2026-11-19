// Ordinary browser APIs, loaded as a script under the same CSP as the app. No eval or inline script.
globalThis.droverFiles = {
    async download(name, text, type) {
        const blob = new Blob([text], { type: `${type};charset=utf-8` });
        const address = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = address;
        link.download = name;
        link.hidden = true;
        document.body.appendChild(link);
        try {
            link.click();
        } finally {
            link.remove();
            // Keep the URL alive until the browser has consumed the click.
            setTimeout(() => URL.revokeObjectURL(address), 60000);
        }
    }
};
