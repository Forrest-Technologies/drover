# 0016. Projects group conversations and share instructions and files

- Status: Accepted
- Date: 2026-11-19
- Source: [Issue 297](https://github.com/Forrest-Technologies/drover/issues/297)

## Context

A space can hold bots and conversations, but it has no smaller work area that keeps related conversations,
instructions, and files together. The work area must allow different bots and models while preserving the space as
the tenancy boundary.

## Decision

A project belongs to exactly one space. It has a name, shared instructions, shared files, and conversations. A
conversation may belong to one project. Moving a conversation into or out of a project does not change its owner,
audience, bot, history, or model choice.

A project does not belong to a bot. Any bot in the same space can run in a conversation in the project. The project does
not grant tools, widen bot authority, share a sandbox, or change model settings. Project instructions join the server's
instruction assembly for each run in the project. The bot's instructions remain their own source.

Projects, their instructions, and their files are space-owned data. Existing space row-level security remains the
tenancy boundary. Conversation audience policies continue to protect private conversation content inside a shared
space. Every foreign key between project data and other space-owned data includes `space_id`.

Every member of a space can see its projects and use one for a conversation. Space owners and admins create, rename,
and remove projects and change their shared instructions and files. A conversation's owner decides whether that
conversation belongs to a project. These rules follow the existing split between managing shared bot context and
chatting in a person's own conversations.

A project file reaches a run through the same explicit file grant path as a file attached to a message. The server
checks the project, conversation, bot, and file in the run's tenant transaction before making the file available. A
project file does not become a permanent bot file and is not copied into every bot sandbox. File content remains
untrusted input and never becomes instructions by being stored in a project.

## Alternatives considered

- One project per bot. This prevents a project from using different bots and models across its conversations.
- Folders only. A folder can group conversations but cannot supply one checked source of shared instructions and files
  to every run.

## Consequences

- A project can gather work across several conversations and bots without adding another tenancy or grant boundary.
- A private conversation stays private when it joins a project. Project membership does not make its messages visible to
  other space members.
- Project metadata, instructions, and files are visible to every member of the space. Only owners and admins can change
  that shared run context.
- Run loading must combine project instructions and file grants with the conversation and bot state in one tenant scope.
- Project file storage counts toward the existing space storage total and follows the existing file limits and deletion
  rules.
- Removing a project needs an explicit policy for its conversations and files. The implementation must preserve
  conversations unless the person separately removes them.

## Reopen when

Projects need membership that differs from their space, a conversation must belong to several projects, or a project
needs to grant tools or sandbox access independently of a bot.
