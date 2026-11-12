// The native modal dialog does the hard parts. showModal() remembers the element that had focus,
// traps focus, and closes on Escape. close() returns focus to the remembered element. Both work only
// while the same dialog element stays in the document, so the component never re-creates it.

export function open(dialog) {
  if (dialog && !dialog.open) {
    dialog.showModal();
  }
}

export function close(dialog) {
  if (dialog?.open) {
    dialog.close();
  }
}
