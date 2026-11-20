// Escape closes what is open beside the conversation. An IME composition, a key something inside the rail already
// took for itself, an open dialog or menu, or a field with a draft is left alone: those stay or close first.
const hasPopoverSelector = (() => {
  try {
    return typeof CSS !== "undefined" && CSS.supports?.("selector(:popover-open)") === true;
  } catch {
    return false;
  }
})();

function isOpenOverlay(target) {
  if (!target || typeof target.closest !== "function") {
    return false;
  }

  const selector = hasPopoverSelector ? "dialog[open], :popover-open" : "dialog[open]";
  try {
    return Boolean(target.closest(selector));
  } catch {
    return Boolean(target.closest("dialog[open]"));
  }
}

function hasFieldDraft(target) {
  if (!target || typeof target.matches !== "function") {
    return false;
  }

  if (target.matches("textarea")) {
    return (target.value ?? "").length > 0;
  }

  if (target.matches("input")) {
    const type = (target.type || "text").toLowerCase();
    return !["button", "checkbox", "radio", "submit", "reset", "file", "image", "hidden"].includes(type)
      && (target.value ?? "").length > 0;
  }

  return target.isContentEditable === true && (target.textContent ?? "").length > 0;
}

export function attach(rail, page) {
  rail.addEventListener("keydown", (event) => {
    if (event.key !== "Escape"
      || event.defaultPrevented
      || event.isComposing
      || isOpenOverlay(event.target)
      || hasFieldDraft(event.target)
      || rail.querySelector(".dv-attached-file")) {
      return;
    }

    event.preventDefault();
    page.invokeMethodAsync("CloseRailFromKeyboard");
  });
}

// The focus goes back to the way that opened the rail: its link in the conversation's header where the header shows
// the links, and otherwise the conversation's menu, which holds the same ways on a phone.
export function focusOpener(header, address) {
  const shown = (element) => element && element.getClientRects().length > 0;
  const link = address ? [...header.querySelectorAll("a.dv-shell-side")].find((way) => way.getAttribute("href") === address) : null;
  const opener = shown(link) ? link : header.querySelector(".dv-shell-more .dv-menu-trigger");
  opener?.focus();
}
