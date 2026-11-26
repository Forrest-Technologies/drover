# 17. Extensions and bot files

An extension is one file of data that adds things a space can pick up: a tool server to connect, themes to choose, rules to give a bot, and the extension's own settings. A person reads what the file holds before anything is added, and the file applies nothing ([ADR 0023](../adr/0023-extensions-add-what-a-space-can-pick-up.md)). A bot is shared as a file of its own, from the bot's menu; that is the last section here.

Built and tested: version 2 of the file format and its strict reader; the preview; the import; the list of what is installed with what each holds; removing an extension; a tool server connected from the extension's row with the extension's settings filled in; themes offered to the space's members on the Appearance choices, checked on the server and again on the device; rules offered as starters where a bot's rules are edited; a bot written down as a bot file and imported on the Bots screen.

Not built yet: skills as a kind of part (Drover has no skills yet, [issue 153](https://github.com/Forrest-Technologies/drover/issues/153)); turning an extension off without removing it; updating one in place; pinning a sign-in service and its scopes for a server that is signed in to; a space default theme ([issue 782](https://github.com/Forrest-Technologies/drover/issues/782)).

## The one rule

**An extension never grants or applies anything. A person does.** A file holds data: where a tool server is, what a theme looks like, what a rule says. Importing it changes what is on offer in the space, and nothing that any bot may do or any person sees:

| What the file holds | Who picks it up, and where |
| --- | --- |
| A tool server | An owner or admin connects it from the extension's row. A tool is given to a bot in the bot's settings, through the bot's ordinary grant route. |
| A theme | Each member chooses it for themselves under Appearance. |
| A rule | An owner or admin adds it to a bot under the bot's Rules, through the bot's ordinary rule route. |
| A setting | An owner or admin fills it in when connecting the tool server. |

Nothing in a file runs inside Drover. The one thing an extension leads to that executes is its tool server, outside Drover, behind the tool gate and the grants every tool connection is behind ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md)).

## The file

A JSON document, at most 256 KB.

```json
{
  "version": 2,
  "id": "sample.helpdesk",
  "name": "Sample helpdesk",
  "description": "A sample desk, its look, and a rule for it.",
  "settings": [
    { "key": "region", "label": "Region", "type": "choice", "choices": ["eu", "us"], "default": "eu" },
    { "key": "token", "label": "API key", "type": "secret", "help": "Under Account, then API." }
  ],
  "contents": [
    {
      "kind": "mcp_connection",
      "name": "Helpdesk",
      "url": "https://{region}.tools.example.test/mcp",
      "auth": "header",
      "headerName": "Authorization",
      "headerTemplate": "Bearer {token}",
      "suggestedTools": [
        { "name": "lookup", "why": "Finds a ticket." }
      ]
    },
    { "kind": "theme", "theme": { "format": 1, "id": "sample-dusk", "name": "Sample dusk" } },
    {
      "kind": "rule",
      "name": "Ask before closing a ticket",
      "checkpoint": "before_tool",
      "effect": "ask",
      "conditions": [{ "kind": "tool_is", "tools": ["close"] }]
    }
  ]
}
```

The theme above is cut short to fit: a real one is a whole theme file.

| Field | Meaning |
| --- | --- |
| `version` | The format's version. This server reads version 2 and refuses any other. |
| `id` | Names the extension wherever it is installed: 3 to 100 lower-case letters, digits, dots, and hyphens. A space holds an id at most once. |
| `name`, `description` | What a person sees. Plain text. |
| `settings` | The options a person fills in, at most 20. See "Settings". |
| `contents` | What the extension adds. Each part has a `kind`: `mcp_connection` (at most one), `theme` (at most 8), `rule` (at most 20). |

The types are `ExtensionPackage`, `ExtensionSetting`, `ConnectionRecipe`, `ThemePart`, and `RuleTemplate` in `Drover.Contracts.Extensions`.

### Why the reader is strict

`ExtensionPackages.Read` (in `Drover.Engine`) refuses the whole file for any field it does not know and any part whose kind it does not know. Skipping what is not understood would add something other than what the file says: a file written for a later version, with a part this server ignores, would install as half of itself with nobody told. The refusal is a sentence that names the unknown kind, or where in the file the trouble is. A part of the kind `bot` is refused with where a bot goes: the Bots screen.

Field names are matched exactly, a number must be a number, a part must name its kind, and nesting is limited to 16 levels.

Each kind is also held to the checker its own feature has, so a file cannot bring in what a person could not make by hand:

- A **tool server** to `ToolConnectionService.FindAddressProblem`: http or https, no user name or password, nothing after the path. Its fields are `name`, `url`, `auth` (`none`, `header`, or `oauth`), `headerName` exactly when `auth` is `header`, `headerTemplate`, and `suggestedTools`. There is no field for a client secret, a grant, or how far the server's results are trusted.
- A **theme** to `ThemeGates.CheckFile`, the checker every theme passes ([09-design-direction.md](09-design-direction.md#themes)): the closed theme format, which has no place for an address or a font file, and every contrast and size bound. It is at most 32 KB, and its id may not be one of Drover's own (`ThemeIds.Reserved`). The checker lives in `Drover.Contracts.Themes`, so the server and the device run the same one.
- A **rule** to `RuleValidator`, through `RuleService.FindProblem`: the words of a bot's rule and its limits. A rule has `name`, `checkpoint`, `effect`, and `conditions`, and no field for a bot, for being on, or for a place among other rules.

A new kind is added the same way: a reader that refuses what it does not know, and the checker of the feature it belongs to.

### Settings

A setting is an option of the extension that a person fills in. It has a `key`, a `label`, a `type`, and optionally `help`, `choices` (for a `choice`), and `default`.

| Type | A value is | Kept |
| --- | --- | --- |
| `text` | One line, up to 200 characters | On the extension's row |
| `number` | A number | On the extension's row |
| `choice` | One of its `choices` | On the extension's row |
| `toggle` | `true` or `false` | On the extension's row |
| `secret` | Up to 4096 characters | Never with the extension: it goes into the tool connection's header, in the vault |

A part uses a setting by writing `{key}`. In version 2 the tool server's `url` and `headerTemplate` may. The reader holds settings to four rules, so a setting is never decoration and never a leak:

- Every setting the file declares is used somewhere, and every `{key}` names a declared setting.
- A secret goes only into `headerTemplate`. An address is stored as it is and shown to every member.
- A `headerTemplate` in a file must hold a secret setting, as in `Bearer {token}`. A file never carries the credential itself. With a header and no `headerTemplate`, the person who connects types the whole value.
- A secret has no `default`.

## What a person does

1. **Choose the file**, or paste its text. The server is never given an address to fetch: a file fetched by address can change between two readings, and fetching is a request the server would make on a stranger's word.
2. **Review.** `POST /api/spaces/{spaceId}/extensions/preview` reads the file and answers with what it holds. Nothing is stored and nothing outside the server is contacted. The screen shows the tool server (below), the settings it will ask for, each theme by name and intent, and each rule as the sentence a bot's rule reads as.
3. **Import.** `POST /api/spaces/{spaceId}/extensions` sends the same text again; the server reads it again and keeps it. Nothing is contacted, no bot is given anything, and nobody's look changes. A theme whose id another installed extension already offers refuses the import.
4. **Pick up** what it offers, each in its own place, as the table under "The one rule" says.
5. **Remove.** `DELETE /api/spaces/{spaceId}/extensions/{id}` removes the row and the tool connection made from it, with that connection's tools, credentials, and every bot's grants on them, through `ToolConnectionService.Remove`. Its themes are no longer offered and its rules no longer suggested. A rule already added to a bot is the bot's own and stays. The screen says all of this before the person says yes.

Preview, import, connect, and remove are for an owner or admin. Any member may read `GET /api/spaces/{spaceId}/extensions` and `GET /api/spaces/{spaceId}/themes`.

## A tool server

1. **The review** shows where calls would go (the host, as the largest text in its block), the full address, how Drover signs in, the settings to fill in, and the suggested tools with the reason for each. Where the address holds a setting, the host is shown as the file writes it and the review says the full address is known when the person connects.
2. **Connect.** `POST /api/spaces/{spaceId}/extensions/{id}/connection`, for an owner or admin, with a value for each setting. The address is the file's with the settings filled in; the request has no field for one. `ExtensionConnector` checks each value against its setting's type, fills them in, and holds the result to `FindAddressProblem` again, so a setting cannot make an address a person could not add by hand. It makes the connection through `ToolConnectionService.Create`, the path a connection added by hand takes, so the same address rules, `AllowToolConnections`, vault, and catalog apply, and the server's results are untrusted. It then asks the server for its tools, holding no tenant scope across that wait.
3. **A local address needs a yes.** An address that is not https, is a loopback or private address, has no dot in its name, or sits under a suffix kept for private use (`ExtensionPackages.IsLocal`) is refused unless the request says `acceptLocalAddress`. The check is made on the address with its settings filled in. The screen shows one switch for it, never on for the person. A file must not quietly aim a server at its own network; a person who runs a tool server on their own network says so once.
4. **What is kept.** The values of the settings that are not secret are written on the row, so the row shows where calls go and the form starts with them if the server is connected again. A secret is used once, for the header, and is never kept with the extension or returned.
5. **Grant.** The row lists the suggested tools and says which the server does not list. A tool is given to a bot in the bot's settings, under Tools, where the bots above it still bound it ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md)). The extension's row grants nothing.

The connection is written on `extension_installs.tool_connection_id`. Removing the connection on the Tools screen clears it, and the row offers Connect again.

Where `AllowToolConnections` is off (the hosted preset), the extension imports, the review and the row say the tool server is not connected on this server, and the connect route refuses with the rule's name. Its themes and rules are offered all the same.

A sign-in connection (`oauth`) is made the same way, and each person signs in on the Tools screen, as for one added by hand.

## Themes

`GET /api/spaces/{spaceId}/themes` answers with the theme files of the space's installed extensions, each with the name of the extension it came from. It is read from the kept files (`ExtensionService.ListThemes`): there is no table of themes, so removing an extension takes its themes with it.

The device does not take the server's check on trust. `ThemeModel.Offer` runs `ThemeGates.CheckFile` on each file and offers only those that pass and take none of Drover's own ids. The Appearance choices list them under "From this space's extensions", each with its extension's name. Choosing one is the same choice as any theme: it applies at once, is kept on the device, and follows the person's account ([18-native-client.md](18-native-client.md)).

- The file of the theme in use is kept on the device (`drover.theme.file`), so the next start draws it before the server answers. It is checked again when it is read back.
- Once the space has answered, a theme it no longer offers is no longer drawn: the app goes back to the default look, and the person's stored choice is left as it is.
- A theme is offered in the space that imported its extension. A person in another space sees the default look there.
- What the space offers is read when a person signs in, before their account's choice is applied, and again each time the Appearance choices open.

No theme can change an invariant of the design, because the theme format has no way to say one ([09-design-direction.md](09-design-direction.md#the-invariants)).

## Rules

The rules of the space's installed extensions come with `GET /api/spaces/{spaceId}/extensions`, each as the body a bot's rule is added with. The bot editor lists them among the starters under Rules, named for the extension (`RuleStarters.From`), after the starters Drover has. Adding one sends it to `POST .../bots/{botId}/rules`, the route a rule written by hand takes, so `RuleService.Create` applies every limit, including whether this server takes a written pattern (`AllowWrittenRulePatterns`). The rule is then the bot's own: it can be changed, switched off, or removed there, and it stays when the extension is removed.

## What is kept

One row in `extension_installs` ([02-data-model.md](02-data-model.md)): the extension's id, its name and description, the SHA-256 of the file's text, the file itself, the tool connection made from it once there is one, the values of its settings that are not secret, who imported it, and when. What it offers is read from the kept file each time. Import, connect, and remove are written to the audit log as `extension.imported`, `extension.connected`, and `extension.removed`.

A row imported under version 1 of the format, when a file could hold a bot, is read as what is left of it (`ExtensionPackages.ReadKept`): its bot became an ordinary bot of the space when it was imported, so the part is dropped. A row with nothing left says it holds nothing and can be removed.

## Deployment policy

Nothing about importing an extension differs between a self-hosted server and the hosted preset, so there is no rule for it. The size limits are the same constants everywhere. A tool server in a file follows `AllowToolConnections`, a rule added to a bot follows `AllowWrittenRulePatterns`, and a theme is data held to the bounds every theme is held to.

## Bot files

A bot is shared as a file of its own. It is not an extension: sharing a bot is something a person does to a bot.

```json
{
  "version": 1,
  "name": "Sample scribe",
  "description": "Takes notes.",
  "instructions": "Keep notes short.",
  "suggestedModel": "sample-model"
}
```

Those are all of its fields (`BotFile` in `Drover.Contracts.Bots`). A bot's sandbox, limits, schedules, memory settings, picture, rules, and grants are not part of the format, so a file cannot set them. `BotFileReader.Read` is as strict as the extension reader, and tells an extension file apart to say where it goes.

- **Save as a file.** The bot's menu in its settings asks `GET /api/spaces/{spaceId}/bots/{botId}/export` for the bot as a file and hands it to the device to save as `{handle}.bot.json`: the system's save dialog on a device, a download in a browser.
- **Import a bot.** The Bots screen takes a file or pasted text. `POST /api/spaces/{spaceId}/bots/import/preview` answers with the bot's name, what it is for, its model, and its instructions in full. `POST /api/spaces/{spaceId}/bots/import` sends the same text again and makes the bot through `BotService.Create`, the path a person's own new bot takes, so every limit on making a bot applies ([16-deployment-policies.md](16-deployment-policies.md), `MaxBotsPerSpace`). The bot's settings then open.
- **Paused.** The bot starts with the tools every new bot starts with, and paused: it starts no run and uses no tool until a person resumes it.
- **Instructions are a stranger's text.** The review shows every character of them. A character that does not show on a screen (a zero-width space, a direction override, a tag character, a control character other than a line break or tab) is written out where it stands, as `[U+200B]`, and the screen says how many there are. The bot is given the text as the file has it; the marks are only in what the person reads.
- **The model.** The server chooses it, the same way for the preview and the import, so the review names what the bot will get: the space's connection that last answered, and on it the file's `suggestedModel` when that connection lists it, then the connection's own default, then the first model it lists. A space with no usable model imports nothing and is told to connect one.

All three routes are for an owner or admin.
