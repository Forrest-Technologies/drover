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

Files that already sit at their final paths: [`/AGENTS.md`](../../AGENTS.md), [`/CODE_STYLE.md`](../../CODE_STYLE.md), [`/.editorconfig`](../../.editorconfig), [`/BannedSymbols.txt`](../../BannedSymbols.txt). The CI workflows are drafts under [`drafts/workflows/`](drafts/workflows/) and move to `.github/workflows/` in Phase 0 step 1, once there is a solution for them to build.

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

1. **Drover owns the run, not the agent framework.** A run is a row, its history is rows, and every step is persisted before the next one starts. The agent framework executes one turn at a time behind `IAgentRuntime` and holds no state Drover needs. This is what makes restart recovery, approvals, delegation, and replacing the framework possible. The framework shipped breaking changes in each of its last three weekly releases (verified), so this boundary is not optional.
2. **One data path for every client.** The web UI is Blazor WebAssembly with global interactivity, so web and the MAUI shell both talk to the server through the same typed client over JSON and SSE. No screen has a server-only code path. The official template imposes global interactivity for the same reason (verified).
3. **Tenant isolation lives in PostgreSQL.** The app role cannot bypass row-level security, the tenant is set per transaction, and a missing setting returns zero rows. A catalog-driven test fails the build when a table has no policy.
4. **Permissions are enforced outside the model.** A tool with no grant is never sent to the model. Grants narrow down the bot tree and are resolved at call time, so a prompt injection can only reach what the bot was already allowed to do.
5. **Everything that waits is a pending tool call.** Human approval and delegation to another bot use the same mechanism: the turn ends with a tool call that has no result yet, the run parks, and it resumes when the result arrives. One mechanism, one set of tests.

## Changes proposed to settled decisions

None are blocking. Four need the owner's eye:

| Decision in the brief | Finding | Proposal |
| --- | --- | --- |
| MCP OAuth "just works" | The official C# SDK's OAuth client awaits one in-process callback and has no begin/complete split (verified in source at 2.4.0). A pending sign-in is lost on restart and needs routing help across instances. | Use the SDK with a small bridge now; see [06-mcp.md](06-mcp.md). Accept "click connect again after a restart". |
| E2B first sandbox adapter | No official .NET SDK exists. The one community package is small. | Drover owns a thin client in the adapter project, generated from E2B's published OpenAPI and proto files. See [07-sandbox.md](07-sandbox.md). |
| Phase split | Phase 0 ships an "ask" grant effect but puts approval prompts in Phase 2. | Move the plain approval prompt into Phase 0. See [11-phase-0-plan.md](11-phase-0-plan.md). |
| Android push through UnifiedPush | No .NET or MAUI binding exists for the Android connector. | Server side is plain Web Push, so one adapter covers web and UnifiedPush. The Android receiver is Drover's own small binding; scheduled in Phase 1 with its own risk entry. |

## Open question, answered provisionally

**"BYOS" is read as bring your own subscription.** The design supports it as a second auth kind on a model connection (API key, or OAuth sign-in). Per provider, as verified today:

| Provider | Third-party use of a consumer plan | Drover |
| --- | --- | --- |
| OpenRouter | Permitted, OAuth PKCE issues a user-owned key | Sign-in supported |
| Hugging Face | Permitted, OAuth `inference-api` scope | Sign-in supported |
| GitHub Copilot | Permitted through the Copilot SDK with a GitHub OAuth app, including hosted apps | Sign-in supported, behind its own adapter |
| OpenAI | Permitted for open source and locally run apps; hosted or paid apps are on a waitlist | Sign-in for self-hosted; key-only on the hosted service until admitted |
| Anthropic | Prohibited for third-party apps | Key-only |
| Google | Prohibited for third-party tools | Key-only |
| xAI, Mistral, Perplexity, others | No public mechanism or unclear | Key-only |

Subscription sign-in is Phase 2 work. If "BYOS" meant "bring your own server", nothing changes: self-hosting is already the primary deployment.

## Verified facts

| Topic | Finding (2026-11-11) |
| --- | --- |
| .NET | .NET 10 is the LTS, supported to 2028-11-14. .NET 11 (STS) shipped 2026-11-10. Drover targets `net10.0`. |
| EF Core on PostgreSQL | EF Core 10.0.x with `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3. The 11.0 provider is still a release candidate. |
| `.slnx` | Default for `dotnet new sln` in the .NET 10 SDK. Supported by Visual Studio 17.14, C# Dev Kit, CodeQL (CLI 2.24.0), and Dependabot discovery. Tested here with `dotnet build` and `dotnet format` on SDK 10.0.115. |
| Microsoft Agent Framework | GA since 2026-04-03, now 1.28.0, MIT. Weekly releases with breaking changes. `Microsoft.Agents.AI` is stable; hosting, A2A, and durable packages are prerelease, and the durable extension requires an Azure service. |
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
