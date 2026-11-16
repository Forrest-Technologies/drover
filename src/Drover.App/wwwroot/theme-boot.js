try {
    const theme = localStorage.getItem("drover.theme");
    if (theme === "system") {
        delete document.documentElement.dataset.theme;
    } else if (theme === "light") {
        document.documentElement.dataset.theme = "light";
    }
} catch {
    // The default theme is already set on the document.
}
