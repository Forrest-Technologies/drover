# 0004. A run is a durable job with a gapless event log

- Status: Accepted
- Date: 2026-11-11
- Source: [03-run-lifecycle.md](../architecture/03-run-lifecycle.md)

## Context

A bot's work must continue when the phone locks, the tab closes, or the server restarts, and clients must be able to catch up exactly.

## Decision

A run is a row claimed by a worker under a lease. Every run in a delegation tree shares a root. Events are numbered per root under a row lock so sequence order equals commit order. Clients resume with the last number they saw. A tool call that was in flight at a crash is retried only if its definition says it is safe; otherwise the model is told the outcome is unknown.

## Alternatives considered

- A separate job system. Rejected: runs already need leases, cancel, and tenancy; a second system duplicates them.
- Per-run sequences merged on read. Deferred: more protocol for a contention problem not yet observed.

## Consequences

Restart recovery needs nothing in memory. Event appends in one tree serialize on one row.

## Reopen when

A load test shows lock waits with wide teams.
