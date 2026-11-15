// Arrow keys, Home, and End move between a region's own items. The component handles the move; this stops
// the same key press from also scrolling the page behind it.

const navigationKeys = new Set(["ArrowDown", "ArrowUp", "ArrowLeft", "ArrowRight", "Home", "End"]);

export function attach(zone) {
  zone.addEventListener("keydown", (event) => {
    if (navigationKeys.has(event.key)) {
      event.preventDefault();
    }
  });
}
