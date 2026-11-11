# 1. Solution and project layout

## Two solutions

| File | Contains | Why separate |
| --- | --- | --- |
| `Drover.slnx` | Server, web client, shared libraries, adapters, analyzers, all tests | Builds with the plain .NET SDK. This is what `docker compose build` and pull request CI touch. |
| `Drover.Apps.slnx` | The MAUI shell, plus references to the shared libraries | Needs MAUI workloads and platform SDKs. Keeping it out of the main solution keeps the image build small and CI fast. |

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
    Drover.Adapters.LocalFiles       IFileStore on a volume
    Drover.Adapters.DataProtection   ISecretVault over ASP.NET Core Data Protection
    Drover.Client                 typed API client and SSE reader
    Drover.Ui                     Razor class library: every screen and component
    Drover.Web                    Blazor WebAssembly host for Drover.Ui
    Drover.Server                 ASP.NET Core host: API, SSE, auth, workers, composition root
    Drover.AppHost                Aspire, local development only
  tools/
    Drover.Analyzers              Roslyn analyzers DRV0001 to DRV0007
  tests/
    Drover.Domain.Tests
    Drover.Data.Tests             RLS isolation suite, real PostgreSQL
    Drover.Engine.Tests
    Drover.Adapters.Conformance   abstract suites every adapter must pass
    Drover.Adapters.Tests         each adapter run through its suite
    Drover.Server.Tests           API tests through WebApplicationFactory
    Drover.Ui.Tests               component tests
    Drover.Analyzers.Tests

Drover.Apps.slnx
  src/
    Drover.App                    MAUI Blazor Hybrid shell: Android, Windows, [iOS, Mac Catalyst]
    (references Drover.Ui, Drover.Client, Drover.Contracts)
```

Later adapters get their own projects in the same pattern: `Drover.Adapters.Docker`, `Drover.Adapters.Apns`, `Drover.Adapters.S3`, and so on.

## What each project owns

| Project | Owns | Must not contain |
| --- | --- | --- |
| `Drover.Contracts` | Request and response records, run event records, enums used on the wire | Logic, package references |
| `Drover.Domain` | Entities, value objects, grant and hierarchy rules as pure functions, the interfaces in [05-dependencies.md](05-dependencies.md) | EF Core, HTTP, any vendor type |
| `Drover.Data` | `DroverDbContext`, entity mappings, migrations, `TenantScope`, the row-level security migration helpers, the `sys` functions, and the **repositories**: every query in the product | Business rules |
| `Drover.Engine` | The **services**. Everything that decides: run state machine, tool gate, effective grants, delegation, loop and budget checks, MCP catalog, scheduler, deployment policy | Vendor types, and queries. It reads and writes only through repositories. |
| `Drover.Agents` | Everything built on the agent framework: building an agent from a bot, PostgreSQL history, session, and checkpoint stores, the tool gate middleware, `GovernedAgent`, team patterns. See [14-agent-framework.md](14-agent-framework.md). | Run lifecycle (claiming, leases, status), HTTP |
| `Drover.Adapters.*` | One vendor each. Translation between the vendor's types and Drover's | Business rules, database access |
| `Drover.Client` | `HttpClient`-based API client, SSE reader with resume, client-side run state reducer | UI |
| `Drover.Ui` | Components, design tokens, markdown rendering, platform service interfaces (`IPushRegistration`, `ISecureStore`, `IFilePicker`, `IMicrophone`) | Direct `HttpClient` use; it goes through `Drover.Client` |
| `Drover.Web` | WebAssembly entry point, web implementations of the platform services, service worker, PWA manifest | Screens |
| `Drover.App` | MAUI entry point, MAUI implementations of the platform services, platform folders | Screens |
| `Drover.Server` | Endpoint mapping, authentication, hosted services, dependency injection wiring, status page | Business rules; endpoints call the engine |

## Dependency direction

```
Contracts  <-  Domain  <-  Data  <-  Engine  <-  Server
                 ^           ^                     |
                 |           +----  Agents  <------+   (Server wires these in)
                 +----------------  Adapters.*  <--+

Contracts  <-  Client  <-  Ui  <-  Web
                            ^
                            +----  App
```

Rules, enforced by project references and by analyzer `DRV0007`:

1. Arrows point one way. `Domain` references only `Contracts`. `Engine` never references an adapter.
2. A vendor package is referenced by exactly one adapter project. Because `Engine`, `Data`, `Contracts`, and `Ui` do not reference it, a vendor type cannot appear in a contract, a column, or a component even by accident. `DRV0007` reports the attempt with a readable message before the compiler does.
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

`Drover.Web` is Blazor WebAssembly with global interactivity, served as static files by `Drover.Server`. Reasons:

- The MAUI shell cannot use server-side interactivity, so every screen needs an API-backed path anyway. Making the web use the same path removes a whole class of "works on web, breaks in the app" bugs.
- The server keeps no per-user UI circuit. That matters for the hosted service's memory use and lets a deploy restart the server without dropping anyone's UI state.
- It can be installed as a PWA, which is the Linux desktop answer for now.

Cost: a larger first download than server-side rendering. The shell is cached by the service worker after first load.

## Apple switch

One MSBuild property, `DroverEnableApple`, default `false`, defined in `Directory.Build.props`:

```xml
<PropertyGroup>
  <DroverEnableApple Condition="'$(DroverEnableApple)' == ''">false</DroverEnableApple>
</PropertyGroup>
```

`Drover.App.csproj` builds its target list from it:

```xml
<PropertyGroup>
  <TargetFrameworks>net10.0-android</TargetFrameworks>
  <TargetFrameworks Condition="$([MSBuild]::IsOSPlatform('windows'))">$(TargetFrameworks);net10.0-windows10.0.19041.0</TargetFrameworks>
  <TargetFrameworks Condition="'$(DroverEnableApple)' == 'true'">$(TargetFrameworks);net10.0-ios;net10.0-maccatalyst</TargetFrameworks>
</PropertyGroup>
```

`Platforms/iOS` and `Platforms/MacCatalyst` stay in the tree with their service implementations. The APNs adapter and relay are designed in [13-deployment-and-push.md](13-deployment-and-push.md) and built last. Turning Apple on is: set the property, add signing, add the CI job.

## Linux desktop

Recommendation: no shell now. Linux users install the PWA.

Revisit in Phase 2 with a thin Avalonia window hosting a web view pointed at the same `Drover.Ui` components. Avalonia is MIT and its web view became open source in Avalonia 12 (verified). The other candidates were checked and set aside: one has had no release since January 2025, one depends on a browser engine build that common distributions do not package, and the MAUI backend for Avalonia is a preview that requires .NET 11.

## Root files

| File | Purpose |
| --- | --- |
| `Directory.Build.props` | `net10.0`, nullable, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, analyzer references, `DroverEnableApple`. See [10-tooling.md](10-tooling.md). |
| `Directory.Packages.props` | Central package management. Every version in one file, which is also what the license check reads. |
| `global.json` | Pins the SDK feature band so local, CI, and the Docker build agree. |
| `nuget.config` | `nuget.org` only, with package source mapping. |
| `packages.lock.json` per project | `RestorePackagesWithLockFile`, so the vulnerability and license scans read exact versions. |
| `compose.yaml`, `Dockerfile` | See [13-deployment-and-push.md](13-deployment-and-push.md). |
| `LICENSE`, `NOTICE` | Apache-2.0. |
