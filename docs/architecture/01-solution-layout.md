# 1. Solution and project layout

## Two solutions

| File | Contains | Why separate |
| --- | --- | --- |
| `Drover.slnx` | Server, shared libraries, adapters, analyzers, all tests | Builds with the plain .NET SDK. This is what pull request CI builds and tests. |
| `Drover.Native.slnx` | The native client, plus references to the shared libraries | Needs the `wasm-tools` workload and platform SDKs. Keeping it out of the main solution keeps the main build and its CI free of them; the native and Android preview workflows format and license-check this solution on their own. See [18-native-client.md](18-native-client.md). |

The Blazor web client (`Drover.Ui`, `Drover.Web`) and the MAUI Android shell (`Drover.App`, in `Drover.Apps.slnx`) were retired on 2026-11-20 by the owner's decision. The native client replaces both.

Both use `.slnx`. Never keep a `.sln` beside a `.slnx`; the SDK treats that as an error.

## Projects

```
Drover.slnx
  src/
    Drover.Contracts              records shared by server and clients
    Drover.Domain                 entities, enums, pure rules, Drover-owned interfaces
    Drover.Data                   EF Core context, mappings, migrations, tenant scope
    Drover.Engine                 run loop, tool gate, grants, hierarchy, delegation, scheduling
    Drover.Agents                 the agent runtime on Microsoft Agent Framework: stores, governance, teams
    Drover.Adapters.OpenAICompatible IModelClientProvider for any compatible endpoint
    Drover.Adapters.Venice           IModelClientProvider and more for Venice
    Drover.Adapters.Mcp              IToolSource over the MCP C# SDK
    Drover.Adapters.E2B              ISandboxProvider over E2B's HTTP API
    Drover.Adapters.WebPush          IPushChannel for browsers and UnifiedPush
    Drover.Adapters.SearXng          IWebSearch over an operator's own SearXNG instance
    Drover.Adapters.LocalFiles       IFileStore on a volume
    Drover.Adapters.DataProtection   ISecretVault over ASP.NET Core Data Protection, ciphertext through Data's SecretRepository
    Drover.Client                 typed API client, SSE reader with reconnect, run state reducer and view model
    Drover.Presentation           the interface without a UI framework: words, drafts, models, view models, the router
    Drover.Server                 ASP.NET Core host: API, SSE, auth, workers, composition root
    Drover.AppHost                Aspire, local development only
  tools/
    Drover.Analyzers              Roslyn analyzers DRV0001 to DRV0008
    Drover.Analyzers.CodeFixes    Code fixes for the analyzers; a separate assembly because the compiler must not load Workspaces
  tests/
    Drover.Domain.Tests
    Drover.Data.Tests             RLS isolation suite, real PostgreSQL
    Drover.Engine.Tests
    Drover.Adapters.Conformance   abstract suites every adapter must pass
    Drover.Adapters.Tests         each adapter run through its suite
    Drover.Server.Tests           API tests through WebApplicationFactory
    Drover.Client.Tests           the reducer against event logs recorded from the engine
    Drover.Presentation.Tests     the router, view models, wording, token parity and contrast, native interface rules,
                                  the shared web files in a real Chromium
    Drover.Analyzers.Tests
```

```
Drover.Native.slnx
  src/
    Drover.Native                 Uno Platform app: web (WebAssembly), desktop, Android, [iOS]
    (references Drover.Presentation, Drover.Client, Drover.Contracts)
```

`tests/Drover.Native.Desktop.Tests` runs the desktop credential store against the computer's real one. It is in neither solution: `Drover.slnx`'s Linux CI has no Secret Service, and the Android and browser jobs that license-check `Drover.Native.slnx` never restore a desktop project. The desktop workflow runs it by path on each system.

`src/Shared/Transport` holds how a client reaches the server, as source files linked into the native client: `AntiforgeryHandler` into its browser build; `AppRequests`, `NativeSession`, `NativeSessionTokens`, and `ServerAddress` into its device builds. `src/Shared/Web` holds the browser build's start script, service worker, notification scripts, and icons, and `src/Shared/Android` its Android notifications, shares, and file delivery. They were shared with the retired web client and Android shell and stay where they were.

