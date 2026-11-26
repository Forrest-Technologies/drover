# 0023. An extension adds what a space can pick up; a bot is shared as its own file

- Status: Proposed in its pull request
- Date: 2026-11-26
- Source: the owner's ask of 2026-11-26: "extensions was supposed to be an extensibility layer, not a glorified bot and connection import tool, a way to customize and add tools, settings, themes, options. Bot import export belongs in bot, not extensions." Replaces the definition in [issue 241](https://github.com/Forrest-Technologies/drover/issues/241)
- Owner decisions, the same day: an extension may hold tool servers, themes, its own settings, and skills and rules, with more kinds to come; it holds data and never code that Drover runs

## Context

The first version of the extension file held a bot, a tool server, or both, so the extensions screen was a second way
to make a bot and a second way to add a tool connection. Nothing in it extended Drover. Sharing a bot, which is
something a person does to a bot, lived on another screen from the bot.

## Decision

- An **extension** is one JSON file of data that adds things a space can pick up. Version 2 of the format has three
  kinds of part and one list of settings: a **tool server** to connect, **themes** for members to choose, **rules**
  to add to a bot, and the extension's own **settings**, which a person fills in and its tool server uses. The
  detail is in [17-extensions.md](../architecture/17-extensions.md).
- **An extension never applies anything.** A person connects its tool server and gives a tool to a bot, each member
  chooses a theme for themselves, and a person adds a rule to a bot. Importing changes what is on offer and nothing
  that any bot may do or any person sees.
- **Data only.** Nothing in a file runs inside Drover. The one thing an extension leads to that executes is a tool
  server, outside Drover, behind the tool gate and the grants every tool connection is behind.
- Each kind is held to the checker its own feature has: a tool server to the rules of a connection added by hand, a
  theme to the theme gates ([ADR 0020](0020-a-family-of-themes.md)), a rule to the validator of a bot's rule. A new
  kind is added the same way: a reader that refuses what it does not know, and the feature's own checker.
- **A bot is shared as a bot file**, written from the bot's own menu and imported on the Bots screen, with the
  review the extension import had: every character of its instructions, and paused until a person turns it on. A bot
  is no longer a kind of extension part, and a file that holds one is refused with where a bot goes.
- The theme checker moves to `Drover.Contracts` so the server and the device run the same one, which
  [issue 778](https://github.com/Forrest-Technologies/drover/issues/778) asked for.

## Alternatives considered

- Keep the bot as one more kind beside the new ones. Lost: it is what made the screen an import tool, and a bot's
  file belongs with the bot.
- Let an extension carry code, run in a bot's sandbox. Lost for now on the owner's decision. It would need a trust
  model for code and a deployment rule for a shared server, and it is what skills
  ([issue 153](https://github.com/Forrest-Technologies/drover/issues/153)) will decide.
- Apply what a file holds on import: grant its suggested tools, add its rules to every bot, make its theme the
  space's look. Lost: a stranger's file would then decide what a bot may do or what a person sees.
- A form editor for the new format. Lost: the old editor's job was to write a bot down, which a bot file now does in
  one step, and a form for a theme's two hundred values is the theme editor of
  [issue 781](https://github.com/Forrest-Technologies/drover/issues/781), not a part of this.

## Consequences

- The extensions screen has one job: bring in a file after reading what it holds, set up its tool server, and take
  it out again. The editor at `/extensions/edit` is gone, with its routes `read` and `from-bot`.
- Themes a space offers come from its extensions, so the space list of
  [issue 782](https://github.com/Forrest-Technologies/drover/issues/782) has a first source. A space default theme
  is still that issue's.
- A file written for version 1 is refused. A file a space imported under version 1 is read as what is left of it
  without its bot, which became an ordinary bot of the space when it was imported.
- **Skills are not a kind yet.** Drover has no skills: no table, no source for the agent framework, no way to give
  one to a bot. A file with a `skill` part is refused by name until issue 153 builds them, and that issue adds the
  kind.
- Not built with this decision: turning an extension off without removing it, and updating one in place.

## Reopen when

Skills exist and want to carry code, or an owner wants an extension to change something for a whole space at once
(a default theme, a rule every bot follows): each is a way for a file to apply something, which this decision rules
out.
