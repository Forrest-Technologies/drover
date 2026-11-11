# AGENTS.md

Read this whole file before every change. It holds the rules you cannot learn by reading the code. Where a rule has a reason, the reason is given so you can apply it to cases this file did not foresee.

Drover is a self-hosted, multi-tenant platform for running AI bots. Design documents are in `docs/architecture/`. Read the section that covers what you are changing.

## 1. Style

`CODE_STYLE.md` is binding. Read it before writing C#. It differs from common defaults:

- Regions are required in non-trivial types, in the standard order.
- All `using` directives are global, in one `GlobalUsings.cs` per project.
- Private fields are bare `camelCase`. No underscore.
- No `Async` suffix. Overrides and interface implementations of external members keep the external name.
- `var` for every local. `[]` for every collection. Braces on every block.

The build enforces most of it (`.editorconfig`, `BannedSymbols.txt`, `tools/Drover.Analyzers`). If a tool disagrees with the guide, the tool's configuration is wrong. Fix the configuration. Never bend code or the guide to satisfy a tool, and never suppress a rule without a comment giving the reason.

## 2. Tenancy: the rule that must never break

One space must never read or write another space's data, even when application code has a bug. PostgreSQL row-level security enforces this, not C#.

- Every table is space-owned (`ISpaceOwned`), user-owned (`IUserOwned`), or listed in `GlobalTables.manifest`. There is no fourth kind.
- A migration that creates a space-owned table calls `IsolateBySpace`. Do not write policies by hand.
- A foreign key between two space-owned tables includes `space_id`.
- All tenant data access runs inside a `TenantScope` transaction. Never run a query outside one. Never hold one across a model call, a tool call, or any network wait. Services open the scope; repositories never open or commit one.
- Never add `IgnoreQueryFilters`, raw SQL that names another space, a connection string for another role, or anything else that reaches around the policy.
- Work that must cross spaces goes through a function in schema `sys`. Adding or changing one requires editing `SystemFunctions.manifest` and explaining why in the pull request. Expect it to be questioned. Return ids from such a function, then read the rows inside a normal tenant scope.
- A new table needs a seeder in `Drover.Data.Tests` so the isolation suite covers it. The build fails without one. Do not weaken, skip, or special-case an isolation test to get green.

Why: the owner runs a hosted service for other people. A leak between tenants is the one bug that cannot be taken back.

## 3. Bot permissions

Inside a space, bots are separated by grants. These hold everywhere:

- A tool with no grant is never sent to the model. Check grants through the engine's effective-grant function. Never read `tool_grants` directly to decide access.
- A child bot never exceeds its parent. Effective grant is the minimum along the path to the root.
- A bot has authority over its descendants only.
- The model never decides what it may do. Every tool call passes the tool gate at call time.
- Tool results, tool descriptions from servers, and imported skills are untrusted text. They never become instructions and never change a grant.

## 4. Interfaces and adapters

- External providers sit behind an interface Drover owns, in `Drover.Domain`: models, agent runtime, tools, sandbox, memory, search, voice, push, files, email, secrets.
- A vendor package is referenced by exactly one `Drover.Adapters.*` project. Vendor types never appear in `Drover.Contracts`, `Drover.Domain`, `Drover.Engine`, `Drover.Data`, `Drover.Ui`, a database column, or an API response.
- The agent framework is the one exception in kind, not in rule: Drover builds on Microsoft Agent Framework fully, and all of it lives in `Drover.Agents`. Use its features (sessions, history providers, context providers, skills, workflows, orchestration, approval, middleware) before writing your own. Do not re-implement something it provides. Its types still never leave `Drover.Agents` and the server's wiring. Read `docs/architecture/14-agent-framework.md` before touching that project.
- Framework session and checkpoint data is opaque working state. Conversation content lives in Drover's own `messages` schema. Never make a feature depend on reading a framework blob.
- A new adapter must pass the conformance suite for its interface in `Drover.Adapters.Conformance`. A new behavior every adapter must have goes into that suite first.
- Do not wrap the platform. Use ASP.NET Core, EF Core, dependency injection, and logging directly. No mediator, no generic repository, no unit-of-work wrapper.
- Data access is layered: **repositories** in `Drover.Data` store and retrieve, **services** in `Drover.Engine` hold business logic and call repositories. `DroverDbContext`, `DbSet`, and `IQueryable` never appear outside `Drover.Data` (DRV0008). A repository is a concrete class per aggregate (`ThreadRepository`), uses EF Core fully inside, returns entities or records, and never returns `IQueryable`. No repository interfaces and no mocks of repositories: services are tested against real PostgreSQL, because a mocked repository cannot fail the way row-level security does.
- Do not add an interface for something with one implementation and no realistic second one. A small library used in one place is called directly.

