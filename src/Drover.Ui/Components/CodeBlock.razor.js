// Clipboard access for CodeBlock. Loaded on first copy.
export function copyText(text) {
  return navigator.clipboard.writeText(text);
}
