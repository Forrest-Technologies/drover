// What markup cannot do for the sandbox screen:
// - tell the page when it is hidden, so the picture stops refreshing while nobody looks;
// - pinch to enlarge the picture and drag to move it, drawn at once, then told to the page, which owns the view and
//   maps taps (ScreenZoom and ScreenPoint, in C#); a drag or a pinch never clicks in the sandbox;
// - read the size the picture frame had when it was tapped, so a tap maps to the sandbox's own pixels;
// - keep the arrow keys over the picture from scrolling the page;
// - while the text field has the focus, hold the picture at the top of what is visible, sized so the field fits
//   right under it above the on-screen keyboard. The visual viewport says how much is visible; the layout viewport
//   does not shrink for the keyboard on Android. Without it, the window's inner height is used.

const arrows = new Set(["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"]);
const tapSlop = 10;
const maxScale = 4;

let owner = null;
let gesture = null;
let swallowClick = false;
let typing = null;
let leaving = 0;
let pointersDown = 0;
const listeners = [];

export function start(page) {
  stop();
  owner = page;
  listen(document, "visibilitychange", () => owner?.invokeMethodAsync("VisibilityChanged", document.visibilityState === "visible"));
  listen(document, "pointerdown", pointerDown);
  listen(document, "pointermove", pointerMove);
  listen(document, "pointerup", pointerEnd);
  listen(document, "pointercancel", pointerEnd);
  listen(window, "click", click, true);
  listen(document, "keydown", keepArrows);
  listen(document, "mousedown", holdFocus);
  listen(document, "focusin", focusIn);
  listen(document, "focusout", focusOut);
  const viewport = window.visualViewport;
  listen(viewport ?? window, "resize", () => typing && requestAnimationFrame(() => fit(true)));
}

export function stop() {
  for (const [target, name, handler, capture] of listeners.splice(0)) {
    target.removeEventListener(name, handler, capture);
  }
  clearTimeout(leaving);
  owner = null;
  gesture = null;
  typing = null;
  leaving = 0;
  pointersDown = 0;
}

export function size(element) {
  const frame = element.closest(".dv-sandbox-frame");
  const tapped = frame?.dataset.tappedSize?.split(" ").map(Number);
  return tapped ?? [element.clientWidth, element.clientHeight];
}

export function keepTyping(form) {
  const field = form?.querySelector("input");
  if (field && document.activeElement !== field) {
    field.focus({ preventScroll: true });
  }
}

function listen(target, name, handler, capture = false) {
  target.addEventListener(name, handler, capture);
  listeners.push([target, name, handler, capture]);
}

// Pinch and drag

function pointerDown(event) {
  pointersDown++;
  const frame = event.target.closest?.(".dv-sandbox-frame");
  if (!frame || (event.pointerType === "mouse" && event.button !== 0)) {
    return;
  }

  if (!gesture || gesture.frame !== frame) {
    const layer = frame.querySelector(".dv-sandbox-layer");
    const box = frame.querySelector(".dv-sandbox-picture");
    gesture = { frame, layer, box, pointers: new Map(), moved: false, view: viewOf(layer) };
  }

  gesture.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
  rebase();
  swallowClick = false;
}

function pointerMove(event) {
  const point = gesture?.pointers.get(event.pointerId);
  if (!point) {
    return;
  }

  point.x = event.clientX;
  point.y = event.clientY;
  const now = centre();
  const start = gesture.start;
  const travel = Math.hypot(now.x - start.x, now.y - start.y);
  const pinching = gesture.pointers.size > 1;

  if (!gesture.moved && !pinching && travel < tapSlop) {
    return;
  }

  gesture.moved = true;
  if (!pinching && gesture.base.scale <= 1) {
    return;
  }

  event.preventDefault();
  const { width, height, left, top } = gesture.box.getBoundingClientRect();
  const scale = clamp(gesture.base.scale * (pinching ? now.spread / start.spread : 1), 1, maxScale);
  const fromX = (start.x - left) / width;
  const fromY = (start.y - top) / height;
  const toX = (now.x - left) / width;
  const toY = (now.y - top) / height;
  show({
    scale,
    x: clamp(toX - ((fromX - gesture.base.x) / gesture.base.scale) * scale, 1 - scale, 0),
    y: clamp(toY - ((fromY - gesture.base.y) / gesture.base.scale) * scale, 1 - scale, 0),
  });
}

function pointerEnd(event) {
  pointersDown = Math.max(0, pointersDown - 1);
  if (pointersDown === 0 && leaving) {
    leaveSoon();
  }

  if (!gesture?.pointers.delete(event.pointerId)) {
    return;
  }

  if (gesture.moved) {
    swallowClick = true;
  }

  if (gesture.pointers.size > 0) {
    rebase();
    return;
  }

  if (gesture.moved) {
    owner?.invokeMethodAsync("ViewChanged", gesture.view.scale, gesture.view.x, gesture.view.y);
  }

  gesture = null;
}

