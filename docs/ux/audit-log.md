# The audit log

Issue: #590.

Wording follows the [copy guide](copy-guide.md).

**The screen's job:** find out who changed what, and when.

Reached from the account menu, item `Audit log`, after `Status`. Owners and admins of the space only. Address `/audit`. Window title `Audit log · Drover`.

## Layout

```text
360 px                                   900 px and wider
+--------------------------------------+ +--------------------------------------------------+
| Audit log              [ Save a copy ]| | Audit log                         [ Save a copy ]|
| When v 7 days                        | | When v 7 days   Who v Anyone   What v All changes |
| Who  v Anyone                        | |                                                  |
| What v All changes                   | | Today                                            |
|                                      | | Sam changed Atlas's instructions           14:02 |
| Today                                | | Atlas, for Sam, added the bot Scout beneath      |
| Sam changed Atlas's instructions     | |   itself                                   13:40 |
|   14:02                              | | Yesterday                                        |
| > what changed                       | | Sam removed the rule "No payments" from          |
| Yesterday                            | |   Atlas                                    09:15 |
| ...                                  | | ...                                              |
|      [ Show earlier entries ]        | |             [ Show earlier entries ]             |
+--------------------------------------+ +--------------------------------------------------+
```

(Sam, Atlas and Scout are samples.)

Components: heading, `Button` `Save a copy` (`Quiet`), three `Select`s in a `FlowPanel` (`When`, `Who`, `What`), day headings, a list of rows, each row a `Disclosure`, and a `Button` `Show earlier entries` (`Quiet`).

- The list is plain: no cards, no stripes, no icons per action. It sits on `bg-1`, rows divided by a 1 px `line`.
- A row puts the deciding part first: the sentence of what happened in `text`, then the time in `text-3`. At 900 px and wider the time is right aligned on the first line. Below, it is on its own line under the sentence.
- A row opens to show what changed: one line per changed field, `{field}: {before}` then `{after}`, in the machine font, each value cut at 400 characters with `Show all`. A row with nothing more to show is not a disclosure and has no chevron.
- Day headings are `Today`, `Yesterday`, then the date in the device's format.

## Filters

| Filter | Options | Default |
| --- | --- | --- |
| `When` | `Today`, `7 days`, `30 days`, `All` | `7 days` |
| `Who` | `Anyone`, then the people and bots that appear in the loaded entries, people first, each by name | `Anyone` |
| `What` | `All changes`, `Bots`, `Tools and grants`, `Rules`, `Routines`, `Sandboxes`, `Projects`, `Extensions` | `All changes` |

Changing a filter reads the list again from the newest entry. The filters are kept in the address as a query so Back returns to them.

## Sentences

Row sentences come from `HistoryWords`, the same words a bot's history uses, so one change reads the same in both places. A bot acting for a person reads `{bot}, for {person}, ...`. A person or bot that can no longer be read by name reads `Someone who is no longer here` or `A removed bot`.

## States and words

| State | Words |
| --- | --- |
| Loading | `Loading the audit log.` |
| Failed | `EmptyState`: title `Drover could not load the audit log`, cause `{failure sentence}`, action `Try again` |
| Nothing has happened yet | `Nothing has been changed in this space yet.` |
| Filtered to nothing | `No change matches these filters.` with the action `Clear the filters` |
| Not permitted (a member opens the address) | `EmptyState`: title `The audit log is for owners and admins`, cause `An owner or admin of the space can read its audit log.`, no action |
| Loading earlier entries | the button reads `Loading earlier entries` and is disabled |
| No earlier entries | the button is removed. One line in `text-3`: `That is the start of the log.` |
| Earlier entries could not be read | under the list, `StatusNote` (`Danger`): `{failure sentence}` and the button stays |
| Saved | toast (`Success`): `Saved {n} entries.` |
| Saved, but there were more | toast (`Info`): `Saved the newest 50,000 entries. Narrow the filters to save the rest.` |
| Save failed | toast (`Error`): `The audit log could not be saved. {failure sentence}` |
| Offline | the Failed state. Entries already on screen stay readable, and `Show earlier entries` shows its failure note |

The three empty states are different screens with different words: nothing yet, filtered to nothing, and not permitted.

## Light and dark

Tokens only: `bg-1` behind the list, `line` between rows, `text` for sentences, `text-3` for times, the machine font for changed values on `bg-2`. No color carries meaning on this screen. Screenshots of each state, dark and light, at 360 px and 1280 px.

## Accessibility

- The page has one level 1 heading, `Audit log`. Day headings are level 2.
- The list is a list. Each row's accessible name is its sentence followed by its time in full (`14:02, today`).
- A row that opens is a button with the expanded state. Enter and Space open and close it.
- Focus order: `Save a copy`, the three filters, the rows in order, `Show earlier entries`.
- After `Show earlier entries`, focus stays on the button and a polite live region says `{n} earlier entries shown.`
- After a filter change a polite live region says `{n} entries.` or the empty sentence.
- The filters wrap at 360 px. Each select's label is visible, not a placeholder.

## Settings and defaults

This spec adds no optional element. The filters are kept in the address, not as settings: `When` starts at `7 days`, `Who` at `Anyone`, `What` at `All changes`.

## Acceptance checks

- [ ] A member who opens `/audit` sees the not-permitted state and the client makes no request for entries after the first refusal.
- [ ] The three empty states show their own words.
- [ ] A row's sentence equals the sentence the bot's history shows for the same change (`tests/Drover.Presentation.Tests`, one case per action family in the `What` filter).
- [ ] A changed value that holds markup is drawn as plain text.
- [ ] `Show earlier entries` keeps what is shown, keeps focus, and announces the count.
- [ ] The saved file holds what the current filters match, newest first.
- [ ] The screen fits at 360 px with a 200 character sentence and passes the shared screen checks, dark and light.