Later adapters get their own projects in the same pattern: `Drover.Adapters.Docker`, `Drover.Adapters.Apns`, `Drover.Adapters.S3`, and so on.

The model transport uses Drover-owned records and `IModelChatClient` in `Domain`. `OpenAICompatible` owns the sole OpenAI SDK reference; Venice receives the shared protocol builder through a Domain delegate, never a project reference. `Drover.Agents` bridges the transport to `IChatClient`. The conversion source in `src/Shared/ModelChatMapping.cs` is linked only into those two SDK/framework boundary projects; it does not create another assembly or change dependency direction.

## What each project owns

| Project | Owns | Must not contain |
| --- | --- | --- |
| `Drover.Contracts` | Request and response records, run event records, enums used on the wire | Logic, package references |
| `Drover.Domain` | Entities, value objects, grant and hierarchy rules as pure functions, the interfaces in [05-dependencies.md](05-dependencies.md) | EF Core, HTTP, any vendor type |
| `Drover.Data` | `DroverDbContext`, entity mappings, migrations, `TenantScope`, the row-level security migration helpers, the `sys` functions, and the **repositories**: every query in the product | Business rules |
| `Drover.Engine` | The **services**. Everything that decides: run state machine, tool gate, effective grants, delegation, loop and budget checks, MCP catalog, scheduler, deployment policy | Vendor types, and queries. It reads and writes only through repositories. |
| `Drover.Agents` | Everything built on the agent framework: building an agent from a bot, PostgreSQL history, session, and checkpoint stores, the tool gate middleware, `GovernedAgent`, team patterns. See [14-agent-framework.md](14-agent-framework.md). | Run lifecycle (claiming, leases, status), HTTP |
| `Drover.Adapters.*` | One vendor each. Translation between the vendor's types and Drover's | Business rules, and database access of their own. An adapter whose store is a table (`Drover.Adapters.DataProtection`) reaches it only through a repository in `Drover.Data` and runs inside the caller's tenant scope. |
| `Drover.Client` | `HttpClient`-based API client, SSE reader with resume, client-side run state reducer | UI |
| `Drover.Presentation` | Everything about the interface that does not draw: words, drafts, tool cards, timelines, bot looks, icon shapes, the markdown node model, session state, toasts, view models, the router | Any UI framework type: no Razor, no XAML |
| `Drover.Native` | The native client: tokens, controls, layouts, shell, pages, platform folders | Interface logic that could live in `Drover.Presentation`; direct `HttpClient` use outside its two `PlatformServices` files |
| `Drover.Server` | Endpoint mapping, authentication, hosted services, dependency injection wiring, status page | Business rules; endpoints call the engine |

## Dependency direction

```
Contracts  <-  Domain  <-  Data  <-  Engine  <-  Server
                 ^           ^                     |
                 |           +----  Agents  <------+   (Server wires these in)
                 +----------------  Adapters.*  <--+

Contracts  <-  Client  <-  Presentation  <-  Native
```

Rules, enforced by project references and by analyzer `DRV0007`:

1. Arrows point one way. `Domain` references only `Contracts`. `Engine` never references an adapter.
2. A vendor package is referenced by exactly one adapter project. An adapter references `Drover.Domain`, and `Drover.Data` only when its store is a table. Because `Engine`, `Data`, `Contracts`, and `Presentation` do not reference it, a vendor type cannot appear in a contract, a column, or a view model even by accident. `DRV0007` reports the attempt with a readable message before the compiler does.
3. `Server` is the only project that references the engine together with `Agents` and the adapters. It is the composition root.
4. Agent framework types are allowed in `Drover.Agents` and in `Server`'s wiring, and nowhere else. `Agents` is the one non-adapter project that holds a vendor's types, because the owner chose to build on that framework fully; it may read and write through `Drover.Data`.
5. Client projects never reference `Domain`, `Data`, or `Engine`. The server owns the work.

## Why these boundaries and not fewer

