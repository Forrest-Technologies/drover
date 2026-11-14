# 9. Design direction

The one-page version of this section, written for anyone building a screen, is [`/DESIGN.md`](../../DESIGN.md). It also holds the list of habits Drover's interface never falls into and which of them the build checks.

Working name for the look: **Night Yard**. A dark workspace, calm at rest, where the bots are the lit things. Human intent is warm, bot activity is cool, and nothing glows unless it is doing something. What is working is turned up: it carries light (bloom), not only color. The owner chose this charged look over a flat one on 2026-11-12.

## Principles

1. **Light means live.** Color and motion mark what is working right now. Everything at rest is neutral.
2. **Show the work.** Tool activity and the bot tree are content, not debug panels.
3. **One thing at a time.** A screen starts with what most people need. Controls appear when a choice makes them relevant.
4. **Fast first.** Every effect has a job: showing state or confirming an action. If removing it loses no information, remove it.

## Tokens

One file, `Drover.Ui/wwwroot/tokens.css`, as CSS custom properties. Web and the MAUI shell load the same file. Components reference semantic tokens only, never raw values.

### Color, dark (primary)

| Token | Value | Use |
| --- | --- | --- |
| `--dv-bg-0` | `#05070A` | App background |
| `--dv-bg-1` | `#0B0E14` | Panels, sidebar |
| `--dv-bg-2` | `#121722` | Cards, message bubbles |
| `--dv-bg-3` | `#1B2230` | Hover, selected |
| `--dv-line` | `#263042` | Dividers, decorative borders |
| `--dv-text` | `#E9EDF5` | Primary text |
| `--dv-text-2` | `#AEB7C7` | Secondary text |
| `--dv-text-3` | `#8791A5` | Captions, timestamps |
| `--dv-ember` | `#FFB454` | Human intent: primary buttons, focus ring, the send action |
| `--dv-ion` | `#67E8F9` | Bot activity: thinking, streaming, running tool |
| `--dv-violet` | `#B4A0FF` | Delegation between bots |
| `--dv-ok` | `#4ADE80` | Success |
| `--dv-warn` | `#FACC15` | Needs attention, approval waiting |
| `--dv-danger` | `#FF7A7A` | Failure, destructive action |

### Color, light

| Token | Value |
| --- | --- |
| `--dv-bg-0`, `-1`, `-2`, `-3` | `#F6F7FA`, `#FFFFFF`, `#EEF0F5`, `#E2E6EE` |
| `--dv-line` | `#D5DAE3` |
| `--dv-text`, `-2`, `-3` | `#12151C`, `#414B5E`, `#5D6779` |
| `--dv-ember`, `--dv-ion`, `--dv-violet` | `#8F5400`, `#0B6E82`, `#5B3FD6` |
| `--dv-ok`, `--dv-warn`, `--dv-danger` | `#15703A`, `#7A5A00`, `#B3261E` |

### Contrast, computed

Ratios against the WCAG formula, calculated for these exact values:

| Foreground | Dark, on `bg-0` to `bg-3` | Light, on `bg-0` to `bg-3` |
| --- | --- | --- |
| `text` | 16.7 to 12.8 | 4.5 or higher on every surface; the lowest pair is `text-3` on `bg-3` at 4.56 |
| `text-2` | 9.7 to 7.4 | |
| `text-3` | 6.2 to 4.7 | |
| `ember` | 11.1 to 8.5 | 4.89 at lowest |
| `ion` | 13.5 to 10.3 | 4.71 at lowest |
| `violet` | 8.8 to 6.7 | 5.37 at lowest |
| `ok`, `warn`, `danger` | 7.8 or higher on `bg-0`, 5.9 or higher on `bg-3` | 4.92 at lowest |

Light `bg-3` was missing from the first version of this table. Adding it put the first light `ember` (`#9A5B00`) at 4.34 on that surface, so light `ember` is `#8F5400`.

Every text and status color clears 4.5:1 on every surface in both themes. `--dv-line` is about 1.5:1 and is decorative only; any border that conveys a control's edge or state uses `--dv-text-3` or a status color, which clear the 3:1 needed for interface elements. A unit test recomputes this table from `tokens.css` and fails if a change drops a pair below its threshold.

### Bot identity color

Each bot gets a hue derived from its id, carried by the head of its character (`--dv-head-0` to `--dv-head-11`, the same in both themes, each clearing 3:1 against the visor at rest, lit, and failed; a test recomputes it) and used as its lane color in the activity view and tree. The hue is picked from twelve fixed steps, `--dv-hue-0` to `--dv-hue-11`, thirty degrees apart at one lightness and chroma per theme, and every step clears 4.5:1 on every surface (7.1 or higher in dark, 4.6 or higher in light). Color is never the only signal: the bot's name or initials always accompany it.

