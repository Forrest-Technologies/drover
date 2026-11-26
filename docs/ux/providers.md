# Providers: model connections, what a model can do, and what runs a bot

Issues: #850 (provider kinds in the forms), #52 (change and remove a connection), #531 (what a model can do, set by an admin), #855 (what runs a bot).

Wording follows the [copy guide](copy-guide.md).

**The idea:** the client knows no provider by name. The server says which kinds it offers and what each one asks for, and the forms are drawn from that.

## 1. Connect a model (setup step 2, and "Add a connection" on Models)

**The screen's job:** connect the model this space's bots use.

```text
+------------------------------------------+
|  o-----(2)-----o     Connect a model     |
|  Connect the model your bots use. The    |
|  key is stored encrypted on this server  |
|  and is never shown again.               |
|                                          |
|  Provider                                |
|  ( Hosted A )( Compatible endpoint )     |
|                                          |
|  Base address                            |
|  |https://____________________________|  |
|  {address hint from the server}          |
|                                          |
|  Key (optional)                          |
|  |____________________________________|  |
|  {key hint from the server}              |
|                                          |
|  (i) {outcome}                           |
|  [ Test connection ]      [ Continue ]*  |
+------------------------------------------+
```

Components: `SetupSteps` (setup only), heading, one line of `text-2`, the provider choice, zero to two `Field`s, a `StatusNote`, two `Button`s.

### Drawn from the server's kinds

| From the kind | Drawn as |
| --- | --- |
| Display name | The option's label. Up to four kinds: a `SegmentedControl` labeled `Provider`, `Fills`. Five or more: a `Select` labeled `Provider` |
| Address rule: fixed | No address field |
| Address rule: required | `Field` labeled `Base address`, `Type` url, placeholder `https://`, the kind's address hint under it |
| Key rule: required | `Field` labeled `Key`, `Type` password |
| Key rule: optional | The same field labeled `Key (optional)` |
| Key rule: none | No key field |
| Key hint | The hint under the key field. Plain text, never a link |

The first kind in the server's order is chosen at first. Changing the kind keeps a typed address and clears a typed key.

