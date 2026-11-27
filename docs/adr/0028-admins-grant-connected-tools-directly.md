# 0028. Space admins can grant connected tools directly

- Status: Accepted
- Date: 2026-11-27
- Source: [Issue #1094](https://github.com/Forrest-Technologies/drover/issues/1094)
- Amends: [ADR 0005](0005-grants-narrow-down-the-tree.md)

## Context

A specialist bot can need a connected tool that its parent has no reason to use.

## Decision

The admin-only bot editor grants connected tools directly to the selected bot. Effective access is the minimum from the calling bot through the nearest direct grant for that tool, inclusive. Without a direct grant, the full path to the root applies. Built-in tools and existing grants keep their inheritance rules. Bots cannot issue direct grants. A review still lowers an applicable grant to Ask.

## Consequences

The parent and siblings receive no grant. A descendant with an inherited grant remains subject to the closest direct grant. Removing the direct grant recalculates access through the remaining path. Delegation and bot messaging still hand work to a separate bot, which uses its own grants when it accepts requests from other bots.
