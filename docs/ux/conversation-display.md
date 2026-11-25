# Conversation display: the status line, a bot's work, and message times

Issues: #862 (the status line), #863 (how much of a bot's work shows), #594 (which model answers, and how long a run has taken), #598 (message times and day separators), #600 (the Settings screen these switches live on).

Wording follows the [copy guide](copy-guide.md).

**The principle:** the conversation holds what was said and what a person must act on. Facts about a reply (which model, how long, how many tokens, what it cost) are extras. They never appear as cards or rows between messages. They sit in one quiet line under the composer, and a person can turn that line off.

## What moves out of the conversation

| Today, inside the conversation | Where it goes |
| --- | --- |
| Under each reply: `Written by {model}, {tokens} tokens, {cost}` and `Show what this reply used` (`ReplyFooter`, `ReplyUsageViewModel`) | The status line shows it for the newest reply. For an older reply it is one action on that reply, `Usage for this reply` |
| Planned by #594: a caption under the bot's name with the model, and a run line with elapsed time and model calls beside the thinking head | The status line. The thinking head keeps its one state word |
| Planned by #598: the time of day on each message | Off by default. Day separators stay. The exact time is in each message's tooltip and menu |
| A card for every tool call and a disclosure for the bot's reasoning, in line | One line per reply by default (#863). The Activity panel beside the conversation holds every step |

What stays in the conversation: messages, a card that waits for the person (an approval, a takeover request, a question), a file a bot shared, a failure line, and the sources of a reply.

## 1. The status line (#862, #594)

One line directly under the composer, a footer of it, as wide as the composer and tight to it, 12 px, `text-3`, machine font for the numbers.

```text
At rest, after a reply
+------------------------------------------------------------------+
| |Write to Atlas________________________________________| [Send]  |
+------------------------------------------------------------------+
  {model} · 42 s · 3 model calls · 1,240 tokens · USD 0.02

While a run works
+------------------------------------------------------------------+
| |Write to Atlas________________________________________| [Stop]  |
+------------------------------------------------------------------+
  Running web search · 1 min 40 s · model call 3 · {model}
```

(Atlas is a sample.)

- The line is one `PlainButton` when it has detail to open, and plain text when it has none. Pressing it opens the usage detail for the newest reply in a popover anchored to the line: the same content the reply's `Show what this reply used` popover has today, with the title `Usage for this reply`.
- Parts are joined by a middle dot with a space each side. A part that is not known is left out. Nothing is shown as zero or as a dash.
- At 360 px the line shows the first two parts that fit and ends there. It never wraps to a second line. The full line is in its accessible name and its popover.
- The line never animates. The line traveling around the composer already reports a running tool.

### Built so far

