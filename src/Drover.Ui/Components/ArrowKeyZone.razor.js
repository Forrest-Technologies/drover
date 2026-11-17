// Arrow keys, Home, and End move between a region's own items. Each item handles the move itself; this stops
// the same key press from also scrolling the page behind it. Only a press on an item is touched: a field or a
// scrolling box inside the region keeps its keys.

const navigationKeys = new Set(["ArrowDown", "ArrowUp", "ArrowLeft", "ArrowRight", "Home", "End"]);

export function attach(zone) {
  zone.addEventListener("keydown", (event) => {
    if (navigationKeys.has(event.key) && event.target instanceof Element && event.target.matches("[data-arrow-item]")) {
      event.preventDefault();
    }
  });
}