### Type

| Token | Value |
| --- | --- |
| `--dv-font-ui` | Inter (variable), then the system UI stack |
| `--dv-font-mono` | JetBrains Mono, then the system monospace stack |
| Scale | 12, 13, 14 (base), 16, 20, 28 px, with line heights 1.5 for text and 1.25 for headings |
| Weights | 400 body, 500 labels, 600 headings |

Both fonts are bundled (SIL OFL 1.1). Nothing loads from a third-party host, which matters for privacy on self-hosted servers and for the MAUI shell offline. Monospace is used for tool arguments, commands, and output, so "what the machine did" always looks different from "what was said".

### Space, shape, depth

| Token | Value |
| --- | --- |
| Spacing | 4 px base: 4, 8, 12, 16, 24, 32, 48 |
| Radius | 6 (controls), 10 (cards), 16 (sheets), full (avatars, pills) |
| Elevation | Two levels only, done with a lighter surface and a 1 px line, not heavy shadows |
| Hit target | 44 px minimum on touch, 32 px on pointer |

### Motion

| Token | Value | Use |
| --- | --- | --- |
| `--dv-dur-1` | 120 ms | Hover, press, small state changes |
| `--dv-dur-2` | 220 ms | Panels, sheets, list insertions |
| `--dv-dur-3` | 420 ms | Delegation handoff, tree layout changes |
| `--dv-ease` | `cubic-bezier(0.2, 0.8, 0.2, 1)` | Everything that moves |
| `--dv-dur-breath`, `--dv-dur-travel` | 2600 ms, 1300 ms | One cycle of the thinking breath and of the tool line. These loop only while the state lasts. |

### Light

| Token | Value | Use |
| --- | --- | --- |
| `--dv-bloom-ion` | An 8 px soft light in `ion` | Around a thinking bot and a running tool's line |
| `--dv-bloom-violet` | The same in `violet` | Around a handoff pulse |

Bloom is used only inside the four state classes below. It is never used for depth, hover, or emphasis.

### Theme scope

The dark values sit on `:root` and on any element marked `data-theme="dark"`; the light values on `data-theme="light"` and, when the person's system asks for light and no theme is forced, on `:root`. A subtree can therefore show the other theme, which is how the gallery shows both at once.

## Motion rules

1. **Motion shows state, nothing else.** The four states that earn motion, each a class in `base.css` (`dv-state-thinking`, `dv-state-streaming`, `dv-state-tool`, `dv-state-delegation`):

   | State | Motion |
   | --- | --- |
   | Bot thinking | A slow breath of light across the bot's visor. No bouncing dots. |
   | Text streaming | Text appears as it arrives. The last few characters fade in over 120 ms so chunks do not flicker. No typewriter effect that delays text already received. |
   | Tool running | A thin ion line travels along the top edge of the tool card. It stops and turns green or red when the tool finishes. |
   | Delegation | A violet pulse travels the connector from parent to child in the tree and in the activity rail, once, when the handoff happens, and back when the result returns. |

2. **Nothing loops unless something is in progress.** When the work stops, the motion stops.
3. **Nothing blocks.** No animation delays input or hides content that has arrived. Durations stay under half a second.
4. **Reduced motion is respected.** Under `prefers-reduced-motion: reduce`, travel and pulse animations are replaced by an instant state change plus a static indicator: a solid ring for thinking, a "running" label for tools, a highlighted connector for delegation. No information is lost.
5. **Layout does not jump.** Streaming text grows downward in a container that keeps the scroll position pinned only while the user is at the bottom.

## Key screens

### 1. Thread

Three regions on a wide window: thread list, conversation, activity rail. On a phone: conversation only, with the list behind a back gesture and the rail as a pull-up sheet.

