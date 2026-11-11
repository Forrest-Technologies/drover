# 0001. Build on Microsoft Agent Framework fully, inside one project

- Status: Accepted
- Date: 2026-11-11
- Source: [14-agent-framework.md](../architecture/14-agent-framework.md)

## Context

Drover needs an agent loop, sessions, compaction, memory injection, skills, multi-agent orchestration, checkpoints, and approval. The framework has all of them, ships weekly with breaking changes, and marks several as experimental. The owner chose to use it as fully as possible, previews included, and to stay current.

## Decision

`Drover.Agents` is built on the framework and holds every framework type. Drover implements the framework's storage abstractions on PostgreSQL and governs through its middleware and agent wrappers. Conversation content is stored in Drover's own schema; framework sessions and checkpoints are opaque, versioned, disposable working state. The conformance suite gates every update and a weekly canary tests the newest prerelease.

## Alternatives considered

- Use the framework for single turns only and own the loop state. Rejected by the owner: it leaves most of the framework unused.
- Write the loop on the chat client abstraction with no framework. Rejected: re-implements what exists.

## Consequences

Drover gets new framework capabilities cheaply. It also absorbs weekly churn, in one project. An in-flight run may restart its current step after an update that changes a blob format.

## Reopen when

A framework feature Drover depends on is removed with no replacement, or the weekly cost of updates exceeds the value.
