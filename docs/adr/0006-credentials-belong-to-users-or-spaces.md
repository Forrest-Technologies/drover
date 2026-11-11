# 0006. Tool and model credentials belong to a user or a space, never to a bot

- Status: Accepted
- Date: 2026-11-11
- Source: [06-mcp.md](../architecture/06-mcp.md)

## Context

A bot acts for a person. It must not be able to use another person's sign-in.

## Decision

A connection's definition belongs to the space. Its credential is shared by the space or private to one user. A run carries the user it acts for, its database transactions carry that user, and the database will not return another user's credential row. There is no fallback from a missing personal credential to someone else's.

## Alternatives considered

- Per-bot credentials. Rejected: breaks the narrowing rule and leaves "acting as whom" unanswered.

## Consequences

A bot that needs its own service account gets a separate connection with a shared credential, granted to that bot.

## Reopen when

A need for bot identities that this cannot express.