- The conversation is quiet. User messages sit right in a `bg-2` bubble; bot messages run full width with no bubble, like a document. Markdown renders live.
- Tool use appears inline as a compact card after the words of the reply that made the call: what the bot did in plain words ("Ran a command", "Wrote a file", "Took a screenshot"; another tool goes by its title), a one-line summary of the arguments, the state in words (running, done, failed, waiting for you, interrupted with the outcome unknown), and a state edge. Tapping it opens the card in place, a disclosure: the arguments, the output in monospace as it streams, error output marked "stderr" in words as well as color, in a bounded box that keeps its newest line in view, and the result with the exit code where there is one. A screenshot is not kept, so its card only says the bot took one. A reopened thread shows the same cards, rebuilt from the runs' logs. Until the rail exists, the card is where the call is read.
- The **activity rail** is the run as a vertical timeline. Each bot in the run gets a lane in its identity color. Steps, tool calls, and delegations are nodes; a delegation branches into the child's lane and rejoins when it returns. Selecting a node shows arguments, live output in monospace, result, duration, and cost. A terminal-style view is used for sandbox commands.
- An approval request is the waiting call's own card, in the conversation where the call was made and so just above the composer while it is the newest thing: "Atlas asks to run a command", the arguments (every one the summary leaves out), and Approve or Deny, each described by that sentence. While the run waits for the answer, nothing moves: no thinking state, no line along the composer. A bar above the composer that can be reached without leaving it is not built yet.
- The composer is one field. Attach, model override, and other options appear in a row only after the plus button is used.
- A cancel control replaces send while a run is active. It cancels the whole tree.

### 2. Crew

The bot hierarchy as a top-down tree on a pannable canvas. This is the first bold screen.

- Each bot is a node: avatar with identity ring, name, a one-line role, and a small live indicator. Connectors are thin lines.
- Live state is visible at a glance: a breathing ring where a bot is thinking, a violet pulse along a connector during a handoff, a warning dot where an approval waits.
- Selecting a node opens a side sheet: what the bot is doing now, recent runs, cost for it and its subtree, and Pause, Edit, Remove.
- A human can add a bot beneath any node. Caps (depth, width) show as a quiet counter on the parent, such as "3 of 5", and the add control disables at the cap with the reason in its label.
- Bots created by another bot carry a small "made by" mark.
- With a keyboard, the tree is an accessible tree view: arrow keys move, Enter opens. A list view with the same data is one toggle away and is the default for screen readers.

### 3. Bot editor

One page, sectioned, with progressive disclosure. Never a wall of settings.

- Always visible: name, avatar, "what this bot is for" (the description other bots read, labeled in those words), and instructions.
- Sections that open on demand, each showing a one-line summary when closed: Model, Tools, Rules, Memory, Schedule, Sandbox, Team.
- **Tools** lists connections, each expandable to its tools. Every tool has a three-way control: Off, Ask, Allow. A tool the parent bot lacks is shown disabled with "Atlas does not have this". A changed tool definition shows a "review" badge with a diff.
- **Rules** holds checks the server makes every time, whatever the instructions say. Closed, it reads "No rules" or "3 rules, 1 off". A rule is built as a sentence: When (one of four moments), If (conditions, added one at a time, each showing only its own fields), Then (only the effects that moment can carry out). The builder shows the sentence as it is written, and a saved rule is listed and announced as that sentence, with a switch, change, and remove. Rules from the bots above are listed first, marked with the bot they come from, with no controls. Starters are labeled as starters and add a rule that can then be changed. The section opens by saying which conditions are exact and which catch common forms. The server does not run patterns a person writes (no preset does), so the section says so in one line and that choice is not offered; a rule that already has one keeps showing it. Every condition that looks for something in text carries "(catches common forms)" in its choice, in the sentence as it is written, in the saved rule's read-back, and in a trial it decides, and says beside its field what it does not catch. A list on named inputs is exact and starts on `to, cc, bcc`; a list of addresses anywhere in the input is its own choice and is marked a pattern. What the server notes about a rule (a tool name nobody has, a list that names no tool) is shown under the rule and read out when it is saved. "Try it" sends a sample to the server, which answers with the evaluator a run uses; the screen only words the answer, and a rule still being written is tried with the rest.
- **Sandbox** offers three choices in plain words (Private, Shared with its team, Shared with chosen bots) and states what sharing means.
- **Team** holds the bot-building permissions and their caps, off by default.
- Anything that needs setup says what is missing and links to it: "Add a sandbox provider to use this".
- The definition (everything but tools, rules, and schedules) is saved together from a bar that appears once something has changed. A tool's grant, a rule, and a schedule are saved as each is changed, because a grant or a rule takes effect at once.
- A closed section's summary sits inside the button that opens it, so it is read out with the section's name.
- A model the catalog knows cannot call tools replaces the Tools controls with the reason and where to change it.
- In Tools, built-in tools come first, then one group for each tool connection, also one that has listed nothing. A tool whose definition changed says the bot asks before each use until the change is accepted, shows what was last reviewed beside what it says now, and offers to accept it for every bot. A tool its server no longer lists says so.
- New bot makes a bot at once on the connection that last answered and opens it in the editor; there is no separate creation form.

### 3a. Tool connections

Its job: add a tool server, see whether it is connected, sign in to it, review changed tools, remove it.