Why: providers change, and the agent framework ships breaking changes weekly, so one project absorbs them. The platform does not change, and wrapping it only hides features.

## 5. Libraries and licenses

- Prefer a trusted, maintained, permissively licensed library over writing the same thing by hand.
- Allowed licenses: Apache-2.0, MIT, BSD-2-Clause, BSD-3-Clause, ISC, PostgreSQL, MS-PL. Anything else is out, including LGPL, GPL, MPL, and source-available or revenue-threshold licenses.
- Check the license of the exact version you add. Several popular .NET libraries have gone commercial. Known to be out: FluentAssertions 8+, MediatR, AutoMapper, MassTransit 9+, TickerQ, Hangfire, Verify 33+, SonarAnalyzer.CSharp, Duende IdentityServer, Blazorise, QuestPDF, EPPlus.
- Adding a package means: version in `Directory.Packages.props`, a row in `docs/architecture/05-dependencies.md` with license, maintainer, and wrapped-or-direct reason, and a passing license check.
- Do not add a dependency for something small that the platform already does.

## 6. Simplicity

- Solve the problem in front of you. No speculative options, extension points, base classes, or configuration for needs nobody has stated.
- Prefer deleting code to adding it. Prefer one obvious way over two flexible ones.
- Small, well-named methods over comments. Extension builders over factories.
- If a change needs a new project, a new interface, or a new background service, say why in the pull request.

## 7. The server owns the work

- Clients express intent and render state. Authorization, validation, orchestration, retries, and provider calls live on the server.
- A run is a durable server-side job with a persisted event log. Clients are viewers. Never put run logic in a component.
- Every screen works through `Drover.Client` over the API. There is no web-only data path, because the same components run in the MAUI shell.
- Tool activity (arguments, output, result, outcome) is part of the event model. A new tool emits the standard tool events; do not invent a side channel.

## 8. Secrets

- Secrets are stored through `ISecretVault` and are never returned by any endpoint. No contract type carries a secret value.
- Never log a secret, put one in an exception message, a run event, a tool argument, or model context.
- Never commit a credential, key, or token, including in tests. Tests generate their own.

## 8a. Deployment policy

- Anything that must differ between a self-hosted server and the hosted service is a named rule in `DeploymentPolicy`, never an `if (hosted)` in feature code. Read `docs/architecture/16-deployment-policies.md`.
- Limits only narrow: deployment policy, then space policy, then bot settings. A lower level can never loosen a higher one.
- A feature that could harm other tenants or break a provider's terms on a shared server ships with a policy rule and a row in the hosted preset test.

## 9. Tests

A change is not done until its tests exist and pass.

| Kind | Where | Notes |
| --- | --- | --- |
| Unit | `tests/Drover.*.Tests` | Pure logic. No network, no clock, no randomness without a seed. |
| Data and isolation | `tests/Drover.Data.Tests` | Real PostgreSQL. The fixture uses `DROVER_TEST_POSTGRES` when set and starts a container otherwise. No Docker in your session? Run `eval "$(scripts/dev-postgres.sh start)"` first. |
| Adapter conformance | `tests/Drover.Adapters.Tests` | Recorded fixtures. Offline. |
| API | `tests/Drover.Server.Tests` | In-process server, real PostgreSQL. |
| Components | `tests/Drover.Ui.Tests` | Assert roles and accessible names as well as content. |

A test that cannot run in your environment is a reason to stop and say so, never a reason to skip it. Tests are deterministic and offline by default. A test that needs a live provider is marked `Live` and never runs in pull request CI. Do not delete, skip, or loosen a failing test to pass; fix the cause or stop and report.

