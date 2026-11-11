# 0010. GitHub Issues are the source of truth for work

- Status: Accepted
- Date: 2026-11-11
- Source: [ROADMAP.md](../../ROADMAP.md)

## Context

Most code is written by coding agents. Plans kept in Markdown drift from what was done.

## Decision

Each unit of work is an issue written as a prompt: context, goal, scope, out of scope, acceptance criteria, verification. Phases are tracking issues with sub-issues, ordered with blocked-by. `ROADMAP.md` gives direction and links to tracking issues and never holds status. Decisions are ADRs. One issue, one branch, one pull request.

## Alternatives considered

- Task files in the repository. Rejected: status in Markdown drifts, and issues can be scripted and assigned.
- A project board. Deferred: milestones and sub-issue progress are enough for one maintainer.

## Consequences

The plan in `docs/architecture` describes the design and order; live state is in issues.

## Reopen when

Several maintainers need a shared board, or the repository goes public and needs a public roadmap view.
