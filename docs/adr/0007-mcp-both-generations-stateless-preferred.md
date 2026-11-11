# 0007. Support both MCP protocol generations and prefer stateless

- Status: Accepted
- Date: 2026-11-11
- Source: [06-mcp.md](../architecture/06-mcp.md)

## Context

MCP's 2026-07-28 revision is stateless; most deployed servers still use the session-based handshake.

## Decision

Connections negotiate automatically: stateless first, then the handshake, then the older transport. The negotiated mode is stored and shown. A connection can be pinned to either generation. Stateless clients are created per use; session-based clients are cached per connection and credential.

## Alternatives considered

- Stateless only. Rejected: would refuse most servers today.
- Session-based only. Rejected: holds connections and state the newer protocol removes.

## Consequences

Tool-list refresh has two paths: time-to-live for stateless, notifications for sessions.

## Reopen when

The older generation is formally retired.
