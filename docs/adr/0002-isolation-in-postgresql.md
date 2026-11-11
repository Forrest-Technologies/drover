# 0002. Isolate users and groups with PostgreSQL row-level security

- Status: Accepted
- Date: 2026-11-11
- Source: [04-tenancy-and-permissions.md](../architecture/04-tenancy-and-permissions.md)

## Context

The owner will host Drover for other people. One user must never read another's data even when application code has a bug.

## Decision

Every user has a personal space. A shared space is a group. Every tenant table carries `space_id`; content tables also carry an audience (one user, or everyone in the space) that children inherit through a composite foreign key. The app connects as a role that cannot bypass row-level security. Identity is set per transaction with `set_config(..., true)` by an EF Core interceptor. Cross-space work goes through named, audited functions listed in a manifest. A catalog-driven test suite fails the build for any table without a policy.

## Alternatives considered

- Application-level filters. Rejected: a forgotten filter is a leak.
- A database or schema per tenant. Rejected: operational cost, and it does not separate users inside a group.

## Consequences

Tenant bugs fail closed. Admins of a shared space cannot read a member's private thread. Every table needs a seeder in the isolation suite.

## Reopen when

A compliance need for admin review of private content; add an explicit audited grant, never a bypass.
