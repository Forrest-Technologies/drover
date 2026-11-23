# Recovery states

Issues: #555, #556, #557, #558, #562, #563, #569, #570, #572, #575, #576, #554, #752, #438.

Wording follows the [copy guide](copy-guide.md).

**The rule behind all of them:** a screen never crashes, never drops what a person typed, and never stops without the one next step. Empty, loading, failed and not permitted are different states with different words.

This spec fixes the words and the layout. Each issue holds the code path.

## 1. One pattern for "could not load"

Every work screen and every pane that reads from the server uses the same three states.

```text
Loading                         Failed                              Loaded
+---------------------------+   +-------------------------------+   +-------------+
|                           |   |                               |   |             |
|  Loading the conversation.|   |  Drover could not load this   |   |  (content)  |
|                           |   |  conversation                 |   |             |
|                           |   |  {failure sentence}           |   |             |
|                           |   |        [ Try again ]*         |   |             |
+---------------------------+   +-------------------------------+   +-------------+
```

- Loading: one line of `text-2`, centered in the content area, a polite live region. No spinner and no skeleton.
- Failed: an `EmptyState` in place of the content. `Title` names the thing, `Cause` is the failure sentence, one action `Try again` (`Primary`). The same `EmptyState` instance is kept while a retry runs, so the button keeps the focus.
- While a retry runs the Loading line shows and `Try again` is disabled.
- A connection that comes back retries by itself. The person does not have to press.

| Where | Loading | Failed title | Issue |
| --- | --- | --- | --- |
| A conversation | `Loading the conversation.` | `Drover could not load this conversation` | #555 |
| A side chat that is gone (404) | | `This side chat is not here` (unchanged) | #555 |
| Any work screen opened with the server unreachable | the screen's own loading line | `The server did not answer` with `Try again` (the session gate, unchanged) | #569 |
| Sign in, Create account, opened with the server unreachable | | the screen's own `StatusNote` (`Danger`) with the failure sentence, and `Try again` beside it | #569 |

A conversation that failed to load never shows `Nothing said yet`. Its message field is visible and disabled, with the accessible description `Available when the conversation has loaded.`

## 2. A reply that was lost mid-stream (#556)

What had arrived stays on screen, drawn at rest. Under it, in place of the old danger note:

```text
| ...the part of the reply that arrived                          |
|                                                                |
| (!) Drover lost track of this reply. {failure sentence}        |
|                                            [ Check again ]     |
```

| State | Line (`StatusNote`, `Danger`) | Button (`Quiet`) |
| --- | --- | --- |
| Lost, a reply | `Drover lost track of this reply. {failure sentence}` | `Check again` |
| Lost, handed work | `Drover lost track of this work. {failure sentence}` | `Check again` |
| Checking | `Checking how it ended.` (`Info`) | `Check again`, disabled |
| Check failed | the Lost line with the new failure sentence | `Check again` |
| Recovered | the line is removed. The reply continues or shows as it ended | |

- While lost: the travel line stops, the bot's head is not lit, Send is enabled.
- Approval cards that were waiting are unlocked and can be answered again. A second answer to a call that was already answered shows `This request was answered already.`
- At 360 px the button sits under the line, left aligned.
- The line is an assertive live region when it first appears, polite for Checking.

## 3. An ended session (#562)

```text
+--------------------------------------+
|  (i) Your session ended. Sign in to  |
|      carry on.                       |
|                                      |
|  Email     |______________________|  |
|  Password  |______________________|  |
|              [ Sign in ]*            |
+--------------------------------------+
```

| Case | What the person sees |
| --- | --- |
| The server stops accepting the session | Sign in opens, with a `StatusNote` (`Info`, not danger) above the form: `Your session ended. Sign in to carry on.` Shown once |
| Unsaved work in the bot editor or the extension editor when it happens | The leave dialog from section 5 asks first, as for any navigation |
| Sign out on a device, and the server could not be told | Sign in opens with a toast (`Warning`): `You are signed out on this device. The server could not be told, so that session stays valid until it expires.` |
| Sign out in a browser, and the server could not be told | The person stays where they are, with a toast (`Error`): `You are still signed in. {failure sentence}` |

