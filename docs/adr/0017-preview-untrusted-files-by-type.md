# 0017. Preview untrusted files by type

- Status: Accepted
- Date: 2026-11-19
- Source: [issue 298](https://github.com/Forrest-Technologies/drover/issues/298)

## Context

Bots can write files into their sandboxes and share them in conversations. A preview must not let bot-written
content use the app's session, send data to another server, or present its text as instructions. HTML needs a
different containment boundary from formats that the client can draw without running page content. The existing
[file rules](../architecture/02-data-model.md#files),
[untrusted output rules](../architecture/06-mcp.md#tool-results-are-untrusted-input), and
[native client](../architecture/18-native-client.md) remain the foundation.

## Decision

Every bot-written file remains untrusted content. The server selects its preview kind from inspected bytes and
size, never from its name or a client-supplied content type.

- Markdown, images, PDF, and diagrams use native client viewers. Markdown keeps raw HTML disabled. Diagram text is
  parsed into a bounded diagram model before the client draws it. These viewers make no request named by the file.
- A remote image reference in Markdown is an external link with its address visible. The client has no load
  control. Opening the link is a person's explicit navigation outside the preview. This settles
  [issue 44](https://github.com/Forrest-Technologies/drover/issues/44) without allowing output to phone home.
- HTML never renders in the app origin. After the normal file access check, the server issues a short-lived,
  single-use preview grant bound to that file version. A separate configured preview origin on a separate registrable domain serves the HTML. The operator must keep that origin cookieless.
  The preview service rejects requests carrying cookies and sets none. A missing preview origin leaves Download available and explains
  what the operator must configure.
- Before serving HTML, the server removes scripts, event handlers, forms, active embeds, refresh instructions,
  base addresses, and external or relative resource and navigation addresses. Inline CSS cannot import or address
  another resource. This reduction complements the origin boundary and content policy.
- A browser uses a sandboxed iframe without same-origin, forms, popups, top navigation, or downloads. A native app
  uses a WebView with isolated storage and no shared cookie jar, credential store, host bridge, navigation away
  from the granted document, downloads, or new windows.
- The preview response applies a fixed policy. Its default source, scripts, connections, frames, objects, base
  address, and forms are disabled. Inline styles and embedded image or font data may render. Referrers are disabled,
  content types are not sniffed, and the app origin is the only allowed frame ancestor.
- HTML size, native render size, diagram complexity, and render time are bounded. A refusal names the limit and
  leaves Download available.

Preview content never enters model instructions, tool grants, or the app's trusted interface tree. Widening the
HTML policy or enabling network access requires a new decision.

## Alternatives considered

- Render HTML in the app origin after sanitizing it. Rejected because a sanitizer is not an origin boundary and the
  page would sit beside session state.
- Render every format in a WebView. Rejected because native viewers need less authority and give better platform
  accessibility.
- Render HTML to a server-side picture. Rejected because it adds an active renderer to the server and loses
  document navigation and accessibility.
- Load remote images after a click inside the message. Rejected because the address can carry data and the remote
  server learns when and where it was opened.

## Consequences

Operators who want HTML preview need a separate HTTPS site. HTML stays static, offline, and isolated. Native
formats remain searchable and accessible through platform controls. Preview grants and renderer limits need
security tests on every target, and file downloads remain the fallback for unsupported or refused content.

## Reopen when

A supported client cannot enforce the origin, storage, navigation, and network boundaries; or a required document
format cannot be rendered accessibly without a contained web renderer.
