// What markup cannot do for the sandbox screen: tell the page when it is hidden, so the picture stops refreshing
// while nobody looks; read the size the picture is shown at, so a tap maps to the sandbox's own pixels; and keep the
// arrow keys that move the pointer over the picture from scrolling the page.

const arrows = new Set(["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"]);
let report = null;

export function watchVisibility(owner) {
  stopWatching();
  report = () => owner.invokeMethodAsync("VisibilityChanged", document.visibilityState === "visible");
  document.addEventListener("visibilitychange", report);
}

export function stopWatching() {
  if (report) {
    document.removeEventListener("visibilitychange", report);
    report = null;
  }
}

export function size(element) {
  const box = element.getBoundingClientRect();
  return [box.width, box.height];
}

export function keepArrows(element) {
  element.addEventListener("keydown", (event) => {
    if (arrows.has(event.key)) {
      event.preventDefault();
    }
  });
}
