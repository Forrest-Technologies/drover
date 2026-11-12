# 5. Dependencies

Licenses and versions were checked on 2026-11-11 against package metadata and license files. Versions are the newest that fit `net10.0`; `Directory.Packages.props` pins exact versions.

The rule from the handoff, restated as the test applied to each row:

- **Wrapped** when the dependency could realistically be replaced, has more than one provider, or would spread its types through the code.
- **Direct** when it is the platform, or a small library used in one place where the call site is already the seam.

## Interfaces Drover owns

All live in `Drover.Domain`. Each has a conformance suite in `Drover.Adapters.Conformance` that every adapter must pass.

| Interface | Purpose | First adapter | Next adapters |
| --- | --- | --- | --- |
| `IAgentRuntime` | Execute a run: agent, tools, approvals, delegation, teams | `Drover.Agents` on Microsoft Agent Framework | None planned. The seam is for testing the engine without a model. |
| `IModelClientProvider` | Turn a model connection into chat and embedding clients, list models with capabilities and prices, test the connection | Two in Phase 0: any OpenAI-compatible endpoint, and Venice as a first-party provider | Anthropic through its official package; ChatGPT plan sign-in for self-hosted servers; other sign-in adapters. See [15-model-providers.md](15-model-providers.md). |
| `IToolSource` | List tools and call a tool on a connection | MCP over HTTP | MCP over stdio inside a sandbox |
| `ISandboxProvider` | Create, pause, resume, destroy an environment; run commands; read and write files | E2B | Operator's Docker host |
| `IMemoryStore` | Store and recall long-term memory for a bot | PostgreSQL with `pgvector` (Phase 1) | |
| `IEmbeddingProvider` | Text to vector | Venice and compatible endpoints, through `IEmbeddingGenerator` (Phase 1) | |
| `IWebSearch` | Search the web | Venice's search endpoint, since a Venice key is already there (Phase 1) | Any provider with a plain HTTP API and operator key |
| `ISpeechToText`, `ITextToSpeech` | Voice in and out | Venice's speech endpoints (Phase 2) | |
| `IPushChannel` | Deliver a wake-up to one subscription | Web Push, which also serves UnifiedPush | Apple relay, Google |
| `IFileStore` | Store and fetch blobs by key | Local volume | S3-compatible through `AWSSDK.S3` |
| `IEmailSender` | Send account email | SMTP through MailKit (Phase 2) | |
| `ISecretVault` | Protect and reveal secrets by id | ASP.NET Core Data Protection with rows in PostgreSQL | An external key manager for the hosted service |

Twelve interfaces. Nothing else in the codebase gets an interface "in case"; see the simplicity rule in `AGENTS.md`.

`Microsoft.Extensions.AI` deserves a note. Its abstractions are stable and are themselves a provider-neutral layer, so Drover does not wrap `IChatClient` a second time. It is confined to `Drover.Agents` and the model provider adapters and never appears in `Domain`, `Engine`, `Contracts`, or the database. Messages are stored in Drover's own part schema and translated at the adapter.

## Runtime dependencies

