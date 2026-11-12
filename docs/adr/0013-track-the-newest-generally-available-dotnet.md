# 0013. Track the newest generally available .NET once MAUI supports it

- Status: Accepted
- Date: 2026-11-12
- Source: pull request "build: move to .NET 11"

## Context

Dependabot moved the container images to .NET 11 while the code, `global.json` and the Roslyn pin stayed on .NET 10, so `main` went red. The earlier text said Drover targets `net10.0` and stays on the LTS line. The owner's policy is different: use the newest generally available .NET as soon as MAUI supports it, and previews for AI libraries. .NET 11 and MAUI 11 are generally available.

## Decision

Drover targets the newest generally available .NET, and moves when MAUI supports it. Today that is `net11.0`, SDK 11.0.100. ASP.NET Core, `Microsoft.Extensions.*` (except the AI libraries), the test hosts and the container images follow it.

Previews are allowed for the AI libraries only: the agent framework, `Microsoft.Extensions.AI` and the MCP SDK.

EF Core and its Npgsql provider move together, and only when the provider is generally available. EF Core 10 and Npgsql 10 run on `net11.0` until the provider for 11 is released. Previews are never used in the data layer.

This supersedes the statements that Drover targets `net10.0` and stays on the .NET 10 LTS line.

## Alternatives considered

- Stay on .NET 10 LTS. Rejected by the owner: the platform and MAUI are ready, and a lagging target costs more later.
- Move EF Core and Npgsql to 11 release candidates. Rejected: the data layer carries tenant isolation and does not take previews.

## Consequences

The build needs SDK 11. Dependabot ignores major bumps of `Microsoft.EntityFrameworkCore*` and `Npgsql*` and takes the rest. `Drover.Analyzers` references Roslyn 5.9.0 and so needs a compiler of 5.9 or newer.

## Reopen when

A .NET release breaks a package Drover depends on with no fix, or the Npgsql EF Core provider for the next major is generally available.
