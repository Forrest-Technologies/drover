# Copy guide

How Drover's interface is worded. It applies to every string a person can read: labels, buttons, placeholders, hints, empty states, toasts, errors, dialog text, accessible names and notifications. `DESIGN.md`, "Words", is the short form. This is the long form, with examples from the code.

The test for any string: would a careful person say this out loud to a colleague, in these words? If it sounds like a slogan, a riddle or a character speaking, rewrite it.

## Rules

### 1. Name the thing

Use the noun the rest of the interface uses. Do not describe the thing instead of naming it.

| Do not write | Write |
| --- | --- |
| `Words someone said` (a search placeholder) | `Search messages` |
| `What this reply used` | `Usage for this reply` |
| `See what happened` | `Show activity` |
| `What got in the way` | `Reason` |
| `How Drover proves who is calling` | `How Drover signs in to this server` |

### 2. A bot is software

Bots have characters, and the four live states have words (`Thinking`, `Writing`, `Running a tool`, `Failed`). Beyond those state words, do not give a bot feelings, wishes, a body or a story.

| Do not write | Write |
| --- | --- |
| `Bots think with a model you bring.` | `A bot needs a model.` |
| `Meet your first bot` | `Create your first bot` |
| `Your first bot leads the crew.` | `Your first bot is at the top of the crew.` |
| `{bot} needs you at its screen` | `{bot} asks you to take over its screen` |
| `Looking over its crew` | `Listing its bots` |
| `These stop a bot that runs away` | `These limits stop a bot before it uses too much` |

### 3. No figures of speech

No metaphor, no idiom, no wordplay. They read as charm on the first day and as noise on every day after, and they do not translate.

| Do not write | Write |
| --- | --- |
| `Adding it up.` | `Loading usage.` |
| `Drover has not worked out the next time yet.` | `The next run time is not known yet.` |
| `Write a bot down so someone else can import it` | `Save a bot as a file that someone else can import` |

### 4. Say the state, then the next step

Empty, loading, failed and not permitted are four states with four different sentences. Each says what is true and the one thing to do. A loading line says what is loading.

| State | Shape | Example |
| --- | --- | --- |
| Loading | `Loading {the thing}.` | `Loading the members.` |
| Empty, first time | what goes here, and the action | `No project yet` and `New project` |
| Empty, filtered | what was looked for, and how to widen it | `No change matches these filters.` and `Clear the filters` |
| Failed | `Drover could not {verb} {the thing}`, the cause, `Try again` | `Drover could not load the audit log` |
| Not permitted | who may, in one sentence | `An owner or admin of the space can read its audit log.` |

### 5. Buttons name the action

A button is a verb and its object: `Add connection`, `Remove rule`, `Save a copy`. Never `OK`, `Submit`, `Yes`, `Continue` on its own when a more exact verb exists. In a confirming dialog the two buttons name the two outcomes: `Keep it` and `Remove`, not `Cancel` and `OK`.

### 6. Placeholders are examples, not instructions

A field has a visible label. A placeholder, when there is one, shows the form of a value (`https://`, `drover.example.org`). It never carries the label, a question or a sentence. A search field's placeholder may name what is searched: `Search messages`.

### 7. No questions as headings or labels

A heading says what the section is. A question makes the person answer in their head before they act.

| Do not write | Write |
| --- | --- |
| `Which bot gets this?` | `Choose a bot` |
| `Notifications arriving late?` | `If notifications arrive late` |

A confirming dialog may ask its one question in the title: `Remove {name} from this space?`

### 8. Errors: what failed, why if known, what to do

No apology, no blame, no code without a sentence, no exclamation mark. Use `Failures.Sentence` for a failed request. Do not write `Something went wrong`, `Oops`, `Uh oh`, `Sorry`, or `Please`.

### 9. Short, and still a sentence

Cut filler words: `just`, `simply`, `easily`, `quickly`, `now you can`, `here you can`, `feel free`, `let's`. Do not cut the verb. `Loading the conversation.` is right. `Loading...` is not: it does not say what.

### 10. Words that never appear

- Hype words: the list in `DESIGN.md`, "Words", and also `smart` and `intelligent`.
- `AI`, `AI-powered`, `assistant` as a description of the product. The product is bots.
- Chat-product habits: `Ask me anything`, `How can I help`, `What's on your mind`, `Let's get started`, `Welcome back`.
- Exclamation marks, emoji, and an ellipsis used for mood. An ellipsis only marks text that was cut.
- The name of another product.

### 11. One word for one thing

| Use | Not |
| --- | --- |
| bot | agent, assistant, AI |
| conversation, side chat | thread, chat, session |
| space | workspace, team, organization |
| crew | hierarchy, org chart |
| model connection | provider account, integration |
| tool server | integration, plugin, connector |
| run | job, task, execution |
| model call | step, turn, iteration |
| note (what a bot keeps about a person) | memory item, fact |
| owner, admin, member | administrator, user |

### 12. Numbers, times and sizes

Use the device's formats. Write units out where there is room (`12 minutes ago`, `1,240 tokens`). A count agrees with its noun: `1 file`, `2 files`. Never `1 file(s)`.

### 13. Accessible names follow the same rules

An accessible name is copy. It names the control and its object (`Remove holiday.png`, `Role of Robin`). It does not repeat the role ("button") and does not describe how to use the control unless that is not obvious.

## Extras are quiet and can be turned off

Token counts, cost, timing, the model's name and similar facts are extras. They never appear as cards or rows inside the conversation. Each has a default and a switch in Settings. Every spec in this folder has a "Settings and defaults" section that gives, for each extra: the default, the setting's stored name, where it is in Settings, and its label. `conversation-display.md` holds the ones for the conversation.

## How to check a string

1. Read it aloud. Does it name the thing and say what to do?
2. Look for a rule above that it breaks.
3. Search the client for the same thing said another way, and use the words already there.
4. If the string is new, add it to the spec of the screen it is on.

## Enforced

A rule test already fails the build on filler such as "no data" and "coming soon" (`DESIGN.md`, "States that say nothing"). Issue #866 extends it with the phrases under rule 10 and the patterns under rules 3 and 8, so a new string that breaks them does not merge.
