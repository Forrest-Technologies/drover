# 0011. Repositories store and retrieve; services hold business logic

- Status: Accepted
- Date: 2026-11-11
- Source: [01-solution-layout.md](../architecture/01-solution-layout.md)

## Context

The first draft had services use the database context directly, on the principle of not wrapping the platform. The owner's usual layering is a repository layer for data and a service layer for logic, and asked whether it fits.

## Decision

It does, for a Drover-specific reason: every query runs under row-level security, and keeping every query in one project makes that set small and reviewable. Repositories live in `Drover.Data`, one concrete class per aggregate. Services live in `Drover.Engine` and call them. The database context, `DbSet`, and `IQueryable` never leave `Drover.Data`; an analyzer enforces it. The service opens the tenant scope; repositories share it.

## Alternatives considered

- Services use the context directly. Rejected: queries spread across the engine, written by many agents, each one a place to get tenancy or performance wrong.
- A generic repository. Rejected: hides what EF Core can do and adds nothing.
- Repository interfaces with mocks. Rejected for now: one implementation, and a mock cannot fail the way a policy does. Services are tested against real PostgreSQL.

## Consequences

One more class per aggregate. Engine code contains no query syntax.

## Reopen when

A second storage implementation becomes real, or service tests against PostgreSQL become too slow to run on every change; then add interfaces.