function rebase() {
  gesture.base = gesture.view;
  gesture.start = centre();
}

function centre() {
  const points = [...gesture.pointers.values()];
  const x = points.reduce((sum, point) => sum + point.x, 0) / points.length;
  const y = points.reduce((sum, point) => sum + point.y, 0) / points.length;
  const spread = points.length > 1 ? Math.hypot(points[0].x - points[1].x, points[0].y - points[1].y) || 1 : 1;
  return { x, y, spread };
}

function show(view) {
  gesture.view = view;
  const layer = gesture.layer;
  layer.style.transition = "none";
  layer.style.transform = `translate(${view.x * 100}%, ${view.y * 100}%) scale(${view.scale})`;
  layer.style.setProperty("--dv-sandbox-zoom", view.scale);
}

function viewOf(layer) {
  const data = layer?.dataset ?? {};
  return { scale: Number(data.scale) || 1, x: Number(data.x) || 0, y: Number(data.y) || 0 };
}

function clamp(value, low, high) {
  return Math.min(high, Math.max(low, value));
}

// A click after a drag or a pinch is not a tap. A real tap keeps the frame's size at that moment.
function click(event) {
  const frame = event.target.closest?.(".dv-sandbox-frame");
  if (!frame) {
    return;
  }

  if (swallowClick) {
    swallowClick = false;
    event.preventDefault();
    event.stopImmediatePropagation();
    return;
  }

  const box = frame.querySelector(".dv-sandbox-picture");
  frame.dataset.tappedSize = `${box.clientWidth} ${box.clientHeight}`;
}

function keepArrows(event) {
  if (arrows.has(event.key) && event.target.closest?.(".dv-sandbox-frame")) {
    event.preventDefault();
  }
}

// Typing with the on-screen keyboard up

function focusIn(event) {
  const screen = event.target.closest?.(".dv-sandbox-type")?.closest(".dv-sandbox-screen");
  if (!screen || event.target.tagName !== "INPUT") {
    return;
  }

  clearTimeout(leaving);
  leaving = 0;
  typing = screen;
  screen.classList.add("dv-sandbox-typing");
  requestAnimationFrame(() => fit(true));
}

// Send does not take the focus from the field, so the on-screen keyboard stays up.
function holdFocus(event) {
  if (typing && event.target.closest?.(".dv-sandbox-type button")) {
    event.preventDefault();
  }
}

// Focus leaves at the start of a tap. The picture keeps its size until the tap is over, so the tap lands where the
// finger was.
function focusOut(event) {
  if (!typing || event.relatedTarget?.closest?.(".dv-sandbox-type")) {
    return;
  }

  clearTimeout(leaving);
  leaving = pointersDown > 0 ? -1 : setTimeout(leave, 0);
}

function leaveSoon() {
  clearTimeout(leaving);
  leaving = setTimeout(leave, 400);
}

function leave() {
  leaving = 0;
  typing?.classList.remove("dv-sandbox-typing");
  typing?.style.removeProperty("--dv-sandbox-room");
  typing = null;
}

function fit(scroll) {
  const screen = typing;
  const frame = screen?.querySelector(".dv-sandbox-frame");
  const form = screen?.querySelector(".dv-sandbox-type");
  if (!frame || !form || !screen.isConnected) {
    return;
  }

  const visible = window.visualViewport?.height ?? window.innerHeight;
  const margin = parseFloat(getComputedStyle(frame).scrollMarginTop) || 0;
  const below = form.getBoundingClientRect().bottom - frame.getBoundingClientRect().bottom;
  screen.style.setProperty("--dv-sandbox-room", `${Math.max(visible - below - 2 * margin, 0)}px`);

  if (scroll) {
    makeRoomBelow(screen, frame, margin);
    frame.scrollIntoView({ block: "start", behavior: "instant" });
  }
}

// The page must reach far enough down for the picture to sit at the top of the whole window, or the browser moves it
// back down when the keyboard closes. The room only grows while the page is open, so nothing moves when it is done.
function makeRoomBelow(screen, frame, margin) {
  const page = document.documentElement;
  const top = frame.getBoundingClientRect().top + window.scrollY - margin;
  const missing = top + Math.max(window.innerHeight, page.clientHeight) - page.scrollHeight;
  if (missing > 0) {
    screen.style.paddingBottom = `${(parseFloat(screen.style.paddingBottom) || 0) + Math.ceil(missing)}px`;
  }
}
