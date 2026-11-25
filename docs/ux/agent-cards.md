# Cards a bot sends

Issues: #905 (the proposal), #880, #544, #207, #885, #158, #488, #490, #510, and the layers listed in [Issues](#issues).

Wording follows the [copy guide](copy-guide.md). Code, tables and other machine text inside a card follow [pretty-display.md](pretty-display.md).

**The rule behind every card:** a card is the bot's content in a frame Drover draws. Drover owns the frame, the state words and every button that does something other than send words. The bot owns only the words it wrote, and those words are drawn as plain text, inside the frame, in the bot's turn. A press never does more than the card shows.

Designed against the default theme of #906 (Front Office) on `feat/uno-assistant-parity`. Every card is built from tokens and the `Facet` frame, so it is correct in every theme of the family and in dark, light and black.

## Contents

1. [The decisions](#1-the-decisions)
2. [The frame every card uses](#2-the-frame-every-card-uses)
3. [The kinds a bot can send](#3-the-kinds-a-bot-can-send)
4. [Slots for the cards other issues own](#4-slots-for-the-cards-other-issues-own)
5. [States](#5-states)
6. [Stored events and a reopened conversation](#6-stored-events-and-a-reopened-conversation)
7. [Contracts](#7-contracts)
8. [Keyboard, touch and 360 px](#8-keyboard-touch-and-360-px)
9. [Accessible names](#9-accessible-names)
10. [Light, dark and every theme](#10-light-dark-and-every-theme)

## 1. The decisions

#905 left four questions open. Each is settled here, with the reason.

### 1.1 How a bot sends a card: a built-in tool, for every kind

A bot shows a card by calling a built-in tool. There is no fenced block that draws a card.

| Tool | Kinds | Waits for the person | Issue |
| --- | --- | --- | --- |
| `conversation.show` | `replies`, `choices`, `form`, `checklist`, `table`, `board` | No. The answer arrives as the person's next message | #880 |
| `ask.person` | a question with choices, or one that wants words | Yes. The run waits and the answer is the call's result | #544 |
| The tools of the slot cards | credential, tool server, routine, approval, takeover | As each issue says | Section 4 |

Why a tool and not a fence:

- A tool call passes the tool gate and its grants (`AGENTS.md`, section 3). A fence is text the client would trust without a check.
- The server validates the card against a closed set before anyone sees it. A card that breaks a limit is refused with a sentence the bot reads, and nothing is drawn. A fence would be checked by every client on its own, or not at all.
- The card is part of the event model (`AGENTS.md`, section 7): `tool.requested`, `tool.completed`, then the stored `tool_call` and `tool_result` parts. A reopened conversation draws it from what is stored, with no second path.
- Only a tool can make a run wait (`ask.person`). Having one way for every kind means the waiting and the non-waiting card share one schema and one drawing.

What stays markdown: prose, code fences, pipe tables, task lists and diagram fences. These are what a bot writes, drawn by `MarkdownView` and [pretty-display.md](pretty-display.md). A fence named like a card kind (` ```choices `) is drawn as code, never as a card.

### 1.2 What a press does: sends exactly the shown words, or answers the waiting call

- **`conversation.show`.** A press on a quick reply, or `Send answer` on a picker or a form, posts the person's own message through the normal send route. The client sends only which options were chosen and what the person typed (`CardAnswer`). The server writes the words from the stored, validated card: a quick reply's label, the chosen labels joined with `, `, or one line per form field, `{label}: {value}`. The person's message shows in the conversation as their bubble, and the card shows its answered state.
- **`ask.person`.** `Send answer` answers the waiting call through `POST .../tool-calls/{id}/answer` (#544). The answer is the call's result and is shown on the card. No bubble is added.

Bot text never becomes an instruction on its own:

1. Nothing is sent without a press by a person who may answer. A card never sends, submits or answers by itself, on a timer, on focus, or on load.
2. An option has one string. It is what is shown and what is sent. The schema has no hidden value, payload, address or action, so a button that says `Yes` cannot send anything else.
3. The words are written by the server from the stored card, never taken from the client's request. A changed client cannot make a press send other words.
4. A press sends words. It never approves a tool call, grants a tool, connects a server, saves a credential or changes a setting. Those happen only through the slot cards in section 4, each behind its own issue's checks.
5. A reaction (#361, #730) never answers a card. `approve` on a message is not a press on a card.
6. A card in a conversation a bot works in for another bot (handed work) is read only, like the rest of that conversation.

### 1.3 Limits and the plain-text fallback

The server refuses a card past these limits with a sentence the bot reads (section 7.3). The numbers live in one place, `CardLimits` in `Drover.Contracts`, and the client never trusts a card that breaks them.

| What | Limit |
| --- | --- |
| Cards in one reply (one model call's response) | 3 |
| Title | 1 to 200 characters, one paragraph |
| Text under the title | 0 to 500 characters |
| `replies` | 2 to 4 replies, each 1 to 40 characters |
| `choices` | 2 to 8 choices, each 1 to 120 characters. `Something else` is added by Drover and never by the bot |
| `form` | 1 to 6 fields. Label 1 to 60. Field name `^[a-z][a-z0-9_]{0,31}$`. Types `text` (one line, 1 to 200 characters), `number` (optional `min`, `max`), `date`, `choice` (2 to 8 options) |
| `checklist` | 1 to 20 items, each 1 to 120 characters, state one of `to_do`, `doing`, `done`, `failed` |
| `table` | 1 to 8 columns, names 1 to 60 characters. 1 to 50 rows. Cells 0 to 200 characters, at most one per column |
| `board` | 1 to 4 figures (value 1 to 16 characters, label 1 to 40, optional state). 0 to 6 states (label 1 to 40, state). 0 to 8 rows (label 1 to 80, value 0 to 40, optional `https` link up to 2,000 characters) |
| The whole card as stored | 16 KiB of JSON |
| Text in `Something else` and in a `text` field | 2,000 and 200 characters |

Every string is plain text. Control characters other than a line break are refused. A line break is allowed only in the text under the title.

**The fallback.** After validating, the server writes `fallback`: the card as plain text, from the card's own fields, never from text the bot sent for it. A client that does not know a kind draws the frame with that text and no buttons, and the person answers by writing a message.

```text
Which report should I start with?
1. Sales by region
2. Returns
3. Stock levels
4. Something else
```

(A sample.) The same text is what the conversation's Markdown export writes for the card, followed by the answer when there is one, and what a model reads back when its tool result is shown to it as text.

### 1.4 The bot's content is marked as the bot's and never restyled into the app

- A card stands in the bot's turn: under the bot's bubble at the full width of the conversation in `bubbles`, and under the bot's words in `document`, as tool cards, sources and files do today (`09-design-direction.md`, "Conversations"). Nothing a bot sends is drawn outside its turn.
- Bot text is drawn as plain text in `text` and `text-2`, in the conversation's reading face. No markdown, no links, no emphasis. `**Pay now**` shows the asterisks.
- A bot never chooses a color, an icon, a weight, a size or a state word. A state it sets is one of a closed set, and Drover draws the word and its status color.
- Bot text is never a heading of the screen, a toast, a notification, a menu item, a `Badge`, a `StatusNote`, the status line, a crew row or the conversation's header. A push or a lock screen never quotes a card (#544).
- The frame's state word, its notes and every button other than an option are Drover's words, from this spec.
- A board is the bot's content, so `DESIGN.md`'s rule against stat cards is about the interface, not about this. It still needs a title that says what it answers, and the tool's description tells the bot so. Its figures are drawn in `text`, never in an accent, and never glow.
- Accessible names say whose card it is: `Question from {bot}`, `Board from {bot}` (section 9).

## 2. The frame every card uses

Every card in a conversation is drawn in one frame, `CardFrame`: the cards of this spec, the slot cards of section 4, and the existing approval, takeover, file and delegation cards (#NEW-9). One frame means one header, one padding, one spacing and one action row on every card.

```text
+------------------------------------------------------------+
| Title, in plain words, wrapping              [ State ]     |  header
| Text under the title, when the card has it                 |
|                                                            |
| (body: options, fields, figures, rows, a table, facts)     |  body
|                                                            |
| [ Send answer ]*  [ Back to the choices ]                  |  action row
| (!) Drover could not send your answer. {failure sentence}  |  note
+------------------------------------------------------------+
```

| Part | Drawing | Tokens |
| --- | --- | --- |
| Frame | `Facet`, `Role = Frame`, the card surface, a 1 px edge. The theme decides the corners | `bg-2`, `line`. Edge `warn` while the card waits for this person, as the approval card does today; `line` in every other state |
| Padding | 16 left, right and bottom, 12 top | `DvSpace4`, `DvSpace3` |
| Space between parts | 12 | `DvSpace3` |
| Header | `FlowPanel`: the title, then the state `Badge` when there is one. At 360 px the badge wraps under the title | Title `DvText15` (the base step), heading weight, `text`, accessible heading level 2. Badge as below |
| Text under the title | Wraps. At most 500 characters, so no "more" | `DvText13`, `text-2` |
| Body | One of the bodies in section 3, or the slot's own | Inside a body, blocks follow `blocks` (#783) |
| Action row | `FlowPanel`, gap 8, wraps. The primary first | `DvSpace2` |
| Buttons | The existing `Button` at its usual height (`Targets.Minimum`). At most one `Primary`, only while the card waits for a press that commits. Everything else `Quiet`. No `Danger` on a card a bot sends | |
| Note | `StatusNote` under the action row: `Danger` for a failure, `Info` for read only and for a kind this client does not know | |
| Width | The conversation column. A picker or a form is at most `DvMeasure` wide; a table and a board take the whole column | |

The state `Badge`:

| Card state | Badge |
| --- | --- |
| Waiting for this person | `Waiting for you`, `Warn` |
| Any other state | no badge. The state is said in the note or the answer line |

Rules of the frame:

- Nothing in it moves. A card is not a live state, so it never glows, pulses or carries the travel line. The one exception is the slot of a running tool call, whose travel line is the tool card's own.
- No icon in the header. The title says what the card is, and an icon would have no job.
- No stripe, no fill color for a state, no shadow.
- The frame keeps its height while the state changes, when it can: a sent answer replaces the body with the answer line in the same frame, and focus moves to that line.
- A card that waits for the person is drawn in full under every value of `A bot's work in the conversation` (#863). So are a table, a checklist and a board: they are the bot's content, not its work. The `conversation.show` call itself is never a step in the one-line summary or in the work ledger; the card is drawn in its place.

## 3. The kinds a bot can send

The tool `conversation.show` takes one card per call. #880 holds its schema and validation; this section is its design, and extends #880 with `replies` and `board`.

### 3.1 Quick replies (`replies`)

A few short answers under a reply. Pressing one sends it.

```text
| Bot bubble: I can start with the summary or the full numbers. |
+---------------------------------------------------------------+
  [ The summary ]  [ The full numbers ]  [ Both ]
```

(Samples.)

- The one kind drawn without the frame's edge and padding: only its action row, directly under the bot's bubble, 8 px below it. The bot's reply above is the question, so the card has no title on screen; its `title` is its accessible name and its fallback.
- Each reply is a `Quiet` button with its label. No primary.
- Shown only while it can still be used: on the newest reply, while no message from a person came after it. Pressed, or once anyone writes, the row is removed, and it is not drawn again when the conversation is reopened. The person's message shows what was chosen.
- No `Something else`: the message field is right there.

### 3.2 A picker (`choices`)

A question, two to eight options, one or several to choose, and `Something else`.

```text
Open, one to choose (360 px)
+--------------------------------------------+
| Which report should I start with?          |
|                                            |
| ( ) Sales by region                        |
| (o) Returns                                |
| ( ) Stock levels                           |
| ( ) Something else                         |
|                                            |
| [ Send answer ]*                           |
+--------------------------------------------+

Something else chosen
+--------------------------------------------+
| Which report should I start with?          |
|                                            |
| ( ) Sales by region                        |
| ( ) Returns                                |
| ( ) Stock levels                           |
| (o) Something else                         |
|     Your answer                            |
|     |____________________________________| |
|                                            |
| [ Send answer ]*                           |
+--------------------------------------------+

Answered
+--------------------------------------------+
| Which report should I start with?          |
| You chose Returns.                         |
+--------------------------------------------+
```

(The question and options are samples.)

- The options are a `ChoiceList` (section 3.8): one row per option, each a full-width row at the list row height, the mark first, the label wrapping. One to choose is a radio group; several is a list of boxes to tick.
- `Something else` is always the last row. Drover adds it, and a bot that sends its own is refused (#880). Choosing it opens one `Field`, `Your answer`, directly under the row, set in by 24 px, and moves the focus into it. Choosing another option hides the field and keeps what was typed while the card is open. The field is never shown before `Something else` is chosen.
- `Send answer` is the one `Primary`. Pressed with nothing chosen, the note says `Choose an answer first.`; with `Something else` chosen and the field empty, the field's error says `Write your answer first.`
- Choosing a row never sends. Only `Send answer` does, so a scroll on a phone that lands on a row sends nothing.
- With several allowed, the person may tick several and `Something else` together; the words are the chosen labels in their order, then the typed text.
- `ask.person` (#544) draws its question with this body. A question with no choices is the `Your answer` field alone, with `Send answer`. #544 adds `Skip this question` (`Quiet`) after `Send answer`.

### 3.3 A short form (`form`)

```text
+--------------------------------------------+
| Book the meeting room                      |
|                                            |
| Date                                       |
| |2026-12-03______________________________| |
| People                                     |
| |6_______________________________________| |
| Room                          v Large room |
|                                            |
| [ Send answer ]*                           |
+--------------------------------------------+
```

(Samples.)

- One `Field` per `text`, `number` and `date` field, one `Select` per `choice` field, in the bot's order, 12 px apart. Every field has its visible label. A required field's label is the bot's label as written; an optional one adds ` (optional)`.
- `number` opens the number keyboard on a phone. `date` takes `yyyy-mm-dd`, shows `2026-12-31` as its placeholder (the form of the value, copy guide rule 6), and is checked as typed.
- A `choice` field ends with `Something else`, which opens a `Field` under it in the same way as the picker.
- No multi-line field, no hidden field, no file field. A field whose label names a credential is refused by the server (#880); credentials go through the credential card (#207).
- Enter in the last single-line field sends. A required field left empty says `{label} is needed.` under it; a number out of range says `{label} is between {min} and {max}.`; a date that does not read says `{label} is a date such as 2026-12-31.` The focus moves to the first field with an error.
- Answered, the fields become read-only facts, one line each, `{label}: {value}`, under the line `You sent this form.`

### 3.4 A checklist (`checklist`)

```text
+--------------------------------------------+
| Moving the reports                         |
|                                            |
| [v] Copy the files                 Done    |
| [>] Check the totals               Doing   |
| [ ] Send the summary               To do   |
+--------------------------------------------+
```

(Samples. The marks are icons, drawn as described below.)

- Read only. The bot sets each item's state; the person cannot tick it. A checklist that changes is a new card from the bot, and the older one stays as it was, so the conversation keeps its history.
- One row per item: a state icon, the label wrapping, the state word at the end in `text-2`. `Done` in `ok`, `Failed` in `danger`; `Doing` and `To do` in `text-2`. The word is always there, so color is never the only sign.
- No live state. `Doing` does not move.

### 3.5 A table (`table`)

- A table drawn as [pretty-display.md](pretty-display.md), "Tables", describes: cells wrap before the table scrolls sideways inside its frame, numbers align to the end, `Copy table` in the block bar.
- Column headers are buttons that sort: the first press sorts up, the second down, the third returns to the bot's order. The sorted column's header says so in words (`Sorted up`), not by an arrow alone.
- An empty cell is empty. Nothing is drawn for it.

### 3.6 A board (`board`)

A few figures, a row of states, and rows that may lead outside the app.

```text
At 360 px
+--------------------------------------------+
| How the shop did this week                 |
|                                            |
| 1,240           | USD 18,300               |
| Orders          | Takings                  |
| ----------------+------------------------- |
| 37              | 4                        |
| Returns         | Late parcels  Needs att. |
|                                            |
| Payments  OK    Shipping  Needs attention  |
|                                            |
| Top product      Blue mug       shop.example (link) |
| Slowest day      Tuesday                   |
+--------------------------------------------+
```

(Every word and number is a sample.)

- **Figures.** Two to a row at 360 px, up to four from 560 px. The value at `DvText20` in `text`, in the machine face when the theme sets numbers in it (`type.machineNumerals`), then the label in `DvText13`, `text-2`. A figure with a state adds its state word after the label. Figures are separated by 1 px `line`, not by boxes.
- **States.** A `FlowPanel` of `{label}` then the state word, wrapping. Words and colors: `ok` is `OK` in `ok`; `attention` is `Needs attention` in `warn`; `failed` is `Failed` in `danger`; `unknown` is `Not known` in `text-2`.
- **Rows.** Label in `text-2`, value in `text` at the end. A row with a link draws its label as a `Link` and adds the link's host in `text-3` after it, so the person sees where it goes before pressing. The link opens outside the app, as a reply's sources do. Only `https` addresses are accepted; anything else is refused by the server.
- No chart. Charts are #516.

### 3.7 What a client draws for a kind it does not know

```text
+--------------------------------------------+
| Which report should I start with?          |
| 1. Sales by region                         |
| 2. Returns                                 |
| 3. Something else                          |
| (i) This version of Drover cannot show     |
|     this card. Answer by writing a message.|
+--------------------------------------------+
```

The title, then `fallback` in `text-2` as plain text, then the `Info` note `This version of Drover cannot show this card. Answer by writing a message.` No buttons.

### 3.8 New pieces

| Piece | Where | Properties | Notes |
| --- | --- | --- | --- |
| `CardFrame` | `src/Drover.Native/Controls` | `Title`, `Text`, `State` (badge text or null), `Waiting` (bool, sets the edge), `Body`, `Actions`, `Note`, `NoteKind`, `Bare` (bool: only the action row, for quick replies), `AccessibleName` | The one frame of section 2. A rule test holds that every card in `src/Drover.Native/Chat` is drawn in it (#NEW-9) |
| `ChoiceList` | `src/Drover.Native/Controls` | `Label`, `Options`, `Multiple`, `Selected` (two-way, a list of indexes), `Disabled` | Rows built on `PlainButton` inside an `ArrowKeyZone`. Radio role for one, check box role for several. Row height `density.row`, at least `Targets.Minimum` |
| Icons `Radio`, `RadioOn`, `Box`, `BoxOn` | `src/Drover.Presentation/Icons` | | In the cut style of the set: an octagon for a radio, a square with a cut corner for a box. The `On` forms fill with `text` |
| Icons `StateToDo`, `StateDoing`, `StateDone`, `StateFailed` | the same | | For the checklist. Drawn in the color of their word |

## 4. Slots for the cards other issues own

These cards keep the security behavior their issues give them. This spec does not change it. Each draws its own body in `CardFrame` and takes its title, buttons and state words from its issue. Where two issues disagreed about words, the words here settle it.

| Card | Tool or source | Title (Drover's words) | Body | Action row | Issue |
| --- | --- | --- | --- | --- | --- |
| Approval | any call set to Ask | `{bot} asks to {request}` | facts, or the arguments as a JSON block | `Approve`*, `Deny` | existing, `ToolCallCard`; frame in #NEW-9 |
| Question | `ask.person` | `{bot} asks` | the picker body of 3.2 | `Send answer`*, `Skip this question` | #544 |
| Credential | `credential.request` | `{bot} asks for a credential` | the bot's text, the hosts line Drover draws, the hidden fields | `Save credential`*, `Skip this request` (the bot may rename both, as #207 says) | #207, #885 |
| Screen takeover | `computer.handover` | as `TakeoverCard` today | as today | as today | existing; frame in #NEW-9 |
| Tool server proposal | `tools.propose_connection` | `Add tool server?` | the host as the largest text, the full address in the machine face, `Proposed by {bot}`, the bot's name and purpose as quoted text | `Connect`*, `Decline` | #490 |
| Tool server from the catalog | `tools.suggest_connection` | `Add tool server?` | the #490 body, filled from the catalog entry | `Connect`*, `Decline` | #488 |
| Interactive view from a tool server | a tool server's interface resource | `From {server}: {tool}` | the sealed frame of #510, or its text | `Show as text` | #510 |
| Routine | `routine.add`, `routine.change` | `Set up a routine?`, `Change this routine?` | the schedule sentence and the whole prompt; for a change, each fact before and after | `Create routine`* or `Change routine`*, `Decline` | #158 |
| A new bot | `crew.add` | `{bot} asks to make a bot` | the facts as today | `Approve`*, `Deny` | existing |
| File | a file the bot shared | the file's name | the preview | `Download`, `Save`, `Share` as today | existing, `FileCard`; frame in #NEW-9 |
| Delegation | `delegate` | as `DelegationCard` today | as today | as today | existing; frame in #NEW-9 |
| Tool call | any other call | its label, as today | its disclosure, with blocks from [pretty-display.md](pretty-display.md) | none | existing |

For a person who may not answer one of these, the note is the issue's own sentence. Where an issue gives none: `Only {person} can answer this.`; on a card only an owner or admin can answer: `An owner or admin of this space can answer this.` This settles #490 and #488: a member reads `An owner or admin of this space can connect this tool server.`

## 5. States

Every card of section 3 has these states. A slot card maps its own states onto the same words where they mean the same thing.

| State | When | What shows | Exact words |
| --- | --- | --- | --- |
| Waiting | The card can be answered by this person | the body and the action row. Edge `warn`. Badge | `Waiting for you` |
| Sending | After `Send answer` or a quick reply, until the server answers | every control disabled, the line in `text-3` in the note's place | `Sending your answer.` |
| Answered | This person answered | the body is replaced by one line in `text-2` | `You chose {labels}.`, `You answered: {text}`, `You chose {labels} and answered: {text}`, `You sent this form.` with the facts under it |
| Answered on another device | The answer arrived from elsewhere while this card was open | the answered line, and the note (`Info`) | `Answered on another device.` |
| Answered by someone else | In a conversation shared with other people, another person answered | the line | `{person} chose {labels}.`, `{person} answered: {text}`, `{person} sent this form.` |
| Closed | A `conversation.show` card no one answered, and a message from a person came after it | the body without the action row, options drawn as text, and the line | `Closed. The conversation went on.` |
| Expired | An `ask.person` card whose run ended (stopped, failed, or past the wait limit of #485) before an answer | the body without the action row, and the line | `Expired. The run ended before an answer.` |
| Skipped | An `ask.person` card the person skipped | the line | `You skipped this question.` |
| Failed | The answer could not be sent | the body as it was, what was typed kept, the note (`Danger`) and `Try again` (`Quiet`) in the action row after `Send answer` | `Drover could not send your answer. {failure sentence}` |
| Read only | This person may not answer: not the person the run acts for, or a conversation of handed work | the body without the action row, and the note (`Info`). In handed work there is no note: the conversation's own notice says it | `Only {person} can answer this.` |
| Not shown | The kind is not known to this client | section 3.7 | `This version of Drover cannot show this card. Answer by writing a message.` |

A card the server refused is never drawn: the call is a failed step in the bot's work, with the refusal sentence, like any tool that failed.

A `table`, a `checklist` and a `board` have no answer, so they have only Waiting (drawn without badge or edge, as content) and Not shown.

Quick replies have Waiting, Sending and Failed only. After Sending they are removed (3.1). A failure brings the row back with the note under it.

Announcements: a new card that waits for this person is announced once, politely: `{bot} asks: {title}`. A failure is announced assertively. The answered, closed and expired lines are not announced, because the person's message or the run's end already is.

## 6. Stored events and a reopened conversation

A card is drawn from what the server stored, live and later alike. There is no client-only state that survives a reload except what the person typed into an open card.

| Kind | Where the card comes from | Where its state comes from |
| --- | --- | --- |
| `conversation.show` | the call's `tool_result` part: the validated card as JSON, with `fallback` | an `answer` part in a later person message names the call: Answered. Any later person message without one: Closed. Otherwise Waiting |
| `ask.person` | the call's arguments and its `tool.approval_requested` event (#544) | the call's status and result: waiting is Waiting, answered is Answered, skipped is Skipped; the run ended while waiting is Expired |
| Slot cards | as each issue says | as each issue says |

- Live, the card appears when `tool.completed` arrives for the call and is filled from the completed message. It is not drawn from `tool.requested`, so a card the server refuses never flashes.
- A reopened conversation shows the same cards in the same places, with the same answers, from `GET .../messages` and the run's stored events. A test replays a recorded event log and compares the two drawings (`AgentCardTests.A_reopened_conversation_draws_the_same_cards_and_answers`).
- A card belongs to the message version it was sent in. When a person edits an earlier message or asks for another reply, the cards of the version not shown are not shown either.
- An answer sent from another device arrives as a person message (`conversation.show`) or as the call's change (`ask.person`), and the open card changes to Answered on another device.
- The server refuses a second answer, and an answer after a later person message, with 409 and the sentence `This card was already answered or the conversation went on.` The client then shows the state the server holds.
- Search (#703 and after) finds a card by its fallback text. The Markdown and JSON exports write the fallback and the answer.

## 7. Contracts

At the level a builder needs. Names are new unless the text says otherwise. #NEW-1 builds 7.1 and 7.2, #880 builds 7.3, #NEW-2 builds 7.4.

### 7.1 `Drover.Contracts.Cards`

A new folder, `src/Drover.Contracts/Cards`.

```csharp
namespace Drover.Contracts.Cards;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(RepliesSpec), CardKinds.Replies)]
[JsonDerivedType(typeof(ChoicesSpec), CardKinds.Choices)]
[JsonDerivedType(typeof(FormSpec), CardKinds.Form)]
[JsonDerivedType(typeof(ChecklistSpec), CardKinds.Checklist)]
[JsonDerivedType(typeof(TableSpec), CardKinds.Table)]
[JsonDerivedType(typeof(BoardSpec), CardKinds.Board)]
public record CardSpec
{
    public required string Title { get; init; }

    public string? Text { get; init; }

    public required string Fallback { get; init; }
}

public sealed record RepliesSpec(IReadOnlyList<string> Replies) : CardSpec;

public sealed record ChoicesSpec(IReadOnlyList<string> Choices, bool Multiple) : CardSpec;

public sealed record FormSpec(IReadOnlyList<CardField> Fields) : CardSpec;

public sealed record CardField(string Name, string Label, string Type, bool Required, double? Min, double? Max, IReadOnlyList<string>? Options);

public sealed record ChecklistSpec(IReadOnlyList<CardItem> Items) : CardSpec;

public sealed record CardItem(string Label, string State);

public sealed record TableSpec(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows) : CardSpec;

public sealed record BoardSpec(IReadOnlyList<CardFigure> Figures, IReadOnlyList<CardItem> States, IReadOnlyList<CardRow> Rows) : CardSpec;

public sealed record CardFigure(string Value, string Label, string? State);

public sealed record CardRow(string Label, string? Value, string? Link);

public sealed record CardAnswer(Guid ToolCallId, IReadOnlyList<int>? Choices = null, string? Text = null, IReadOnlyDictionary<string, string>? Values = null);
```

- `CardKinds` holds the six kind names and the state names (`to_do`, `doing`, `done`, `failed` for items; `ok`, `attention`, `failed`, `unknown` for a board).
- `CardLimits` holds every number of section 1.3, read by the server's validation and by the client's checks.
- `CardText.Plain(CardSpec)` writes the fallback (1.3) and `CardText.Words(CardSpec, CardAnswer)` writes the words of an answer (1.2). Both pure, in `Drover.Contracts` so the server and the tests share them; the client reads `Fallback` and never writes words.
- An unknown `kind` reads as a plain `CardSpec` with its title and fallback, because of `IgnoreUnrecognizedTypeDiscriminators`. That is the Not shown state.
- The Presentation layer uses these records as they are. It does not copy them into a second set of types. The names end in `Spec` so they do not meet the native controls or the Presentation `ToolCard`.

### 7.2 What changes in existing contracts

| Contract | Change |
| --- | --- |
| `MessagePartResponse` | kind `answer` is added to the list in its summary. New member `CardAnswer? Answer` |
| `SendMessageRequest` | `CardAnswer? Answer = null` as the last parameter (#880) |
| `ConversationExport` | the fallback and the answer for each card (#NEW-2) |

No new run event type. A card travels in the standard tool events and the stored parts.

### 7.3 The tool (`conversation.show`)

#880 holds the tool, its grant to new and existing bots, and its validation table. This spec adds to it:

- `kind` gains `replies` and `board`, with the limits of 1.3.
- The result is the `CardSpec` as JSON, with `fallback` written by the server and `something_else` no longer needed (a `ChoicesSpec` always offers it).
- Refusal sentences added to #880's table:

| Rule | Sentence the bot reads |
| --- | --- |
| `replies` without 2 to 4 replies, or one over 40 characters | `Quick replies need 2 to 4 replies of at most 40 characters.` |
| `board` without a figure, or past a limit | `A board needs 1 to 4 figures, at most 6 states and at most 8 rows.` |
| A board row link that is not `https` | `A board row can link only to an https address.` |
| A state outside the closed set | `A state is one of to_do, doing, done, failed for a checklist, and ok, attention, failed, unknown for a board.` |
| A control character in any text | `Card text is plain text with no control characters.` |
| The card over 16 KiB | `A card is at most 16 KiB.` |

### 7.4 Answering

#NEW-2 builds what #880 section 2 calls "Engine: answering": `AnswerPart`, `CardAnswers.Check`, the 409, and the words written by the server through `CardText.Words`. The endpoint is the existing send route, `POST /api/spaces/{spaceId}/threads/{threadId}/messages`, with `answer` set. Who may answer: the person the run acts for, under row-level security, so another person's private conversation is not found (404).

## 8. Keyboard, touch and 360 px

- **Order.** The title is not a stop. Then each option or field in order, then the action row from its first button. In a picker the whole `ChoiceList` is one stop: arrow keys move between rows, Home and End go to the ends. One to choose: the arrow moves the choice as well, as a radio group does; Space chooses. Several: Space ticks the row with the focus.
- **Something else.** Choosing it moves the focus to `Your answer`. Enter there sends. Escape there hides the field, chooses nothing, and puts the focus back on `Something else`.
- **Enter** in the last single-line field of a form sends. Escape in a card does nothing else; the card is not a dialog.
- **The card never takes the focus** by itself while the person writes in the message field. A sent answer moves the focus from the pressed button to the answer line, which is a `FocusLine`, so the focus is never lost.
- **Quick replies** are tab stops in order, after the reply's own actions.
- **Touch.** Every row and button is at least 44 px tall on touch (`Targets.Minimum`). Rows are full width, so the target is the whole row, not the mark.
- **At 360 px.** One column. Option rows wrap their text. The action row wraps with the primary first. Board figures two to a row. A table scrolls sideways inside its frame only, and the conversation never does. At 200 percent text the same holds.
- **Wider.** From 560 px a quick replies row and the board's figures sit side by side; options stay one per row at every width, because a list of rows reads faster than a grid.

## 9. Accessible names

| Piece | Name | Notes |
| --- | --- | --- |
| A quick replies row | `Quick replies from {bot}` | a group |
| A quick reply | its label | description `Sends "{label}" as your message.` |
| A picker | `Question from {bot}` | a group, labeled by its title |
| An option | its label | radio or check box role, with its state |
| `Something else` | `Something else` | |
| `Your answer` | `Your answer` | |
| `Send answer` | `Send answer` | description: the card's title |
| A form | `Form from {bot}` | |
| A checklist | `Checklist from {bot}` | each item `{label}, {state word}` |
| A table | `Table from {bot}` | the table itself as #565 names tables |
| A sort header | `Sort by {column}` | and `Sorted up` or `Sorted down` when it is |
| A board | `Board from {bot}` | each figure `{label}: {value}`, and `, {state word}` when it has one |
| A board row link | `{label}, opens {host} outside Drover` | |
| A card this client cannot show | `Card from {bot}` | |

## 10. Light, dark and every theme

- Tokens only: `bg-2`, `line`, `warn` for the waiting edge and badge, `text`, `text-2`, `text-3`, `ok`, `danger` for state words, `ember` only on the primary button and the focus ring. No `ion` and no `violet` on any card: a card is not a bot working and not work passing between bots.
- `Facet` draws the corners in the theme's language, so a card is round in Front Office, cut in Night Yard, and square or ticked where a theme says so.
- In `bubbles` a card stands under the bot's bubble at the full width; in `document` under the bot's words beside its head.
- Black mode moves the card surface down one level with every other surface. `text-2` on `bg-2` keeps 4.5:1 in every theme and mode; the theme gates already check that pair.
- A block inside a card (a table, the arguments of an approval) follows the `blocks` group of #783.
- Screenshots for the pull request of each layer: every state of section 5 for the kinds it builds, dark and light, at 360 and 1280 px, in Front Office and Night Yard.

## Settings and defaults

This spec adds none. Cards are not an extra (`conversation-display.md`), so no setting hides them, and `A bot's work in the conversation` never hides a card.

## Issues

Built bottom up. Each issue is one pull request.

| Stack | Issue | Layer | Builds |
| --- | --- | --- | --- |
| Cards 1 | #NEW-1 | Contracts | 7.1, 7.2 |
| Cards 1 | #880 | Engine | 7.3: the tool, its validation and grants |
| Cards 1 | #NEW-2 | Engine and server | 7.4, the 409, the export |
| Cards 2 | #NEW-3 | Presentation | cards from stored parts, section 6; the view models and states of section 5 |
| Cards 2 | #NEW-4 | Native | `CardFrame`, `ChoiceList`, the icons; quick replies and the picker |
| Cards 2 | #NEW-5 | Native | the form |
| Cards 2 | #NEW-6 | Native | checklist, table and board, and the Not shown state |
| Cards 3 | #544 | All layers | the waiting question on the picker body |
| Cards 3 | #NEW-9 | Native | approval, takeover, file and delegation cards in `CardFrame` |
| Slots | #207, #885, #158, #488, #490, #510 | As each says | their bodies in `CardFrame`, with the words of section 4 |

## Acceptance checks

- [ ] A press on a quick reply, a picker or a form sends exactly the words `CardText.Words` writes from the stored card, whatever the request's text says (`tests/Drover.Engine.Tests`, `CardAnswersTests.The_server_writes_the_words_from_the_stored_card`).
- [ ] A card schema has no field that carries a value, an address or an action other than a board row's `https` link (`tests/Drover.Engine.Tests`, `ConversationCardsTests.An_option_has_one_string_and_no_hidden_value`).
- [ ] Every limit of 1.3 refuses with its sentence, and no card is drawn.
- [ ] A client given an unknown kind draws the title, the fallback and the Not shown note, with no buttons (`tests/Drover.Presentation.Tests`, `AgentCardTests.An_unknown_kind_shows_its_fallback_and_no_buttons`).
- [ ] Bot text with markdown, a link or an emoji code is drawn as that literal text (`AgentCardTests.Bot_text_is_plain_text`).
- [ ] Every state of section 5 has its exact words (`AgentCardTests.Each_state_has_its_exact_words`, one case per row).
- [ ] A reopened conversation draws the same cards with the same answers (`AgentCardTests.A_reopened_conversation_draws_the_same_cards_and_answers`).
- [ ] Every card in `src/Drover.Native/Chat` is drawn in `CardFrame` (`NativeInterfaceRuleTests.Every_card_in_a_conversation_uses_the_card_frame`).
- [ ] At 360 px every kind fits with no sideways scroll of the conversation, by keyboard and touch, at 200 percent text (`tests/Drover.Native.UITests`, `AgentCardChecks.Every_kind_fits_at_360_and_works_by_keyboard`).
- [ ] No card uses `ion` or `violet`, and nothing on a card animates.
- [ ] Every sentence in this file appears exactly as written.