An address a person pastes is sent as typed. The server may correct it (#530), and the saved connection then shows the corrected address.

### Named providers

Until the kinds come from the server (#850), the provider choice is Venice, then each other kind the server offers with its own wire (such as `openai`, the provider's Responses API), then the named providers the compatible kind carries in `GET /api/model-provider-kinds`, in the server's order, then `Another compatible server`. A named provider that a kind with its own wire covers (the same id or the same name) is left out of the choice, so no name is offered twice; it stays in the server's list. It is a `Select` labeled `Provider` (five or more choices), in setup and in the Models page's add form. A named provider is a starting point: the connection is a compatible one with that address.

| Choice | Drawn as |
| --- | --- |
| A named provider | The address field is hidden and the form holds the provider's address. A text button `Change address` shows the field filled in. The provider's name is the connection's name |
| Key rule: none | No key field |
| Key rule: optional | The field labeled `Key (optional)` |
| A key page | One `Link` under the key field: `Get a key from {name}`, opened in the browser. The text and address are drawn as plain values from the server, never as markup |
| A local provider | One line: `Drover's server must be able to reach this address.` |
| A kind with its own wire | No address field. The key field and its hint as the server describes the kind. The kind's name is the connection's name |
| Another compatible server | The address field, as before |

| State | Words |
| --- | --- |
| Loading the list | `Loading the provider list.` Venice and `Another compatible server` can be chosen meanwhile |
| The list could not be read | `Could not read the provider list.` with `Try again`. `Another compatible server` still works |
| A server with no named providers | The two choices of before |

No provider name or id is written in the client; all of it comes from the server.

### States and words

| State | Words |
| --- | --- |
| Reading the kinds | `Asking this server which providers it offers.` in place of the form |
| The kinds could not be read | `EmptyState`: title `Drover could not load the providers`, cause `{failure sentence}`, action `Try again` |
| One kind only | No provider choice. The heading line adds the kind's name: `This server uses {kind}.` |
| A required field is empty and a button is pressed | Under the field: `Enter the base address.` or `Enter the key.` |
| Testing | `Asking the provider. This can take a few seconds.` (polite). Both buttons disabled |
| Test passed | `StatusNote` (`Ok`): `Connected. {n} models found.` With none listed: `Connected. This server lists no models, so you will type a model's name.` |
| Test failed | `StatusNote` (`Danger`): the provider failure sentence from the server (wrong key, wrong address, not reachable, refused by the server's address rule) |
| The server no longer offers this kind | `StatusNote` (`Danger`): `This server no longer offers {kind}. Choose another provider.` |
| Continue with no passing test | Allowed. The connection is saved as not tested |

`Test connection` is `Quiet`, `Continue` is `Primary`. In the Models page's add form `Continue` reads `Add connection`.

## 2. Models: change and remove a connection (#52)

Each connection row on Models gains a `Menu` (`IconButton` with the label `More for {connection name}`), shown to admins and owners only, with `Change name or address` and `Remove`.

### Change

A `Dialog` titled `Change {connection name}`: `Field` `Name`, and `Field` `Base address` when the kind takes one. Buttons `Keep as it is` and `Save` (`Primary`). The key is not here: `Replace key` stays its own action.

| Outcome | Words |
| --- | --- |
| Saved, address changed | the dialog closes. Toast (`Info`): `{connection name} was changed. Test it to read its models again.` The row shows `Not tested` |
| Saved, name only | the dialog closes. Toast (`Success`): `Renamed to {new name}.` |
| Refused (bad address, private address not allowed) | the server's sentence under the field |

### Remove

A `Dialog`:

| Case | Title | Body | Buttons |
| --- | --- | --- | --- |
| No bot uses it | `Remove {connection name}` | `Its stored key is deleted with it. This cannot be undone.` | `Keep it` (focused), `Remove` (`Danger`) |
| Bots use it | `{connection name} is in use` | `{n} bots use this connection. Give them another model first.` With one: `1 bot uses this connection. Give it another model first.` | `Close` |

After a removal, focus goes to the next connection row, or to `Add a connection`.

### A connection row's state

| State | `Badge` |
| --- | --- |
| Ready | `Ready` (`Ok`) |
| Not tested | `Not tested` (`Neutral`) |
| Failed its last test | `Not working` (`Danger`), with the failure sentence under the name |
| Kind switched off by the operator | `Off` (`Neutral`), with `The operator of this server switched {kind} off.` |

A member who is not an admin sees the rows and badges and no menu.

## 3. What a model can do (#531)

On Models, each model row has `Edit what this model can do` in its menu (admins and owners). It opens a `Sheet` titled `{model name}`.

```text
+--------------------------------------+
|  {model name}                    [x] |
|  What Drover knows about this model. |
|  Fill in what the provider did not   |
|  report.                             |
|                                      |
|  Context size (tokens)  |________|   |
|  Longest answer (tokens)|________|   |
|  [x] Calls tools                     |
|  [ ] Reads images                    |
|  [ ] Thinks before answering         |
|      Effort levels  v                |
|  [ ] Answers in a set shape          |
|  [ ] Makes images                    |
|                                      |
|  Price per million tokens            |
|  Input |____| Output |____| Cached |____| Currency |USD| |
|                                      |
|  [ Use the provider's values ] [ Save ]* |
+--------------------------------------+
```

- A value the provider reported shows as the field's value with the caption `From the provider` under it. A value an admin set shows the caption `Set here`.
- `Effort levels` shows only when `Thinks before answering` is on.
- `Use the provider's values` clears everything set here, after a `Dialog`: title `Use the provider's values`, body `What was set here for {model name} is removed.`, buttons `Keep them`, `Remove` (`Danger`).

| Field problem | Words |
| --- | --- |
| A size out of range | `Enter a size between 1 and 10,000,000 tokens.` |
| A price out of range | `Enter a price of zero or more per million tokens.` |
| A currency that is not three letters | `Enter a three letter currency code, such as USD.` |

In the model row, an ability set here carries no extra mark. The row shows what the model can do, wherever that came from.

Number fields open a number keyboard on a phone.

## 4. What runs a bot (#855)

Bot editor, Model section. Shown only when the server offers more than one runtime.

```text
|  Model                                         |
|  Connection   v  {connection}                  |
|  Model        v  {model}                       |
|                                                |
|  Runs on      v  Drover (default)              |
|  {what the chosen runtime changes}             |
```

A `Select` labeled `Runs on`. The server's default runtime is first, shown as `{name} (default)`. Under the select, when the choice is not the default, a `StatusNote` (`Info`) built from the runtime's descriptor:

| From the descriptor | Sentence |
| --- | --- |
| It brings its own tools | `{runtime} brings its own tools: {list}. They run only inside this bot's sandbox.` |
| It needs a sandbox, and the bot has one | (nothing more) |
| It needs a sandbox, and the bot has none | `StatusNote` (`Danger`): `{runtime} needs a sandbox. Give this bot one in the Sandbox section.` with the link `Open the Sandbox section`. Save is refused with the same sentence |
| It lacks an ability the bot uses | `{runtime} cannot {ability}. This bot will not {ability} while it runs on {runtime}.` One line per ability |

The tools in the Tools section and their Off, Ask, Allow settings do not change with the runtime. The note never says a runtime may do more than the grants allow.

| State | Words |
| --- | --- |
| One runtime on the server | No `Runs on` control |
| The runtimes could not be read | The select is disabled with the hint `{failure sentence}` and a `Link` `Try again`. The rest of the section works |
| The bot's runtime is no longer offered | `StatusNote` (`Danger`): `This server no longer offers {runtime}, so this bot cannot run. Choose another.` The select shows no choice until one is made |

In a conversation, the status line above the message field reads `{model} on {runtime}` when the runtime is not the default (`conversation-display.md`). Nothing is added under a reply.

## Layout at each width

- Below 560 the two buttons of the connect form stack, `Continue` first. Price fields stack two per row.
- The sheet in section 3 opens from the bottom below 640 px and from the trailing edge at 640 px and wider.

## Light and dark

Tokens only. Badges use their kinds. The `Ok` note uses `ok`, the `Danger` notes use `danger`, both with their icons. Screenshots of the connect form with two, four and five kinds, each state in the tables, the two dialogs, the sheet, and `Runs on` with each note, dark and light, at 360 px.

## Accessibility

- The provider choice is one group named `Provider`. Arrow keys move inside it, Tab leaves it.
- Focus order in the connect form: provider, address, key, `Test connection`, `Continue`.
- The outcome note is a polite live region for a passing test and assertive for a failing one.
- `Key` is a password field with autofill off. It is never shown again after saving, and the form says so in its first line.
- Each switch in the sheet has its label as its name, and its caption (`From the provider` or `Set here`) as its description.
- The `Runs on` note is tied to the select as its description.

## Settings and defaults

This spec adds one extra: the runtime's name in the status line above the message field, shown as `{model} on {runtime}` when the runtime is not the default. It is part of the `Model` part of the status line and follows its switch: `drover.conversation.status-line.model`, in Settings, Conversation, labeled `Model`, on by default (`conversation-display.md`). It is never drawn under a reply.

Nothing else here is optional.

## Acceptance checks

- [ ] Against a test server with three kinds, the form shows three options with the server's names and draws each kind's fields from its rules.
- [ ] No provider name is a string constant in client source, apart from `ModelConnectionKinds` (`NativeInterfaceRuleTests.No_provider_kind_is_named_in_client_source`).
- [ ] A hint from the server that contains markup or an address is drawn as plain text.
- [ ] Every sentence in the tables appears exactly as written.
- [ ] A member sees no menu, no `Edit what this model can do`, and a disabled `Runs on`.
- [ ] On a server with one runtime, the bot editor's Model section is pixel-for-pixel what it is today.
- [ ] All forms pass the screen checks at 360 px, dark and light.
