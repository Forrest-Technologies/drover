// Escape closes what is open beside the conversation. A key something inside the rail already took for itself, an
// open dialog, or an open menu is left alone: those close first.
export function attach(rail, page) {
  rail.addEventListener("keydown", (event) => {
    if (event.key !== "Escape" || event.defaultPrevented || event.target.closest?.("dialog[open], :popover-open")) {
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