Focus lands on the Email field. The note is read before the form (it is first in reading order). After signing in the person returns to the address they were at when #573 has landed, and to the crew otherwise.

## 4. What was typed is kept (#554, #557, #576)

| Case | Behavior | Words |
| --- | --- | --- |
| A draft in a bot's message field when the person goes elsewhere (#554) | The text and the uploaded files are there on return, and after the app restarts | none in the conversation |
| A bot in the crew with a draft waiting (#554) | A `Badge` (`Neutral`) on its row | `Draft`. The row's accessible name ends with `, draft waiting` |
| An edit to an earlier message whose send failed (#557) | The edit field stays open with the text, and the failure shows under it | `{failure sentence}` in a `StatusNote` (`Danger`), with `Send again` (`Primary`) and `Discard the edit` (`Quiet`) |
| "Use another server" pressed by mistake (#576) | Connect opens with the address that was left, selected | none |

The draft badge is the only new visible element. It is not colored as a live state: a draft is at rest.

## 5. Leaving with unsaved work (#572)

One dialog for the bot editor and the extension editor, for a navigation, a window close and a sign-out.

```text
+------------------------------------------+
|  Leave without saving                    |
|                                          |
|  The changes to {bot} are not saved yet. |
|  Leaving drops them.                     |
|                                          |
|        [ Stay here ]   [ Leave ]         |
+------------------------------------------+
```

| Screen | Title | Body | Buttons |
| --- | --- | --- | --- |
| Bot editor | `Leave without saving` | `The changes to {bot} are not saved yet. Leaving drops them.` | `Stay here` (focused, `Quiet`), `Leave` (`Danger`) |
| Extension editor | `Leave without saving` | `The extension you are writing is not saved yet. Leaving drops it.` | the same |

Other words from #572, now final:

| Case | Words |
| --- | --- |
| A section closed with a half-written rule or routine in it | the section's summary line ends with `Not saved yet` |
| Save worked but some sandbox tool changes did not | `The bot was saved, but {n} of its sandbox tool changes were not. {failure sentence} Save again to retry them.` With one: `The bot was saved, but 1 of its sandbox tool changes was not. {failure sentence} Save again to retry it.` |
| Duplicate with unsaved changes | toast (`Info`): `{copy} was made from {bot} as last saved.` with the action `Open {copy}` |
| Discard | toast (`Info`): `Changes to {bot} were dropped.` with the action `Undo` |

Escape answers `Stay here`. The dialog never opens twice for one attempt to leave.

## 6. Undo in place of a question (#575)

Removing a rule stays one press. A toast offers the way back.

| Event | Toast | Action (accessible name) |
| --- | --- | --- |
| Rule removed | `Rule removed: {rule name}.` (`Info`, no end time) | `Undo` (`Undo removing {rule name}`) |
| Put back, Rules section on screen | announced, no toast: `Rule put back: {rule name}.` | |
| Put back, elsewhere | `Rule put back: {rule name}.` (`Success`) | |
| Could not be put back | `The rule could not be put back. {failure sentence}` (`Error`). The first toast keeps `Undo` | |

After a removal, focus goes to `Add a rule`.

## 7. Toasts that stay out of the way (#563)

- A toast that changes keeps its card and its focus. A new toast never moves focus.
- The same message twice shows one card, moved to the newest place and announced again.
- An error or warning with no action leaves after 30 seconds unless the pointer is over it or focus is inside it. Info and success leave after 6 seconds. A toast with an action stays until used or dismissed.
- At most three errors show. A fourth removes the oldest one that is not held.
- Below 900 px the toasts sit at the top of the window under the bar, full width less 16 px each side, inside the safe area. At 900 px and wider they sit at the bottom right. They never cover the message field, Send, Stop or the dock.
- Each card has `Dismiss` (`Dismiss: {first words of the message}` as its accessible name).

## 8. Sending (#558, #752)

| Case | Behavior | Words |
| --- | --- | --- |
| Enter while the input method is composing a word (#558) | Enter finishes the word. It does not send | none |
| A phone or tablet with a hardware keyboard (#558) | Enter sends and Shift+Enter makes a line, as on a desktop. With no hardware keyboard, Enter makes a line and the Send button sends | The field's hint follows: `Enter to send, Shift+Enter for a new line` or no hint |
| A conversation that holds delegated work (#752) | No message field and no Attach. In their place one line | `This conversation holds work one bot handed to another. You can read it here.` (`text-2`) |

## 9. Choosing a model connection that works (#570)

Where a person picks a model connection (New bot, setup), connections are listed ready first, then not yet tested, then failed, each with its state as a word.

| State | `Badge` | Selectable |
| --- | --- | --- |
| Ready | `Ready` (`Ok`) | yes, the first ready one is chosen by default |
| Not tested yet | `Not tested` (`Neutral`) | yes |
| Failed | `Not working` (`Danger`) | yes, with a `StatusNote` (`Danger`) under the select once chosen: `This connection failed its last test. Test it on the Models page before you use it.` with the link `Open Models` |
| Switched off | `Off` (`Neutral`) | no |

When no connection is ready, nothing is chosen by default and the note reads `No model connection is working. Add or test one on the Models page.` with the same link.

## 10. A failed model call says which thing to fix (#438)

The failure line under a reply (`FailureLine`) uses one fixed sentence per cause. New sentences, now final:

| Cause | Sentence | Action |
| --- | --- | --- |
| The conversation is too long for the model | `This conversation is longer than {bot}'s model can read. Start a new conversation, or pick a model that reads more.` | `Open {bot}'s settings` |
| The provider did not find the model | `{bot}'s provider did not find its model. Pick another model in the bot's settings, or check the connection's address.` | `Open {bot}'s settings` |
| The provider's content filter refused | the existing refused sentence | none |

A refusal written by the model itself is the bot's reply: it is drawn as a normal message, with no failure line.

## Light and dark

All states use tokens: `danger` for a failure line and its icon, `text-2` for loading lines, `bg-2` and `line` for dialogs and toasts. No state uses `ion`, `ember` glow or `violet`: nothing here is live. Screenshots of every state in sections 1 to 6 and 9, dark and light, at 360 px, go in the pull request that builds it.

## Accessibility

- A failed state's title is a heading. Its action is the first focusable control in the content area.
- `Try again`, `Check again` and `Undo` keep focus while they work and after they fail.
- Loading lines are polite live regions. A failure that replaces content is assertive once.
- The leave dialog's default is the safe answer. Escape and the window's close button answer `Stay here`.
- The draft badge and the connection state badges are text, so they are read without color.

## Settings and defaults

This spec adds no optional element. Every state here tells a person that something failed or was kept, so none of them can be turned off.

The `Draft` badge in the crew (#554) is always shown while a draft exists.

## Acceptance checks

- [ ] No screen shows an empty-state sentence while it is loading or after it failed to load (`tests/Drover.Presentation.Tests`, one case per view model in #569's table).
- [ ] Every sentence in this file appears in the client exactly as written. A test compares the strings for sections 2, 3, 5, 6 and 10.
- [ ] With the server stopped, every route in `Screens.Table` opens to a failed state with a working `Try again` (the check build of #569).
- [ ] A lost reply keeps its text on screen and `Check again` keeps focus through a failed check.
- [ ] The leave dialog opens for navigation, window close and sign-out, once each.
- [ ] Toasts never cover the message field at 360, 600 and 1280 px (screenshots in #563's pull request).
- [ ] Every state is reachable and operable by keyboard alone.