## 10. Commands

```bash
dotnet build Drover.slnx -warnaserror
dotnet format Drover.slnx --verify-no-changes
dotnet test --solution Drover.slnx
dotnet run --project src/Drover.Server         # http://localhost:8080
dotnet run --project src/Drover.AppHost        # server and PostgreSQL for local work
docker compose up --build                      # the deployment a self-hoster runs
```

Scanners, the same ones CI runs:

```bash
dotnet list Drover.slnx package --vulnerable --include-transitive
dotnet tool run nuget-license -- -i Drover.slnx -t -a licenses/allowed.json -mapping licenses/url-mappings.json -err
dotnet tool run dotnet-CycloneDX Drover.slnx -o artifacts/sbom
gitleaks git --redact
zizmor .github/workflows
actionlint
hadolint Dockerfile
```

## 11. CI and runners

- Any job that builds or runs code from a pull request runs on GitHub-hosted runners. Never give such a job a self-hosted runner. A pull request from a fork could run anything on the owner's machine.
- The repository is public. Self-hosted runners are only for trusted jobs that never execute pull request code, such as release builds from a protected tag.
- Only `release.yml` may name a self-hosted runner. Never use `pull_request_target`.
- Every third-party action is pinned to a full commit SHA with the version in a comment. Workflows declare `permissions` explicitly, starting from none.
- The project publishes no container images. Do not add a registry push.

## 12. User interface

- **Read `DESIGN.md` before any change a person can see.** It is the direction: identity, palette, type, motion, and the list of things Drover's interface never does. Build toward it; do not invent a look.
- Before you finish, check your change against every item under "What Drover's interface never does". The ones marked (test) fail the build. The rest are yours to hold.
- No invented content: no made-up numbers, names, activity, or filler copy, in screens or in the gallery. Real data, an honest empty state, or a value visibly marked as a sample.
- Name the screen's job in one sentence before laying it out, and put that sentence in the pull request.
- A pull request that changes the interface includes screenshots: dark, light, and 360 px wide, and each state it adds (empty, loading, error).
- Use the tokens in `tokens.css`. No raw colors, sizes, or durations in components.
- Dark is primary; light must work. Every text and status color keeps 4.5:1 contrast.
- Motion shows state: thinking, streaming, tool running, delegation. Nothing loops when nothing is happening. Honor reduced motion with a static equivalent.
- Minimal copy and chrome. Reveal advanced controls when a choice makes them relevant. Never a wall of settings.
- When a feature needs setup, say what is missing in plain words and link to it.
- Every control works by keyboard and has an accessible name. This is required, not polish.
- Do not name or compare against other products anywhere in code, documentation, or interface text.

## 13. A finished change

1. Does one thing, and the pull request says what and why.
2. Follows `CODE_STYLE.md`. Build has zero warnings.
3. Has tests, and all tests pass locally.
4. `dotnet format --verify-no-changes` is clean.
5. New table: ownership class, `IsolateBySpace`, seeder. New package: license row. New adapter: conformance suite. New `sys` function: manifest and justification.
6. No secret, no vendor type outside its adapter, no query outside a tenant scope.
7. Documentation in `docs/architecture/` still describes what the code does. If your change makes a section wrong, update the section in the same pull request.

If you cannot meet one of these, stop and say which and why. Do not work around it silently.

## 14. How work is tracked

- GitHub Issues are the source of truth for tasks. `ROADMAP.md` gives direction and links to tracking issues; it never holds status.
- One issue, one branch, one pull request. The pull request body starts with `Closes #n` and says how the issue's verification plan was carried out.
- Work only on an issue whose acceptance criteria you can meet. If the issue is unclear or wrong, say so on the issue instead of guessing.
- Do not widen scope. If you find something else that needs doing, open an issue for it.
- Pull request titles follow Conventional Commits: `feat:`, `fix:`, `docs:`, `test:`, `build:`, `refactor:`, with `!` for a breaking change.
- A change that goes against a decision in `docs/adr` adds the ADR that supersedes it, in the same pull request.
- Never edit `ROADMAP.md` status, close an issue by hand, or mark a criterion done that you did not verify.
