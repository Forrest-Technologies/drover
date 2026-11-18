# Native migration verification

Captured on 2026-11-18 in Chromium. The root images are the trimmed Release Uno browser publish under the
server's exact content security policy. `web/` contains the existing Blazor client with the same API fixtures.
Every fixture is explicitly marked Sample. Each scene is captured in dark and light at 1280 and 360 px, at
100 percent zoom and reduced motion.

The capture and smoke script is `scripts/check-native-browser.mjs`. It requires the existing Playwright runtime
from a Release build of `tests/Drover.Ui.Tests`. Its default target is the native publish; set
`DROVER_REFERENCE_ORIGIN` to the running existing client's origin to capture the reference. It checks actual
accessible headings, expected extension failures, unexpected console errors, and the bytes of the extension
download invoked through its accessible control. Reference exports use the existing download link.

These captures establish that the sampled states render. They do not establish pixel parity or full state,
keyboard, zoom, screen-reader, provider, or device coverage. In particular the desktop usage table has not yet
been translated, spacing differences remain, and the four placeholder screens are excluded. Current gaps and
upstream constraints are recorded in `docs/architecture/18-native-client.md`.
