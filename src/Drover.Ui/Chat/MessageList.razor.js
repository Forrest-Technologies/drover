// The list is laid out bottom-up, so its newest message is at scroll position zero.
export function showNewest(list) {
    list.scrollTop = 0;
}

// Brings the message a link named into the middle of the view and puts focus on it, so a keyboard or screen reader
// lands there too.
export function showMoment(id) {
    const message = document.getElementById(id);

    if (message) {
        message.scrollIntoView({ block: "center" });
        message.focus({ preventScroll: true });
    }
}
