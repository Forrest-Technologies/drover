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
- A browser uses a sandboxed iframe (`sandbox=""`, `credentialless`, `allow=""`, `referrerpolicy="no-referrer"`)
  without same-origin, forms, popups, top navigation, or downloads. Native desktop and Android targets
  (`net11.0-desktop`, `net11.0-android`) currently refuse in-app HTML preview before requesting a server grant and
  fall back to Download or Save, because the shared Uno host does not yet wire a non-persistent WebView controller
  with guaranteed isolated cookie, cache, and credential storage across Linux, macOS, Windows, and Android. Lifting
  that refusal on a native target requires a platform-specific non-persistent WebView controller that enforces
  isolated storage, no shared cookie jar or credential store, no host bridge, no navigation away from the granted
  document, no downloads, and no new windows, verified by a platform security test.
- The preview response applies a fixed policy. Its default source, scripts, connections, frames, objects, base
  address, and forms are disabled. Inline styles and embedded image or font data may render. Referrers are disabled,
  content types are not sniffed, and the app origin is the only allowed frame ancestor.
- HTML size, native render size, diagram complexity, and render time are bounded. A refusal names the limit and
  leaves Download available.
- PDF preview caps Flate output inside the parser at 1 MiB per stream. Flate output and unfiltered page
  content share a 2 MiB document budget. The cap covers the row predictor too: PNG and TIFF predictors are
  undone in place, so their output is never larger than the bounded input, and a predictor that cannot be
  decoded within that bound is refused. This keeps ordinary cross reference streams readable. The provider
  refuses filter chains and filters other than Flate and DCT, with Download available for those files. The
  request has a deadline and checks cancellation during screening and decoding and between pages.
- The parser cannot be stopped from outside, so a page that never reaches a cancellation check keeps running
  after the deadline. The server slot is given back when the deadline passes and the parse is counted as
  abandoned until its thread ends; at two abandoned parses no new parse starts. The user's own slot stays held
  until the thread ends, so one person cannot leave more than one parse running. A hard stop needs the parse in
  a child process with a time and memory limit.
- Reading a PDF's text for search and attachments (`DocumentReader.Read`) uses the same provider with wider
  bounds (64 MiB per stream, 128 MiB per document), so no PDF read path decodes without a cap. Chains of
  ASCII85, ASCIIHex, RunLength, CCITT, JBIG2 and JPX filters stay readable there. LZW is refused because its
  output cannot be bounded.

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
