# Conversation display: the status line, a bot's work, and message times

Issues: #862 (the status line), #863 (how much of a bot's work shows), #594 (which model answers, and how long a run has taken), #598 (message times and day separators), #600 (the Settings screen these switches live on).

Wording follows the [copy guide](copy-guide.md).

**The principle:** the conversation holds what was said and what a person must act on. Facts about a reply (which model, how long, how many tokens, what it cost) are extras. They never appear as cards or rows between messages. They sit in one quiet line under the composer, and a person can turn that line off.

## What moves out of the conversation

| Today, inside the conversation | Where it goes |
| --- | --- |
| Under each reply: `Written by {model}, {tokens} tokens, {cost}` and `Show what this reply used` (`ReplyFooter`, `ReplyUsageViewModel`) | The status bar shows the newest reply’s usage; its usage popup names the model that wrote it. For an older reply it is one action on that reply, `Usage for this reply` |
| Planned by #594: a caption under the bot's name with the model, and a run line with elapsed time and model calls beside the thinking head | The status line. The thinking head keeps its one state word |
| Planned by #598: the time of day on each message | Off by default. Day separators stay. The exact time is in each message's tooltip and menu |
| A card for every tool call and a disclosure for the bot's reasoning, in line | One line per reply by default (#863). The Activity panel beside the conversation holds every step |

What stays in the conversation: messages, a card that waits for the person (an approval, a takeover request, a question), a file a bot shared, a failure line, and the sources of a reply.

## 1. Conversation status bar

The bar spans the conversation under the composer or the read-only notice. The current model sits on the
left. Input tokens, output tokens, and cost each occupy their own segment. While the bot works, the bar
shows its current state alongside the model. Usage comes from the newest completed reply and includes any
runs it handed to other bots. Unknown figures and figures the person may not read are omitted.

Choose the model name to open the model picker, where the bot's default is a separate action. The picker
shows recently selected models from this session first, then lists the full catalog the bot is allowed to use.
Models with a provider namespace are grouped by it. Models without one appear under the bot's connection.
Search matches the model's name, ID, or provider, and the current choice has a checkmark.

The composer's plus button opens Attach file in a popup above the button. Opening it leaves the composer
in place. Selecting an action closes its popup. Escape and a press outside dismiss it as well.

Selecting a usage segment opens **Usage for this reply**, which identifies the model that wrote the reply.
It shows the reading state and offers **Try again** if the usage request fails.

Elapsed time and model call count (#594), reconnect and offline status, and individual status bar switches (#862)
are planned separately.

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
- While a call runs, its work and its row open by themselves so the output can be watched as it arrives, and close when it ends. A person who opens or closes either keeps that choice. On trial: if the owner does not like it, it becomes a setting or goes.
- While a step runs, the line of live work travels around the work's frame.

The line's accessible name is `{bot}'s activity, {n} steps. Opens the Activity panel.`

## 3. Message times (#598)

- Day separators are always shown: a 1 px `line` with the day centered in `text-3`. `Today`, `Yesterday`, the weekday within the week, then the date.
- The time of day on messages is off by default. With it on, the time shows in `text-3` on the line that names who is speaking, on the first message of a run of messages from the same side within 5 minutes.
- With it off or on, the exact date and time is the tooltip of the speaker's name and an item in the message's menu: `Sent {full date and time}`.

## Settings and defaults

**Appearance → Show conversation status bar** is on by default and applies immediately. The device keeps the
preference. With the bar hidden, the same model picker opens from the plus menu, and the bar stops fetching
reply usage. A missing or unrecognized stored value uses the default.

The planned **Conversation** group on the Settings screen (#600) will also hold the individual display switches.

| Setting (stored name) | Location | Control and label | Default |
| --- | --- | --- | --- |
| `drover.conversation.status-line` | Appearance | `Switch`: `Show conversation status bar` | On |
| `drover.conversation.status-line.model` | Settings, Conversation, under the switch above, shown when it is on | `Switch`: `Model` | On |
| `drover.conversation.status-line.time` | the same | `Switch`: `Time and model calls` | On |
| `drover.conversation.status-line.usage` | the same | `Switch`: `Tokens and cost` | On |
| `drover.conversation.work` | Settings, Conversation | `Select`: `A bot's work in the conversation`, with `One line per reply`, `Every step`, `Nothing` | `One line per reply` |
| `drover.conversation.times` | Settings, Conversation | `Switch`: `Show the time on messages` | Off |

## Layout at each width

The status bar stays on one line and the model name truncates first. The picker is bounded by the window size
and pages long result lists. The popups use the same theme tokens as the conversation.

## Accessibility

- The model and usage buttons have distinct accessible names. The model button exposes the full name when truncated.
- Working state remains available in the conversation's announcements.
- The keyboard reaches all actions and can dismiss the popups with Escape.
- The switch has the same visible and accessible name.

## Acceptance checks

- [ ] No model name, token count, cost or elapsed time is drawn between or under messages, in any setting.
- [ ] With the status line off, the composer has the same height and focus order as today.
- [ ] Each row of the Settings table exists with its exact label, default and stored name (`tests/Drover.Presentation.Tests`, one case per row).
- [ ] The line is one line at 360 px with a 60 character model name.
- [ ] Nothing in the line animates, and the elapsed time is not announced.
- [ ] A card that waits for the person shows in full under every value of `A bot's work in the conversation`.
- [ ] Every sentence in this file appears exactly as written.
