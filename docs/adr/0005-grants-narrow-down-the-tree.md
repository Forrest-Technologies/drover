# 0005. Tool grants are per bot and per tool, narrow down the bot tree, and are enforced outside the model

- Status: Accepted
- Date: 2026-11-11
- Source: [04-tenancy-and-permissions.md](../architecture/04-tenancy-and-permissions.md)

## Context

Bots can create and direct other bots. A child must never exceed its parent, and a prompt injection must not be able to widen what a bot can do.

## Decision

A grant is allow, ask, or deny; no grant means the tool is invisible. A bot's effective grant is the minimum along its path to the root, computed at call time. A bot has authority over its descendants only. Every tool call passes the tool gate, which is framework middleware. Delegation goes down the tree only.

## Alternatives considered

- Per-server grants. Rejected by the brief.
- Checking grants only when tools are listed. Rejected: grants can change mid-run.

## Consequences

Revoking from a parent takes effect on the whole subtree at once. Sideways delegation waits for explicit links.

## Reopen when

Daily use keeps needing sideways handoffs; add delegation links with the rule that borrowed authority asks.
