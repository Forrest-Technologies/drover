// The popover attribute opens, closes, light-dismisses, and handles Escape natively. This module adds
// the two things markup cannot: the keys that move between items must not scroll the page behind the
// menu, and a menu that focus has left by Tab should not stay open.

const navigationKeys = new Set(["ArrowDown", "ArrowUp", "Home", "End"]);

export function attach(list, trigger) {
  list.addEventListener("keydown", (event) => {
    if (navigationKeys.has(event.key)) {
      event.preventDefault();
    }
  });

  list.addEventListener("focusout", (event) => {
    const next = event.relatedTarget;
    if (next && next !== trigger && !list.contains(next)) {
      list.hidePopover();
    }
  });
}
