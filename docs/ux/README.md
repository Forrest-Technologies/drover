# Interface specs

Each file here is the design for one group of roadmap issues. A spec is written so that a person or a coding agent can build the screen from it with no further design input. `DESIGN.md` is the direction and `docs/architecture/09-design-direction.md` is the full specification. When a spec and those two disagree, they win and the spec gets fixed.

| Spec | Issues |
| --- | --- |
| [uploads.md](uploads.md) | #844, #821, #814, #836 |
| [recovery-states.md](recovery-states.md) | #555, #556, #557, #558, #562, #563, #569, #570, #572, #575, #576, #554, #752, #438 |
| [first-run.md](first-run.md) | #592, #583, #584 |
| [providers.md](providers.md) | #850, #52, #531, #855 |
| [app-updates-and-ios.md](app-updates-and-ios.md) | #513, #612, #617, #857 |
| [members-and-spaces.md](members-and-spaces.md) | #517, #518, #591, #523 |
| [sign-in.md](sign-in.md) | #524, #860 |
| [audit-log.md](audit-log.md) | #590 |
| [conversation-display.md](conversation-display.md) | #862, #863, #594, #598, #600 |
| [copy-guide.md](copy-guide.md) | #864, #865, #866, and every spec |

Several of these issues were written with their wording marked provisional until a design was approved. These specs are that design. Where a spec repeats an issue's wording, the wording is now final. Where a spec changes it, the spec says so and the spec wins.

## What every spec assumes

These hold for every screen and are not repeated in each file.

### Components

Use the controls in `src/Drover.Native/Controls`, listed in `docs/architecture/18-native-client.md`, "Controls". The ones these specs use:

| Control | Use |
| --- | --- |
| `Button` | `Kind` is `Primary` (one per screen or dialog, ember), `Quiet` (everything else) or `Danger` (the confirming press of something that cannot be undone) |
| `IconButton` | An icon with a required `Label`. Never an icon alone |
| `Field`, `TextArea` | A labeled input with `Hint` and `Error`. The label is always visible, never placeholder only |
| `Select`, `SegmentedControl`, `Switch` | One of many (five or more, or long names), one of few (two to four short names), on or off |
| `Dialog` | A question or a short form that must be answered before going on. Radius 16 |
| `Sheet` | A list or a longer form beside the work. Trailing edge at 640 px and wider, bottom edge below 640 |
| `EmptyState` | `Title`, `Cause`, one action. Used for empty, failed and not-permitted, each with its own words |
| `StatusNote` | One line about the thing beside it. `Kind` is `Info`, `Ok` or `Danger` |
| `Badge` | A short state word. `Kind` is `Neutral`, `Live`, `Ok`, `Warn` or `Danger` |
| `ToastHost` | A result of an action that needs no decision. `Info`, `Success`, `Warning`, `Error`. May carry one action |
| `Disclosure`, `Tabs`, `Menu`, `Link`, `Tooltip`, `FlowPanel`, `Facet` | As documented |

A spec that needs a control that does not exist says so under "New pieces" and gives its properties. Do not add one otherwise.

### Layout

- Entry screens (connect, sign in, register, reset) use `EntryLayout`: one column, at most 440 px wide, centered on the yard, 24 px padding, 16 px between fields.
- Work screens use `WorkLayout`. Content is at most 720 px wide for forms and full width for lists.
- Breakpoints from `Styles.xaml`: 560 (phone), 640 (sheet moves to the bottom), 900 (the crew column shows), 1200 (the rail shows).
- Every screen works at 360 px wide with no sideways scroll. A row of buttons that does not fit wraps (`FlowPanel`), primary first.
- Spacing is the 4 px scale: 4, 8, 12, 16, 24, 32, 48.

### Light and dark

- Every color is a token from `Tokens.xaml`. No raw color in a control.
- A spec names tokens by role (`text`, `text-2`, `bg-1`, `ember`, `danger`). The value comes from the theme, so a screen built from tokens is correct in both.
- Every text and status color keeps 4.5:1 on its surface in both themes. A screenshot of each state in dark and in light, at 360 px, goes in the pull request.
- State is never shown by color alone. A colored state always has a word or an icon with a name.

### Accessibility

- Every control has an accessible name. For a `Field` it is its label. For an `IconButton` it is its `Label`. A spec gives the name where it differs from the visible text.
- Focus order is reading order unless a spec gives another. Focus is always visible: the 2 px ember ring.
- Touch targets are 44 px, pointer targets 32 px (`Targets.Minimum`).
- A change the person did not cause (a result arriving, a failure) is announced through a live region: polite for results, assertive only for a failure that stops their work.
- When a dialog or sheet opens, focus moves to its first field, or to its least destructive button when it has no field. When it closes, focus returns to the control that opened it.
- Enter submits a form from any single-line field. Escape closes a dialog or sheet without acting.
- Text sizes follow the type scale and survive 200 percent zoom.
- Reduced motion: nothing in these specs animates except the four live states in `DESIGN.md`.

### Words

The [copy guide](copy-guide.md) is binding for every string. In short:

- Sentence case, plain words, no exclamation marks. A button names its action.
- An error says what failed and the one thing to do. `Failures.Sentence(exception)` supplies the sentence for a request that failed. A spec writes "{failure sentence}" where it is used.
- `{name}` in a spec is a value filled in at run time. Everything else is exact.
- No sample names or numbers on a real screen. Samples in a spec (Sam, Atlas, Team) are for the gallery, marked as samples.

### Settings and defaults

An extra is a fact that is nice to see and not needed to act: token counts, cost, timing, a model's name. An extra never appears as a card or a row inside the conversation. Each one has a default and a switch in Settings. Every spec has a "Settings and defaults" section that gives the default, the stored name, the location in Settings and the label, or says that the spec adds none.

### Wireframes

Wireframes are ASCII. `[ Label ]` is a button, `[ Label ]*` the primary one, `(o)` a chosen option, `[x]` a switch that is on, `|____|` a field, `v` a select. Widths are not to scale. The 360 px layout is the one drawn unless the caption says otherwise.

### Acceptance checks

Each spec ends with checks. They are added to the acceptance criteria of the issue that builds the screen. A check that can be a test names the test project to put it in.
