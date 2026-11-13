// The list is laid out bottom-up, so its newest message is at scroll position zero.
export function showNewest(list) {
    list.scrollTop = 0;
}