| Dependency | License | Maintainer and standing | Use | Wrapped? |
| --- | --- | --- | --- | --- |
| ASP.NET Core, Minimal APIs, Identity, Data Protection, OpenAPI | MIT | Microsoft, part of .NET 10 LTS | Server, sign-in, key ring, API description | Direct. The platform. Data Protection sits behind `ISecretVault` only for secret storage. |
| Entity Framework Core 10 | MIT | Microsoft | Data access, migrations | Direct. The platform. |
| `Npgsql`, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x | PostgreSQL License (permissive) | Npgsql team, the standard .NET driver | PostgreSQL driver and provider | Direct. PostgreSQL is a settled decision. |
| `Microsoft.AspNetCore.OpenApi` | MIT | Microsoft | The OpenAPI description of the endpoints, served at `/openapi/v1.json` | Direct. The platform. |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | MIT | Microsoft | The Identity stores on EF Core: the `users` table and its children | Direct. The platform. |
| `EFCore.NamingConventions` | Apache-2.0 | Maintained by an EF Core team member | `snake_case` names | Direct. One line of configuration. |
| Microsoft Agent Framework: `Microsoft.Agents.AI` 1.28.0 and `.Workflows` 1.28.0 in use; `.Harness` and preview packages as adopted | MIT | Microsoft. GA since April 2026; weekly releases with breaking changes | The agent runtime, in full: agents, sessions, history, compaction, context providers, skills, workflows, orchestration, approval, telemetry, evaluation | **Built on, inside one project.** `Drover.Agents` holds every framework type. Latest release tracked weekly, previews allowed, conformance suite as the gate. The adoption map is in [14-agent-framework.md](14-agent-framework.md); the experimental APIs in use are listed below. |
| `Microsoft.Extensions.AI`, `.OpenAI` | MIT | Microsoft, stable | Chat and embedding clients | Confined to adapters and `Drover.Agents`, as above. `Microsoft.Extensions.AI` 10.11.0 arrives with the agent framework; analyzer DRV0007 keeps its namespace inside those projects. |
| `Anthropic` (official) | Apache-2.0 | Anthropic | Native connection kind for that provider | Behind `IModelClientProvider` |
| `ModelContextProtocol` 2.4.x | Apache-2.0 | The protocol's project, with Microsoft | MCP client, OAuth | **Wrapped** by `IToolSource`. Its types stop at the adapter. |
| Markdig | BSD-2-Clause | Alexandre Mutel; the standard .NET markdown library, active | Markdown to HTML in `Drover.Ui` | Direct. One call site, `MarkdownView`. |
| HtmlSanitizer, with AngleSharp | MIT, MIT | Michael Ganss; AngleSharp project. Both active | Sanitize rendered markdown | Direct. Same call site. |
| `Lib.Net.Http.WebPush` | MIT | Tomasz Peczek; steady releases, no extra crypto dependency | Web Push and VAPID | **Wrapped** by `IPushChannel` |
| Cronos | MIT | The Hangfire authors | Parse cron expressions, compute next run | Direct. One call site in the scheduler. |
| `Microsoft.Extensions.Http.Resilience` (Polly underneath) | MIT (BSD-3-Clause) | Microsoft; Polly is an established project | Timeouts, retries, circuit breakers on outbound HTTP | Direct. Configured where each `HttpClient` is registered. |
| OpenTelemetry .NET | Apache-2.0 | CNCF project | Traces and metrics, exported only if the operator configures an endpoint | Direct. Standard instrumentation. |
| `Microsoft.Extensions.Logging` | MIT | Microsoft | Logging, as the guide requires | Direct. No third-party logger. |
| .NET MAUI, `BlazorWebView` | MIT | Microsoft | Native shell | Direct for the shell. Native features sit behind the small platform interfaces in `Drover.Ui`. |
| Aspire hosting packages | MIT | Microsoft | Local development only | Direct. Not referenced by anything that ships. |
| Inter, JetBrains Mono (fonts) | SIL OFL 1.1 | Their respective projects | UI and monospace type, bundled | Assets |

### Experimental APIs in use

Each experimental API Drover uses, so one that changes is easy to find. The diagnostic is allowed only in the projects named, by a `NoWarn` with a comment in the project file.

| API | Diagnostic | Where | Why |
| --- | --- | --- | --- |
| `Microsoft.Agents.AI.AgentSessionStore` | MAAI001 | `Drover.Agents` (`PostgresAgentSessionStore`), `Drover.Adapters.Tests` | The framework's session store abstraction, implemented on `agent_sessions` |
| `Microsoft.Agents.AI.AgentSessionStoreKey` | MAAI001 | Same | The key with space and audience partitions |

The tool approval types (`ApprovalRequiredAIFunction`, `ToolApprovalRequestContent`, `ToolApprovalResponseContent`) were experimental when the design was written and are stable in `Microsoft.Extensions.AI` 10.11.0.

Phase 1 and 2 additions, already checked:

| Dependency | License | Standing | Use | Wrapped? |
| --- | --- | --- | --- | --- |
| `Pgvector.EntityFrameworkCore` | MIT | Single maintainer, slow but stable. Not yet verified against EF Core 10; test before adopting, fall back to raw SQL for the one vector query | Memory search | Behind `IMemoryStore` |
| `AWSSDK.S3` | Apache-2.0 | Amazon | Object storage for the hosted service | Behind `IFileStore` |
| MailKit | MIT | Jeffrey Stedfast, the standard .NET mail library | SMTP | Behind `IEmailSender` |
| `Docker.DotNet.Enhanced` | MIT | Testcontainers project; the maintained fork | Docker sandbox adapter | Behind `ISandboxProvider` |
| SkiaSharp | MIT | Microsoft | Image resizing for attachments | Direct, one call site |
| OpenIddict | Apache-2.0 | Kévin Chalet, active | Only if Drover must act as an OAuth server for third-party clients | Direct if adopted |

No library is used for E2B or for Apple's push service. Neither has an official or well-adopted .NET client, and both are small HTTP APIs. Drover owns those clients inside their adapter projects. See [07-sandbox.md](07-sandbox.md) and [13-deployment-and-push.md](13-deployment-and-push.md).

