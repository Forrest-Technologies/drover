# 0015. One native client for web, Android, and desktop

- Status: Proposed. It is accepted when the pull request that carries it merges, and from then it supersedes [0008](0008-own-ui-components.md), and [0003](0003-one-data-path-for-all-clients.md) where the two conflict.
- Date: 2026-11-17
- Source: [18-native-client.md](../architecture/18-native-client.md)

## Context

The interface is a Razor component library with two hosts: Blazor WebAssembly in a browser, and a web view inside the Android shell. The owner wants native apps for web, Android, and desktop from one codebase, with the Apple targets behind the existing switch, and no loss of looks, features, or security.

## Decision

The interface is drawn by one Uno Platform app, `Drover.Native`, with the Skia renderer on every target. Only the open source platform is used: no design tools, no paid features, no package that is not under an allowed license.

- Everything about the interface that does not draw lives in `Drover.Presentation`, a plain library both clients reference: the words, drafts, and models that moved there unchanged, the view models, and the router.
- The design tokens stay specified in `tokens.css`. `Tokens.xaml` restates every one, and a test fails when they differ.
- Every component of `Drover.Ui` has a native control of the same name. They are Drover's own, drawn from tokens, as 0008 decided; only what they are built on changes.
- What 0003 decided stands: every screen talks to the server through `Drover.Client`, and run events stream over Server-Sent Events. What changes is the first line: the web interface is no longer Blazor WebAssembly once its screens have moved.
- Transport is the same source as before, compiled into both clients: the same-origin cookie session with the antiforgery header in a browser, the chosen HTTPS origin with the session kept by the platform on a device.
- The content security policy is not widened for the native browser build.
- The web client and the Android shell keep building and shipping until every screen has moved. The native browser build is served beside the web client under `/native/` during that time.

## Alternatives considered

- Keep Blazor on the web and the web view on devices. Rejected by the owner: the apps are not native.
- .NET MAUI with native controls. Rejected: no web target and no Linux desktop, so two interface codebases would remain.
- Blazor Hybrid on every device with the web as it is. Rejected: it is what exists, and it is a web view.
- Avalonia. Not chosen: its browser target is less complete than the platform chosen here, and its control model is further from the XAML the platform shares with Windows.

## Consequences

- One set of controls and one look on every target. The gallery is compared against the web client's, picture by picture.
- In a browser the interface is a canvas. Assistive technology is served by a parallel tree of elements that a person must switch on; password managers, find in page, translation, and the browser's text-size setting do not work as they do on a page of elements. [Section 18](../architecture/18-native-client.md) lists each with what was observed.
- The browser download is far larger than the web client's.
- The platform's runtime evaluates script strings. The ones known are answered without `eval`; an upgrade can add one, and the published build must be run under the policy after each.
- About eighty screens move one at a time. Until the last has moved there are two clients to keep working.
- Five packages the platform brings carry no license in their metadata, so the license check for `Drover.Native.slnx` needs reviewed exceptions before it can pass.

## Reopen when

The accessibility tree cannot be made to serve a screen reader user without an extra step; or password managers cannot be made to work on sign-in; or the platform needs the security policy widened; or the browser download cannot be brought near the web client's.
