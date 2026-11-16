// The list is laid out bottom-up, so its newest message is at scroll position zero.
export function showNewest(list) {
    list.scrollTop = 0;
}

// Brings a message linked to into the middle of the list and gives it the focus, without scrolling the page twice.
export function reveal(list, id) {
    const message = list.querySelector(`#${CSS.escape(id)}`);
    message?.scrollIntoView({ block: "center", behavior: "instant" });
    message?.focus({ preventScroll: true });
}