On 2026-11-24 the owner decided that the line sits under the composer as a footer, without an icon, with the working state in the bot-activity color. That decision supersedes what this section and "Light and dark" said before (above the message field, inside the composer's frame, no accent color at all); their sentences now say what was decided.

Built:

- The line under the composer, and under the notice of a read-only conversation: text only, parts joined by a middle dot, the numbers in the machine font, `text-3` and `text-2` when the pointer is over it or it has focus. It never wraps and never animates. Where it is too narrow it shows the leading parts that fit, and the first is cut short if it alone is too long. The full sentence is its accessible name.
- While a run works: the state (`Thinking`, `Writing`, `Waiting for you`, and for a running tool the words its card already uses, such as `Running a command`), then the model, with `, for this conversation` when it was chosen for this conversation only.
- At rest: the model that wrote the newest reply, `{n} tokens`, and `{currency} {amount}` when the price is known. A new conversation shows `{model}`.
- Usage still being read, usage that could not be read (the popover says `Drover could not load usage. {failure sentence}` with `Try again`), and a person who may not read usage (plain text, no tokens and no cost).
- The line is one `PlainButton` when it opens the usage and plain text when it does not. The popover is titled `Usage for this reply`, takes the focus, closes with Escape, and puts the focus back on the line.
- Each finished reply's one action, `Usage for this reply`, an `IconButton` with the information icon beside `Copy reply`, opening the same popover. No model, tokens or cost are written under a reply.

Not built yet:

- The time a reply took and the clock while a run works, the model calls, and `Waiting to start`.
- `Running {tool name}` by the tool's name: the line uses the card's words for the call.
- The line for a dropped connection, and the offline behavior.
- The Settings group and its switches (#600), so the line cannot be turned off yet.
- How much of a bot's work shows (#863) and message times (#598).
- `Usage for this reply` as an item of a reply's menu: replies have no menu.

### Parts and words

| Part | While a run works | At rest |
| --- | --- | --- |
| State | `Waiting to start`, `Thinking`, `Writing`, `Running {tool name}`, `Waiting for you` | not shown |
| Time | `42 s`, then `3 min 5 s`. Shown from 10 seconds on. No clock while `Waiting for you` | the time the newest reply took |
| Model calls | `model call 3`, from the second call on | `3 model calls`. `1 model call` is not shown |
| Model | `{model}`. With a model chosen for this conversation only: `{model}, for this conversation` | the same |
| Tokens | not shown | `{n} tokens` |
| Cost | not shown | `{currency} {amount}`. Left out when the price is not known |

Order while a run works: state, time, model calls, model. Order at rest: model, time, model calls, tokens, cost.

### States

| State | The line |
| --- | --- |
| A new conversation, nothing said yet | `{model}` |
| A run is working | the working form |
| The newest reply finished | the at-rest form for that reply |
| The newest reply's usage is still being read | the at-rest form without tokens and cost. They appear when read |
| Usage could not be read | the at-rest form without tokens and cost. The popover says `Drover could not load usage. {failure sentence}` with `Try again` |
| The person may not read usage (a member, where the space hides it) | no tokens and no cost. The line is plain text |
| The connection dropped | `The connection dropped. Reconnecting.` replaces the line until events resume |
| A read-only conversation (handed work) | the line shows under the notice that takes the message field's place |
| The status line is turned off | no line. The composer is exactly as tall as it is without it |
| Offline | the last line stays, in `text-3`, with no clock running |

### A reply's own usage

Each reply gains one action, `Usage for this reply`: an item in the reply's menu where it has one, and otherwise an `IconButton` with that label beside `Copy reply`. It opens the same popover, anchored to the reply. No text is drawn under the reply for it. The reply's footer keeps `Copy reply` and the other action icons, and loses the model, the tokens and the cost.

## 2. How much of a bot's work shows (#863)

| Choice | What the conversation shows for one reply |
| --- | --- |
| `One line per reply` (default) | One line above the reply: `{n} steps` with the kinds in words (`Read a file, ran a command, searched the web`), cut to one line. It opens the Activity panel at that reply. A step that waits for the person is always drawn in full |
| `Every step` | A card for each tool call and a disclosure for reasoning, as today |
| `Nothing` | No line and no cards. A step that waits for the person is still drawn in full, and a failure line still shows |

A card that needs an answer (an approval, a takeover request, a question from the bot) is never hidden by this setting.

### Built so far

On 2026-11-25 the owner asked to see a bot's work where it happened, between the things it said, as one short line that opens to the detail. Built, with no setting yet (the Settings group is not built):

- The work between two things a bot said in one run (its reasoning and the calls that fold) stands between those words: above the words it led to, or under the last words when the bot worked after them. Work before the turn's first words stands under the bot's head. Nothing is gathered at the top of the turn any more.
- Closed, the work is one line: what was done, in order, a kind repeated counted once ("Searched the web 3 times, read a page"), then the number of steps, the time they took and how many failed, or `running` in the bot-activity color while a step runs. The chevron opens it to a row per step, and a row opens to the call, its arguments, output and result.
- Work of one step is its row alone, with no line above it.
- While a step runs, the line of live work travels around the work's frame.

The line's accessible name is `{bot}'s activity, {n} steps. Opens the Activity panel.`

## 3. Message times (#598)

- Day separators are always shown: a 1 px `line` with the day centered in `text-3`. `Today`, `Yesterday`, the weekday within the week, then the date.
- The time of day on messages is off by default. With it on, the time shows in `text-3` on the line that names who is speaking, on the first message of a run of messages from the same side within 5 minutes.
- With it off or on, the exact date and time is the tooltip of the speaker's name and an item in the message's menu: `Sent {full date and time}`.

## Settings and defaults

A new group on the Settings screen (#600), named `Conversation`, between `Appearance` and `Notifications`. Each setting is kept on the device through `IDeviceSettings`, like the theme, and is not sent to the server. Each takes effect at once, with no Save button.

| Setting (stored name) | Location | Control and label | Default |
| --- | --- | --- | --- |
| `drover.conversation.status-line` | Settings, Conversation | `Switch`: `Show the status line under the composer`. About text: `The model, the time a reply took, and what it used.` | On |
| `drover.conversation.status-line.model` | Settings, Conversation, under the switch above, shown when it is on | `Switch`: `Model` | On |
| `drover.conversation.status-line.time` | the same | `Switch`: `Time and model calls` | On |
| `drover.conversation.status-line.usage` | the same | `Switch`: `Tokens and cost` | On |
| `drover.conversation.work` | Settings, Conversation | `Select`: `A bot's work in the conversation`, with `One line per reply`, `Every step`, `Nothing` | `One line per reply` |
| `drover.conversation.times` | Settings, Conversation | `Switch`: `Show the time on messages` | Off |

```text
Settings
  Conversation
    [x] Show the status line under the composer
        The model, the time a reply took, and what it used.
        [x] Model
        [x] Time and model calls
        [x] Tokens and cost
    A bot's work in the conversation   v One line per reply
    [ ] Show the time on messages
```

With all three part switches off, the status line shows only the working state while a run works and nothing at rest.

A stored value that is missing or not understood means the default. The settings are removed when the person signs out on a device or uses another server.

## Layout at each width

- Below 560 px the status line shows what fits on one line, in the order given, and the popover holds the rest.
- The Settings group is one column at every width. The three part switches are indented 24 px under their parent.

## Light and dark

Tokens only: `text-3` for the line, `text-2` when the pointer is over it or it has focus, the machine font for numbers. The line has no fill, no border and no icon. It uses no accent color except the working state, which is a live state and takes the bot-activity color. Screenshots: the line at rest, while working, cut at 360 px, turned off, and the Settings group, dark and light.

## Accessibility

- The line's accessible name is the full sentence, never the cut one: `{model}, 42 seconds, 3 model calls, 1,240 tokens, {currency} {amount}. Shows usage for this reply.`
- It is not a live region. A state change is already announced by the conversation (`{bot}, thinking`). The elapsed time is never announced as it ticks.
- It is in the focus order between the conversation and the message field, and only when it opens something.
- The popover takes focus when it opens, closes with Escape, and returns focus to the line.
- `text-3` on the conversation's surface, where the line stands, keeps 4.5:1 in both themes.
- Each switch's name is its label, and the parent switch's description is its About text.

## Acceptance checks

- [ ] No model name, token count, cost or elapsed time is drawn between or under messages, in any setting.
- [ ] With the status line off, the composer has the same height and focus order as today.
- [ ] Each row of the Settings table exists with its exact label, default and stored name (`tests/Drover.Presentation.Tests`, one case per row).
- [ ] The line is one line at 360 px with a 60 character model name.
- [ ] Nothing in the line animates, and the elapsed time is not announced.
- [ ] A card that waits for the person shows in full under every value of `A bot's work in the conversation`.
- [ ] Every sentence in this file appears exactly as written.
