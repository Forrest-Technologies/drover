# Native integration pass

Captured on 2026-11-18 in Chromium (Playwright 1.63), reduced motion, against one running Drover server: the
Release server publish (Production environment) on a throwaway PostgreSQL, the existing client at `/`, and the
Release native browser publish served under `/native/` (`NativeClient:Path`), with the server's own security
headers. Runs streamed for real from a stand-in OpenAI-compatible model connected as a "compatible endpoint"
model connection. Every name, message, note, and file in the pictures is marked Sample.

`web/<scene>-<width>-<theme>.png` is the existing client, `native/<scene>-<width>-<theme>.png` the native build,
at 1280 and 360 px, dark and light: 57 scenes, 456 pictures. `captures.json` lists each picture's address, title,
and the console errors seen (the expected 401 and 404 of a signed-out start are among them).

They were taken with `scripts/check-native-browser.mjs` in its live mode (`DROVER_LIVE_ORIGIN`,
`DROVER_LIVE_SCENES`, see the top of the script). A loading state is an API path held unanswered (`hold`), a failed
state one answered 503 (`fail`). The native build was given 4.5 s after its loader went away before acting, the
web client 0.8 s; the native build reads its data several seconds later than the web client (see the manual's
limits table).

Native pictures of the scenes marked "before fixes" below were taken with the build before this pass's fixes,
because their state (no model, no bot) could not be brought back on the same server; the fixes to those screens
are visible in the later scenes of the same screen.

## Scenes and what differs

"Matches" means the same content, order, and states, with differences of a few pixels at most. Spacing
differences of 2 to 10 px (the entry layout sits a few pixels higher; the top bar is 1 to 2 px shorter) are in
almost every pair and not repeated below.

| Scene | Address | Result |
| --- | --- | --- |
| setup-account | `/setup` (first run) | Matches |
| sign-in-setup-open | `/sign-in` while setup is open | Matches: both go to `/setup` |
| sign-in | `/sign-in` | Native puts the focus in Email on load (ring visible); the web client does not. Before fixes the button did not span the form (fixed) |
| sign-in-failed | `/sign-in`, wrong password | Matches |
| setup-model-empty, setup-model-connected | `/setup/model` | The two-line provider choice is shorter in native (less padding in the segmented control) |
| setup-bot-no-model, setup-bot | `/setup/bot` | The "No model" card does not span the panel in native |
| talk-first-run, talk-no-bot | `/` | Matches |
| bots-empty, bots-no-bot, bots, bots-loading, bots-failed | `/bots` | Matches |
| bot-editor, bot-editor-lower, bot-editor-failed | `/bots/{id}` | Matches. Native keeps the top bar fixed while the page scrolls; the web client's scrolls away. The handle is heavier in native |
| bot-sandbox | `/bots/{id}/sandbox` | Matches (no sandbox provider on this server) |
| models-empty (before fixes) | `/models` | Native showed plain text instead of the empty state card (fixed, not recaptured) |
| models, models-add, models-loading, models-failed | `/models` | Matches after fixes (header button, card head, action rows). The provider choice is not full width in native |
| tools-empty, tools-add, tools-failed | `/tools` | Matches after fixes (visible choice label, address placeholder, actions at the end). "More choices" is a boxed disclosure with a chevron in native, a plain summary line in the web client |
| extensions-empty, extensions-failed | `/extensions` | Matches |
| extension-editor | `/extensions/edit?bot={id}` | Matches; a little more room above the card's text in native |
| usage-empty (before fixes), usage, usage-failed | `/usage` | Header fixed. At 1280 the native table's Cost and Bot columns are narrower: "price not known on its own" and "Sample researcher" are cut off |
| status (before fixes), status-space, status-loading, status-failed | `/status` | Matches after fixes (header, rows under lines, badge at the end). In "This space" with no rows, native shows the two links on their own line, the web client inside the sentence |
| share-empty | `/share` | Matches |
| not-found | `/no-such-page` | Matches; native's title is "Not found - Drover", the web client's "Drover" |
| talk-content, talk-content-earlier | `/talk/{lead}` | Matches after fixes (the text file card). Inline code has no padding in native. Crew names are cut with an ellipsis in native ("Sample note ta...") where the web client has room |
| talk-approval | `/talk/{helper}`, a crew tool call waiting | Matches |
| talk-failed | `/talk/{note taker}`, failure line | Matches |
| talk-streaming | `/talk/{researcher}`, reply streaming | Matches (both show "Writing" and the text so far) |
| talk-delegation-running | `/talk/{lead}`, a hand-off waiting for approval while another streams | **Differs:** the native approval card is an empty frame with only Approve and Deny for several seconds after the conversation shows; it fills in later |
| talk-side, talk-side-new | `/talk/{lead}/side/...` | Matches |
| talk-rail-activity, talk-rail-activity-run | `/talk/{lead}/activity...` | **Differs:** at capture time native still showed "Nothing said yet" in the conversation and the run as Working; both settle to the web client's picture after about 10 s |
| talk-rail-summary | `.../summary` | Matches |
| talk-rail-memory | `.../memory` | Matches after fixes (it was empty: trimmed constructor) |
| talk-rail-files | `.../files` | Matches after fixes (same cause). "Download" is not underlined in native |
| talk-rail-screen | `.../screen` | The rail buttons wrap to a second row in native when the rail is open |
| talk-rail-work | `.../work/{bot}/{thread}` | Native's conversation shows the delegation card as an empty frame at capture time (fills in later) |
| talk-loading, talk-load-failed | `/talk/{lead}`, messages held or failed | Matches |
| threads-legacy | `/threads/{thread}` | Native lands on the same conversation; its delegation card was still an empty frame at capture time |

Not captured: the development gallery (not in a Release publish), the bot editor's opened sections and dialogs,
a tool server's tool review (no tool server could be reached from this machine), a run's tool output detail, and
an actual sandbox screen.
