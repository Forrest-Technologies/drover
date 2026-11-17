// The native modal dialog does the hard parts. showModal() remembers the element that had focus,
// makes the rest of the page inert, and closes on Escape. close() returns focus to the remembered element.
// Both work only while the same dialog element stays in the document, so the component never re-creates it.
//
// One thing it does not do is keep Tab inside. After the last control the browser moves focus out of the
// document (to its own toolbar, or to the page body), and Shift+Tab does the same from the first. A modal
// dialog has to keep Tab and Shift+Tab inside it, so the keys wrap from the last control to the first.

const wrapped = new WeakSet();

const focusable =
  'a[href], button, input, select, textarea, summary, [tabindex]:not([tabindex="-1"])';

function controls(dialog) {
  return [...dialog.querySelectorAll(focusable)].filter(
    (control) => !control.disabled && control.tabIndex >= 0 && control.getClientRects().length > 0,
  );
}

function keepFocusInside(event) {
  if (event.key !== "Tab" || event.defaultPrevented) {
    return;
  }

  const dialog = event.currentTarget;
  const inside = controls(dialog);
  if (inside.length === 0) {
    event.preventDefault();
    return;
  }

  const first = inside[0];
  const last = inside[inside.length - 1];
  const active = document.activeElement;
  if (event.shiftKey && (active === first || active === dialog)) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && active === last) {
    event.preventDefault();
    first.focus();
  }
}

export function open(dialog) {
  if (!dialog) {
    return;
  }

  if (!wrapped.has(dialog)) {
    wrapped.add(dialog);
    dialog.addEventListener("keydown", keepFocusInside);
  }

  if (!dialog.open) {
    dialog.showModal();
  }
}

export function close(dialog) {
  if (dialog?.open) {
    dialog.close();
  }
}