## Test and build dependencies

| Dependency | License | Standing | Use |
| --- | --- | --- | --- |
| xUnit v3 | Apache-2.0 | xUnit project, active | Test framework |
| `xunit.v3.extensibility.core` | Apache-2.0 | xunit project, active | Lets `Drover.Adapters.Conformance`, a library of abstract suites, use `[Fact]` without being a test executable (`xunit.v3` refuses a library) |
| AwesomeAssertions | Apache-2.0 | Community fork, active | Assertions |
| NSubstitute | BSD-3-Clause | Community, active | Test doubles |
| Testcontainers, `Testcontainers.PostgreSql` | MIT | Active | Real PostgreSQL in tests |
| bUnit | MIT | Egil Hansen, active | Blazor component tests |
| `Microsoft.AspNetCore.Mvc.Testing` | MIT | Microsoft | API tests |
| coverlet | MIT | .NET Foundation | Coverage |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | MIT | Microsoft | Enforce the banned calls in `CODE_STYLE.md` |
| `Microsoft.EntityFrameworkCore.Design`, `dotnet-ef` | MIT | Microsoft | Generate migrations. The design package is private to `Drover.Data`; the tool is pinned in `dotnet-tools.json`. |
| `Microsoft.CodeAnalysis.CSharp` | MIT | Microsoft | Build `Drover.Analyzers`. Version 4.14 for compiler compatibility; `Drover.Data` privately overrides CSharp and CSharp.Workspaces to 5.0.0 for EF Core design tooling |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | MIT | Microsoft | Build `Drover.Analyzers.CodeFixes`. Kept out of `Drover.Analyzers` because the compiler hosts analyzers and must not load Workspaces (RS1038) |
| `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`, `.CSharp.CodeFix.Testing` | MIT | Microsoft (Roslyn SDK) | Run analyzers and code fixes over source samples in `Drover.Analyzers.Tests`. The xUnit verifier packages are v2 only, so the tests use the default verifier. Samples compile against the running runtime, so nothing is downloaded. They pull in `Microsoft.VisualStudio.Validation`, MIT by URL only, mapped in `licenses/url-mappings.json` |
| `System.Security.Cryptography.Pkcs` | MIT | Microsoft | Not used directly. Pinned to 10.0.15 because the Roslyn testing packages ask for 9.0.6, which has known vulnerabilities |

## Considered and rejected

| Dependency | Reason |
| --- | --- |
| FluentAssertions 8 and later | Commercial license since January 2025 |
| MediatR, AutoMapper | Commercial license since July 2025. Not needed: endpoints call engine services directly; mapping is hand-written extension methods |
| MassTransit 9 | Commercial since January 2026. Not needed: jobs are rows in PostgreSQL |
| TickerQ | Commercial since 2026-10-25 |
| Hangfire | LGPL-3.0 core |
| Quartz.NET | Apache-2.0 and healthy, but runs already are durable jobs with leases; a second job system would duplicate that. Cronos covers the cron math. |
| Verify | Binaries carry a maintenance fee agreement since September 2026 |
| Moq | License is fine; trust was damaged by the 2023 telemetry episode. NSubstitute instead. |
| SonarAnalyzer.CSharp | Source-available license since November 2024 |
| StyleCop.Analyzers | Stable release dates from 2019; its rules disagree with the style guide in several places |
| Duende IdentityServer | Commercial |
| ImageSharp | Split license with a revenue threshold. Usable today, but SkiaSharp avoids the question. |
| Blazorise, QuestPDF, EPPlus | Commercial or non-commercial-only |
| MinIO .NET client | No release in a year and the server project was archived. `AWSSDK.S3` talks to any S3-compatible store. |
| The community E2B package | Small and low adoption. Used as a reference only. |
| A component library for the UI | See [09-design-direction.md](09-design-direction.md) |
| SignalR for run streaming | Fine platform piece, but reconnect does not replay; Server-Sent Events give resume from a cursor for free |

## License policy, enforced

- Allowed: Apache-2.0, MIT, BSD-2-Clause, BSD-3-Clause, ISC, PostgreSQL, MS-PL, SIL OFL 1.1 (fonts), Unicode.
- Everything else fails the license check in CI, including LGPL, GPL, MPL, any source-available license, and any package with no license expression.
- Adding a package means: add it to `Directory.Packages.props`, add a row to this file with license, maintainer, and wrapped-or-direct reason, and let the license check pass. `AGENTS.md` states this.
- Transitive packages are checked too. An exception needs an entry in `licenses/allowed-exceptions.json` with a reason, which a reviewer sees.