- **`Domain` and `Engine` are split** so the grant and hierarchy rules are pure functions testable without a database, and so adapters can reference the interfaces without pulling in the engine.
- **`Data` is its own project** because the migration role, the row-level security helpers, and the isolation tests form one unit that should change together.
- **Adapters are one project per vendor**, not one "Infrastructure" project. That is the cheapest way to make the wrap rule physical instead of a convention.
- **Repositories and services.** `Drover.Data` holds one repository per aggregate; `Drover.Engine` holds the services that use them. This is the owner's usual layering, and it earns its place here for a reason specific to Drover: with every query in one project, the set of statements that run under row-level security is small, reviewable, and written by fewer hands. Analyzer `DRV0008` keeps `DroverDbContext`, `DbSet`, and `IQueryable` inside `Drover.Data`.
- **What the repositories are not.** Not generic (`IRepository<T>` hides what EF Core can do), not behind interfaces (one implementation, and services are tested against real PostgreSQL because a mock cannot fail the way a policy does), and not owners of the transaction (the service opens the `TenantScope`; several repository calls share it). Inside a repository, EF Core is used directly and fully. See [ADR 0011](../adr/0011-repositories-and-services.md).

## Web hosting model

The browser client is the native client's WebAssembly build, published into the server's web root and served by `Drover.Server` as static files at `/` (`Clients/NativeClientFiles.cs`; `NativeClient:Path` names another folder). Every address that is not a file is its page. Reasons:

- Every target, the browser included, uses the same API-backed path, which removes a whole class of "works on web, breaks in the app" bugs.
- The server keeps no per-user UI circuit. That matters for the hosted service's memory use and lets a deploy restart the server without dropping anyone's UI state.
- It can be installed as a PWA.

Until 2026-11-20 the build was served at `/native/` beside the retired web client. That address sends the browser to the same address at `/` (or to `/` alone when the path is not plain segments), `/native/service-worker.js` is answered with a worker that removes the worker and stored shell a browser kept from there and hands its push subscription to the client at the root, and an app installed from there is moved to the root by a start page and a manifest that keeps its identity (`Clients/RetiredNativeAddress.cs`, [18-native-client.md](18-native-client.md)).

Cost: a larger first download than server-side rendering. The shell is cached by the service worker after first load. The worker answers only GET requests for Drover's own files and pages: a page is asked of the server first and falls back to the stored shell, a file comes from the stored shell, and a request the worker cannot answer gets a network error response, never an unhandled rejection. The API and sign-in callbacks (`/api/`, `/health/`, `/openapi/`, `/oauth/`), the retired `/native/` address, anything that is not a GET, and every request to another host go to the network as if there were no worker, because a request the worker made itself would be held to the worker's own content security policy. `ServiceWorkerTests` and `RetiredNativeWorkerTests` (in `Drover.Presentation.Tests`) check this in a real Chromium against the published worker.

## Apple switch

One MSBuild property, `DroverEnableApple`, default `false`, defined in `Directory.Build.props`:

```xml
<PropertyGroup>
  <DroverEnableApple Condition="'$(DroverEnableApple)' == ''">false</DroverEnableApple>
</PropertyGroup>
```

`Drover.Native.csproj` builds its target list from it:

```xml
<PropertyGroup>
  <TargetFrameworks>net11.0-desktop;net11.0-browserwasm;net11.0-android</TargetFrameworks>
  <TargetFrameworks Condition="'$(DroverEnableApple)' == 'true'">$(TargetFrameworks);net11.0-ios</TargetFrameworks>
</PropertyGroup>
```

macOS uses the desktop target. The APNs adapter and relay are designed in [13-deployment-and-push.md](13-deployment-and-push.md) and built last. Turning iOS on is: set the property, add signing, add the CI job.

## Linux desktop

The native client's desktop target runs on Linux, as on macOS and Windows ([18-native-client.md](18-native-client.md)). The PWA remains an option.

## Root files

| File | Purpose |
| --- | --- |
| `Directory.Build.props` | `net11.0`, nullable, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, analyzer references, `DroverEnableApple`. See [10-tooling.md](10-tooling.md). |
| `Directory.Packages.props` | Central package management with transitive pinning. Every version in one file. No lock files; see [10-tooling.md](10-tooling.md). |
| `global.json` | Pins the SDK feature band so local, CI, and the Docker build agree. |
| `nuget.config` | `nuget.org` only, with package source mapping. |
| `compose.yaml`, `Dockerfile` | See [13-deployment-and-push.md](13-deployment-and-push.md). |
| `LICENSE`, `NOTICE` | Apache-2.0. |
