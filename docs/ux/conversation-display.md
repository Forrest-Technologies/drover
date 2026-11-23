# Conversation display: the status line, a bot's work, and message times

Issues: #862 (the status line), #863 (how much of a bot's work shows), #594 (which model answers, and how long a run has taken), #598 (message times and day separators), #600 (the Settings screen these switches live on).

Wording follows the [copy guide](copy-guide.md).

**The principle:** the conversation holds what was said and what a person must act on. Facts about a reply (which model, how long, how many tokens, what it cost) are extras. They never appear as cards or rows between messages. They sit in one quiet line above the message field, and a person can turn that line off.

## What moves out of the conversation

| Today, inside the conversation | Where it goes |
| --- | --- |
| Under each reply: `Written by {model}, {tokens} tokens, {cost}` and `Show what this reply used` (`ReplyFooter`, `ReplyUsageViewModel`) | The status line shows it for the newest reply. For an older reply it is one action on that reply, `Usage for this reply` |
| Planned by #594: a caption under the bot's name with the model, and a run line with elapsed time and model calls beside the thinking head | The status line. The thinking head keeps its one state word |
| Planned by #598: the time of day on each message | Off by default. Day separators stay. The exact time is in each message's tooltip and menu |
| A card for every tool call and a disclosure for the bot's reasoning, in line | One line per reply by default (#863). The Activity panel beside the conversation holds every step |

What stays in the conversation: messages, a card that waits for the person (an approval, a takeover request, a question), a file a bot shared, a failure line, and the sources of a reply.

## 1. The status line (#862, #594)

One line directly above the message field, inside the composer's frame, 12 px, `text-3`, machine font for the numbers.

```text
At rest, after a reply
+------------------------------------------------------------------+
| {model} · 42 s · 3 model calls · 1,240 tokens · USD 0.02         |
| |Write to Atlas________________________________________| [Send]  |
+------------------------------------------------------------------+

While a run works
+------------------------------------------------------------------+
| Running web search · 1 min 40 s · model call 3 · {model}         |
| |Write to Atlas________________________________________| [Stop]  |
+------------------------------------------------------------------+
```

(Atlas is a sample.)

- The line is one `PlainButton` when it has detail to open, and plain text when it has none. Pressing it opens the usage detail for the newest reply in a popover anchored to the line: the same content the reply's `Show what this reply used` popover has today, with the title `Usage for this reply`.
- Parts are joined by a middle dot with a space each side. A part that is not known is left out. Nothing is shown as zero or as a dash.
- At 360 px the line shows the first two parts that fit and ends there. It never wraps to a second line. The full line is in its accessible name and its popover.
- The line never animates. The travel line on the composer's top edge already reports a running tool.

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
| A read-only conversation (handed work) | the line shows above the notice that takes the message field's place |
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

The line's accessible name is `{bot}'s activity, {n} steps. Opens the Activity panel.`

## 3. Message times (#598)

- Day separators are always shown: a 1 px `line` with the day centered in `text-3`. `Today`, `Yesterday`, the weekday within the week, then the date.
- The time of day on messages is off by default. With it on, the time shows in `text-3` on the line that names who is speaking, on the first message of a run of messages from the same side within 5 minutes.
- With it off or on, the exact date and time is the tooltip of the speaker's name and an item in the message's menu: `Sent {full date and time}`.

## Settings and defaults

A new group on the Settings screen (#600), named `Conversation`, between `Appearance` and `Notifications`. Each setting is kept on the device through `IDeviceSettings`, like the theme, and is not sent to the server. Each takes effect at once, with no Save button.

| Setting (stored name) | Location | Control and label | Default |
| --- | --- | --- | --- |
| `drover.conversation.status-line` | Settings, Conversation | `Switch`: `Show the status line above the message field`. About text: `The model, the time a reply took, and what it used.` | On |
| `drover.conversation.status-line.model` | Settings, Conversation, under the switch above, shown when it is on | `Switch`: `Model` | On |
| `drover.conversation.status-line.time` | the same | `Switch`: `Time and model calls` | On |
| `drover.conversation.status-line.usage` | the same | `Switch`: `Tokens and cost` | On |
| `drover.conversation.work` | Settings, Conversation | `Select`: `A bot's work in the conversation`, with `One line per reply`, `Every step`, `Nothing` | `One line per reply` |
| `drover.conversation.times` | Settings, Conversation | `Switch`: `Show the time on messages` | Off |

```text
Settings
  Conversation
    [x] Show the status line above the message field
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

Tokens only: `text-3` for the line, `text-2` when the pointer is over it or it has focus, the machine font for numbers. The line has no fill, no border and no icon. It uses no accent color: nothing in it is a live state. Screenshots: the line at rest, while working, cut at 360 px, turned off, and the Settings group, dark and light.

## Accessibility

- The line's accessible name is the full sentence, never the cut one: `{model}, 42 seconds, 3 model calls, 1,240 tokens, {currency} {amount}. Shows usage for this reply.`
- It is not a live region. A state change is already announced by the conversation (`{bot}, thinking`). The elapsed time is never announced as it ticks.
- It is in the focus order between the conversation and the message field, and only when it opens something.
- The popover takes focus when it opens, closes with Escape, and returns focus to the line.
- `text-3` on the composer's surface keeps 4.5:1 in both themes.
- Each switch's name is its label, and the parent switch's description is its About text.

## Acceptance checks

- [ ] No model name, token count, cost or elapsed time is drawn between or under messages, in any setting.
- [ ] With the status line off, the composer has the same height and focus order as today.
- [ ] Each row of the Settings table exists with its exact label, default and stored name (`tests/Drover.Presentation.Tests`, one case per row).
- [ ] The line is one line at 360 px with a 60 character model name.
- [ ] Nothing in the line animates, and the elapsed time is not announced.
- [ ] A card that waits for the person shows in full under every value of `A bot's work in the conversation`.
- [ ] Every sentence in this file appears exactly as written.
