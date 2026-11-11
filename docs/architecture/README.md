# Drover architecture

Status: proposal for owner review. Written 2026-11-11. Facts marked "verified" were checked against primary sources on that date; see [Verified facts](#verified-facts).

This document answers the architecture handoff. It is split into one file per hand-back item so a coding agent can load only the part it needs.

| # | File | Hand-back item |
| --- | --- | --- |
| 1 | [01-solution-layout.md](01-solution-layout.md) | Solution and project layout, dependency direction |
| 2 | [02-data-model.md](02-data-model.md) | Core data model |
| 3 | [03-run-lifecycle.md](03-run-lifecycle.md) | Run lifecycle, streaming, resume, cancel, delegation, restart |
| 4 | [04-tenancy-and-permissions.md](04-tenancy-and-permissions.md) | Row-level security, isolation tests, bot permission model |
| 5 | [05-dependencies.md](05-dependencies.md) | Every dependency: wrapped or direct, license, standing |
| 6 | [06-mcp.md](06-mcp.md) | MCP connections for OAuth, header, and no auth |
| 7 | [07-sandbox.md](07-sandbox.md) | Sandbox interface, E2B adapter, sharing enforcement |
| 8 | [08-auth-and-secrets.md](08-auth-and-secrets.md) | Sign-in, sessions, secret handling |
| 9 | [09-design-direction.md](09-design-direction.md) | Tokens, key screens, motion rules, component approach |
| 10 | [10-tooling.md](10-tooling.md) | `AGENTS.md`, `.editorconfig`, analyzers, CI workflows, scanners |
| 11 | [11-phase-0-plan.md](11-phase-0-plan.md) | Ordered Phase 0 build plan |
| 12 | [12-risks.md](12-risks.md) | Risks and least-sure decisions |
| 13 | [13-deployment-and-push.md](13-deployment-and-push.md) | Compose deployment, first run, push notifications |
| 14 | [14-agent-framework.md](14-agent-framework.md) | What Drover uses from the agent framework, and how it stays current |
| 15 | [15-model-providers.md](15-model-providers.md) | Model connections: Venice first-party, compatible endpoints, subscription sign-in |
| 16 | [16-deployment-policies.md](16-deployment-policies.md) | Deployment policy: self-hosted by default, a hosted preset that locks the server into compliance |

Files that already sit at their final paths: [`/AGENTS.md`](../../AGENTS.md), [`/CODE_STYLE.md`](../../CODE_STYLE.md), [`/.editorconfig`](../../.editorconfig), [`/BannedSymbols.txt`](../../BannedSymbols.txt), and the CI and security workflows under `.github/`.

## The shape in one page

```
 Web (Blazor WASM, PWA)      Android / Windows (MAUI Blazor Hybrid)      [iOS / macOS: switched off]
          \                              |                                   /
           +------------ Drover.Ui (Razor components) + Drover.Client -------+
                                         |
                         HTTPS: JSON API + Server-Sent Events
                                         |
 +-------------------------------- Drover.Server ---------------------------------+
 |  API endpoints | SSE streams | auth | run workers | scheduler | status         |
 |                              Drover.Engine                                     |
 |   run loop, tool gate, grants, hierarchy, delegation, MCP connections          |
 |        |              |               |              |            |            |
 |  IAgentRuntime   IToolSource   ISandboxProvider  IPushChannel  ISecretVault ...|
 +--------|--------------|---------------|--------------|------------|------------+
   Agent Framework    MCP SDK          E2B          Web Push    Data Protection
   + Extensions.AI
                                         |
                              PostgreSQL (row-level security)
```

Five decisions carry most of the weight:

1. **The agent framework runs the agent; Drover governs it.** Drover builds on Microsoft Agent Framework fully: sessions, history, compaction, context providers, skills, workflows, orchestration, approval. Drover adds what the framework does not have: tenant isolation, per-bot grants down a hierarchy, the run as a durable job, and the event log clients resume from. All framework code sits in one project, and conversation content stays in Drover's own schema, so a weekly framework update cannot reach the rest of the code or the data.
2. **One data path for every client.** The web UI is Blazor WebAssembly with global interactivity, so web and the MAUI shell both talk to the server through the same typed client over JSON and SSE. No screen has a server-only code path. The official template imposes global interactivity for the same reason (verified).
3. **Isolation between users lives in PostgreSQL.** Every user has a personal space. A shared space is a group, and inside it a row belongs to one user or to the group. The app role cannot bypass row-level security, identity is set per transaction, and a missing setting returns zero rows. A catalog-driven test fails the build when a table has no policy.
4. **Permissions are enforced outside the model.** A tool with no grant is never sent to the model. Grants narrow down the bot tree and are resolved at call time, so a prompt injection can only reach what the bot was already allowed to do.
5. **Stateless first.** No UI circuits on the server, MCP's stateless protocol preferred, short database transactions, runs resumable from stored state. Any server instance can pick up any work after a restart.

## Changes proposed to settled decisions

None are blocking. Four need the owner's eye:

| Decision in the brief | Finding | Proposal |
| --- | --- | --- |
| MCP OAuth "just works" | The official C# SDK's OAuth client awaits one in-process callback and has no begin/complete split (verified in source at 2.4.0). A pending sign-in is lost on restart and needs routing help across instances. | Use the SDK with a small bridge now; see [06-mcp.md](06-mcp.md). Accept "click connect again after a restart". |
| E2B first sandbox adapter | No official .NET SDK exists. The one community package is small. | Drover owns a thin client in the adapter project, generated from E2B's published OpenAPI and proto files. See [07-sandbox.md](07-sandbox.md). |
| Phase split | Phase 0 ships an "ask" grant effect but puts approval prompts in Phase 2. | Move the plain approval prompt into Phase 0. See [11-phase-0-plan.md](11-phase-0-plan.md). |
| Android push through UnifiedPush | No .NET or MAUI binding exists for the Android connector. No better open option was found. | Keep UnifiedPush, implement its small protocol directly in C#, and add a zero-setup fallback that needs no account. One Web Push adapter on the server covers all of it. |

## Owner decisions recorded 2026-11-11

| Topic | Decision | Where |
| --- | --- | --- |
| Agent framework | Use it as fully as possible, adopt preview packages, stay on the latest release | [14-agent-framework.md](14-agent-framework.md) |
| "BYOS" | Bring your own subscription. Supported wherever the provider permits it, including ChatGPT plan sign-in for self-hosted servers | [15-model-providers.md](15-model-providers.md) |
| Venice | A first-party provider from Phase 0 | [15-model-providers.md](15-model-providers.md) |
| MCP | Support both protocol generations, prefer stateless | [06-mcp.md](06-mcp.md) |
| Row-level security | Enforced between users as the normal case; in a shared space, by group unless a row is private to a user | [04-tenancy-and-permissions.md](04-tenancy-and-permissions.md) |
| Android push | Stay with the open standard unless something better exists; one server adapter | [13-deployment-and-push.md](13-deployment-and-push.md) |
| E2B without an official .NET SDK | Accepted | [07-sandbox.md](07-sandbox.md) |

Subscription sign-in by provider, as verified:

| Provider | Third-party use of a consumer plan | Drover |
| --- | --- | --- |
| OpenAI | Permitted for open source and locally hosted apps, including a user's own remote machine; a paid or remotely hosted app needs separate admission | Sign-in for self-hosted servers; key-only on the hosted service until admitted |
| OpenRouter | Permitted, OAuth issues a user-owned key | Sign-in supported |
| Hugging Face | Permitted, OAuth with an inference scope | Sign-in supported |
| GitHub Copilot | Permitted through its SDK with a GitHub OAuth app, including hosted apps | Sign-in supported |
| Venice | Key only; no sign-in exists | First-party key connection |
| Anthropic, Google | Prohibited for third-party apps | Key-only |
| Others | No public mechanism or unclear | Key-only |

## Verified facts

| Topic | Finding (2026-11-11) |
| --- | --- |
| .NET | .NET 10 is the LTS, supported to 2028-11-14. .NET 11 (STS) shipped 2026-11-10. Drover targets `net10.0`. |
| EF Core on PostgreSQL | EF Core 10.0.x with `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3. The 11.0 provider is still a release candidate. |
| `.slnx` | Default for `dotnet new sln` in the .NET 10 SDK. Supported by Visual Studio 17.14, C# Dev Kit, CodeQL (CLI 2.24.0), and Dependabot discovery. Tested here with `dotnet build` and `dotnet format` on SDK 10.0.115. |
| Microsoft Agent Framework | GA since 2026-04-03, now 1.28.0, MIT. Weekly releases with breaking changes. Read in source: pluggable session store, history provider, checkpoint store, and skills source; function middleware; approval content; orchestration builders; harness capabilities. No PostgreSQL stores are built in. The durable extension targets a cloud scheduler. |
| Venice | OpenAI-compatible chat with streaming and tools. Keyless model list with capabilities, prices, and privacy mode. Its own system prompt is on unless disabled. Key-only auth. |
| Microsoft.Extensions.AI | `IChatClient` and `IEmbeddingGenerator` are stable. `Microsoft.Extensions.AI.OpenAI` is stable. The official `Anthropic` package (Apache-2.0) exposes `IChatClient`. |
| MCP C# SDK | 2.4.0, Apache-2.0, stable. Implements spec revision 2026-07-28 with fallback to older servers. Client handles discovery, PKCE, resource indicators, client ID metadata documents, dynamic registration, and refresh. Token storage is pluggable through `ITokenCache`. |
| MCP spec 2026-07-28 | Stateless. Client ID metadata documents are preferred; dynamic client registration is deprecated. Sampling and roots are deprecated. |
| E2B | Official SDKs for JavaScript and Python only. REST control plane and an in-sandbox daemon with published OpenAPI and proto files. Per-sandbox access token, egress allow and deny rules, pause and resume. Runtime is Apache-2.0 and self-hostable. |
| MAUI | No Linux target, including in MAUI 11. The "MAUI Blazor Hybrid and Web App" template generates MAUI app, web app, and shared library, and requires global interactivity. |
| Aspire | Now named "Aspire", MIT. Development-time only; a hand-written compose file is unaffected. |
| Licenses that changed | Out: FluentAssertions 8+, MediatR, AutoMapper, MassTransit 9, TickerQ (commercial since 2026-10-25), Verify binaries 33+ (maintenance fee), SonarAnalyzer.CSharp (source-available), Hangfire (LGPL), Blazorise, QuestPDF, EPPlus, Duende IdentityServer. |
| Scanner incidents | Trivy's GitHub actions and one binary release were compromised in March 2026; KICS twice after. Neither is in the recommended set. |

Not verified, and treated as assumptions: Rider's current `.slnx` support, `Pgvector.EntityFrameworkCore` against EF Core 10 (Phase 1, test first), and E2B's in-sandbox daemon accepting gRPC-Web (the adapter uses the Connect JSON protocol instead).

## Conventions used in this document

- "Space" is the tenant. "Principal" is the human a run acts for.
- SQL names are `snake_case`. C# follows `CODE_STYLE.md`; every snippet here does too.
- "Phase 0, 1, 2" are the phases in the handoff.