- One frame for each server: its name, its address and facts in monospace, and its state in words (Connected, Not connected with the last error, Not checked yet).
- The add form asks for a name and an address, then how Drover proves who is calling: nothing, a header, or a sign-in. A credential's fields appear only under its choice, and a client id, secret, and sign-in service only when the person says they registered a client. Only what the final choice uses is sent.
- A secret is typed once and never shown back. A stored header has no field until it is being replaced.
- A sign-in that is missing or has lapsed is said in a sentence with the one button that fixes it. One sign-in for the whole space is an owner's or admin's to give; a person's own is theirs.
- The provider's page sends the browser back to the start page, which hands it to this screen; the screen says how the sign-in went.
- A changed tool shows what was last reviewed beside what it says now, and one button accepts it. Removing a server asks first and says what goes with it.
- The bot editor's Tools section links here when the space has no tool server.

### 4. Activity

Every run in the space, newest first, filterable by bot, status, and "waiting for me". Each row expands to the same timeline as the thread rail. This is where a human audits what the crew did overnight and answers approvals in bulk.

### 5. Setup and Status

Setup is three steps with one decision each: create your account, connect a model (base address and key, with a "test" button that reports the result in plain words), meet your first bot. Status is a single page of rows, each green, amber, or red with a sentence: database, migrations, each model connection, each tool connection, sandbox provider, push, background workers, key ring backup.

## Accessibility

Required for every component, and tested:

- Full keyboard operation, visible focus (a 2 px ember ring with 2 px offset), logical tab order, no keyboard traps.
- Names, roles, and states for assistive technology. Streaming messages use a polite live region that announces completed sentences, not every token. Tool state changes are announced once.
- Text contrast 4.5:1, interface elements 3:1, as computed above.
- Reduced motion, as above. Text scales to 200 percent without loss.
- Component tests assert roles and names; an automated accessibility check runs against the key screens in CI.

## Component approach

**Recommendation: Drover's own small component set on plain CSS with custom properties and Blazor CSS isolation.**

| Option | Finding | Verdict |
| --- | --- | --- |
| Established styled library (several checked; all MIT or Apache-2.0 and healthy) | Each carries a strong look of its own. Themes change colors and corners, but spacing, density, motion, and component anatomy stay the library's. The two bold screens, tree and timeline, exist in none of them. | No. Drover would fight the library on exactly the screens that matter. |
| Utility stylesheet (Tailwind v4, MIT) | Works with a standalone binary and no Node. Adds a build step and a tool download to the Docker build, and long class lists to every component. | No. Tokens as custom properties give the same consistency with no build step. |
| Headless primitives package | One credible candidate exists (Apache-2.0, active, single maintainer). Useful for hard widgets: combobox, menu, popover positioning. | Not at the start. Adopt for a specific widget only if building it accessibly costs more than a day. |
| **Own components, plain CSS** | Full control of the aesthetic. No dependency to restyle or outgrow. Uses native elements that are accessible by default: `dialog`, the popover attribute, `details`, real buttons and inputs. | **Yes.** |

Icons are one set at one stroke weight (1.75 on a 24 unit grid), drawn as inline SVG by the `Icon` component from shapes held in `Drover.Ui/Icons` (Lucide, ISC). An icon-only control is an `IconButton`, which refuses to render without a label.

The gallery is the page `/dev/gallery`. It renders only when the host reports a development environment (`UiEnvironment`); a published build, which is what the compose deployment runs, shows the not-found page there.

The set is small: Button, IconButton, Field, Select, Switch, SegmentedControl, Dialog, Sheet, Menu, Tooltip, Toast, Avatar, Badge, Tabs, Tree, Timeline, MarkdownView, CodeBlock, EmptyState. Each has component tests and a page in an in-app gallery (development builds only) that doubles as the visual reference.

## Markdown

Markdig, as decided. No reason was found to do otherwise.

- **Streaming**: the message is re-parsed on each flushed chunk (at most ten times a second). An unclosed code fence renders as a code block to the end of the text, which is the right look mid-stream. Completed messages are parsed once and cached.
- **Safety**: the pipeline disables raw HTML, and the output then passes through HtmlSanitizer with a short allowlist of elements and attributes. Links open in a new context with `rel="noopener noreferrer"`. Only `http`, `https`, and `mailto` addresses survive.
- **Remote images** are not loaded automatically ([06-mcp.md](06-mcp.md)).
- **MAUI web view**: same component, same code. Nothing platform-specific.
- Code blocks get a copy button and syntax coloring from a small bundled highlighter, loaded on first use.
