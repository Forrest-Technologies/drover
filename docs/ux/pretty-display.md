# Pretty display: code, output and everything else the app shows

Issues: #783, #595, #565, #931, #932, #933, #934, #935, #936, #596. The order to build them is under [Issues](#issues).

Wording follows the [copy guide](copy-guide.md). Cards a bot sends are in [agent-cards.md](agent-cards.md); a block inside a card follows this file.

**The rule behind it:** the same kind of content looks and acts the same wherever it shows. Machine text (code, commands, output, arguments, results, JSON, logs, differences, tables a program printed, configuration) is always one block, with one bar, in the machine face, colored only by the theme's `blocks` group. Words people and bots wrote are always markdown, never a block. A long value is always cut the same way and always reachable in full.

Designed against the default theme of #906 (Front Office) on `develop`, where #677 already draws blocks by what they hold. This file says what changes from that, and what stays.

## 1. Where content shows today

Checked against the code on the trunk. "Block" is `CodeBlock` with `CodeContent.Detect` (#677). "Box" is `OutputBox`, the live output with no detection.

| Area | Surface | What it shows | Drawn by today | What changes |
| --- | --- | --- | --- | --- |
| Conversation | A reply's words | markdown | `MarkdownView` | Nothing; the parts below are its pieces |
| | A code fence | code | block, not bounded, "Copy code", "Wrap lines", "Open full screen", "Show all {n} lines" past 400 lines | Collapse past the theme's lines, save, the phone bar (2.2 to 2.5) |
| | A pipe table | table | a grid, cells on one line, no copy | One table drawing with wrap, copy and full screen (2.9) |
| | A task list | ticks | boxes read as `Done`, `Not done` | Nothing |
| | A link | link | `Hyperlink` in `ember`, http, https and mailto only | The real host when the text looks like another address (2.12) |
| | A picture by address | picture | `Load image from {host}` first, 480 wide | The viewer of #596 |
| | A diagram fence | diagram | code in a reply; a diagram only in a file preview | A diagram in a finished reply (#565) |
| | Math | formula | as typed | Formulas (#595, layer 4) |
| | Reasoning | markdown | `Disclosure` "Reasoning" | Nothing |
| | Sources | links | "Sources", one link each | Numbers and markers (#596) |
| | Search hits in a conversation | snippet | three lines, matches bold | A hit inside code shows its line in the machine face (#705) |
| Tool calls | Arguments | JSON | block, bounded to 12 lines, wrap off | Folding JSON (2.6) |
| | Output while running | text | box, always wraps, 12 lines, keeps its newest line in view | Terminal escapes removed (2.8) |
| | Output after it ends | text | block "Output", bounded, with a box for errors | One command block (2.8) |
| | Result | text, JSON, a file's language | block, bounded | Folding JSON (2.6) |
| | Approval details | facts or JSON | `Pieces.Facts` (not selectable) or a block | Selectable facts (2.13) |
| | A note changed | before and after | two lines of text | A difference (2.7) |
| | The work ledger | rows that open to the call | the same blocks as the card | Nothing beyond the card's changes |
| Other cards | Delegation | the task, the answer | plain text (not selectable), markdown | Selectable task (2.13) |
| | Takeover | reason | plain text | Nothing |
| | A failure line | the server's report | "What the server reported", machine text, no copy | A block with copy (2.10) |
| Files | A picture | picture | 480 wide, Save, Share, Download | The viewer of #596 |
| | A text file | text | box, no copy, no highlighting, "Show all" to 100,000 characters | A block in the file's language (2.11) |
| | A preview (markdown, PDF, diagram) | document | `NativeFilePreviewControl` | Nothing |
| | Bot-written HTML | document | sealed frame in the browser, refusal on devices | Only the outer frame follows the theme (#783) |
| Activity | Timeline node | one line | machine text, cut at the end | Nothing |
| | A moment's command | command | box drawn as a terminal from the one-line summary | The whole command in a command block (2.8) |
| | A moment's arguments and result | JSON, text | block, not bounded | Collapsed like a reply's block (2.4), folding JSON (2.6) |
| | A moment's output | text | box, even after the call ended | A block when the call ended (2.8) |
| Sandbox page | Command and output | command, text | command in machine text with no `$` and no copy; output and "Errors" as plain blocks | One command block; errors tagged `stderr` (2.8) |
| Bot editor | Instructions, routine prompt | text | `TextArea`, "Read the whole prompt" that never closes | `Show all` and `Show less` (2.13) |
| | A routine's custom time | cron | line in machine text, field in the interface face | The field in the machine face (2.14) |
| | Rules, "Try it" | text, JSON | machine text, no copy, not indented | A block (2.10) |
| Tool connections | A changed tool | before and after | "Reviewed" and "Now" as text; no schema | A difference, and the input schema as a folded JSON block (2.7) |
| Extensions | The file as JSON | JSON | `TextArea` with `Machine`, 18 rows, "Copy its text" | Nothing: it is an editor |
| Usage | Figures | numbers | machine face on the page, interface face in the popover, cost never | One rule for figures (2.14) |
| Status, About | Facts | text | `checked {time}`, version and platform in machine text | Nothing |
| Audit log | Entries | events | not built (#590) | The entry's detail as a folded JSON block when #590 builds it (2.6) |
| Lists | `ListCard` facts | one line | machine text, cut at the end | Values cut in the middle where it helps (2.13) |

Gaps this table shows, and the rule that closes each:

| Gap | Rule |
| --- | --- |
| Finished tool output is a block in the card and a box in Activity, and a plain block with no `stderr` tag on the sandbox page | 2.8 |
| A command is shown three ways, one of them flattened to one line | 2.8 |
| Blocks are bounded (a box that scrolls) in cards and unbounded in Activity, with two line heights | 2.4 |
| No copy on a failure report, a text file, "Try it" or a markdown table | 2.2, 2.9, 2.10, 2.11 |
| "Wrap lines" lasts only for the session | 2.3 |
| Before and after are two texts to compare by eye | 2.7 |
| The machine face is used for some identifiers and not others, and for usage figures in one place only | 2.14 |
| Text a person may want to copy cannot be selected | 2.13 |
| Terminal color codes are drawn as characters | 2.8 |

## 2. The rules

### 2.1 One block for machine text

Every piece of machine text is a `CodeBlock`. `CodeContent.Detect` decides its kind as #677 built it (code, command, JSON, table, diff, log, binary, bytes, text). The box (`OutputBox`) is kept only while output streams, because it keeps the newest line in view; when the call ends, the same place shows a block.

Inside a block: the machine face at the `blocks` text size, the `blocks` surface, line and padding (#783). Real tabs at four. No accent color ever.

### 2.2 The block bar

Every block has the same bar, above its text.

```text
Wider than 560 px
+------------------------------------------------------------------+
| Python                       [wrap] [save] [full screen] [copy]  |
|------------------------------------------------------------------|
| def total(rows):                                                 |
|     return sum(row.amount for row in rows)                       |
| ...                                                              |
| [ Show all 180 lines ]                                           |
+------------------------------------------------------------------+

At 360 px
+--------------------------------------------+
| Python                       [copy] [...]  |
|--------------------------------------------|
```

(Samples.)

- The label first, in the machine face, `text-3`: the language the fence named, or the kind's word as #677 has them (`JSON`, `Diff`, `Log`, `Output`, `Table, 12 rows`, `Binary output, {n} bytes`). A language name from a bot is untrusted: it is shown as text and only selects from the map of known languages.
- Then `IconButton`s, in this order: `Wrap lines` (a toggle, pressed while lines wrap), `Save as file`, `Open full screen`, and the copy button (`Copy code`, `Copy output`, `Copy table`). They are the controls' usual size.
- Below 560 px the bar holds the label, the copy button and one `Menu` (`More for {label}`) with `Wrap lines`, `Save as file` and `Open full screen`.
- A button that does nothing for this block is not there: no wrap for a table or the hex view, no save for a block under 3 lines.
- Copy says `Copied`, or `Copy failed. Select the text and copy it by hand.` when the platform refused (#565). Copy and save always act on the whole text as it arrived, folded or collapsed or not.

### 2.3 Wrap

- One choice per device for every block, kept as `drover.code.wrap` in `IDeviceSettings` (#595). Today's choice lasts only for the session.
- With nothing stored: lines wrap below 560 px, and from 560 px they follow the theme's `blocks.wrapByDefault` (#783).
- A table and the hex view never wrap. A box always wraps.

### 2.4 Collapse

- A finished block longer than the theme's `blocks.visibleLines` shows that many lines, then `Show all {n} lines`. Open, the button reads `Show fewer lines`. A table collapses by rows: `Show all {n} rows`, `Show fewer rows`.
- This replaces the bounded box that scrolls inside the page. A finished block never scrolls up and down inside the conversation, a card or a panel: it collapses. Only a box streaming live output scrolls, to keep its newest line in view.
- A block still streaming is never collapsed. It collapses once, when it ends, unless the person opened it.
- Past 400 lines or 64 KiB, `Show all {n} lines` draws the rest as #677 does today, with `Loading` while it draws. `Open full screen` always shows everything.
- The collapsed state is said: the block's accessible description is `Showing {visible} of {n} lines.`

### 2.5 Save

`Save as file` through `PlatformFiles.SaveText(name, text, mediaType)`: the system's save dialog on a device, a download in a browser. The name is fixed by the kind and never takes text from the content:

| Kind | Name |
| --- | --- |
| Code | `code.{extension}`, the extension from a fixed map of languages; `code.txt` when not known (#595) |
| JSON | `data.json` |
| Diff | `changes.diff` |
| Log | `log.txt` |
| Table | `table.tsv` |
| Output, text | `output.txt` |

Saved: a toast `Saved {name}.` Cancelled: nothing. Failed: a toast `Could not save {name}.` (the existing words).

### 2.6 JSON

- JSON that arrived on one line is indented by two spaces (#677 does this today). Copy gives the text as it arrived.
- **Folding.** An object or an array with more than 20 members, or deeper than the second level, starts folded, drawn as one line: `"rows": [ 40 items ]`, `"meta": { 12 keys }`. A press on the line, or Enter or Space on it, opens one level. An open one can be folded again. `Open all` and `Fold all` are items of the block's `More` menu, and buttons in full screen.
- Each foldable line is a row with a chevron (`ChevronRight` folded, `ChevronDown` open) before it, and is one stop for the keyboard. Arrow keys move between foldable rows, Right opens, Left folds.
- A folded line's accessible name: `{key}, list of {n} items, folded` or `{key}, object with {n} keys, folded`.
- **Bounded before it is read.** JSON from a tool or a bot is untrusted, so the outline is built only when the text is at most `CodeContent.MaxDetectLength` characters (1,048,576) and nests at most `ToolResults.MaxReadDepth` levels (64), read with a reader that stops at that depth and never by recursion. Past either, the block is highlighted text with no folding, decided before anything is indented or drawn.
- Past 2,000 lines once indented the block is also drawn as highlighted text with no folding, because a tree that long costs too much to draw on a phone.
- Where folding JSON shows: tool arguments and results, Activity, the input schema of a tool (2.7), the result of "Try it", an audit entry's detail (#590).

### 2.7 Differences

- A unified diff is drawn as #677 does: added lines in `ok`, removed lines in `danger`, each keeping its `+` or `-`, hunk headers in `text-3`.
- **Before and after.** Wherever the app shows a value before and after a change, it is one difference block, worked out by lines, with the label `Changes`: a tool's description and its input schema in Tool connections, a note a bot changed (`Updated a note`), a routine a bot asks to change (#158), a credential's hosts (#207). A value of one line is shown as two lines, `-` before and `+` after.
- **Bounded.** A tool's description and schema come from a remote tool server and are untrusted, so the line difference is worked out only when each side is at most 2,000 lines and 64 KiB (`TextDifference.MaxLines`, `TextDifference.MaxLength`, #931). Past either, nothing is matched: the block shows every line before as removed, then every line after as added, so it stays correct and costs one pass.
- **A tool's input.** A tool in Tool connections gains a `What it accepts` `Disclosure`, closed, holding its input schema as a folded JSON block. A changed tool shows the schema's difference there instead.
- At 360 px a difference is always unified, never side by side.

### 2.8 Commands and their output

```text
+------------------------------------------------------------------+
| Command                                          [copy] [...]    |
|------------------------------------------------------------------|
| $ dotnet test --filter Cards                                     |
|   --logger "console;verbosity=normal"                            |
|                                                                  |
| 12 passed, 0 failed                                              |
| stderr: warning: one test was skipped                            |
|                                                                  |
| Exit code 0                                                      |
+------------------------------------------------------------------+
```

(A sample.)

- One drawing everywhere a command runs: the tool card, the work ledger, Activity and the sandbox page. The command is the whole command as sent, every line, after a quiet `$` in `text-3` that is not copied. Never the one-line summary.
- Then the output. Error output is tagged `stderr: ` in words and in `danger`, line by line, as the box does today, in every place, including the sandbox page's "Errors".
- Then `Exit code {n}` in `text-2`. A nonzero code also says `Failed` beside it, in `danger`.
- **Cleaned before drawing.** Terminal escape sequences (colors, titles, cursor moves) are removed, never drawn as characters and never turned into colors. A carriage return within a line keeps only what came after it, so a progress bar reads as its last state. A backspace removes the character before it. Copy gives the cleaned text; save gives the cleaned text too.
- `Copy command` copies the command alone; `Copy output` the output alone. Below 560 px both are in the `More` menu, with copy output as the bar's copy button.

### 2.9 Tables

One table drawing for a markdown table, a table a program printed, and a bot's table card:

- Each column takes its natural width up to 320 px, then wraps. The table scrolls sideways inside its own frame only when its columns at 96 px each still do not fit. The conversation never scrolls sideways.
- A header row when the table has one. Numbers align to the end.
- The bar: `Table, {n} rows`, `Open full screen` (where the header row stays in view), and `Copy table`, which copies tab-separated text with tabs and line breaks inside cells turned into spaces (#595).
- Collapse by rows past the theme's visible lines (2.4).
- A screen reader reads it as a table, with the names #565 gives.
- A bot's table card also sorts (agent-cards.md, 3.5). Other tables do not.

### 2.10 Errors and reports

- A failure line's `What the server reported` holds a block, kind `Log` or `Text`, with `Copy output`. It stays closed by default.
- The result of "Try it" in Rules is a block, with JSON folded and `Copy output`.
- The words of an error stay in the interface face. Only the report is machine text.

### 2.11 Files

- A text file in a conversation is a block in the language its extension names, highlighted, with the bar. Its card keeps `Download`, `Save` and `Share`, so the block's `Save as file` is not shown.
- `Show all` on a text file keeps its 100,000 character limit and its sentence: `Only the first 100,000 characters of {name} are shown. Download it for the rest.`
- Pictures, PDF, markdown and diagram previews are as today and #596.

### 2.12 Links

- Only `http`, `https` and `mailto` become links, as today.
- A link whose visible text looks like a web address but whose real address has another host shows the real host after it, in `text-3`: `docs.example.org (goes to other.example)`. Its accessible description is the real address.
- A link opens outside the app. Nothing a bot wrote navigates inside Drover.

### 2.13 Long values and long text

- **One-line values a machine reads** (an id, a path, an address, a model's name, a handle, a hash) are cut in the middle with an ellipsis, keeping the start and the end: `src/Drover.Nat…/CodeBlock.cs`. Everything else that must stay on one line is cut at the end. The whole value is the tooltip and the accessible name. Where the value is one a person may paste elsewhere (an id, an address, a path), a long press or the context menu offers `Copy`.
- **Longer text in a panel or a card** (a routine's prompt, a delegated task, a memory note, a fact of an approval) shows its first 6 lines, then `Show all`. Open, it reads `Show less`. This replaces `Read the whole prompt`, which never closes today.
- **Selectable.** Text a bot or a machine wrote can be selected and copied wherever it shows: a delegated task, memory notes, facts, a tool's reviewed words, bot text on a card.

### 2.14 The machine face

| What | Face |
| --- | --- |
| Commands, arguments, output, results, code, JSON, logs, differences | machine, always |
| Identifiers a machine reads: tool names, model names as the server sends them, cron expressions, addresses, paths, handles, ids | machine, always, in text and in the field that takes them |
| Usage figures: tokens, cost, durations | machine, wherever they show: the status line, the usage popover, the Usage page |
| Times and other counts | as the theme says (`type.machineNumerals`) |
| Words people and bots wrote, labels, errors, state words | the interface face, or the reading face in a conversation |

`Field` gains `Machine`, as `TextArea` has, for the fields that take identifiers (a routine's cron expression, a tool server's address).

### 2.15 Highlighting

- The tokenizer of #677 colors code by weight and by the text tiers (`text`, `text-2`, `text-3`). With a theme's `blocks.highlighting` set to `quiet`, quoted text and comments take the two bounded colors of #783. No accent color, ever.
- A block that is still streaming is one color. It is highlighted once, when its closing fence arrives, through the incremental drawing of #560.
- A language that is not known is one color, with no note.
- This settles #595's question about a syntax palette: there is none beyond #783's two quiet colors. A theme that wants more contrast uses weight.

### 2.16 Every theme and mode

Every block reads the `blocks` group of #783 and nothing else: surface, line, padding, text size, visible lines, wrap default, highlighting. #783 lists values for the themes of ADR 0020. Front Office, which came after, takes these, which #783 adds with the rest:

| Theme | Surface | Line | Padding | Text | Lines | Wrap | Highlighting |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Front Office | bg-1 | 1 | 3 | 13 | 12 | off | weight |

`bg-1` because a bot's bubble in Front Office is `bg-2`: a block inside a bubble needs a surface of its own. Every color in a block clears 4.5:1 on that surface in dark, light and black; the theme gates check it.

## Settings and defaults

| Setting (stored name) | Location | Control and label | Default |
| --- | --- | --- | --- |
| `drover.code.wrap` | Not in Settings: the block's own `Wrap lines` button sets it | `Wrap lines` | Not stored: wrap below 560 px, the theme's `blocks.wrapByDefault` from 560 px |

Nothing else is added. Collapse, folding and cutting are not extras: they are how content fits.

## Accessibility

- Each block is a group named by its label: `Python code`, `JSON`, `Command`, `Table, 12 rows`.
- Every bar button names its block: `Copy code, Python`, `Wrap lines, Python`, `Save Python code as a file`.
- `Show all {n} lines` and `Show fewer lines` keep the focus on themselves after the press.
- A folded JSON line, a difference line and a log level say their meaning in words, never by color alone (`added`, `removed`, `Error`).
- A block that scrolls sideways is one focus stop that arrow keys scroll.
- At 200 percent text and 360 px, no block makes the conversation scroll sideways.

## Issues

| Stack | Issue | Layer | Builds |
| --- | --- | --- | --- |
| Display 1 | #783 | Presentation and native | the `blocks` group, with Front Office's row (2.16) |
| Display 1 | #595 | Native, then presentation | 2.2 to 2.5 (bar, wrap, collapse, save), 2.9 (tables), math. Highlighting is built (#677); what is left is 2.15 |
| Display 1 | #565 | Native | copy that tells the truth, tables for screen readers, diagrams in replies |
| Display 2 | #931 | Presentation | 2.8's cleaning and whole commands, 2.6's JSON outline, 2.7's line difference, 2.13's middle cut, as pure functions |
| Display 2 | #932 | Native | one block everywhere: 2.1, 2.8, 2.10, 2.11 in Activity, the sandbox page, file text, failure reports and "Try it" |
| Display 2 | #933 | Native | folding JSON (2.6) |
| Display 2 | #934 | Native | before and after as a difference, and `What it accepts` (2.7) |
| Display 3 | #935 | Native | long values, `Show all` and `Show less`, selectable text (2.13) |
| Display 3 | #936 | Native | the machine face rules, `Field.Machine`, and the real host of a link (2.12, 2.14) |
| Display 3 | #596 | All layers | the picture viewer and source markers |

## Acceptance checks

- [ ] Every surface in the table of section 1 that shows machine text draws a `CodeBlock` once the content has ended (`tests/Drover.Presentation.Tests`, `NativeInterfaceRuleTests.Finished_machine_text_is_drawn_in_a_code_block`).
- [ ] A finished block longer than the theme's visible lines collapses and never scrolls up and down inside the page (`tests/Drover.Native.UITests`, `PrettyDisplayChecks.A_long_block_collapses_and_opens_at_360`).
- [ ] A saved file's name never holds text from the content (`CodeSaveNameTests.Uses_safe_language_extension_and_never_includes_reply_text`).
- [ ] Terminal escapes never reach the screen or the clipboard (`TerminalTextTests.Escapes_are_removed_and_a_carriage_return_keeps_the_last_state`).
- [ ] A command is drawn whole, every line, in every place it shows (`CommandTextTests.A_multi_line_command_is_kept_whole`).
- [ ] A JSON object past 20 members or the second level starts folded, and copy gives the original text (`JsonOutlineTests.Large_and_deep_members_start_folded_and_copy_is_the_original`).
- [ ] JSON past the size or depth cap is drawn as highlighted text with no folding, without being indented first (`JsonOutlineTests.Json_past_the_size_or_depth_cap_is_not_folded`).
- [ ] Before and after show as one difference with `+` and `-` kept (`TextDifferenceTests.Lines_removed_and_added_keep_their_marks`).
- [ ] Before and after past 2,000 lines or 64 KiB a side show the removed block, then the added block, with no matching (`TextDifferenceTests.Past_the_cap_shows_removed_then_added`).
- [ ] A link whose text names another host shows the real host (`MarkdownLinkTests.Link_text_that_names_another_host_shows_the_real_host`).
- [ ] No block uses `ember`, `ion` or `violet`.
- [ ] Every sentence in this file appears exactly as written.
