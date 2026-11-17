# 17. Extensions

An extension is one file that adds something to a space. Today that is a bot. A person reads what the file holds before anything is added, and the file can grant nothing.

Built and tested: the file format and its strict reader; the preview; the import of a bot, paused; the list of what is installed; the screen at `/extensions`; writing a bot down as an extension; the editor at `/extensions/edit`, a form with the file's JSON one tab away.

Not built yet, tracked in the extensions issue: a tool connection as a second kind of content, with its address, sign-in service, and scopes pinned; turning an extension off, removing it, and updating it; saving the file from the native app (there the editor copies the text or imports it).

## The one rule

**An extension never grants anything. A person does.** A file adds a bot's words and nothing a bot is allowed to do. Its bot starts with the tools every new bot starts with ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md)), which a file cannot change, and it starts paused.

## The file

A JSON document, at most 256 KB.

```json
{
  "version": 1,
  "id": "sample.note-taker",
  "name": "Sample note taker",
  "description": "Keeps notes.",
  "contents": [
    {
      "kind": "bot",
      "name": "Sample scribe",
      "description": "Takes notes.",
      "instructions": "Keep notes short.",
      "suggestedModel": "sample-model"
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `version` | The format's version. This server reads version 1 and refuses any other. |
| `id` | Names the extension wherever it is installed: 3 to 100 lower-case letters, digits, dots, and hyphens. A space holds an id at most once. |
| `name`, `description` | What a person sees. Plain text. |
| `contents` | What the extension adds. Each part has a `kind`. Version 1 knows `bot`, at most one. |

A `bot` part has `name`, `instructions`, and optionally `description` (what other bots read to decide whether to hand it a task) and `suggestedModel`. Those are all of its fields. A bot's sandbox, limits, schedules, memory settings, picture, and grants are not part of the format, so a file cannot set them.

The types are `ExtensionPackage` and `BotTemplate` in `Drover.Contracts.Extensions`.

### Why the reader is strict

`ExtensionPackages.Read` (in `Drover.Engine`) refuses the whole file for any field it does not know and any part whose kind it does not know. Skipping what is not understood would add something other than what the file says: a file written for a later version, with a part this server ignores, would install as half of itself with nobody told. The refusal is a sentence that names the unknown kind, or where in the file the trouble is.

Field names are matched exactly, a number must be a number, a part must name its kind, and nesting is limited to 16 levels.

## What a person does

1. **Choose the file**, or paste its text. The server is never given an address to fetch: a file fetched by address can change between two readings, and fetching is a request the server would make on a stranger's word.
2. **Review.** `POST /api/spaces/{spaceId}/extensions/preview` reads the file and answers with what it would add. Nothing is stored and nothing outside the server is contacted. The screen shows the bot's name, what it is for, the model it will think with, that it gets only the tools any new bot gets, and its instructions in full.
3. **Import.** `POST /api/spaces/{spaceId}/extensions` sends the same text again; the server reads it again and makes the bot through `BotService.Create`, the path a person's own new bot takes, so every limit on making a bot applies ([16-deployment-policies.md](16-deployment-policies.md), `MaxBotsPerSpace`). The review has nothing to fill in: there is one decision, yes or no.
4. **Turn on.** The bot is paused until a person resumes it, with the bot's ordinary resume. Until then it starts no run and uses no tool.

All three routes are for an owner or admin, because importing makes a bot. Any member may read `GET /api/spaces/{spaceId}/extensions`.

### Instructions are a stranger's text

Imported instructions become what a bot is told, so the review shows every character of them. A character that does not show on a screen (a zero-width space, a direction override, a tag character, a control character other than a line break or tab) is written out where it stands, as `[U+200B]`, and the screen says how many there are. The bot is given the text as the file has it; the marks are only in what the person reads.

### The model

The server chooses the model, the same way for the preview and the import, so the review names what the bot will get. The connection is the space's connection that last answered. The model is the file's `suggestedModel` when that connection lists it, then the connection's own default, then the first model it lists. A suggestion is advice: it never puts a bot on a model the space does not have. The person changes the model in the bot's settings, like any bot's, before or after turning it on. A space with no usable model imports nothing and is told to connect one.

## Making one

The editor at `/extensions/edit` writes a file. Its job is to write a bot down so someone else can import it, and hand over the file.

- **The form comes first**: the extension's name, id, and what it is for, then the bot's name, what it is for, its instructions, and a suggested model. The id follows the name (`ExtensionFile.IdFrom`) until the person types one.
- **The JSON tab shows the same file as text** and lets a person edit it. `ExtensionFile.Write`, in `Drover.Contracts`, writes the text, so the editor and the server write the same thing. Going back to the form sends the text to `POST /api/spaces/{spaceId}/extensions/read`, which reads it with the reader an import uses and answers with the package. Text it cannot read stays on the JSON tab with the sentence that says why. The editor therefore cannot produce a file the importer would refuse without saying so, and there is one reader, on the server.
- **Starting from a bot**: a bot's settings open the editor on `GET /api/spaces/{spaceId}/extensions/from-bot/{botId}`, which answers with the bot as a package: its name, what it is for, its instructions, and its model as a suggestion. Nothing else of a bot has a field to be in, so its grants, sandbox, limits, schedules, and notes cannot leave with it.
- **What is done with the file**: checked (the server reads it and says whether it would import), copied as text, downloaded in a browser, or imported into this space, which hands the text to the import above so it is reviewed like any other file.

Both routes are for an owner or admin. The editor is one shared component, so it is the same on web, desktop, and phone. A browser saves the file through a link; the native app has no such link yet and offers Copy and Import.

## What is kept

One row in `extension_installs` ([02-data-model.md](02-data-model.md)): the extension's id, its name and description, the SHA-256 of the file's text, the file itself, the bot it added, who imported it, and when. The import is written to the audit log as `extension.imported`.

The bot belongs to the space from the moment it is made. Editing it changes the bot and not the row, and removing the bot leaves the row, which then says its bot was removed.

## Deployment policy

Nothing about importing a bot differs between a self-hosted server and the hosted preset, so there is no rule for it. The size limit is the same constant everywhere, and the bot count is already bounded by `MaxBotsPerSpace`. A tool connection as content will follow `AllowToolConnections` when it is built.
