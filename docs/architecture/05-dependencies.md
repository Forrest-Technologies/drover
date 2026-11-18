# 5. Dependencies

Licenses and versions were checked on 2026-11-11 against package metadata and license files. Versions are the newest generally available that fit `net11.0` (previews only for AI libraries, [ADR 0013](../adr/0013-track-the-newest-generally-available-dotnet.md)); `Directory.Packages.props` pins exact versions.

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

`Microsoft.Extensions.AI` stays inside the model SDK adapter and `Drover.Agents`. `IModelClientProvider` returns a Drover-owned `IModelChatClient`; requests, streaming updates, tool declarations, content, usage and citations use Domain records. This is a translation seam, not a replacement for agent sessions, orchestration or tool invocation. A Domain `CompatibleChatClientBuilder` delegate lets Venice compose the shared OpenAI protocol implementation without referencing another adapter. Only `Drover.Adapters.OpenAICompatible` references the OpenAI packages. The shared conversion source is compiled independently inside the two permitted boundary projects.

## Runtime dependencies

| Dependency | License | Maintainer and standing | Use | Wrapped? |
| --- | --- | --- | --- | --- |
| ASP.NET Core, Minimal APIs, Identity, Data Protection, OpenAPI | MIT | Microsoft, part of .NET 11 | Server, sign-in, key ring, API description | Direct. The platform. Data Protection sits behind `ISecretVault` only for secret storage. |
| Entity Framework Core 10 | MIT | Microsoft | Data access, migrations | Direct. The platform. |
| `Npgsql`, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x | PostgreSQL License (permissive) | Npgsql team, the standard .NET driver | PostgreSQL driver and provider | Direct. PostgreSQL is a settled decision. |
| `Microsoft.AspNetCore.OpenApi` | MIT | Microsoft | The OpenAPI description of the endpoints, served at `/openapi/v1.json` in Development only | Direct. The platform. |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | MIT | Microsoft | The Identity stores on EF Core: the `users` table and its children | Direct. The platform. |
| `EFCore.NamingConventions` | Apache-2.0 | Maintained by an EF Core team member | `snake_case` names | Direct. One line of configuration. |
| Microsoft Agent Framework: `Microsoft.Agents.AI` 1.28.0 and `.Workflows` 1.28.0 in use; `.Harness` and preview packages as adopted | MIT | Microsoft. GA since April 2026; weekly releases with breaking changes | The agent runtime, in full: agents, sessions, history, compaction, context providers, skills, workflows, orchestration, approval, telemetry, evaluation | **Built on, inside one project.** `Drover.Agents` holds every framework type. Latest release tracked weekly, previews allowed, conformance suite as the gate. The adoption map is in [14-agent-framework.md](14-agent-framework.md); the experimental APIs in use are listed below. |
| `Microsoft.Extensions.AI` 11.0.0 and its abstractions | MIT | Microsoft | Chat types at the agent boundary | Arrives with the framework in `Drover.Agents` and the SDK bridge in `OpenAICompatible`; never referenced by `Domain`. |
| `Microsoft.Extensions.AI.OpenAI` 11.0.0, official `OpenAI` client and `System.ClientModel` | MIT | Microsoft and OpenAI | Compatible protocol serialization, tools, streaming and reasoning | Referenced only by `Drover.Adapters.OpenAICompatible`. Venice composes its Drover-owned transport delegate. |
| `Anthropic` (official) | Apache-2.0 | Anthropic | Native connection kind for that provider | Behind `IModelClientProvider` |
| `ModelContextProtocol` 2.4.x | Apache-2.0 | The protocol's project, with Microsoft | MCP client, OAuth | **Wrapped** by `IToolSource`. Its types stop at the adapter. |
| Markdig 1.6.0 | BSD-2-Clause | Alexandre Mutel; the standard .NET markdown library, active | Markdown to HTML in `Drover.Ui` | Direct. One call site, `MarkdownDocument`, which `MarkdownView` renders from. |
| HtmlSanitizer 11.0.26, with AngleSharp and AngleSharp.Css | MIT, MIT, MIT | Michael Ganss; AngleSharp project. Both active | Sanitize rendered markdown | Direct. Same call site. |
| `Lib.Net.Http.WebPush` | MIT | Tomasz Peczek; steady releases, no extra crypto dependency on .NET (its one dependency there is the same author's `Lib.Net.Http.EncryptedContentEncoding`, MIT) | Web Push and VAPID | **Wrapped** by `IPushChannel`, referenced only by `Drover.Adapters.WebPush` |
| Cronos 0.13.0 | MIT | The Hangfire authors | Parse cron expressions, compute next run | Direct, in `Drover.Engine`. `ScheduleService` checks an expression, refuses one whose times are closer together than the deployment allows, and computes the next time it is due; the scheduler (`ScheduleFiring`) uses the same calculation each time a schedule comes due. |
| `Microsoft.Extensions.Http.Resilience` (Polly underneath) | MIT (BSD-3-Clause) | Microsoft; Polly is an established project | Timeouts, retries, circuit breakers on outbound HTTP | Direct. Configured where each `HttpClient` is registered. |
| `Microsoft.Extensions.Http` | MIT | Microsoft | The client factory, used by `Drover.Ui.Tests` so its fake server hands out a client the way every client in Drover is made. Part of the ASP.NET Core shared framework, which that project references for the service worker test's small server; present everywhere else through the resilience package. | Direct, no package reference of its own. Test project only. |
| `Microsoft.Extensions.TimeProvider.Testing` | MIT | Microsoft | A clock a test moves by hand, so a screen that reads the server every ten seconds is tested without waiting. | Direct. Test project only (`Drover.Ui.Tests`). |
| `Microsoft.Playwright` 1.63.0 | MIT | Microsoft | Drives a real Chromium against the published service worker, the one piece of the client a component test cannot reach. | Direct. Test project only (`Drover.Ui.Tests`). Uses an installed Chromium or Chrome (`DROVER_TEST_CHROMIUM`, or a usual path), so no browser download. The package bundles the Playwright driver and a Node runtime (Apache-2.0 and MIT), about 135 MB in the test output. |
| YamlDotNet 16.3.0 | MIT | Antoine Aubry and contributors; the standard .NET YAML library, active | `ComposeFileTests` reads `compose.yaml` and `compose.proxy.yaml` as data to check the hardening rules | Direct. Test project only. |
| OpenTelemetry .NET | Apache-2.0 | CNCF project | Traces and metrics, exported only if the operator configures an endpoint | Direct. Standard instrumentation. |
| `Microsoft.Extensions.Logging` | MIT | Microsoft | Logging, as the guide requires | Direct. No third-party logger. |
| `Microsoft.Maui.Controls`, `Microsoft.AspNetCore.Components.WebView.Maui` | MIT | Microsoft | Android shell | Direct in `Drover.App`; renders the shared Drover UI. |
| `CommunityToolkit.Mvvm` 8.4.2 | MIT | Microsoft and the .NET Foundation; active | `ObservableObject`, `[ObservableProperty]`, and `[RelayCommand]` for the view models | Direct, in `Drover.Presentation`. Source generators only: no runtime wrapper worth owning. |
| Uno Platform: `Uno.Sdk` 7.0.22, and through it `Uno.WinUI` and its runtime packages for Skia desktop, WebAssembly, and Android, `Uno.Wasm.Bootstrap`, `Uno.Resizetizer`, `Uno.WinUI.Graphics2DSK`, `Uno.Fonts.*`, `Uno.UI.Adapter.Microsoft.Extensions.Logging`, `Uno.Extensions.Logging.WebAssembly.Console`, `Uno.WinUI.DevServer` (development builds only) | Apache-2.0 | Uno Platform; active, frequent releases | The UI framework of the native client | Direct, in `Drover.Native` only. The SDK version is in `global.json`; it chooses the versions of its own packages. Its design tools and app inspection server are switched off, and two packages it adds that are not open source (`Uno.Sdk.Extras`, under an end-user agreement; `Uno.Settings.DevServer`, with no license stated) are removed from the package list by the project file. |
| SkiaSharp and HarfBuzzSharp, with their native assets | MIT | Microsoft and the Mono project; active | Drawing and text shaping under the native client. Drover draws the icons, the bot characters, the yard, and the live states with SkiaSharp directly | Arrive with the platform. Used directly through `SKCanvasElement` in `Drover.Native/Controls`. |
| `Uno.Diagnostics.Eventing` 2.0.1, `Uno.icu-wasm`, `Uno.icu-macos`, `Uno.icu-win` 77.4.1, `Tmds.DBus.Generator` 0.92.0 | No license in the package metadata. Their repositories state Apache-2.0, Apache-2.0 for the packaging of ICU (itself under the Unicode license), and MIT | Uno Platform; Tom Deseyn | Arrive with the platform: its event tracing, the Unicode data its text layout needs, and a build-time generator for the Linux desktop | **Reviewed exact-version metadata corrections** in `licenses/allowed-exceptions.json`; these augment missing NuGet metadata rather than excluding packages from checking. ICU packaging was checked at tag 77.4.1 (`f887e003`); its Dockerfile builds ICU 77.1, whose full Unicode V3 and third-party notices ship in `Assets/Licenses`. Eventing's Apache license is unchanged across its release history. The D-Bus generator's MIT license is pinned to its package repository commit `282acebc`. Upstream permalinks and reasons are in each correction. Use `-override licenses/allowed-exceptions.json` when checking `Drover.Native.slnx`. |
| Aspire hosting packages | MIT | Microsoft | Local development only | Direct. Not referenced by anything that ships. |
| Inter 4.1 (variable), JetBrains Mono 2.304 (regular, medium) | SIL OFL 1.1, the one license outside the code list, accepted for fonts only | Their respective projects | UI and monospace type, bundled as WOFF2 in `Drover.Ui/wwwroot/fonts` with each license file beside them. Nothing loads from another host. The native client bundles the same releases as TrueType in `Drover.Native/Assets/Fonts`: Inter as the static regular, medium, and semibold files and the display semibold, because its renderer does not vary a font along an axis. | Assets |

### Experimental APIs in use

Each experimental API Drover uses, so one that changes is easy to find. The diagnostic is allowed only in the projects named, with a comment explaining either the project allowance or a locally scoped pragma.

| API | Diagnostic | Where | Why |
| --- | --- | --- | --- |
| `Microsoft.Agents.AI.AgentSessionStore` | MAAI001 | `Drover.Agents` (`PostgresAgentSessionStore`), `Drover.Adapters.Tests` | The framework's session store abstraction, implemented on `agent_sessions` |
| `Microsoft.Agents.AI.AgentSessionStoreKey` | MAAI001 | Same | The key with space and audience partitions |
| `OpenAI.Chat.ChatCompletionOptions.ReasoningEffortLevel`, `ChatReasoningEffortLevel` | OPENAI001 | `Drover.Adapters.OpenAICompatible` (`ProtocolChatClient`), locally scoped pragma | Preserves provider `max` and `minimal` effort values missing from the framework enum. Exact whole/streamed request fixtures pin this SDK seam. |

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

`Drover.Adapters.E2B` adds no package. It uses `System.Net.Http`, `System.Text.Json`, and `Microsoft.Extensions.Http.Resilience`, which is already central, and it is the only project that talks to E2B: its control plane, its in-sandbox daemon, and their JSON shapes are internal to it.

## Test and build dependencies

| Dependency | License | Standing | Use |
| --- | --- | --- | --- |
| xUnit v3 | Apache-2.0 | xUnit project, active | Test framework |
| `xunit.v3.extensibility.core` | Apache-2.0 | xunit project, active | Lets `Drover.Adapters.Conformance`, a library of abstract suites, use `[Fact]` without being a test executable (`xunit.v3` refuses a library) |
| `ModelContextProtocol.AspNetCore` 2.4.x | Apache-2.0 | The protocol's project, with Microsoft | Test only (`Drover.Adapters.Tests`): the protocol library's own server, so the tool source conformance suite runs over real HTTP against real servers of both protocol generations. Never referenced by a shipping project. |
| AwesomeAssertions | Apache-2.0 | Community fork, active | Assertions |
| NSubstitute | BSD-3-Clause | Community, active | Test doubles |
| Testcontainers, `Testcontainers.PostgreSql` | MIT | Active | Real PostgreSQL in tests |
| bUnit 2.15.2 | MIT | Egil Hansen, active | Blazor component tests in `Drover.Ui.Tests` |
| `Microsoft.AspNetCore.Mvc.Testing` | MIT | Microsoft | API tests |
| coverlet | MIT | .NET Foundation | Coverage |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | MIT | Microsoft | Enforce the banned calls in `CODE_STYLE.md` |
| `Microsoft.EntityFrameworkCore.Design`, `dotnet-ef` | MIT | Microsoft | Generate migrations. The design package is private to `Drover.Data`; the tool is pinned in `dotnet-tools.json`. |
| `Microsoft.CodeAnalysis.CSharp` | MIT | Microsoft | Build `Drover.Analyzers`. Version 5.9.0, so `Drover.Analyzers` needs a compiler with Roslyn 5.9 or newer; SDK 11.0.100 ships 5.12. The SDK 10.0.115 compiler (Roslyn 5.0) refuses to load them (CS9057), which is why this repository needs SDK 11. `Drover.Data` privately overrides CSharp and CSharp.Workspaces to 5.0.0 for EF Core design tooling |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | MIT | Microsoft | Build `Drover.Analyzers.CodeFixes`. Kept out of `Drover.Analyzers` because the compiler hosts analyzers and must not load Workspaces (RS1038) |
| `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`, `.CSharp.CodeFix.Testing` | MIT | Microsoft (Roslyn SDK) | Run analyzers and code fixes over source samples in `Drover.Analyzers.Tests`. The xUnit verifier packages are v2 only, so the tests use the default verifier. Samples compile against the running runtime, so nothing is downloaded. They pull in `Microsoft.VisualStudio.Validation`, MIT by URL only, mapped in `licenses/url-mappings.json` |
| `System.Security.Cryptography.Pkcs` | MIT | Microsoft | Not used directly. Pinned to 11.0.0 because the Roslyn testing packages ask for 9.0.6, which has known vulnerabilities |

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
